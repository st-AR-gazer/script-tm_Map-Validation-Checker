namespace MapValidationChecker.CharacterizationTests;

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = Directory.CreateTempSubdirectory("map-validation-checker-tests-").FullName;
    }

    public string Path { get; }

    public string GetPath(string relativePath) => System.IO.Path.Combine(Path, relativePath);

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }
}
