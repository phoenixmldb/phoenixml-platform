using System;
using System.Threading.Tasks;
using Phoenixml.Editor.Status;
using Xunit;

namespace Phoenixml.Editor.Tests.Status;

public class LastActionStatusTests
{
    [Fact]
    public void NotifySetsMessageAndRaisesChanged()
    {
        var status = new LastActionStatus(autoClearAfter: TimeSpan.FromMilliseconds(500));
        var changes = 0;
        status.Changed += () => changes++;

        status.Notify("hello");

        Assert.Equal("hello", status.Message);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void NotifyReplacesPriorMessage()
    {
        var status = new LastActionStatus(TimeSpan.FromMilliseconds(500));
        status.Notify("a");
        status.Notify("b");

        Assert.Equal("b", status.Message);
    }

    [Fact]
    public async Task MessageClearsAfterTTL()
    {
        var status = new LastActionStatus(TimeSpan.FromMilliseconds(50));
        var tcs = new TaskCompletionSource();
        status.Changed += () =>
        {
            if (status.Message is null) tcs.TrySetResult();
        };

        status.Notify("flash");
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.Null(status.Message);
    }
}
