namespace OrbitMesh.Server;

public static class LogSanitizerExtensions
{
    private static readonly char[] LineBreakChars = ['\r', '\n', '\u2028', '\u2029'];

    /// <summary>Escapes characters a line-based log sink would treat as a line break, so a value taken
    /// from user input (a header, query string, or request path) can't forge fake-looking extra log
    /// entries (CRLF/line-separator injection - see CodeQL cs/log-forging).</summary>
    public static string? ForLog(this string? value) =>
        value == null || value.IndexOfAny(LineBreakChars) < 0
            ? value
            : value.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\u2028", "\\u2028").Replace("\u2029", "\\u2029");
}
