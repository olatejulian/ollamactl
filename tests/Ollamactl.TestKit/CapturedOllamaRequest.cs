namespace Ollamactl.TestKit;

public sealed record CapturedOllamaRequest(
    string Method,
    string Path,
    string QueryString,
    string Body,
    IReadOnlyDictionary<string, string[]> Headers);
