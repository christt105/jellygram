using Bot.Utils;
using Telegram.Bot.Types;
using TL;
using Xunit;

namespace Bot.Tests;

public class ForwardMetadataTests
{
    private static TL.Message TlMessageWithDocument(long documentId) => new()
    {
        media = new MessageMediaDocument
        {
            document = new TL.Document { id = documentId }
        }
    };

    [Fact]
    public void Extract_ReturnsNothingForAMessageThatWasNeverForwarded()
    {
        var info = ForwardMetadata.Extract(null, TlMessageWithDocument(123));

        Assert.Equal(123, info.DocumentId);
        Assert.Null(info.FromType);
        Assert.Null(info.FromId);
        Assert.Null(info.FromName);
        Assert.False(info.Hidden);
    }

    [Fact]
    public void Extract_ReadsUserForwards()
    {
        var origin = new MessageOriginUser
        {
            SenderUser = new Telegram.Bot.Types.User { Id = 42, FirstName = "Ana", LastName = "Perez" }
        };

        var info = ForwardMetadata.Extract(origin, TlMessageWithDocument(5794420050676948545));

        Assert.Equal(5794420050676948545, info.DocumentId);
        Assert.Equal("user", info.FromType);
        Assert.Equal("42", info.FromId);
        Assert.Equal("Ana Perez", info.FromName);
        Assert.False(info.Hidden);
    }

    [Fact]
    public void Extract_HiddenForwardsHaveNoIdButKeepTheDisplayName()
    {
        var origin = new MessageOriginHiddenUser { SenderUserName = "Someone" };

        var info = ForwardMetadata.Extract(origin, TlMessageWithDocument(99));

        Assert.Equal("hidden_user", info.FromType);
        Assert.Null(info.FromId);
        Assert.Equal("Someone", info.FromName);
        Assert.True(info.Hidden);
    }

    [Fact]
    public void Extract_ReadsGroupForwards()
    {
        var origin = new MessageOriginChat
        {
            SenderChat = new Telegram.Bot.Types.Chat { Id = -1001234567890, Title = "Las Cositas 3: La venganza" }
        };

        var info = ForwardMetadata.Extract(origin, TlMessageWithDocument(1));

        Assert.Equal("chat", info.FromType);
        Assert.Equal("-1001234567890", info.FromId);
        Assert.Equal("Las Cositas 3: La venganza", info.FromName);
        Assert.False(info.Hidden);
    }

    [Fact]
    public void Extract_ReadsChannelForwards()
    {
        var origin = new MessageOriginChannel
        {
            Chat = new Telegram.Bot.Types.Chat { Id = -1009876543210, Title = "Doraemon [Castellano] [1979] [AVI]" },
            MessageId = 36121
        };

        var info = ForwardMetadata.Extract(origin, TlMessageWithDocument(2));

        Assert.Equal("channel", info.FromType);
        Assert.Equal("-1009876543210", info.FromId);
        Assert.Equal("Doraemon [Castellano] [1979] [AVI]", info.FromName);
        Assert.False(info.Hidden);
    }

    [Fact]
    public void Extract_LeavesDocumentIdNullWhenTheMessageHasNoDocument()
    {
        var tlMessage = new TL.Message { media = new MessageMediaPhoto() };

        var info = ForwardMetadata.Extract(null, tlMessage);

        Assert.Null(info.DocumentId);
    }

    [Fact]
    public void Extract_LeavesDocumentIdNullWhenThereIsNoRawMessage()
    {
        var info = ForwardMetadata.Extract(null, null);

        Assert.Null(info.DocumentId);
    }
}
