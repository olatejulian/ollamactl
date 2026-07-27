namespace Ollamactl.Cli.Runtime;

public static class ExitCodes
{
    public const int Success = 0;
    public const int GeneralError = 1;
    public const int UsageError = 2;
    public const int Unavailable = 3;
    public const int Cancelled = 130;
}
