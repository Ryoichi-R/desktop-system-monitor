using System.Xml;
using System.Xml.Linq;
using DesktopSystemMonitor.Core.Platform;

namespace DesktopSystemMonitor.Mac;

/// <summary>
/// macOSのLaunchAgentを用いたログイン時自動起動。plistは固定ラベル・固定レイアウトで
/// 生成し、標準出力と標準エラーは必ず/dev/nullへ送る。
/// </summary>
public sealed class MacStartupRegistry : IStartupRegistry
{
    internal const string Label = "local.desktop-system-monitor";
    private readonly string _plistPath;

    public MacStartupRegistry() : this(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) { }

    internal MacStartupRegistry(string userHome)
    {
        if (string.IsNullOrWhiteSpace(userHome) || !Path.IsPathRooted(userHome))
        {
            throw new ArgumentException("A fully qualified user home path is required.", nameof(userHome));
        }

        _plistPath = Path.Combine(userHome, "Library", "LaunchAgents", Label + ".plist");
    }

    internal string PlistPath => _plistPath;

    public bool IsEnabled(string executablePath)
    {
        if (!TryGetAbsolutePath(executablePath, out string? expectedExecutable) || !File.Exists(_plistPath))
        {
            return false;
        }

        try
        {
            XDocument document = XDocument.Load(_plistPath, LoadOptions.PreserveWhitespace);
            XElement? dictionary = document.Root?.Element("dict");
            if (dictionary is null) return false;
            return ReadString(dictionary, "Label") == Label &&
                ReadStringArrayFirst(dictionary, "ProgramArguments") == expectedExecutable &&
                ReadBoolean(dictionary, "RunAtLoad") &&
                ReadString(dictionary, "StandardOutPath") == "/dev/null" &&
                ReadString(dictionary, "StandardErrorPath") == "/dev/null";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException)
        {
            return false;
        }
    }

    public void Enable(string executablePath)
    {
        if (!TryGetAbsolutePath(executablePath, out string? absoluteExecutable))
        {
            throw new ArgumentException("The startup executable path must be absolute.", nameof(executablePath));
        }
        if (!File.Exists(absoluteExecutable))
        {
            throw new FileNotFoundException("The startup executable does not exist.", absoluteExecutable);
        }

        string directory = Path.GetDirectoryName(_plistPath)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".{Label}.{Guid.NewGuid():N}.tmp");
        try
        {
            BuildPlist(absoluteExecutable).Save(temporary, SaveOptions.DisableFormatting);
            File.Move(temporary, _plistPath, overwrite: true);
            // launchd discovers this plist at the next login. Bootstrapping a RunAtLoad
            // agent here would start a second monitor while this instance is running.
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public void Disable()
    {
        if (!File.Exists(_plistPath)) return;
        // Do not bootout: it can terminate this very process when launched at login.
        File.Delete(_plistPath);
    }

    private static XDocument BuildPlist(string executablePath) => new(
        new XDeclaration("1.0", "UTF-8", null),
        new XDocumentType("plist", "-//Apple//DTD PLIST 1.0//EN", "http://www.apple.com/DTDs/PropertyList-1.0.dtd", null),
        new XElement("plist", new XAttribute("version", "1.0"),
            new XElement("dict",
                new XElement("key", "Label"), new XElement("string", Label),
                new XElement("key", "ProgramArguments"), new XElement("array", new XElement("string", executablePath)),
                new XElement("key", "RunAtLoad"), new XElement("true"),
                new XElement("key", "StandardOutPath"), new XElement("string", "/dev/null"),
                new XElement("key", "StandardErrorPath"), new XElement("string", "/dev/null"))));

    private static string? ReadString(XElement dictionary, string key)
    {
        XElement? keyElement = dictionary.Elements("key").FirstOrDefault(element => element.Value == key);
        return keyElement?.NodesAfterSelf().OfType<XElement>().FirstOrDefault()?.Name.LocalName == "string"
            ? keyElement.NodesAfterSelf().OfType<XElement>().First().Value
            : null;
    }

    private static string? ReadStringArrayFirst(XElement dictionary, string key)
    {
        XElement? keyElement = dictionary.Elements("key").FirstOrDefault(element => element.Value == key);
        XElement? array = keyElement?.NodesAfterSelf().OfType<XElement>().FirstOrDefault(element => element.Name == "array");
        return array?.Element("string")?.Value;
    }

    private static bool ReadBoolean(XElement dictionary, string key)
    {
        XElement? keyElement = dictionary.Elements("key").FirstOrDefault(element => element.Value == key);
        return keyElement?.NodesAfterSelf().OfType<XElement>().FirstOrDefault()?.Name == "true";
    }

    private static bool TryGetAbsolutePath(string? path, out string? absolute)
    {
        absolute = null;
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) return false;
        absolute = Path.GetFullPath(path);
        return true;
    }

}
