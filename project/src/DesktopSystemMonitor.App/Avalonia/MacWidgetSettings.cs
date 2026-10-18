using System.Text.Json;
using DesktopSystemMonitor.Core.Settings;

namespace DesktopSystemMonitor.App;

internal sealed record MacWidgetSettings
{
    public int CpuPeakWindowSeconds { get; init; } = 60;
    public int NetworkPeakWindowSeconds { get; init; } = 60;
    public int Version { get; init; } = 1;
    public double Scale { get; init; } = 1;
    public WidgetDisplayMode DisplayMode { get; init; } = WidgetDisplayMode.Standard;
    public bool DiagnosticLogging { get; init; }
    public bool HideWhenFullScreen { get; init; } = true;
    public bool Topmost { get; init; }
    public bool DesktopLayer { get; init; }
    public bool ClickThrough { get; init; }
    public bool StartAtLogin { get; init; }
    public int? X { get; init; }
    public int? Y { get; init; }

    internal MacWidgetSettings Normalize() => this with
    {
        CpuPeakWindowSeconds = Math.Clamp(CpuPeakWindowSeconds, 10, 60),
        NetworkPeakWindowSeconds = Math.Clamp(NetworkPeakWindowSeconds, 10, 60),
        Scale = double.IsFinite(Scale) && Scale is >= 0.5 and <= 1.5 ? Math.Round(Scale * 100, MidpointRounding.AwayFromZero) / 100 : 1,
        DisplayMode = Enum.IsDefined(DisplayMode) ? DisplayMode : WidgetDisplayMode.Standard,
        Topmost = DesktopLayer ? false : Topmost,
        X = X is >= -100000 and <= 100000 && Y is >= -100000 and <= 100000 ? X : null,
        Y = X is >= -100000 and <= 100000 && Y is >= -100000 and <= 100000 ? Y : null,
    };
}

internal interface IMacWidgetSettingsStore
{
    MacWidgetSettings Load();
    void Save(MacWidgetSettings settings);
}

internal sealed class MacWidgetSettingsStore(string path) : IMacWidgetSettingsStore
{
    internal static MacWidgetSettingsStore CreateDefault() => new(new DesktopSystemMonitor.Mac.MacAppPathProvider().SettingsFilePath);

    public MacWidgetSettings Load()
    {
        if (!File.Exists(path)) return new();
        if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Settings file is too large.");
        var settings = JsonSerializer.Deserialize<MacWidgetSettings>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Settings must be an object.");
        if (settings.Version != 1) throw new InvalidDataException("Unsupported settings version.");
        return settings.Normalize();
    }

    public void Save(MacWidgetSettings settings)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".mac-widget-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings.Normalize()));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
