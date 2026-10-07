namespace AcLayerStandardizer.Core;

// The mapping window shows its first-run banner when launched with this argument.
// The connector passes it on the first launch in an AutoCAD session only.
// Deliberately free of AutoCAD and process types.
public static class NoticePolicy
{
    public const string Argument = "--first-run-notice";

    public static bool ShouldPassNotice(bool alreadyShownThisSession) => !alreadyShownThisSession;
}
