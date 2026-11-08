namespace HoyoFileClean;

internal static class FilePathRules
{
    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        "COM¹", "COM²", "COM³", "LPT¹", "LPT²", "LPT³"
    };

    public static string NormalizeRelative(string path, string source)
    {
        string normalized = path.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized) || Path.IsPathRooted(normalized)
            || normalized.Split('/').Any(segment => segment.Length == 0
                || segment is "." or ".."
                || segment.EndsWith('.') || segment.EndsWith(' ')
                || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || ReservedDeviceNames.Contains(segment.Split('.')[0])))
        {
            throw new InvalidDataException($"{source} 文件路径不是安全的相对路径：{path}");
        }
        return normalized;
    }

    public static void ValidatePaths(IEnumerable<string> filePaths, string source)
    {
        string[] paths = filePaths.ToArray();
        var index = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
        {
            if (!index.Add(path))
            {
                throw new InvalidDataException($"{source} 中存在重复文件路径：{path}");
            }
        }
        foreach (string path in paths)
        {
            for (int separator = path.IndexOf('/'); separator >= 0;
                separator = path.IndexOf('/', separator + 1))
            {
                if (index.Contains(path[..separator]))
                {
                    throw new InvalidDataException($"{source} 中的文件同时被用作父目录：{path[..separator]}");
                }
            }
        }
    }
}
