namespace HoyoFileClean;

public sealed record LocalManifestFile(string Name, string FullPath, long Size, DateTime LastWriteTimeUtc);

public static class ManifestDiscovery
{
    // Metadata only: filenames in launcher caches need not have an extension.
    public static IReadOnlyList<LocalManifestFile> Find(string directory)
        => new DirectoryInfo(directory).EnumerateFiles("*", new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.ReparsePoint
        })
        .Select(file => new LocalManifestFile(file.Name, file.FullName, file.Length, file.LastWriteTimeUtc))
        .OrderByDescending(file => file.LastWriteTimeUtc)
        .ThenBy(file => file.Name, StringComparer.Ordinal)
        .ToArray();
}
