namespace AcLayerStandardizer.Core;

public enum LaunchDecision { Launch, FocusExisting }

// One mapping window per AutoCAD session: two windows would send conflicting
// pending reports, so the close guard could not protect either one's connections.
// Deliberately free of AutoCAD and Win32 types.
public static class WindowInstance
{
    public const string AlreadyOpenMessage =
        "The Layer Herder window is already open; it follows the active drawing.";

    public static LaunchDecision Decide(bool processAlive) =>
        processAlive ? LaunchDecision.FocusExisting : LaunchDecision.Launch;
}
