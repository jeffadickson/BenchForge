using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace BenchDouble.Host.Server.Setup;

/// <summary>Optional installed-VISA resource enumeration, without opening instrument sessions or sending commands.</summary>
/// <remarks>
/// Uses VPP-4.3 resource-manager search functions only. Driver enumeration behavior is vendor-dependent.
/// Cancellation is observed between native calls; an in-progress synchronous vendor call cannot be interrupted.
/// Physical resources remain unregistered and untested until a host-owned driver supports them.
/// </remarks>
public sealed class NativeVisaDiscoveryProvider : ISetupDiscoveryProvider
{
    private readonly string _scope;
    private readonly string? _host;
    private readonly Func<string, CancellationToken, string[]> _enumerate;

    /// <summary>Configure an explicit local transport or one Ethernet host; never scan an arbitrary subnet.</summary>
    /// <param name="scope">GPIB, USB or Ethernet.</param>
    /// <param name="host">Required single DNS hostname or IPv4 address for Ethernet; null otherwise.</param>
    /// <param name="enumerate">Optional injected VISA catalog for deterministic testing.</param>
    public NativeVisaDiscoveryProvider(string scope, string? host = null,
        Func<string, CancellationToken, string[]>? enumerate = null)
    {
        if (scope is not ("GPIB" or "USB" or "Ethernet")) throw new ArgumentException("Choose GPIB, USB or Ethernet discovery.", nameof(scope));
        if (scope == "Ethernet" && (host is null || host.Length > 253 || !Regex.IsMatch(host, @"\A[A-Za-z0-9][A-Za-z0-9.-]*\z") ||
            (host.EndsWith('.') ? host[..^1] : host).Split('.').Any(label => label.Length is < 1 or > 63 || label[0] == '-' || label[^1] == '-')))
            throw new ArgumentException("Ethernet discovery requires one hostname or IPv4 address, without wildcards.", nameof(host));
        _scope = scope;
        _host = host;
        _enumerate = enumerate ?? EnumerateNative;
    }

    /// <summary>List resources reported by installed VISA within the selected scope, leaving identity unknown.</summary>
    /// <param name="ct">Cancellation before and between native search calls.</param>
    /// <returns>Physical resource inventory or an actionable missing-provider diagnostic.</returns>
    public async ValueTask<SetupDiscovery> DiscoverAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var expression = _scope switch
        {
            "GPIB" => "GPIB[0-9]*::?*",
            "USB" => "USB?*INSTR",
            _ => "TCPIP[0-9]*::" + _host + "::?*INSTR"
        };
        try
        {
            var resources = await Task.Run(() => _enumerate(expression, ct), ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (resources is null || resources.Length > 4096 || resources.Any(r => r is null || r.Length > 255 || r.Any(char.IsControl)))
                throw new InvalidOperationException("Invalid or oversized VISA inventory.");
            var controllers = new Dictionary<string, DiscoveredController>(StringComparer.OrdinalIgnoreCase);
            var endpoints = new List<DiscoveredEndpoint>();
            var diagnostics = new List<string> { "VISA address enumeration only: model, availability and physical identity have not been probed; any USB serial is descriptor evidence only. Save physical mappings as untested drafts until host drivers are installed." };
            foreach (var resource in resources.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();
                var parts = resource.Split("::");
                if (_scope == "GPIB" && parts.Length >= 2 && Regex.IsMatch(parts[0], @"\AGPIB[0-9]+\z"))
                {
                    controllers.TryAdd(parts[0], new("visa", parts[0], "VISA " + parts[0] + " (identify adapter manually)"));
                    if (parts.Length == 3 && parts[2] == "INSTR" && int.TryParse(parts[1], out var address) && address is >= 0 and <= 30)
                        endpoints.Add(new("visa", "GPIB", address.ToString(System.Globalization.CultureInfo.InvariantCulture), parts[0], null, null));
                    else if (parts.Length != 2 || parts[1] != "INTFC")
                        diagnostics.Add("Unsupported GPIB resource shape; use a provider supporting secondary addresses: " + resource);
                }
                else if (_scope == "USB" && Regex.IsMatch(parts[0], @"\AUSB[0-9]+\z") && parts.Length is 5 or 6 && parts[^1] == "INSTR" && parts.Skip(1).All(p => !string.IsNullOrWhiteSpace(p)))
                    endpoints.Add(new("visa", "USB", resource, null, null,
                        parts.Length >= 5 ? new SetupIdentity(Serial: parts[3]) : null));
                else if (_scope == "Ethernet" && parts.Length == 4 && parts[^1] == "INSTR" && !string.IsNullOrWhiteSpace(parts[2]) && Regex.IsMatch(parts[0], @"\ATCPIP[0-9]+\z") && string.Equals(parts[1], _host, StringComparison.OrdinalIgnoreCase))
                    endpoints.Add(new("visa", "Ethernet", resource, null, null, null));
            }
            if (endpoints.Count == 0) diagnostics.Add("No matching instrument resources reported. Check cabling, power and the vendor VISA configuration utility; missing devices are not proof of an empty bus.");
            return new(Guid.NewGuid().ToString("N"), controllers.Values.ToArray(), endpoints.ToArray(), diagnostics.ToArray());
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or InvalidOperationException)
        {
            return new(Guid.NewGuid().ToString("N"), [], [], ["VISA discovery unavailable: " + error.Message + " Install a compatible vendor VISA runtime and configure its controllers, or use manual offline entry."]);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int OpenResourceManager(out uint session);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Ansi)]
    private delegate int FindResource(uint session, [MarshalAs(UnmanagedType.LPStr)] string expression,
        out uint list, out uint count, [Out] byte[] description);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int FindNext(uint list, [Out] byte[] description);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int Close(uint session);

    private static string[] EnumerateNative(string expression, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        string[] libraries = OperatingSystem.IsWindows() ? [Environment.Is64BitProcess ? "visa64.dll" : "visa32.dll"]
            : OperatingSystem.IsMacOS() ? ["/Library/Frameworks/VISA.framework/VISA"] : ["libvisa.so", "libvisa.so.7"];
        nint library = 0;
        foreach (var name in libraries) if (NativeLibrary.TryLoad(name, out library)) break;
        if (library == 0) throw new DllNotFoundException("No VISA shared library was found.");
        try
        {
            T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
            var open = Export<OpenResourceManager>("viOpenDefaultRM");
            var find = Export<FindResource>("viFindRsrc");
            var next = Export<FindNext>("viFindNext");
            var close = Export<Close>("viClose");
            Check(open(out var manager));
            try
            {
                ct.ThrowIfCancellationRequested();
                // VISA resource descriptions use VI_FIND_BUFLEN (256 bytes), including the NUL terminator.
                var buffer = new byte[256];
                var status = find(manager, expression, out var list, out var count, buffer);
                if (status == unchecked((int)0xBFFF0011)) return []; // VI_ERROR_RSRC_NFOUND
                Check(status);
                try
                {
                    if (count > 4096) throw new InvalidOperationException("VISA inventory exceeds the 4096-resource limit; narrow the scope.");
                    var result = new List<string>();
                    for (uint index = 0; index < count; ++index)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (index > 0) { Array.Clear(buffer); Check(next(list, buffer)); }
                        var end = Array.IndexOf(buffer, (byte)0);
                        if (end < 0) throw new InvalidOperationException("VISA returned an unterminated resource descriptor.");
                        result.Add(Encoding.ASCII.GetString(buffer, 0, end));
                    }
                    return result.ToArray();
                }
                finally { if (list != 0) close(list); }
            }
            finally { close(manager); }
        }
        finally { NativeLibrary.Free(library); }
    }

    private static void Check(int status)
    {
        if (status < 0) throw new InvalidOperationException($"VISA status 0x{status:X8}.");
    }
}
