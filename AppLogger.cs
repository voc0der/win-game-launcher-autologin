namespace UbisoftAutoLogin;

internal static class AppPaths
{
    public static string BaseDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "UbisoftAutoLogin");

    public static string ConfigFile { get; } = Path.Combine(BaseDirectory, "config.json");

    public static string LogDirectory { get; } = Path.Combine(BaseDirectory, "logs");

    public static string LogFile { get; } = Path.Combine(LogDirectory, "app.log");
}

internal sealed class AppLogger : IDisposable
{
    private readonly object _gate = new();
    private StreamWriter? _writer;

    public AppLogger()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogDirectory);
            _writer = new StreamWriter(new FileStream(AppPaths.LogFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            {
                AutoFlush = true
            };
            Info("Application starting.");
        }
        catch
        {
            _writer = null;
        }
    }

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message, Exception? exception = null)
    {
        if (exception is null)
        {
            Write("ERROR", message);
            return;
        }

        Write("ERROR", $"{message} {exception.GetType().Name}: {exception.Message}");
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private void Write(string level, string message)
    {
        lock (_gate)
        {
            _writer?.WriteLine($"{DateTimeOffset.Now:O} [{level}] {message}");
        }
    }
}
