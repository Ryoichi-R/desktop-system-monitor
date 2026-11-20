using System.Text.Json;
using System.Text.Json.Nodes;

namespace DesktopSystemMonitor.Core.Settings;

/// <summary>
/// Migrates a raw settings JSON blob forward across schema versions. Unknown
/// fields are preserved so that a future version can round-trip through an
/// older client without losing user-entered values.
/// </summary>
public static class SettingsMigrator
{
    // Adding a serialized enum value changes the schema vocabulary. Such a
    // change must increment CurrentSchemaVersion and include a migration so an
    // older client recognizes the document as future/read-only before enum
    // deserialization.
    public static JsonNode Migrate(JsonNode root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (root is not JsonObject obj)
        {
            throw new InvalidDataException("Settings root must be a JSON object.");
        }

        int version = 0;
        foreach ((string key, JsonNode? value) in obj.ToArray())
        {
            if (!KnownKeys.TryGetValue(key, out string? canonical)) continue;
            if (canonical == nameof(AppSettings.SchemaVersion))
            {
                if (value is not JsonValue v || !v.TryGetValue<int>(out int parsed) || parsed < 0)
                    throw new InvalidDataException("Invalid settings schema version.");
                if (parsed > AppSettings.CurrentSchemaVersion)
                    throw new UnsupportedSettingsVersionException(parsed, AppSettings.CurrentSchemaVersion);
                version = parsed;
            }
        }
        // Canonicalize before adding migration defaults. For differently cased
        // aliases the final value in document order wins, as in deserialization.
        foreach (var (key, value) in obj.ToArray())
        {
            if (!KnownKeys.TryGetValue(key, out string? canonical)) continue;
            obj.Remove(key);
            obj[canonical] = value?.DeepClone();
        }

        while (version < AppSettings.CurrentSchemaVersion)
        {
            version = version switch
            {
                0 => MigrateFromV0(obj),
                1 => MigrateFromV1(obj),
                2 => MigrateFromV2(obj),
                3 => MigrateFromV3(obj),
                4 => MigrateFromV4(obj),
                _ => throw new InvalidDataException($"No migration path from schema version {version}."),
            };
        }

        obj["SchemaVersion"] = version;
        return obj;
    }

    // Check every root declaration before duplicate rejection or deserialization.
    internal static void CheckVersionDeclarations(ReadOnlySpan<byte> json)
    {
        var reader = new Utf8JsonReader(json);
        while (reader.Read())
        {
            if (reader.CurrentDepth != 1 || reader.TokenType != JsonTokenType.PropertyName
                || !string.Equals(reader.GetString(), nameof(AppSettings.SchemaVersion), StringComparison.OrdinalIgnoreCase))
                continue;
            if (reader.Read() && reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int version)
                && version > AppSettings.CurrentSchemaVersion)
                throw new UnsupportedSettingsVersionException(version, AppSettings.CurrentSchemaVersion);
        }
    }

    private static int MigrateFromV0(JsonObject obj)
    {
        obj.TryAdd("SamplingIntervalSeconds", 1.0);
        obj.TryAdd("UiScalePercent", 100.0);
        obj.TryAdd("Opacity", 1.0);
        obj.TryAdd("LayerMode", nameof(WindowLayerMode.AlwaysOnTop));
        obj.TryAdd("ClickThrough", true);
        obj.TryAdd("AutoHideOnFullScreen", true);
        obj.TryAdd("StartWithWindows", false);
        obj.TryAdd("DiagnosticLoggingEnabled", false);
        obj.TryAdd("NetworkUnitSystem", nameof(Formatting.RateUnitSystem.DecimalBytes));
        obj.TryAdd("FontFamilyName", "Segoe UI Variable Text");
        obj.TryAdd("ForegroundColor", "#FFF3F3F3");
        obj.TryAdd("MutedColor", "#FFB0B0B0");
        obj.TryAdd("AccentColor", "#FF7DC8FF");
        obj.TryAdd("RxAccentColor", "#FF5DCAA5");
        obj.TryAdd("TxAccentColor", "#FFEF9F27");
        obj.TryAdd("BackgroundColor", "#B0000000");
        obj.TryAdd("SelectedNetworkAdapterLuids", new JsonArray());
        obj.TryAdd("BatteryChargeTargetPercent", null);
        obj.TryAdd("LearnedBatteryChargeTargetPercent", null);
        obj.TryAdd("BatteryChargeTargetCandidatePercent", null);
        return 1;
    }

    private static int MigrateFromV1(JsonObject obj)
    {
        obj.TryAdd("BackgroundFillMode", nameof(BackgroundFillMode.Solid));
        obj.TryAdd("BackgroundEdgeFadePercent", 22);
        return 2;
    }

    private static int MigrateFromV2(JsonObject obj)
    {
        bool hasSavedPosition = obj["SavedRightEdgeDip"] is not null
            && obj["SavedTopEdgeDip"] is not null;
        obj.TryAdd(
            "PlacementMode",
            hasSavedPosition ? nameof(WindowPlacementMode.Custom) : nameof(WindowPlacementMode.Preset));
        obj.TryAdd("PlacementAnchor", nameof(WindowPlacementAnchor.TopRight));
        obj.TryAdd("HorizontalMarginDip", 8);
        obj.TryAdd("VerticalMarginDip", 8);
        return 3;
    }

    private static int MigrateFromV3(JsonObject obj)
    {
        obj.TryAdd(nameof(AppSettings.DisplayMode), nameof(WidgetDisplayMode.Standard));
        return 4;
    }

    private static int MigrateFromV4(JsonObject obj) => 5;

    internal static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private static readonly Dictionary<string, string> KnownKeys =
        JsonSerializer.SerializeToNode(new AppSettings(), SerializerOptions)!.AsObject()
            .ToDictionary(property => property.Key, property => property.Key, StringComparer.OrdinalIgnoreCase);
}

public sealed class UnsupportedSettingsVersionException(int foundVersion, int supportedVersion)
    : Exception($"Settings schema version {foundVersion} is newer than this build supports ({supportedVersion}).")
{
    public int FoundVersion { get; } = foundVersion;
    public int SupportedVersion { get; } = supportedVersion;
}
