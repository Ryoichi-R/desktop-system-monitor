namespace DesktopSystemMonitor.Core.Metrics;

/// <summary>
/// OSごとの具象メトリクスソース生成を1箇所へ集約する。個々の生成が失敗した場合の
/// Unavailable*フォールバックはAppComposition側の責務のままとし、このfactoryは
/// 「正常時にどの具象クラスを使うか」だけを切り替える。
/// </summary>
public interface IMetricSourceFactory
{
    IMetricSource<CpuSnapshot> CreateCpu();
    IMetricSource<MemorySnapshot> CreateMemory();
    IGpuMetricSource CreateGpu();
    INetworkMetricSource CreateNetwork();

    /// <summary>onDiagnosticはハードウェアポーリングの個別失敗（クラッシュしない範囲の異常）を報告する。</summary>
    IPowerMetricSource CreatePower(Action<string, Exception> onDiagnostic);
    IDiskMetricSource CreateDisk();
    IBatteryMetricSource CreateBattery();
}
