using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;

namespace DesktopSystemMonitor.Core.Settings;

/// <summary>
/// Persists <see cref="AppSettings"/> to a JSON file using write-to-temp +
/// File.Replace for atomicity, with a .bak fallback if the primary is
/// corrupted at read time.
/// </summary>
public sealed class FileSystemSettingsStore : ISettingsStore
{
    private readonly string _path;
    private readonly string _tempPath;
    private readonly string _backupPath;
    private readonly object _gate = new();
    private readonly Action<FileStream> _flushToDisk;
    private JsonObject? _preservedRoot;
    private bool _readOnlyDueToFutureSchema;

    public bool IsReadOnly
    {
        get
        {
            lock (_gate)
            {
                return _readOnlyDueToFutureSchema;
            }
        }
    }

    public FileSystemSettingsStore(string path)
        : this(path, stream => stream.Flush(flushToDisk: true))
    {
    }

    internal FileSystemSettingsStore(string path, Action<FileStream> flushToDisk)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
        _flushToDisk = flushToDisk ?? throw new ArgumentNullException(nameof(flushToDisk));
        _tempPath = path + ".tmp";
        _backupPath = path + ".bak";
    }

    public AppSettings Load()
    {
        lock (_gate)
        {
            _readOnlyDueToFutureSchema = false;
            if (!File.Exists(_path))
            {
                _preservedRoot = null;
                return new AppSettings().Normalized();
            }
            try
            {
                return LoadFrom(_path);
            }
            catch (UnsupportedSettingsVersionException)
            {
                // A newer client owns this file. Use safe runtime defaults but
                // never overwrite the unknown schema from this older build.
                _preservedRoot = null;
                _readOnlyDueToFutureSchema = true;
                return new AppSettings().Normalized();
            }
            catch (Exception primaryEx) when (primaryEx is JsonException or InvalidDataException or IOException)
            {
                if (File.Exists(_backupPath))
                {
                    try
                    {
                        return LoadFrom(_backupPath);
                    }
                    catch (UnsupportedSettingsVersionException)
                    {
                        _preservedRoot = null;
                        _readOnlyDueToFutureSchema = true;
                        return new AppSettings().Normalized();
                    }
                    catch
                    {
                        // fall through
                    }
                }
                _preservedRoot = null;
                return new AppSettings().Normalized();
            }
        }
    }

    private AppSettings LoadFrom(string path)
    {
        using var stream = File.OpenRead(path);
        JsonNode? node = JsonNode.Parse(stream);
        if (node is null)
        {
            throw new InvalidDataException("Settings JSON was null.");
        }
        JsonNode migrated = SettingsMigrator.Migrate(node);
        AppSettings? loaded = migrated.Deserialize<AppSettings>(SettingsMigrator.SerializerOptions)
            ?? throw new InvalidDataException("Settings JSON did not deserialize.");
        _preservedRoot = migrated.DeepClone().AsObject();
        return loaded.Normalized();
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_gate)
        {
            if (_readOnlyDueToFutureSchema)
            {
                throw new InvalidOperationException("Settings are read-only because the file was written by a newer application version.");
            }
            AppSettings normalized = settings.Normalized();

            string? dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            JsonObject current = JsonSerializer.SerializeToNode(normalized, SettingsMigrator.SerializerOptions)!.AsObject();
            JsonObject merged = _preservedRoot?.DeepClone().AsObject() ?? new JsonObject();
            foreach ((string key, JsonNode? value) in current)
            {
                merged[key] = value?.DeepClone();
            }
            string json = merged.ToJsonString(SettingsMigrator.SerializerOptions);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            using (var stream = new FileStream(
                _tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            {
                stream.Write(bytes);
                _flushToDisk(stream);
            }

            if (File.Exists(_path))
            {
                File.Replace(_tempPath, _path, _backupPath);
            }
            else
            {
                File.Move(_tempPath, _path);
            }
            _preservedRoot = merged;
        }
    }

    public static string DefaultPath()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, "DesktopSystemMonitor", "settings.json");
    }
}
