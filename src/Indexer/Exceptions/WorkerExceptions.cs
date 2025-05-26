namespace Indexer.Exceptions;

public class UnknownScriptLanguageException : Exception
{
    public string? FileName { get; }

    public UnknownScriptLanguageException(string? fileName = null)
        : base("Unable to determine script language")
    {
        FileName = fileName;
    }

    public UnknownScriptLanguageException(string message, string? fileName = null)
        : base(message)
    {
        FileName = fileName;
    }
}