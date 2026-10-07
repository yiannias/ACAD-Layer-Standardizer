using Xunit;
using AcLayerStandardizer.Core;

namespace AcLayerStandardizer.Tests;

public class ClassifyCloseTests
{
    [Theory]
    [InlineData("QUIT", true)]
    [InlineData("quit", true)]
    [InlineData("_QUIT", true)]
    [InlineData(".QUIT", true)]
    [InlineData("'quit", true)]
    [InlineData(" Quit ", true)]
    [InlineData("EXIT", true)]
    [InlineData("CLOSE", false)]
    [InlineData("_CLOSE", false)]
    [InlineData("CLOSEALL", false)]
    [InlineData("", false)]
    [InlineData("QUITXYZ", false)]
    public void classifies_the_command_in_progress(string command, bool expectQuit)
    {
        Assert.Equal(expectQuit, CloseGuard.ClassifyClose(command) == CloseKind.Quit);
    }

    [Fact]
    public void no_command_in_progress_is_a_drawing_close()
    {
        Assert.Equal(CloseKind.Drawing, CloseGuard.ClassifyClose(null));
    }
}
