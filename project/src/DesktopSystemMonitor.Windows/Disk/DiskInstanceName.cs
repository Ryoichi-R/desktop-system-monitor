using System.Text.RegularExpressions;

namespace DesktopSystemMonitor.Windows.Disk;

public readonly record struct PhysicalDiskInstance(int DiskNumber, string InstanceName);

public static class DiskInstanceName
{
    private static readonly Regex CounterPathRegex = new(
        @"^(?:\\\\[^\\]+)?\\PhysicalDisk\((?<instance>[^)]+)\)\\",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex NumberRegex = new(
        @"^(?<number>\d+)(?:\s|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryParseCounterPath(string path, out PhysicalDiskInstance instance)
    {
        instance = default;
        Match pathMatch = CounterPathRegex.Match(path ?? string.Empty);
        if (!pathMatch.Success)
        {
            return false;
        }
        string name = pathMatch.Groups["instance"].Value;
        if (name.Equals("_Total", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        Match numberMatch = NumberRegex.Match(name);
        if (!numberMatch.Success || !int.TryParse(numberMatch.Groups["number"].Value, out int number))
        {
            return false;
        }
        instance = new PhysicalDiskInstance(number, name);
        return true;
    }
}
