using System.Runtime.InteropServices;
using UIA = Interop.UIAutomationClient;

namespace UbisoftAutoLogin;

internal sealed class FillCoordinator
{
    private readonly Func<AppConfig> _getConfig;
    private readonly CredentialService _credentials;
    private readonly AppLogger _logger;
    private readonly Action<string> _status;
    private readonly ForegroundWindowActivator _foregroundActivator;
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
        _foregroundActivator = new ForegroundWindowActivator(logger);
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
            for (var attempt = 1; attempt <= config.MaxFillAttempts; attempt++)
            {
                if (!UbisoftWindowDetector.TryGetCandidate(window.RootHwnd, out var currentWindow) ||
                    currentWindow.ProcessId != window.ProcessId)
                {
                    _logger.Info("Fill stopped because the original Ubisoft window is no longer available.");
                    return;
                }

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
                var coordinateResult = TryFillWithCoordinates(window.RootHwnd, savedCredentials.Password, config);
                if (coordinateResult == FillAttemptResult.Submitted)
                {
                    _status("Submitted");
                    _logger.Info("Submitted using coordinate fallback.");
                    return;
                }

                if (coordinateResult == FillAttemptResult.Aborted)
                {
                    return;
                }

                if (attempt < config.MaxFillAttempts)
                {
                    _status($"Retrying fill ({attempt + 1}/{config.MaxFillAttempts})");
                    _logger.Warn($"Fill attempt {attempt}/{config.MaxFillAttempts} did not complete; retrying.");
                    await Task.Delay(config.RetryDelayMs, cancellationToken).ConfigureAwait(true);
                }
            }

            _status("Fill failed after retries");
            _logger.Warn($"Fill failed after {config.MaxFillAttempts} attempts.");
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
            var passwordField = FindPasswordField(edits, out var isVerifiedPasswordField);
            if (passwordField is null)
            {
                _logger.Info($"UI Automation found {edits.Count} usable edit controls but no password field.");
                return FillAttemptResult.Failed;
            }

            var usernameField = FindUsernameField(edits, passwordField);
            if (usernameField is not null && !string.IsNullOrWhiteSpace(username))
            {
                TrySetValue(usernameField, username);
            }

            if (!TrySetValue(passwordField, password))
            {
                _logger.Info("UI Automation password candidate did not expose a writable Value pattern.");
                return FillAttemptResult.Failed;
            }

            _status("UIA password field found");
            _logger.Info("UI Automation password field found and filled.");

            var button = FindSubmitButton(root);
            if (button is not null && isVerifiedPasswordField)
            {
                if (TryInvoke(button))
                {
                    _logger.Info("Invoked the UI Automation submit button without requiring foreground input.");
                    return FillAttemptResult.Submitted;
                }

                _logger.Warn("UI Automation submit button did not support the Invoke pattern.");
            }
            else if (button is not null)
            {
                _logger.Info("UI Automation used a heuristic password field; foreground verification is required before submit.");
            }
            else
            {
                _logger.Info("UI Automation submit button was not exposed; Enter requires foreground activation.");
            }

            var activationResult = _foregroundActivator.TryActivateUbisoft(rootHwnd);
            if (activationResult != ForegroundActivationResult.Activated)
            {
                return HandleActivationFailure(activationResult, "UI Automation submit");
            }

            passwordField.SetFocus();
            if (button is not null && TryInvoke(button))
            {
                _logger.Info("Invoked the UI Automation submit button after foreground verification.");
                return FillAttemptResult.Submitted;
            }

            return InputSender.SendEnterIfForeground(rootHwnd, _logger)
                ? FillAttemptResult.Submitted
                : FillAttemptResult.Failed;
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException)
        {
            _logger.Warn($"UI Automation failed: {ex.Message}");
            return FillAttemptResult.Failed;
        }
    }

    private FillAttemptResult TryFillWithCoordinates(IntPtr rootHwnd, string password, AppConfig config)
    {
        var activationResult = _foregroundActivator.TryActivateUbisoft(rootHwnd);
        if (activationResult != ForegroundActivationResult.Activated)
        {
            return HandleActivationFailure(activationResult, "Coordinate fallback");
        }

        if (!NativeMethods.GetWindowRect(rootHwnd, out var rect))
        {
            _logger.Warn("Coordinate fallback could not read the Ubisoft window rectangle.");
            return FillAttemptResult.Failed;
        }

        var x = rect.Left + (int)Math.Round(rect.Width * config.PasswordBoxXPercent);
        var y = rect.Top + (int)Math.Round(rect.Height * config.PasswordBoxYPercent);

        if (!InputSender.Click(rootHwnd, x, y, _logger))
        {
            return FillAttemptResult.Failed;
        }

        if (!InputSender.SendSelectAllIfForeground(rootHwnd, _logger))
        {
            return FillAttemptResult.Failed;
        }

        if (!InputSender.SendTextIfForeground(rootHwnd, password, _logger))
        {
            return FillAttemptResult.Failed;
        }

        if (!InputSender.SendEnterIfForeground(rootHwnd, _logger))
        {
            return FillAttemptResult.Failed;
        }

        return FillAttemptResult.Submitted;
    }

    private FillAttemptResult HandleActivationFailure(ForegroundActivationResult result, string operation)
    {
        if (result == ForegroundActivationResult.BlockedByOtherApplication)
        {
            _status("Waiting: Ubisoft not foreground");
            _logger.Warn($"{operation} paused because another application owns the foreground.");
            return FillAttemptResult.Aborted;
        }

        _logger.Warn($"{operation} could not activate Ubisoft; the attempt can be retried.");
        return FillAttemptResult.Failed;
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

    private static UIA.IUIAutomationElement? FindPasswordField(
        IReadOnlyList<UIA.IUIAutomationElement> edits,
        out bool isVerifiedPasswordField)
    {
        isVerifiedPasswordField = false;

        foreach (var edit in edits)
        {
            if (GetBool(edit, UIA.UIA_PropertyIds.UIA_IsPasswordPropertyId))
            {
                isVerifiedPasswordField = true;
                return edit;
            }
        }

        foreach (var edit in edits)
        {
            if (LooksLikePassword(edit))
            {
                isVerifiedPasswordField = true;
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
