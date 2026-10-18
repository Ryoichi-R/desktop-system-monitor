using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace DesktopSystemMonitor.App;

internal sealed record BuildInformation(string Version, string SourceId, string Dirty, string BuiltUtc, string Execution)
{
    internal const string ApplicationName = "Desktop System Monitor";
    internal static BuildInformation Current { get; } = Read(typeof(BuildInformation).Assembly,
        OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "macOS" : "不明",
        RuntimeInformation.ProcessArchitecture);

    internal IReadOnlyList<KeyValuePair<string, string>> Fields => new KeyValuePair<string, string>[]
    {
        new("アプリ名", ApplicationName),
        new("バージョン", Version),
        new("ソース識別子", SourceId),
        new("未コミット変更の有無", Dirty),
        new("ビルド日時", BuiltUtc),
        new("実行版", Execution),
    };

    internal string CopyText => string.Join(Environment.NewLine, Fields.Select(item => $"{item.Key}: {item.Value}"));

    internal static BuildInformation Read(Assembly assembly, string os, Architecture architecture)
    {
        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value, StringComparer.Ordinal);
        string? Value(string key) => metadata.GetValueOrDefault(key);
        var source = Value("DesktopSourceId");
        var dirty = Value("DesktopDirty") switch { "true" => "あり", "false" => "なし", _ => "不明" };
        var built = DateTimeOffset.TryParseExact(Value("DesktopBuiltUtc"), "yyyy-MM-dd'T'HH:mm:ss'Z'",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var utc)
            ? utc.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC (+00:00)'", CultureInfo.InvariantCulture) : "不明";
        var arch = architecture switch { Architecture.X64 => "x64", Architecture.Arm64 => "ARM64", _ => architecture.ToString() };
        return new(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "不明",
            string.IsNullOrEmpty(source) || source == "unknown" ? "不明" : source, dirty, built, $"{os} / {arch}");
    }
}
