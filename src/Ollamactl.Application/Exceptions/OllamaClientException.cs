namespace Ollamactl.Application.Exceptions;

public sealed class OllamaClientException : Exception
{
    public OllamaClientException(string message)
        : base(message)
    {
    }

    public OllamaClientException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
