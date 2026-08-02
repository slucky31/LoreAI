namespace LoreAI.Infrastructure.Classification;

public sealed class ClassificationParseException : Exception
{
    public ClassificationParseException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
