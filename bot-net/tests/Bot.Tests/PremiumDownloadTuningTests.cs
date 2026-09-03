using Bot.Utils;
using Xunit;

namespace Bot.Tests;

public class PremiumDownloadTuningTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("three")]
    public void ParseConnections_FallsBackToTheLabDefault(string? value)
    {
        Assert.Equal(PremiumDownloadTuning.DefaultConnections, PremiumDownloadTuning.ParseConnections(value));
    }

    [Fact]
    public void ParseConnections_TakesAValidCount()
    {
        Assert.Equal(1, PremiumDownloadTuning.ParseConnections("1"));
        Assert.Equal(5, PremiumDownloadTuning.ParseConnections("5"));
    }

    [Fact]
    public void ParseConnections_ClampsAbsurdCounts()
    {
        Assert.Equal(PremiumDownloadTuning.MaxConnections, PremiumDownloadTuning.ParseConnections("64"));
    }

    [Fact]
    public void UseMultipleConnections_NeedsBothASessionAndMoreThanOneConnection()
    {
        Assert.True(PremiumDownloadTuning.UseMultipleConnections(sessionReady: true, connections: 3));
        Assert.False(PremiumDownloadTuning.UseMultipleConnections(sessionReady: true, connections: 1));
        Assert.False(PremiumDownloadTuning.UseMultipleConnections(sessionReady: true, connections: 0));
        Assert.False(PremiumDownloadTuning.UseMultipleConnections(sessionReady: false, connections: 3));
    }

    [Fact]
    public void Connections_ReadsTheEnvironmentVariable()
    {
        var previous = Environment.GetEnvironmentVariable(PremiumDownloadTuning.ConnectionsVariable);
        try
        {
            Environment.SetEnvironmentVariable(PremiumDownloadTuning.ConnectionsVariable, "4");
            Assert.Equal(4, PremiumDownloadTuning.Connections);

            Environment.SetEnvironmentVariable(PremiumDownloadTuning.ConnectionsVariable, null);
            Assert.Equal(PremiumDownloadTuning.DefaultConnections, PremiumDownloadTuning.Connections);
        }
        finally
        {
            Environment.SetEnvironmentVariable(PremiumDownloadTuning.ConnectionsVariable, previous);
        }
    }
}
