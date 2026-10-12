using System.Text;

namespace DesktopSystemMonitor.App.Diagnostics;

/// <summary>
/// Carries placement and window-size measurements to <see cref="DiagnosticLog"/>.
/// Unlike ordinary exception messages, this text is written to the opt-in log,
/// so callers compose it only from numbers, fixed field names, enum values, and
/// monitor device names. Every other character is replaced and the text is
/// length-limited before it reaches the log.
/// </summary>
internal sealed class PlacementDiagnosticDetail : Exception
{
    internal const int MaximumLength = 1024;

    public PlacementDiagnosticDetail()
        : this(string.Empty)
    {
    }

    public PlacementDiagnosticDetail(string detail)
        : base(Sanitize(detail))
    {
    }

    public PlacementDiagnosticDetail(string detail, Exception innerException)
        : base(Sanitize(detail), innerException)
    {
    }

    internal string Detail => Message;

    internal static string Sanitize(string? detail)
    {
        if (string.IsNullOrEmpty(detail))
        {
            return string.Empty;
        }
        int length = Math.Min(detail.Length, MaximumLength);
        var builder = new StringBuilder(length);
        for (int index = 0; index < length; index++)
        {
            char value = detail[index];
            builder.Append(IsAllowed(value) ? value : '?');
        }
        return builder.ToString();
    }

    private static bool IsAllowed(char value) =>
        char.IsAsciiLetterOrDigit(value)
        || value is ' ' or '.' or ',' or ';' or ':' or '=' or '(' or ')' or '_' or '-' or '+' or '\\';
}
