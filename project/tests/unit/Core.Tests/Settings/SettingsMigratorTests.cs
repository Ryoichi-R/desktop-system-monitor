using System.Text.Json.Nodes;
using System.Reflection;
using DesktopSystemMonitor.Core.Formatting;
using DesktopSystemMonitor.Core.Settings;
using Xunit;

namespace DesktopSystemMonitor.Core.Tests.Settings;

public class SettingsMigratorTests
{
    [Fact]
    public void v0_document_gets_defaults_and_current_version()
    {
        var node = JsonNode.Parse("{}")!;
        var migrated = SettingsMigrator.Migrate(node);
        Assert.Equal(AppSettings.CurrentSchemaVersion, migrated["SchemaVersion"]!.GetValue<int>());
        Assert.NotNull(migrated["SamplingIntervalSeconds"]);
        Assert.Equal(nameof(WindowLayerMode.AlwaysOnTop), migrated["LayerMode"]!.GetValue<string>());
        Assert.Equal("#FF5DCAA5", migrated["RxAccentColor"]!.GetValue<string>());
        Assert.Equal("#FFEF9F27", migrated["TxAccentColor"]!.GetValue<string>());
    }

    [Fact]
    public void unknown_fields_survive_migration()
    {
        var node = JsonNode.Parse("""{ "MysteriousFutureField": 123 }""")!;
        var migrated = SettingsMigrator.Migrate(node);
        Assert.Equal(123, migrated["MysteriousFutureField"]!.GetValue<int>());
    }

    [Fact]
    public void unknown_schema_version_throws()
    {
        var node = JsonNode.Parse("""{ "SchemaVersion": 42 }""")!;
        Assert.Throws<UnsupportedSettingsVersionException>(() => SettingsMigrator.Migrate(node));
    }

    [Fact]
    public void explicit_v0_layer_mode_is_preserved()
    {
        var node = JsonNode.Parse("""{ "LayerMode": "OnDesktop" }""")!;

        JsonNode migrated = SettingsMigrator.Migrate(node);

        Assert.Equal(nameof(WindowLayerMode.OnDesktop), migrated["LayerMode"]!.GetValue<string>());
    }

    [Fact]
    public void v1_document_gets_solid_background_defaults()
    {
        var node = JsonNode.Parse("""{ "SchemaVersion": 1, "BackgroundColor": "#80112233" }""")!;

        JsonNode migrated = SettingsMigrator.Migrate(node);

        Assert.Equal(4, migrated["SchemaVersion"]!.GetValue<int>());
        Assert.Equal(nameof(BackgroundFillMode.Solid), migrated["BackgroundFillMode"]!.GetValue<string>());
        Assert.Equal(22, migrated["BackgroundEdgeFadePercent"]!.GetValue<int>());
        Assert.Equal("#80112233", migrated["BackgroundColor"]!.GetValue<string>());
    }

    [Fact]
    public void schema_four_serialized_enum_vocabulary_is_explicit()
    {
        var expected = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [nameof(AppSettings.BackgroundFillMode)] = ["Solid", "EdgeFade"],
            [nameof(AppSettings.LayerMode)] = ["OnDesktop", "AlwaysOnTop", "Normal"],
            [nameof(AppSettings.PlacementMode)] = ["Preset", "Custom"],
            [nameof(AppSettings.PlacementAnchor)] = ["TopRight", "BottomRight", "TopLeft", "BottomLeft"],
            [nameof(AppSettings.NetworkUnitSystem)] = [
                "DecimalBytes",
                "DecimalBits",
                "FixedKilobitsPerSecond",
            ],
            [nameof(AppSettings.DisplayMode)] = ["Standard", "Reduced"],
        };
        Dictionary<string, string[]> actual = typeof(AppSettings)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.SetMethod is not null && property.PropertyType.IsEnum)
            .ToDictionary(
                property => property.Name,
                property => Enum.GetNames(property.PropertyType),
                StringComparer.Ordinal);

        Assert.Equal(expected.Keys.Order(), actual.Keys.Order());
        foreach ((string property, string[] vocabulary) in expected)
        {
            Assert.Equal(vocabulary, actual[property]);
        }
        Assert.Equal(4, AppSettings.CurrentSchemaVersion);
        Assert.Equal(Enum.GetNames<RateUnitSystem>(), expected[nameof(AppSettings.NetworkUnitSystem)]);
    }

    [Theory]
    [InlineData("{ \"SchemaVersion\": 2 }", WindowPlacementMode.Preset)]
    [InlineData("{ \"SchemaVersion\": 2, \"SavedRightEdgeDip\": 1200, \"SavedTopEdgeDip\": 40 }", WindowPlacementMode.Custom)]
    public void v2_document_preserves_whether_a_custom_position_existed(
        string json,
        WindowPlacementMode expectedMode)
    {
        JsonNode migrated = SettingsMigrator.Migrate(JsonNode.Parse(json)!);

        Assert.Equal(4, migrated["SchemaVersion"]!.GetValue<int>());
        Assert.Equal(expectedMode.ToString(), migrated["PlacementMode"]!.GetValue<string>());
        Assert.Equal(nameof(WindowPlacementAnchor.TopRight), migrated["PlacementAnchor"]!.GetValue<string>());
        Assert.Equal(8, migrated["HorizontalMarginDip"]!.GetValue<int>());
        Assert.Equal(8, migrated["VerticalMarginDip"]!.GetValue<int>());
    }

    [Fact]
    public void schema_three_document_gets_standard_display_mode()
    {
        JsonNode migrated = SettingsMigrator.Migrate(JsonNode.Parse("{ \"SchemaVersion\": 3, \"FutureOption\": true }")!);

        Assert.Equal(4, migrated["SchemaVersion"]!.GetValue<int>());
        Assert.Equal(nameof(WidgetDisplayMode.Standard), migrated["DisplayMode"]!.GetValue<string>());
        Assert.True(migrated["FutureOption"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void every_previous_schema_version_reaches_current_schema(int version)
    {
        JsonNode migrated = SettingsMigrator.Migrate(JsonNode.Parse($"{{ \"SchemaVersion\": {version} }}")!);

        Assert.Equal(AppSettings.CurrentSchemaVersion, migrated["SchemaVersion"]!.GetValue<int>());
    }
}
