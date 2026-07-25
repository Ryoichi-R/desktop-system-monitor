using System.Text.RegularExpressions;

namespace DesktopSystemMonitor.Windows.Disk;

public readonly record struct PhysicalDiskInstance(int DiskNumber, string InstanceName);

public static partial class DiskInstanceName
{
    [GeneratedRegex(@"^(?:\\\\[^\\]+)?\\PhysicalDisk\((?<instance>[^)]+)\)\\", RegexOptions.CultureInvariant)]
    private static partial Regex CounterPathRegex();

    [GeneratedRegex(@"^(?<number>\d+)(?:\s|$)", RegexOptions.CultureInvariant)]
    private static partial Regex NumberRegex();

    public static bool TryParseCounterPath(string path, out PhysicalDiskInstance instance)
    {
        instance = default;
        Match pathMatch = CounterPathRegex().Match(path ?? string.Empty);
        if (!pathMatch.Success)
        {
            return false;
        }
        string name = pathMatch.Groups["instance"].Value;
        if (name.Equals("_Total", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        Match numberMatch = NumberRegex().Match(name);
        if (!numberMatch.Success || !int.TryParse(numberMatch.Groups["number"].Value, out int number))
        {
            return false;
        }
        instance = new PhysicalDiskInstance(number, name);
        return true;
    }
}
