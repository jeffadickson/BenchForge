// Keep the original standalone command contract and deterministic report bytes.
var root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
var jobsIndex = Array.IndexOf(args, "--jobs");
var jobs = jobsIndex >= 0 ? Math.Clamp(int.Parse(args[jobsIndex + 1]), 1, 256) : Math.Min(Environment.ProcessorCount, 8);
return RoslynAudit.Run(root, args.Length > 1 ? args[1] : "build/api-audit.json", jobs);
