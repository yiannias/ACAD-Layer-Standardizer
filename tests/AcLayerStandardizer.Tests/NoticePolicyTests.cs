using Xunit;
using AcLayerStandardizer.Core;

namespace AcLayerStandardizer.Tests;

public class NoticePolicyTests
{
    [Fact]
    public void The_first_launch_passes_the_notice_and_later_ones_do_not()
    {
        Assert.True(NoticePolicy.ShouldPassNotice(alreadyShownThisSession: false));
        Assert.False(NoticePolicy.ShouldPassNotice(alreadyShownThisSession: true));
        Assert.Equal("--first-run-notice", NoticePolicy.Argument);
    }
}
