namespace DesktopSystemMonitor.Core.Settings;

public interface ISettingsStore
{
    bool IsReadOnly { get; }
    AppSettings Load();
    void Save(AppSettings settings);
}
