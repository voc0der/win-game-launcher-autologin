namespace UbisoftAutoLogin;

internal static class ForegroundOwnerPolicy
{
    private static readonly HashSet<string> PlayniteProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Playnite.DesktopApp",
        "Playnite.FullscreenApp"
    };

    public static bool CanMinimizeForUbisoftLogin(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        var normalized = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4]
            : processName;

        return PlayniteProcessNames.Contains(normalized);
    }
}
