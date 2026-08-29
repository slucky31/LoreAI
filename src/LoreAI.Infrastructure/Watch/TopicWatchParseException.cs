namespace LoreAI.Infrastructure.Watch;

public sealed class TopicWatchParseException : Exception
{
    public TopicWatchParseException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
