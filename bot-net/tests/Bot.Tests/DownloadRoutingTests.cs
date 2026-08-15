using Bot.Models;
using Bot.Utils;
using Xunit;

namespace Bot.Tests;

public class DownloadRoutingTests
{
    private static DownloadFileItem File(int? userMessageId = null, string storagePeer = "bot") =>
        new()
        {
            Id = 1,
            MessageId = 4242,
            UserMessageId = userMessageId,
            Filename = "Mugen Train.mkv",
            Filesize = 1048576,
            StoragePeer = storagePeer
        };

    [Fact]
    public void Choose_UsesTheAccountWhenItHasAnIdAndASession()
    {
        var route = DownloadRouting.Choose(File(userMessageId: 91001), userSessionReady: true);

        Assert.Equal(DownloadIdentity.UserAccount, route.Identity);
        Assert.Equal(91001, route.MessageId);
    }

    [Fact]
    public void Choose_FallsBackToTheBotWithoutASession()
    {
        var route = DownloadRouting.Choose(File(userMessageId: 91001), userSessionReady: false);

        Assert.Equal(DownloadIdentity.Bot, route.Identity);
        Assert.Equal(4242, route.MessageId);
    }

    [Fact]
    public void Choose_FallsBackToTheBotWhenTheColumnIsEmpty()
    {
        var route = DownloadRouting.Choose(File(), userSessionReady: true);

        Assert.Equal(DownloadIdentity.Bot, route.Identity);
        Assert.Equal(4242, route.MessageId);
    }

    [Fact]
    public void Choose_UsesTheBotWithNeitherIdNorSession()
    {
        var route = DownloadRouting.Choose(File(), userSessionReady: false);

        Assert.Equal(DownloadIdentity.Bot, route.Identity);
        Assert.Equal(4242, route.MessageId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Choose_NeverSendsASavedMessagesIdToTheBot(bool userSessionReady)
    {
        // message_id is the account's own numbering there, so the bot would fetch someone
        // else's message with it. Reading those files back has to go through the account.
        var route = DownloadRouting.Choose(File(storagePeer: "saved"), userSessionReady);

        Assert.Equal(DownloadIdentity.SavedMessages, route.Identity);
        Assert.Equal(4242, route.MessageId);
    }
}
