namespace UbisoftAutoLogin;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        using var logger = new AppLogger();
        var configStore = new ConfigStore(logger);
        var credentials = new CredentialService(logger);

        Application.Run(new TrayApplicationContext(configStore, credentials, logger));
    }
}
