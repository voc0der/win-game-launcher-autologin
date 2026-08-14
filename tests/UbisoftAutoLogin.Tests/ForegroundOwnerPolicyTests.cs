using UbisoftAutoLogin;
using Xunit;

namespace UbisoftAutoLogin.Tests;

public sealed class ForegroundOwnerPolicyTests
{
    [Theory]
    [InlineData("Playnite.FullscreenApp")]
    [InlineData("playnite.desktopapp")]
    [InlineData("Playnite.FullscreenApp.exe")]
    public void AllowsExactPlayniteProcesses(string processName)
    {
        Assert.True(ForegroundOwnerPolicy.CanMinimizeForUbisoftLogin(processName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Playnite")]
    [InlineData("Playnite.FullscreenApp.Helper")]
    [InlineData("explorer")]
    public void RejectsOtherForegroundProcesses(string? processName)
    {
        Assert.False(ForegroundOwnerPolicy.CanMinimizeForUbisoftLogin(processName));
    }
}
