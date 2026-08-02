using System.Text.Json;
using System.Text.Json.Nodes;
using DesktopSystemMonitor.Core.Settings;
using Xunit;

namespace DesktopSystemMonitor.Core.Tests.Settings;

public class SettingsStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public SettingsStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "dsm-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "settings.json");
    }

    [Fact]
    public void missing_file_returns_defaults()
    {
        var store = new FileSystemSettingsStore(_path);
        var settings = store.Load();
        Assert.Equal(AppSettings.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.Equal(1.0, settings.SamplingIntervalSeconds);
        Assert.True(settings.ClickThrough);
        Assert.Equal(WidgetDisplayMode.Standard, settings.DisplayMode);
        Assert.Equal(WindowLayerMode.AlwaysOnTop, settings.LayerMode);
        Assert.Equal("#FF5DCAA5", settings.RxAccentColor);
        Assert.Equal("#FFEF9F27", settings.TxAccentColor);
    }

    [Fact]
    public void save_then_load_round_trips_customized_values()
    {
        var store = new FileSystemSettingsStore(_path);
        var original = new AppSettings
        {
            SamplingIntervalSeconds = 2.0,
            UiScalePercent = 125,
            DisplayMode = WidgetDisplayMode.Reduced,
            LayerMode = WindowLayerMode.AlwaysOnTop,
            NetworkUnitSystem = DesktopSystemMonitor.Core.Formatting.RateUnitSystem.FixedKilobitsPerSecond,
            ClickThrough = false,
            StartWithWindows = true,
            RxAccentColor = "#FF112233",
            TxAccentColor = "#FF445566",
            BatteryChargeTargetPercent = 90,
            LearnedBatteryChargeTargetPercent = 80,
            BatteryChargeTargetCandidatePercent = 60,
            BackgroundEnabled = true,
            BackgroundOpacity = 0.42,
            BackgroundFillMode = BackgroundFillMode.EdgeFade,
            BackgroundEdgeFadePercent = 35,
            HideBackgroundBehindWindows = true,
            BackgroundColor = "#FF123456",
        }.Normalized();
        store.Save(original);

        var loaded = store.Load();
        Assert.Equal(original.SamplingIntervalSeconds, loaded.SamplingIntervalSeconds);
        Assert.Equal(original.UiScalePercent, loaded.UiScalePercent);
        Assert.Equal(WidgetDisplayMode.Reduced, loaded.DisplayMode);
        Assert.Equal(original.LayerMode, loaded.LayerMode);
        Assert.Equal(original.NetworkUnitSystem, loaded.NetworkUnitSystem);
        Assert.False(loaded.ClickThrough);
        Assert.True(loaded.StartWithWindows);
        Assert.Equal(original.RxAccentColor, loaded.RxAccentColor);
        Assert.Equal(original.TxAccentColor, loaded.TxAccentColor);
        Assert.Equal(90, loaded.BatteryChargeTargetPercent);
        Assert.Equal(80, loaded.LearnedBatteryChargeTargetPercent);
        Assert.Equal(60, loaded.BatteryChargeTargetCandidatePercent);
        Assert.True(loaded.BackgroundEnabled);
        Assert.Equal(0.42, loaded.BackgroundOpacity);
        Assert.Equal(BackgroundFillMode.EdgeFade, loaded.BackgroundFillMode);
        Assert.Equal(35, loaded.BackgroundEdgeFadePercent);
        Assert.True(loaded.HideBackgroundBehindWindows);
        Assert.Equal("#FF123456", loaded.BackgroundColor);
    }

    [Fact]
    public void legacy_argb_background_supplies_opacity_only_when_new_field_is_absent()
    {
        File.WriteAllText(_path, """{ "SchemaVersion": 1, "BackgroundColor": "#80112233" }""");
        var store = new FileSystemSettingsStore(_path);

        AppSettings loaded = store.Load();

        Assert.False(loaded.BackgroundEnabled);
        Assert.Null(loaded.BackgroundOpacity);
        Assert.Equal(BackgroundFillMode.Solid, loaded.BackgroundFillMode);
        Assert.Equal(22, loaded.BackgroundEdgeFadePercent);
        Assert.Equal("#112233", loaded.EffectiveBackgroundRgb);
        Assert.Equal(128d / 255d, loaded.EffectiveBackgroundOpacity, 10);
    }

    [Fact]
    public void schema_three_primary_and_backup_migrate_to_schema_four()
    {
        File.WriteAllText(_path, "{ \"SchemaVersion\": 3, \"DisplayMode\": \"Reduced\" }");
        var primaryStore = new FileSystemSettingsStore(_path);
        AppSettings primary = primaryStore.Load();

        Assert.False(primaryStore.IsReadOnly);
        Assert.Equal(WidgetDisplayMode.Reduced, primary.DisplayMode);
        Assert.Equal(AppSettings.CurrentSchemaVersion, primary.SchemaVersion);

        File.WriteAllText(_path, "{broken");
        File.WriteAllText(_path + ".bak", "{ \"SchemaVersion\": 3, \"FutureOption\": true }");
        var backupStore = new FileSystemSettingsStore(_path);
        AppSettings backup = backupStore.Load();

        Assert.False(backupStore.IsReadOnly);
        Assert.Equal(WidgetDisplayMode.Standard, backup.DisplayMode);
        Assert.Equal(AppSettings.CurrentSchemaVersion, backup.SchemaVersion);
    }

    [Fact]
    public void save_uses_atomic_replace_and_leaves_a_backup()
    {
        var store = new FileSystemSettingsStore(_path);
        store.Save(new AppSettings());
        store.Save(new AppSettings { SamplingIntervalSeconds = 3.0 });
        Assert.True(File.Exists(_path));
        Assert.True(File.Exists(_path + ".bak"));
    }

    [Fact]
    public void flush_failure_does_not_replace_the_primary_file()
    {
        const string original = """{ "SchemaVersion": 1, "Opacity": 0.75 }""";
        File.WriteAllText(_path, original);
        var store = new FileSystemSettingsStore(
            _path,
            _ => throw new IOException("injected flush failure"));

        Assert.Throws<IOException>(() => store.Save(new AppSettings { Opacity = 0.5 }));

        Assert.Equal(original, File.ReadAllText(_path));
        Assert.False(File.Exists(_path + ".bak"));
    }

    [Fact]
    public void corrupted_file_falls_back_to_defaults_without_throwing()
    {
        File.WriteAllText(_path, "{not json");
        var store = new FileSystemSettingsStore(_path);
        var loaded = store.Load();
        Assert.Equal(1.0, loaded.SamplingIntervalSeconds);
    }

    [Fact]
    public void sampling_interval_is_clamped_to_valid_range()
    {
        var store = new FileSystemSettingsStore(_path);
        store.Save(new AppSettings { SamplingIntervalSeconds = 0.1 });
        Assert.Equal(0.5, store.Load().SamplingIntervalSeconds);
        store.Save(new AppSettings { SamplingIntervalSeconds = 10 });
        Assert.Equal(5.0, store.Load().SamplingIntervalSeconds);
    }

    [Fact]
    public void unknown_numeric_enum_values_fall_back_to_defaults()
    {
        File.WriteAllText(
            _path,
            """{ "SchemaVersion": 1, "LayerMode": 999, "NetworkUnitSystem": 999 }""");
        var store = new FileSystemSettingsStore(_path);

        AppSettings loaded = store.Load();

        Assert.Equal(WindowLayerMode.AlwaysOnTop, loaded.LayerMode);
        Assert.Equal(DesktopSystemMonitor.Core.Formatting.RateUnitSystem.DecimalBytes, loaded.NetworkUnitSystem);
    }

    [Fact]
    public void save_preserves_unknown_fields_loaded_from_disk()
    {
        File.WriteAllText(_path, """{ "SchemaVersion": 1, "FutureOption": { "Enabled": true } }""");
        var store = new FileSystemSettingsStore(_path);
        AppSettings loaded = store.Load();
        store.Save(loaded with { Opacity = 0.75 });

        string json = File.ReadAllText(_path);
        Assert.Contains("FutureOption", json, StringComparison.Ordinal);
        Assert.Contains("Enabled", json, StringComparison.Ordinal);
    }

    [Fact]
    public void temperature_overrides_round_trip_false_and_explicit_null()
    {
        var store = new FileSystemSettingsStore(_path);
        store.Save(new AppSettings
        {
            ShowTemperatures = true,
            ShowCpuTemperature = false,
            ShowGpuTemperature = null,
        });

        AppSettings loaded = store.Load();
        string json = File.ReadAllText(_path);

        Assert.False(loaded.EffectiveShowCpuTemperature);
        Assert.True(loaded.EffectiveShowGpuTemperature);
        Assert.Contains("\"ShowCpuTemperature\": false", json, StringComparison.Ordinal);
        Assert.Contains("\"ShowGpuTemperature\": null", json, StringComparison.Ordinal);
    }

    [Fact]
    public void legacy_temperature_only_json_is_inherited_by_both_overrides()
    {
        File.WriteAllText(_path, """{ "SchemaVersion": 1, "ShowTemperatures": true, "FutureOption": 7 }""");
        var store = new FileSystemSettingsStore(_path);

        AppSettings loaded = store.Load();
        store.Save(loaded with { ShowCpuTemperature = true, ShowGpuTemperature = false });
        string json = File.ReadAllText(_path);

        Assert.True(loaded.EffectiveShowCpuTemperature);
        Assert.True(loaded.EffectiveShowGpuTemperature);
        Assert.Contains("FutureOption", json, StringComparison.Ordinal);
        Assert.Contains("\"ShowGpuTemperature\": false", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("red", "#FFF3F3F3")]
    [InlineData("#abcdef", "#ABCDEF")]
    [InlineData("#80aabbcc", "#80AABBCC")]
    public void color_settings_are_normalized(string input, string expected)
    {
        Assert.Equal(expected, new AppSettings { ForegroundColor = input }.Normalized().ForegroundColor);
    }

    [Fact]
    public void arrow_colors_use_independent_fallbacks()
    {
        AppSettings settings = new AppSettings
        {
            RxAccentColor = "invalid",
            TxAccentColor = "#80abcdef",
        }.Normalized();

        Assert.Equal("#FF5DCAA5", settings.RxAccentColor);
        Assert.Equal("#80ABCDEF", settings.TxAccentColor);
    }

    [Fact]
    public void future_schema_is_never_overwritten_by_this_version()
    {
        const string futureJson = """{ "SchemaVersion": 42, "FutureOption": "keep-me" }""";
        File.WriteAllText(_path, futureJson);
        var store = new FileSystemSettingsStore(_path);

        AppSettings defaults = store.Load();
        Assert.True(store.IsReadOnly);
        Assert.Equal(AppSettings.CurrentSchemaVersion, defaults.SchemaVersion);
        Assert.Throws<InvalidOperationException>(() => store.Save(defaults with { Opacity = 0.5 }));
        Assert.Equal(futureJson, File.ReadAllText(_path));
    }

    [Fact]
    public void future_schema_with_unknown_enum_is_read_only_not_corrupt()
    {
        const string futureJson =
            """{ "SchemaVersion": 5, "LayerMode": "FutureDesktopLayer", "FutureOption": "keep-me" }""";
        File.WriteAllText(_path, futureJson);
        var store = new FileSystemSettingsStore(_path);

        _ = store.Load();

        Assert.True(store.IsReadOnly);
        Assert.Equal(futureJson, File.ReadAllText(_path));
    }

    [Fact]
    public void future_schema_backup_also_makes_store_read_only()
    {
        File.WriteAllText(_path, "{broken");
        File.WriteAllText(_path + ".bak", """{ "SchemaVersion": 42, "FutureOption": true }""");
        var store = new FileSystemSettingsStore(_path);

        AppSettings defaults = store.Load();
        Assert.Throws<InvalidOperationException>(() => store.Save(defaults));
        Assert.Equal("{broken", File.ReadAllText(_path));
    }

    [Fact]
    public void pre_feature_writer_preserves_unknown_battery_learning_fields()
    {
        const string original =
            """{ "SchemaVersion": 1, "Opacity": 1.0, "BatteryChargeTargetPercent": 90, "LearnedBatteryChargeTargetPercent": 80, "BatteryChargeTargetCandidatePercent": 60 }""";
        JsonObject preservedRoot = JsonNode.Parse(original)!.AsObject();
        PreFeatureSettings loaded = preservedRoot.Deserialize<PreFeatureSettings>()!;
        JsonObject fieldsKnownToOldVersion = JsonSerializer.SerializeToNode(
            loaded with { Opacity = 0.75 })!.AsObject();
        foreach ((string key, JsonNode? value) in fieldsKnownToOldVersion)
        {
            preservedRoot[key] = value?.DeepClone();
        }
        string json = preservedRoot.ToJsonString();

        JsonObject saved = JsonNode.Parse(json)!.AsObject();
        Assert.Equal(90, saved["BatteryChargeTargetPercent"]!.GetValue<int>());
        Assert.Equal(80, saved["LearnedBatteryChargeTargetPercent"]!.GetValue<int>());
        Assert.Equal(60, saved["BatteryChargeTargetCandidatePercent"]!.GetValue<int>());
        Assert.Equal(0.75, saved["Opacity"]!.GetValue<double>());
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    private sealed record PreFeatureSettings
    {
        public int SchemaVersion { get; init; }
        public double Opacity { get; init; }
    }
}
