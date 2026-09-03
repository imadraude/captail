namespace Captail.Tests;

using System.Diagnostics;
using System.IO;
using Captail.Interop;
using Xunit;

public sealed class CaptureInteropTests
{
    [Fact]
    public void TryGetProcessImageInfoReturnsCurrentProcess()
    {
        uint currentPid = (uint)Process.GetCurrentProcess().Id;
        bool success = CaptureInterop.TryGetProcessImageInfo(currentPid, out string executable, out string fullPath);

        Assert.True(success);
        Assert.EndsWith(".exe", executable, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(fullPath));
    }

    [Fact]
    public void TryGetProcessImageInfoReturnsFalseForInvalidPid()
    {
        bool success = CaptureInterop.TryGetProcessImageInfo(0, out string executable, out string fullPath);

        Assert.False(success);
        Assert.Equal("", executable);
        Assert.Equal("", fullPath);
    }

    [Fact]
    public void TryGetProcessImageInfoReturnsFalseForNonExistentPid()
    {
        bool success = CaptureInterop.TryGetProcessImageInfo(99999999, out string executable, out string fullPath);

        Assert.False(success);
        Assert.Equal("", executable);
        Assert.Equal("", fullPath);
    }
}
