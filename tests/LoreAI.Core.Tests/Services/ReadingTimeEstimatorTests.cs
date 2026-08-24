using LoreAI.Core.Services;

namespace LoreAI.Core.Tests.Services;

public class ReadingTimeEstimatorTests
{
    [Fact]
    public void EstimateMinutes_NullWordCount_ReturnsNull()
    {
        Assert.Null(ReadingTimeEstimator.EstimateMinutes(null));
    }

    [Fact]
    public void EstimateMinutes_ZeroWordCount_ReturnsNull()
    {
        Assert.Null(ReadingTimeEstimator.EstimateMinutes(0));
    }

    [Theory]
    [InlineData(220, 1)]
    [InlineData(440, 2)]
    [InlineData(221, 2)] // Arrondi au supérieur : une minute entamée compte pour une minute pleine.
    [InlineData(1, 1)]
    public void EstimateMinutes_RoundsUpToTheNextMinute(int wordCount, int expectedMinutes)
    {
        Assert.Equal(expectedMinutes, ReadingTimeEstimator.EstimateMinutes(wordCount));
    }
}
