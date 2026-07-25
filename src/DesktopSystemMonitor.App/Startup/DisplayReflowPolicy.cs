namespace DesktopSystemMonitor.App.Startup;

internal static class DisplayReflowPolicy
{
    private static readonly TimeSpan MinimumDelay = TimeSpan.FromMilliseconds(1);

    internal static TimeSpan ComputeDelay(
        DateTimeOffset waveStarted,
        DateTimeOffset now,
        TimeSpan debounce,
        TimeSpan maximumDelay)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(debounce, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maximumDelay, TimeSpan.Zero);
        TimeSpan remaining = maximumDelay - (now - waveStarted);
        if (remaining <= TimeSpan.Zero)
        {
            return MinimumDelay;
        }
        return remaining < debounce ? remaining : debounce;
    }

    internal static bool CanPersistPosition(
        bool displayReflowPending,
        bool userMoveInProgress,
        bool layoutChanged) =>
        !displayReflowPending && !userMoveInProgress && !layoutChanged;
}

internal enum DisplayReflowCompletionDecision
{
    Ignore,
    RetryAfterUserMove,
    Reflow,
}

/// <summary>
/// Owns the display-change wave lifecycle independently of WPF timers. The
/// composition layer performs monitor enumeration and window movement only
/// after this coordinator grants a single completion for the current wave.
/// </summary>
internal sealed class DisplayReflowCoordinator
{
    private readonly TimeSpan _debounce;
    private readonly TimeSpan _maximumDelay;
    private DateTimeOffset? _waveStarted;

    internal DisplayReflowCoordinator(TimeSpan debounce, TimeSpan maximumDelay)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(debounce, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maximumDelay, TimeSpan.Zero);
        _debounce = debounce;
        _maximumDelay = maximumDelay;
    }

    internal bool Pending { get; private set; }
    internal bool UserMoveInProgress { get; private set; }
    internal bool ShutdownStarted { get; private set; }

    internal TimeSpan? BeginWave(DateTimeOffset now)
    {
        if (ShutdownStarted)
        {
            return null;
        }
        if (!Pending)
        {
            _waveStarted = now;
        }
        Pending = true;
        return DisplayReflowPolicy.ComputeDelay(
            _waveStarted ?? now,
            now,
            _debounce,
            _maximumDelay);
    }

    internal void BeginUserMove() => UserMoveInProgress = true;

    internal void EndUserMove() => UserMoveInProgress = false;

    internal bool CanPersist(bool layoutChanged) =>
        !ShutdownStarted
        && DisplayReflowPolicy.CanPersistPosition(
            Pending,
            UserMoveInProgress,
            layoutChanged);

    internal DisplayReflowCompletionDecision RequestCompletion()
    {
        if (!Pending || ShutdownStarted)
        {
            return DisplayReflowCompletionDecision.Ignore;
        }
        return UserMoveInProgress
            ? DisplayReflowCompletionDecision.RetryAfterUserMove
            : DisplayReflowCompletionDecision.Reflow;
    }

    internal void FinishCompletion()
    {
        Pending = false;
        _waveStarted = null;
    }

    internal void BeginShutdown() => ShutdownStarted = true;
}
