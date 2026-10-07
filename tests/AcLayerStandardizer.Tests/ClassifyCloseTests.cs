using Xunit;
using AcLayerStandardizer.Core;

namespace AcLayerStandardizer.Tests;

public class ClassifyCloseTests
{
    [Theory]
    [InlineData("QUIT", CloseKind.Quit)]
    [InlineData("quit", CloseKind.Quit)]
    [InlineData("_QUIT", CloseKind.Quit)]
    [InlineData(".QUIT", CloseKind.Quit)]
    [InlineData("'quit", CloseKind.Quit)]
    [InlineData(" Quit ", CloseKind.Quit)]
    [InlineData("EXIT", CloseKind.Quit)]
    [InlineData("CLOSE", CloseKind.Drawing)]
    [InlineData("_CLOSE", CloseKind.Drawing)]
    [InlineData("CLOSEALL", CloseKind.Drawing)]
    [InlineData("", CloseKind.Drawing)]
    [InlineData(null, CloseKind.Drawing)]
    [InlineData("QUITXYZ", CloseKind.Drawing)]
    public void classifies_the_command_in_progress(string? command, CloseKind expected)
    {
        Assert.Equal(expected, CloseGuard.ClassifyClose(command));
    }
}
