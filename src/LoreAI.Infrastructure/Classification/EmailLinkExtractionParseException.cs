namespace LoreAI.Infrastructure.Classification;

public sealed class EmailLinkExtractionParseException : Exception
{
    public EmailLinkExtractionParseException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
