namespace MapValidationChecker.Cli.Infrastructure;

internal static class GbxFile
{
    public static bool HasMagic(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> magic = stackalloc byte[3];
            var read = stream.Read(magic);
            return read == 3 && magic[0] == 0x47 && magic[1] == 0x42 && magic[2] == 0x58;
        }
        catch
        {
            return false;
        }
    }
}
