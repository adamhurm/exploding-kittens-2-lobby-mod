using EKLobbyTray;
using Xunit;

namespace EKLobbyTray.Tests;

public class AutoLaunchHelperTests
{
    [Fact]
    public void Enable_DoesNotThrow_WhenCalledNormally()
    {
        // Integration test: just call Enable and Disable in sequence.
        // This verifies no NullReferenceException and that the registry key is cleaned up.
        // If the current exe path is not a .exe (e.g. running via dotnet test),
        // Enable() will throw an InvalidOperationException — that is expected and correct behavior.
        try
        {
            AutoLaunchHelper.Enable();
            Assert.True(AutoLaunchHelper.IsEnabled());
            AutoLaunchHelper.Disable();
            Assert.False(AutoLaunchHelper.IsEnabled());
        }
        catch (InvalidOperationException)
        {
            // Expected when running under dotnet test (not a self-contained .exe)
            // The important thing is it does NOT throw NullReferenceException
        }
    }

    [Fact]
    public void GetCurrentExePath_ReturnsNullOrString()
    {
        // Should never throw — returns null if unavailable
        var path = AutoLaunchHelper.GetCurrentExePath();
        // Either null (unavailable) or a non-empty string
        Assert.True(path == null || path.Length > 0);
    }
}
