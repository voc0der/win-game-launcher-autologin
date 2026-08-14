namespace UbisoftAutoLogin;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly ConfigStore _configStore;
    private readonly CredentialService _credentials;
    private readonly AppLogger _logger;
    private readonly Icon _trayIcon;
    private readonly NotifyIcon _notifyIcon;
    private readonly DispatcherForm _dispatcher;
    private readonly WindowEventHook _windowEventHook;
    private readonly FillCoordinator _fillCoordinator;
    private readonly Dictionary<IntPtr, DateTimeOffset> _lastAttemptByHwnd = new();
    private readonly HashSet<uint> _activeProcessIds = new();
    private AppConfig _config;

    public TrayApplicationContext(ConfigStore configStore, CredentialService credentials, AppLogger logger)
    {
        _configStore = configStore;
        _credentials = credentials;
        _logger = logger;
        _config = _configStore.Load();

        _dispatcher = new DispatcherForm();
        _ = _dispatcher.Handle;
        _trayIcon = TrayIconLoader.Load(_logger);

        _notifyIcon = new NotifyIcon
        {
            Icon = _trayIcon,
            Text = "Ubisoft Auto Login",
            Visible = true,
            ContextMenuStrip = BuildContextMenu()
        };

        _windowEventHook = new WindowEventHook(_dispatcher, _logger);
        _windowEventHook.WindowEventReceived += OnWindowEventReceived;

        _fillCoordinator = new FillCoordinator(
            () => _config,
            _credentials,
            _logger,
            ShowStatus);

        _dispatcher.BeginInvoke((Action)(() =>
        {
            if (_windowEventHook.Start())
            {
                ShowStatus("Hook active");
            }

            PromptForCredentialsIfNeeded();
        }));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _windowEventHook.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _trayIcon.Dispose();
            _dispatcher.Dispose();
        }

        base.Dispose(disposing);
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        var setCredentials = new ToolStripMenuItem("Set / Update Credentials");
        setCredentials.Click += (_, _) => ShowCredentialsDialog();

        var testFill = new ToolStripMenuItem("Test Fill Current Ubisoft Window");
        testFill.Click += async (_, _) => await TestFillCurrentWindowAsync();

        var exit = new ToolStripMenuItem("Exit");
        exit.Click += (_, _) => Exit();

        menu.Items.Add(setCredentials);
        menu.Items.Add(testFill);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exit);
        return menu;
    }

    private void OnWindowEventReceived(object? sender, WindowEventArgs e)
    {
        if (!UbisoftWindowDetector.TryGetCandidate(e.Hwnd, out var window))
        {
            return;
        }

        if (_activeProcessIds.Contains(window.ProcessId) || !ShouldAttempt(window.RootHwnd, e.EventType))
        {
            return;
        }

        _ = FillWindowAsync(window);
    }

    private async Task TestFillCurrentWindowAsync()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (!UbisoftWindowDetector.TryGetCandidate(foreground, out var window))
        {
            ShowStatus("No Ubisoft window in foreground");
            _logger.Warn("Test fill requested, but foreground window was not a Ubisoft candidate.");
            return;
        }

        await FillWindowAsync(window);
    }

    private async Task FillWindowAsync(UbisoftWindow window)
    {
        if (!_activeProcessIds.Add(window.ProcessId))
        {
            _logger.Info($"Skipped overlapping fill for Ubisoft pid={window.ProcessId}.");
            return;
        }

        try
        {
            await _fillCoordinator.FillAsync(window, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            _logger.Warn("Fill operation canceled.");
        }
        catch (Exception ex)
        {
            _logger.Error("Fill operation failed.", ex);
            ShowStatus("Fill failed");
        }
        finally
        {
            _activeProcessIds.Remove(window.ProcessId);
        }
    }

    private bool ShouldAttempt(IntPtr rootHwnd, uint eventType)
    {
        var now = DateTimeOffset.UtcNow;
        var debounce = TimeSpan.FromMilliseconds(_config.DebounceMs);
        var foregroundRetry = eventType == NativeMethods.EVENT_SYSTEM_FOREGROUND &&
                              UbisoftWindowDetector.IsForegroundWithinTarget(rootHwnd);

        if (_lastAttemptByHwnd.TryGetValue(rootHwnd, out var lastAttempt) &&
            now - lastAttempt < debounce &&
            !foregroundRetry)
        {
            return false;
        }

        _lastAttemptByHwnd[rootHwnd] = now;
        PruneDebounceMap(now, debounce);
        return true;
    }

    private void PruneDebounceMap(DateTimeOffset now, TimeSpan debounce)
    {
        var staleKeys = _lastAttemptByHwnd
            .Where(pair => now - pair.Value > debounce + TimeSpan.FromMinutes(1))
            .Select(pair => pair.Key)
            .ToList();

        foreach (var key in staleKeys)
        {
            _lastAttemptByHwnd.Remove(key);
        }
    }

    private void PromptForCredentialsIfNeeded()
    {
        if (!_credentials.HasCredentials())
        {
            ShowCredentialsDialog();
        }
    }

    private void ShowCredentialsDialog()
    {
        using var dialog = new CredentialsDialog(_credentials.ReadUsername());
        var result = dialog.ShowDialog();
        if (result != DialogResult.OK)
        {
            _logger.Warn("Credential dialog was canceled.");
            return;
        }

        if (string.IsNullOrWhiteSpace(dialog.Username) || string.IsNullOrEmpty(dialog.Password))
        {
            MessageBox.Show(
                "Both username/email and password are required.",
                "Ubisoft Auto Login",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            _logger.Warn("Credential dialog submitted with missing values.");
            return;
        }

        _credentials.SaveCredentials(dialog.Username, dialog.Password);
        ShowStatus("Credentials saved");
    }

    private void ShowStatus(string message)
    {
        _logger.Info($"Status: {message}");
        try
        {
            _notifyIcon.ShowBalloonTip(2500, "Ubisoft Auto Login", message, ToolTipIcon.Info);
        }
        catch (InvalidOperationException)
        {
            // NotifyIcon can reject balloons while the shell is not ready.
        }
    }

    private void Exit()
    {
        _logger.Info("Application exiting.");
        Dispose();
        ExitThread();
    }

    private sealed class DispatcherForm : Form
    {
        public DispatcherForm()
        {
            Text = "Ubisoft Auto Login Dispatcher";
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(1, 1);
            Location = new Point(-32000, -32000);
        }

        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(false);
        }
    }
}
