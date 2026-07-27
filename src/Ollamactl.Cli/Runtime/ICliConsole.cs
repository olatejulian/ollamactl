namespace Ollamactl.Cli.Runtime;

public interface ICliConsole
{
    TextWriter StandardOutput { get; }

    TextWriter StandardError { get; }
}

public sealed class SystemCliConsole : ICliConsole
{
    public TextWriter StandardOutput => Console.Out;

    public TextWriter StandardError => Console.Error;
}
