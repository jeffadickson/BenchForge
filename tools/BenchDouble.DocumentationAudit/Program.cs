using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Syntax audit, not a semantic proof of documentation correctness. Include
// implicit-public interface/enum members and exclude members of internal types.
var root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
var rows = new List<object>();
foreach (var area in new[] { "src", "tools", "examples", "tests" })
foreach (var file in Directory.EnumerateFiles(Path.Combine(root, area), "*.cs", SearchOption.AllDirectories).Order())
{
    var path = Path.GetRelativePath(root, file).Replace('\\', '/');
    if (path.Split('/').Any(x => x is "obj" or "bin")) continue;
    var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), new CSharpParseOptions(LanguageVersion.CSharp12));
    var errors = tree.GetDiagnostics().Where(x => x.Severity == DiagnosticSeverity.Error).ToArray();
    if (errors.Length > 0) throw new InvalidDataException($"Cannot audit {path}: {string.Join("; ", errors.Select(x => x.ToString()))}");
    foreach (var member in tree.GetRoot().DescendantNodes().OfType<MemberDeclarationSyntax>())
    {
        if (member is BaseNamespaceDeclarationSyntax or GlobalStatementSyntax || !Visible(member)) continue;
        if (member.Ancestors().OfType<BaseTypeDeclarationSyntax>().Any(x => !Visible(x))) continue;
        var identifier = member switch
        {
            BaseTypeDeclarationSyntax x => x.Identifier.Text,
            DelegateDeclarationSyntax x => x.Identifier.Text,
            MethodDeclarationSyntax x => x.Identifier.Text,
            ConstructorDeclarationSyntax x => x.Identifier.Text,
            PropertyDeclarationSyntax x => x.Identifier.Text,
            EventDeclarationSyntax x => x.Identifier.Text,
            EnumMemberDeclarationSyntax x => x.Identifier.Text,
            BaseFieldDeclarationSyntax x => string.Join(",", x.Declaration.Variables.Select(v => v.Identifier.Text)),
            _ => member.Kind().ToString()
        };
        var parameters = member switch
        {
            BaseMethodDeclarationSyntax x => x.ParameterList.Parameters,
            TypeDeclarationSyntax x => x.ParameterList?.Parameters ?? default,
            DelegateDeclarationSyntax x => x.ParameterList.Parameters,
            IndexerDeclarationSyntax x => x.ParameterList.Parameters,
            _ => default
        };
        var owner = string.Join(".", member.Ancestors().OfType<BaseTypeDeclarationSyntax>().Reverse().Select(x => x.Identifier.Text));
        var signature = identifier + (parameters.Count == 0 ? "" : "(" + string.Join(",", parameters.Select(x => x.Type + " " + x.Identifier.Text)) + ")");
        var issues = new List<string>();
        var trivia = member.GetLeadingTrivia().Select(x => x.GetStructure()).OfType<DocumentationCommentTriviaSyntax>().ToArray();
        XElement? xml = null;
        try
        {
            var content = string.Join("\n", trivia.SelectMany(x => x.ToFullString().Split('\n')).Select(x => x.TrimStart().StartsWith("///") ? x.TrimStart()[3..] : x));
            xml = XElement.Parse("<doc>" + content + "</doc>");
        }
        catch (System.Xml.XmlException) { issues.Add("malformed XML"); }
        bool inherited = xml?.Element("inheritdoc") is not null;
        if (!inherited && string.IsNullOrWhiteSpace(xml?.Element("summary")?.Value)) issues.Add("summary");
        if (!inherited)
        {
            foreach (var parameter in parameters)
                if (xml?.Elements("param").Any(x => (string?)x.Attribute("name") == parameter.Identifier.Text && !string.IsNullOrWhiteSpace(x.Value)) != true)
                    issues.Add("param:" + parameter.Identifier.Text);
            if (member is MethodDeclarationSyntax method && method.ReturnType.ToString() != "void" && string.IsNullOrWhiteSpace(xml?.Element("returns")?.Value)) issues.Add("returns");
        }
        rows.Add(new { path, symbol = owner + "." + signature, line = tree.GetLineSpan(member.Span).StartLinePosition.Line + 1, issues });
    }
}
var destination = Path.GetFullPath(args.Length > 1 ? args[1] : "build/api-audit.json");
Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
File.WriteAllText(destination, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Audited {rows.Count} declarations: {destination}");

static bool Visible(MemberDeclarationSyntax member) =>
    member.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword) || x.IsKind(SyntaxKind.ProtectedKeyword)) ||
    (member.Parent is InterfaceDeclarationSyntax && !member.Modifiers.Any(x => x.IsKind(SyntaxKind.PrivateKeyword) || x.IsKind(SyntaxKind.InternalKeyword))) ||
    member is EnumMemberDeclarationSyntax;
