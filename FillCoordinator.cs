using System.Runtime.InteropServices;
using UIA = Interop.UIAutomationClient;

namespace UbisoftAutoLogin;

internal sealed class FillCoordinator
{
    private readonly Func<AppConfig> _getConfig;
    private readonly CredentialService _credentials;
    private readonly AppLogger _logger;
    private readonly Action<string> _status;
    private UIA.IUIAutomation? _automation;

    public FillCoordinator(
        Func<AppConfig> getConfig,
        CredentialService credentials,
        AppLogger logger,
        Action<string> status)
    {
        _getConfig = getConfig;
        _credentials = credentials;
        _logger = logger;
        _status = status;
    }

    private UIA.IUIAutomation Automation => _automation ??= new UIA.CUIAutomationClass();

    public async Task FillAsync(UbisoftWindow window, CancellationToken cancellationToken)
    {
        var config = _getConfig();
        _status("Ubisoft window detected");
        _logger.Info($"Ubisoft window detected. hwnd=0x{window.RootHwnd.ToInt64():X}, pid={window.ProcessId}, process={window.ProcessName}.");

        await Task.Delay(config.DelayBeforeFillMs, cancellationToken).ConfigureAwait(true);

        var savedCredentials = _credentials.ReadCredentials();
        if (savedCredentials is null)
        {
            _status("Credentials not set");
            _logger.Warn("Fill aborted because credentials are not set.");
            return;
        }

        try
        {
            var uiaResult = TryFillWithUiAutomation(window.RootHwnd, savedCredentials.Username, savedCredentials.Password);
            if (uiaResult == FillAttemptResult.Submitted)
            {
                _status("Submitted");
                _logger.Info("Submitted using UI Automation.");
                return;
            }

            if (uiaResult == FillAttemptResult.Aborted)
            {
                return;
            }

            _status("UIA failed, using coordinate fallback");
            _logger.Warn("UI Automation failed; falling back to configured coordinates.");
            if (TryFillWithCoordinates(window.RootHwnd, savedCredentials.Password, config))
            {
                _status("Submitted");
                _logger.Info("Submitted using coordinate fallback.");
            }
        }
        finally
        {
            savedCredentials = null;
        }
    }

    private FillAttemptResult TryFillWithUiAutomation(IntPtr rootHwnd, string username, string password)
    {
        try
        {
            var root = Automation.ElementFromHandle(rootHwnd);
            if (root is null)
            {
                return FillAttemptResult.Failed;
            }

            var edits = FindVisibleEnabledElements(root, UIA.UIA_ControlTypeIds.UIA_EditControlTypeId);
            var passwordField = FindPasswordField(edits);
            if (passwordField is null)
            {
                return FillAttemptResult.Failed;
            }

            var usernameField = FindUsernameField(edits, passwordField);
            if (usernameField is not null && !string.IsNullOrWhiteSpace(username))
            {
                TrySetValue(usernameField, username);
            }

            passwordField.SetFocus();
            if (!TrySetValue(passwordField, password))
            {
                return FillAttemptResult.Failed;
            }

            _status("UIA password field found");
            _logger.Info("UI Automation password field found and filled.");

            if (!BringToForegroundAndVerify(rootHwnd))
            {
                _status("Aborted: Ubisoft not foreground");
                _logger.Warn("UI Automation submit aborted because Ubisoft was not foreground.");
                return FillAttemptResult.Aborted;
            }

            var button = FindSubmitButton(root);
            if (button is not null && TryInvoke(button))
            {
                return FillAttemptResult.Submitted;
            }

            return InputSender.SendEnterIfForeground(rootHwnd, _logger)
                ? FillAttemptResult.Submitted
                : FillAttemptResult.Aborted;
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException)
        {
            _logger.Warn($"UI Automation failed: {ex.Message}");
            return FillAttemptResult.Failed;
        }
    }

    private bool TryFillWithCoordinates(IntPtr rootHwnd, string password, AppConfig config)
    {
        if (!NativeMethods.GetWindowRect(rootHwnd, out var rect))
        {
            _logger.Warn("Coordinate fallback aborted because GetWindowRect failed.");
            return false;
        }

        if (!BringToForegroundAndVerify(rootHwnd))
        {
            _status("Aborted: Ubisoft not foreground");
            _logger.Warn("Coordinate fallback aborted before click because Ubisoft was not foreground.");
            return false;
        }

        var x = rect.Left + (int)Math.Round(rect.Width * config.PasswordBoxXPercent);
        var y = rect.Top + (int)Math.Round(rect.Height * config.PasswordBoxYPercent);

        if (!InputSender.Click(rootHwnd, x, y, _logger))
        {
            _status("Aborted: Ubisoft not foreground");
            return false;
        }

        if (!InputSender.SendTextIfForeground(rootHwnd, password, _logger))
        {
            _status("Aborted: Ubisoft not foreground");
            return false;
        }

        if (!InputSender.SendEnterIfForeground(rootHwnd, _logger))
        {
            _status("Aborted: Ubisoft not foreground");
            return false;
        }

        return true;
    }

    private bool BringToForegroundAndVerify(IntPtr rootHwnd)
    {
        NativeMethods.SetForegroundWindow(rootHwnd);
        Thread.Sleep(150);
        return UbisoftWindowDetector.IsForegroundWithinTarget(rootHwnd);
    }

    private List<UIA.IUIAutomationElement> FindVisibleEnabledElements(UIA.IUIAutomationElement root, int controlTypeId)
    {
        var condition = Automation.CreatePropertyCondition(UIA.UIA_PropertyIds.UIA_ControlTypePropertyId, controlTypeId);
        var collection = root.FindAll(UIA.TreeScope.TreeScope_Descendants, condition);
        var elements = new List<UIA.IUIAutomationElement>(collection.Length);

        for (var i = 0; i < collection.Length; i++)
        {
            var element = collection.GetElement(i);
            if (IsUsable(element))
            {
                elements.Add(element);
            }
        }

        return elements;
    }

    private static UIA.IUIAutomationElement? FindPasswordField(IReadOnlyList<UIA.IUIAutomationElement> edits)
    {
        foreach (var edit in edits)
        {
            if (GetBool(edit, UIA.UIA_PropertyIds.UIA_IsPasswordPropertyId))
            {
                return edit;
            }
        }

        foreach (var edit in edits)
        {
            if (LooksLikePassword(edit))
            {
                return edit;
            }
        }

        return edits.Count switch
        {
            1 => edits[0],
            > 1 => edits[^1],
            _ => null
        };
    }

    private static UIA.IUIAutomationElement? FindUsernameField(IReadOnlyList<UIA.IUIAutomationElement> edits, UIA.IUIAutomationElement passwordField)
    {
        foreach (var edit in edits)
        {
            if (ReferenceEquals(edit, passwordField) || GetBool(edit, UIA.UIA_PropertyIds.UIA_IsPasswordPropertyId))
            {
                continue;
            }

            var text = $"{GetString(edit, UIA.UIA_PropertyIds.UIA_NamePropertyId)} {GetString(edit, UIA.UIA_PropertyIds.UIA_AutomationIdPropertyId)}";
            if (text.Contains("email", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("user", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("account", StringComparison.OrdinalIgnoreCase))
            {
                return edit;
            }
        }

        var passwordIndex = IndexOf(edits, passwordField);
        if (passwordIndex > 0)
        {
            return edits[passwordIndex - 1];
        }

        return null;
    }

    private static int IndexOf(IReadOnlyList<UIA.IUIAutomationElement> elements, UIA.IUIAutomationElement target)
    {
        for (var i = 0; i < elements.Count; i++)
        {
            if (ReferenceEquals(elements[i], target))
            {
                return i;
            }
        }

        return -1;
    }

    private UIA.IUIAutomationElement? FindSubmitButton(UIA.IUIAutomationElement root)
    {
        var buttons = FindVisibleEnabledElements(root, UIA.UIA_ControlTypeIds.UIA_ButtonControlTypeId);
        foreach (var button in buttons)
        {
            var text = $"{GetString(button, UIA.UIA_PropertyIds.UIA_NamePropertyId)} {GetString(button, UIA.UIA_PropertyIds.UIA_AutomationIdPropertyId)}";
            if (text.Contains("log in", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("login", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("sign in", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("continue", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("next", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("submit", StringComparison.OrdinalIgnoreCase))
            {
                return button;
            }
        }

        return null;
    }

    private static bool TrySetValue(UIA.IUIAutomationElement element, string value)
    {
        var patternObject = element.GetCurrentPattern(UIA.UIA_PatternIds.UIA_ValuePatternId);
        if (patternObject is not UIA.IUIAutomationValuePattern valuePattern ||
            valuePattern.CurrentIsReadOnly != 0)
        {
            return false;
        }

        valuePattern.SetValue(value);
        return true;
    }

    private static bool TryInvoke(UIA.IUIAutomationElement element)
    {
        var patternObject = element.GetCurrentPattern(UIA.UIA_PatternIds.UIA_InvokePatternId);
        if (patternObject is not UIA.IUIAutomationInvokePattern invokePattern)
        {
            return false;
        }

        invokePattern.Invoke();
        return true;
    }

    private static bool IsUsable(UIA.IUIAutomationElement element)
    {
        try
        {
            if (!GetBool(element, UIA.UIA_PropertyIds.UIA_IsEnabledPropertyId) ||
                GetBool(element, UIA.UIA_PropertyIds.UIA_IsOffscreenPropertyId))
            {
                return false;
            }

            var rect = element.CurrentBoundingRectangle;
            return rect.right > rect.left && rect.bottom > rect.top;
        }
        catch (COMException)
        {
            return false;
        }
    }

    private static bool LooksLikePassword(UIA.IUIAutomationElement element)
    {
        var text = $"{GetString(element, UIA.UIA_PropertyIds.UIA_NamePropertyId)} {GetString(element, UIA.UIA_PropertyIds.UIA_AutomationIdPropertyId)}";
        return text.Contains("password", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("passwort", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("mot de passe", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("contrasena", StringComparison.OrdinalIgnoreCase);
    }

    private static bool GetBool(UIA.IUIAutomationElement element, int propertyId)
    {
        var value = element.GetCurrentPropertyValueEx(propertyId, 1);
        return value switch
        {
            bool boolValue => boolValue,
            int intValue => intValue != 0,
            _ => false
        };
    }

    private static string GetString(UIA.IUIAutomationElement element, int propertyId)
    {
        var value = element.GetCurrentPropertyValueEx(propertyId, 1);
        return value is string stringValue ? stringValue : string.Empty;
    }

    private enum FillAttemptResult
    {
        Failed,
        Aborted,
        Submitted
    }
}
