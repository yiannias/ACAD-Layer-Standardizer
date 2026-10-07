namespace AcLayerStandardizer.Core;

public enum LaunchOutcome { Launched, AlreadyOpen, ExecutableNotFound, StartFailed }

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
            LaunchOutcome.ExecutableNotFound =>
                "The Layer Standardizer window could not start: its program file (acad_layer_ui.exe) was not found " +
                $"(looked in: {shown}). {Reinstall}",
            LaunchOutcome.StartFailed =>
                $"The Layer Standardizer window could not start ({shown}). {Reinstall}",
            _ => null,
        };
    }
}
