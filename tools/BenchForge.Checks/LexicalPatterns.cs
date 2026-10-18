using System.Text.RegularExpressions;

// Exact lexical patterns retained from the reference preflight.
internal static class LexicalPatterns
{
    internal static readonly Regex DECL = new("\\b(public|protected)\\s+(?:(?:static|sealed|abstract|partial|async|override|virtual|new|readonly|required|unsafe|ref|extern)\\s+)*(?:(class|struct|interface|enum|record|delegate)\\s+(\\w+)|(?:[\\w<>,?.\\[\\] ]+?)\\s+(\\w+)\\s*\\(([^;{}()]*)\\)\\s*(?:=>|\\{|;)|[\\w<>,?.\\[\\] ]+?\\s+(\\w+)\\s*(?:\\{|=>))", RegexOptions.CultureInvariant | RegexOptions.Singleline);
    internal static readonly Regex LITERALS = new("//[^\\n]*|/\\*[\\s\\S]*?\\*/|@\\\"(?:[^\\\"]|\\\"\\\")*\\\"|\\\"(?:\\\\.|[^\\\"\\\\])*\\\"|'(?:\\\\.|[^'\\\\])*'", RegexOptions.CultureInvariant | RegexOptions.Singleline);
    internal static readonly Regex PARAM = new("<param\\s+name=[\"\\']([^\"\\']+)[\"\\']\\s*>\\s*(.*?)\\s*</param>", RegexOptions.CultureInvariant | RegexOptions.Singleline);
    internal static readonly Regex RECORD_PRIMARY = new("(?m)^[ \\t]*(?:public|protected)\\s+(?:(?:sealed|abstract|partial|readonly)\\s+)*record\\s+(?:(?:class|struct)\\s+)?(?<name>\\w+)\\s*\\((?<params>[^{};()]*)\\)", RegexOptions.CultureInvariant | RegexOptions.Singleline);
    internal static readonly Regex CONSTRUCTOR = new("(?m)^[ \\t]*(?:public|protected)\\s+(?<name>[A-Za-z_]\\w*)\\s*\\((?<params>[^{};()]*)\\)\\s*(?::[^{};]*?)?(?:\\{|=>|;)", RegexOptions.CultureInvariant | RegexOptions.Singleline);
}
