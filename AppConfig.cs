using System.Text.Json;

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

internal sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly AppLogger _logger;

    public ConfigStore(AppLogger logger)
    {
        _logger = logger;
        Directory.CreateDirectory(AppPaths.BaseDirectory);
    }

    public AppConfig Load()
    {
        try
        {
            if (!File.Exists(AppPaths.ConfigFile))
            {
                var defaults = new AppConfig();
                Save(defaults);
                return defaults;
            }

            var json = File.ReadAllText(AppPaths.ConfigFile);
            var config = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            config.Normalize();
            Save(config);
            return config;
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to load config; using defaults.", ex);
            return new AppConfig();
        }
    }

    public void Save(AppConfig config)
    {
        try
        {
            config.Normalize();
            Directory.CreateDirectory(AppPaths.BaseDirectory);
            File.WriteAllText(AppPaths.ConfigFile, JsonSerializer.Serialize(config, JsonOptions));
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to save config.", ex);
        }
    }
}
