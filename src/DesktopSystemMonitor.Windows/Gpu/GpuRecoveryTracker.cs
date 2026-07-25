namespace DesktopSystemMonitor.Windows.Gpu;

/// <summary>Tracks consecutive PDH failures without depending on native handles.</summary>
internal sealed class GpuRecoveryTracker(int failureThreshold = 3)
{
    private readonly int _failureThreshold = failureThreshold > 0
        ? failureThreshold
        : throw new ArgumentOutOfRangeException(nameof(failureThreshold));
    private int _consecutiveCollectFailures;
    private int _consecutiveInvalidFailures;

    public bool RecordCollectResult(bool succeeded)
    {
        if (succeeded)
        {
            _consecutiveCollectFailures = 0;
            return false;
        }
        return IncrementAndTest(ref _consecutiveCollectFailures);
    }

    public bool RecordFormattedValues(int expectedCounterCount, int validValueCount)
    {
        if (expectedCounterCount <= 0 || validValueCount > 0)
        {
            _consecutiveInvalidFailures = 0;
            return false;
        }
        return IncrementAndTest(ref _consecutiveInvalidFailures);
    }

    public void Reset()
    {
        _consecutiveCollectFailures = 0;
        _consecutiveInvalidFailures = 0;
    }

    private bool IncrementAndTest(ref int failures)
    {
        failures++;
        if (failures < _failureThreshold)
        {
            return false;
        }
        failures = 0;
        return true;
    }
}
