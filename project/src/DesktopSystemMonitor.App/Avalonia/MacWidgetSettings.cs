using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalSettings { get; init; }

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

internal enum MacSettingsStatus { Normal, Missing, Corrupt, Inaccessible, FutureVersion, Recoverable }

internal sealed record MacSettingsRecovery(string? MainHash, string BackupHash);
internal sealed record MacSettingsRead(MacSettingsStatus Status, MacWidgetSettings? Settings = null, MacSettingsRecovery? Recovery = null);

internal sealed class MacWidgetSettingsStore(string path) : IMacWidgetSettingsStore
{
    private const int MaximumSize = 65536;
    private static readonly JsonSerializerOptions Options = new() { AllowDuplicateProperties = false };
    private string BackupPath => path + ".bak";

    internal static MacWidgetSettingsStore CreateDefault() => new(new DesktopSystemMonitor.Mac.MacAppPathProvider().SettingsFilePath);

    public MacWidgetSettings Load() => Inspect() switch
    {
        { Status: MacSettingsStatus.Normal, Settings: { } settings } => settings,
        { Status: MacSettingsStatus.Missing } => new(),
        _ => throw new InvalidDataException("Settings are protected from automatic replacement."),
    };

    internal MacSettingsRead Inspect()
    {
        try
        {
            var main = Read(path);
            if (main.Status is MacSettingsStatus.Missing or MacSettingsStatus.Corrupt)
            {
                var backup = Read(BackupPath);
                if (backup is { Status: MacSettingsStatus.Normal, Hash: { } hash })
                    return new(MacSettingsStatus.Recoverable, Recovery: new(main.Hash, hash));
            }
            return new(main.Status, main.Settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(MacSettingsStatus.Inaccessible);
        }
    }

    public void Save(MacWidgetSettings settings)
    {
        if (settings.Version != 1) throw new InvalidDataException("Cannot save an unsupported settings version.");
        var main = Read(path);
        if (main.Status != MacSettingsStatus.Normal &&
            (main.Status != MacSettingsStatus.Missing || Read(BackupPath).Status == MacSettingsStatus.Normal))
            throw new IOException("Settings must be recovered before saving.");

        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(settings.Normalize(), Options);
        if (bytes.Length > MaximumSize) throw new IOException("Settings file would exceed the size limit.");
        if (main.Bytes is { } old && old.AsSpan().SequenceEqual(bytes)) return;

        string temporary = Stage(bytes);
        try
        {
            RequireHash(path, main.Hash);
            if (main.Bytes is { } validMain) Replace(BackupPath, validMain);
            RequireHash(path, main.Hash);
            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    internal MacWidgetSettings Recover(MacSettingsRecovery request)
    {
        var main = Read(path);
        var backup = Read(BackupPath);
        if (main.Status is not (MacSettingsStatus.Missing or MacSettingsStatus.Corrupt) ||
            main.Hash != request.MainHash || backup.Hash != request.BackupHash ||
            backup is not { Status: MacSettingsStatus.Normal, Bytes: { } bytes, Settings: { } settings })
            throw new IOException("Settings or backup changed after recovery was offered.");

        string temporary = Stage(bytes);
        try
        {
            if (main.Hash is { } originalHash)
            {
                string archive = path + $".corrupt-{Guid.NewGuid():N}";
                File.Copy(path, archive, overwrite: false);
                RequireHash(archive, originalHash);
            }
            RequireHash(path, main.Hash);
            RequireHash(BackupPath, backup.Hash);
            File.Move(temporary, path, overwrite: true);
            return settings;
        }
        finally { File.Delete(temporary); }
    }

    private static string? HashFile(string file)
    {
        try
        {
            using var stream = File.OpenRead(file);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return null; }
    }

    private static void RequireHash(string file, string? expected)
    {
        if (HashFile(file) != expected) throw new IOException("Settings changed during the operation.");
    }

    private string Stage(byte[] bytes)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".mac-widget-{Guid.NewGuid():N}.tmp");
        try
        {
            using var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
            return temporary;
        }
        catch
        {
            File.Delete(temporary);
            throw;
        }
    }

    private void Replace(string destination, byte[] bytes)
    {
        string temporary = Stage(bytes);
        try { File.Move(temporary, destination, overwrite: true); }
        finally { File.Delete(temporary); }
    }

    private static (MacSettingsStatus Status, string? Hash, byte[]? Bytes, MacWidgetSettings? Settings) Read(string file)
    {
        FileStream stream;
        try { stream = File.OpenRead(file); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        { return (MacSettingsStatus.Missing, null, null, null); }

        using (stream)
        {
            if (stream.Length > MaximumSize)
                return (MacSettingsStatus.Corrupt, Convert.ToHexString(SHA256.HashData(stream)), null, null);
            byte[] bytes = new byte[MaximumSize + 1];
            int count = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
            if (count > MaximumSize)
            {
                stream.Position = 0;
                return (MacSettingsStatus.Corrupt, Convert.ToHexString(SHA256.HashData(stream)), null, null);
            }
            Array.Resize(ref bytes, count);
            string hash = Convert.ToHexString(SHA256.HashData(bytes));
            try
            {
                ReadOnlySpan<byte> json = bytes;
                if (json.StartsWith((ReadOnlySpan<byte>)[0xef, 0xbb, 0xbf])) json = json[3..];
                // Inspect the version before deserializing fields which may change in future versions.
                using var document = JsonDocument.Parse(json.ToArray());
                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    document.RootElement.TryGetProperty(nameof(MacWidgetSettings.Version), out var version) &&
                    version.ValueKind == JsonValueKind.Number && version.GetDouble() > 1)
                    return (MacSettingsStatus.FutureVersion, hash, null, null);
                var settings = JsonSerializer.Deserialize<MacWidgetSettings>(json, Options);
                if (settings?.Version == 1) return (MacSettingsStatus.Normal, hash, bytes, settings.Normalize());
            }
            catch (JsonException) { }
            return (MacSettingsStatus.Corrupt, hash, null, null);
        }
    }
}
