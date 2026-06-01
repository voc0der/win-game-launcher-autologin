namespace UbisoftAutoLogin;

internal sealed class AppConfig
{
    public double PasswordBoxXPercent { get; set; } = 0.50;
    public double PasswordBoxYPercent { get; set; } = 0.58;
    public int DelayBeforeFillMs { get; set; } = 2500;
    public int DebounceMs { get; set; } = 15000;

    public void Normalize()
    {
        PasswordBoxXPercent = Clamp(PasswordBoxXPercent, 0.05, 0.95, 0.50);
        PasswordBoxYPercent = Clamp(PasswordBoxYPercent, 0.05, 0.95, 0.58);
        DelayBeforeFillMs = Clamp(DelayBeforeFillMs, 1500, 30000, 2500);
        DebounceMs = Clamp(DebounceMs, 1000, 120000, 15000);
    }

    private static double Clamp(double value, double min, double max, double fallback)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return fallback;
        }

        return Math.Min(max, Math.Max(min, value));
    }

    private static int Clamp(int value, int min, int max, int fallback)
    {
        if (value <= 0)
        {
            return fallback;
        }

        return Math.Min(max, Math.Max(min, value));
    }
}
