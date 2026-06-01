using UbisoftAutoLogin;
using Xunit;

namespace UbisoftAutoLogin.Tests;

public sealed class AppConfigTests
{
    [Fact]
    public void DefaultsMatchDocumentedFallbackValues()
    {
        var config = new AppConfig();

        Assert.Equal(0.50, config.PasswordBoxXPercent);
        Assert.Equal(0.58, config.PasswordBoxYPercent);
        Assert.Equal(2500, config.DelayBeforeFillMs);
        Assert.Equal(15000, config.DebounceMs);
    }

    [Fact]
    public void NormalizeFallsBackForInvalidValues()
    {
        var config = new AppConfig
        {
            PasswordBoxXPercent = double.NaN,
            PasswordBoxYPercent = double.PositiveInfinity,
            DelayBeforeFillMs = 0,
            DebounceMs = -1
        };

        config.Normalize();

        Assert.Equal(0.50, config.PasswordBoxXPercent);
        Assert.Equal(0.58, config.PasswordBoxYPercent);
        Assert.Equal(2500, config.DelayBeforeFillMs);
        Assert.Equal(15000, config.DebounceMs);
    }

    [Fact]
    public void NormalizeClampsToSafeRanges()
    {
        var config = new AppConfig
        {
            PasswordBoxXPercent = 2.0,
            PasswordBoxYPercent = -2.0,
            DelayBeforeFillMs = 50,
            DebounceMs = 500000
        };

        config.Normalize();

        Assert.Equal(0.95, config.PasswordBoxXPercent);
        Assert.Equal(0.05, config.PasswordBoxYPercent);
        Assert.Equal(1500, config.DelayBeforeFillMs);
        Assert.Equal(120000, config.DebounceMs);
    }
}
