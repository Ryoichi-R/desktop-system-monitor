namespace DesktopSystemMonitor.App.Startup;

internal enum SamplingSuspensionTransition
{
    None,
    Pause,
    Resume,
}

internal sealed class SamplingSuspensionCoordinator
{
    internal bool Suspended { get; private set; }

    internal SamplingSuspensionTransition Evaluate(bool sessionLocked, bool fullScreenHidden)
    {
        bool shouldSuspend = sessionLocked || fullScreenHidden;
        if (shouldSuspend == Suspended)
        {
            return SamplingSuspensionTransition.None;
        }

        Suspended = shouldSuspend;
        return shouldSuspend
            ? SamplingSuspensionTransition.Pause
            : SamplingSuspensionTransition.Resume;
    }
}
