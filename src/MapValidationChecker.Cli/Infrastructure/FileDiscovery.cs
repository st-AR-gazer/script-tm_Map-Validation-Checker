namespace MapValidationChecker.Cli.Infrastructure;

internal static class FileDiscovery
{
    public static IEnumerable<string> Enumerate(string path, bool recursive)
    {
        if (File.Exists(path))
        {
            yield return path;
            yield break;
        }

        if (!Directory.Exists(path))
            yield break;

        var searchOption = recursive
            ? SearchOption.AllDirectories
            : SearchOption.TopDirectoryOnly;
        foreach (var file in Directory.EnumerateFiles(path, "*", searchOption))
            yield return file;
    }
}
