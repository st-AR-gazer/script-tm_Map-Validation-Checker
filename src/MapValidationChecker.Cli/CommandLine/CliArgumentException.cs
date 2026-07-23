namespace MapValidationChecker.Cli.CommandLine;

internal sealed class CliArgumentException : Exception
{
    public CliArgumentException(string message)
        : base(message)
    {
    }
}
