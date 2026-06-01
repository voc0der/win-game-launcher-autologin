using System.Text.Json;

namespace UbisoftAutoLogin;

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
