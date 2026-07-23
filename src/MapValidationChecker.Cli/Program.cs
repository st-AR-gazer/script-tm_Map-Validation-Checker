using MapValidationChecker.Cli.Application;

namespace MapValidationChecker.Cli;

internal static class Program
{
    private static int Main(string[] args) =>
        new CliApplication(Console.Out, Console.Error).Run(args);
}
