namespace AcLayerStandardizer.Core;

// NotReady: AutoCAD was not ready for the window (for example its main window
// was not found); not an install problem, so the advice is to try again.
public enum LaunchOutcome { Launched, AlreadyOpen, ExecutableNotFound, StartFailed, NotReady }

// Turns a failed launch of the mapping window into the plain message the command
// line shows. Deliberately free of AutoCAD and process types.
public static class LaunchGuard
{
    private const string Reinstall = "Please reinstall the Layer Standardizer.";

    public static string? DescribeFailure(LaunchOutcome outcome, string? detail)
    {
        var shown = detail ?? "unknown";
        return outcome switch
        {
            LaunchOutcome.Launched => null,
            LaunchOutcome.AlreadyOpen => null,
            LaunchOutcome.ExecutableNotFound =>
                "The Layer Standardizer window could not start: its program file (acad_layer_ui.exe) was not found " +
                $"(looked in: {shown}). {Reinstall}",
            LaunchOutcome.StartFailed =>
                $"The Layer Standardizer window could not start ({shown}). {Reinstall}",
            LaunchOutcome.NotReady =>
                $"The Layer Standardizer window could not start ({shown}). Try the command again.",
            _ => $"The Layer Standardizer window could not start ({shown}).",
        };
    }
}
