using Ado.Domain;

namespace Ado.Infrastructure;

public sealed class DownloadTarget
{
    public string Path { get; }
    private DownloadTarget(string path) => Path = path;

    public static DownloadTarget Validate(string? destination)
    {
        if (string.IsNullOrWhiteSpace(destination) || destination.Any(char.IsControl)) throw Invalid();
        try
        {
            string path = System.IO.Path.GetFullPath(destination);
            if (OperatingSystem.IsWindows())
            {
                if (path.StartsWith("\\\\", StringComparison.Ordinal) || path[2..].Contains(':')) throw Invalid();
                foreach (string part in path[3..].Split(System.IO.Path.DirectorySeparatorChar))
                {
                    string stem = part.Split('.')[0].ToUpperInvariant();
                    if (part.EndsWith(' ') || part.EndsWith('.') || stem is "CON" or "PRN" or "AUX" or "NUL"
                        || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && char.IsDigit(stem[3]))) throw Invalid();
                }
            }
            var target = new DownloadTarget(path);
            target.Check();
            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { throw Invalid(); }
    }

    public void Check()
    {
        try
        {
            _ = File.GetAttributes(Path);
            throw new AdoException("destination_exists", "The destination already exists; choose a new file. Overwrite is not supported.", ExitCode.Safety);
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { throw Invalid(); }
        var parent = new DirectoryInfo(System.IO.Path.GetDirectoryName(Path)!);
        for (DirectoryInfo? current = parent; current is not null; current = current.Parent)
            if (!current.Exists || (current.Attributes & FileAttributes.ReparsePoint) != 0) throw Invalid();
    }

    internal string TemporaryPath() => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, ".ado-" + Guid.NewGuid().ToString("N") + ".partial");
    private static AdoException Invalid() => new("invalid_destination",
        "Choose a new local file in an existing directory without symlink/reparse-point ancestors. Network/device paths and alternate streams are not supported.", ExitCode.Safety);
}
