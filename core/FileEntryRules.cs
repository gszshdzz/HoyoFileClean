namespace HoyoFileClean;

// File semantics shared by format adapters; format and collection checks stay with their readers.
internal static class FileEntryRules
{
    internal static ManifestFile Normalize(ManifestFile? file, bool hasHashes, string? input = null,
        string? metadataPath = null)
    {
        if (file is null || file.Size < 0 || (hasHashes ? !IsMd5(file.Hash) : file.Hash is not null))
            throw new CoreException(CoreErrorCode.FileMetadataInvalid, metadataPath, input);
        return file with
        {
            Path = FilePathRules.NormalizeRelative(file.Path, input),
            Hash = file.Hash?.ToLowerInvariant()
        };
    }

    private static bool IsMd5(string? value)
        => value is { Length: 32 } && value.All(character => character is >= '0' and <= '9'
            or >= 'a' and <= 'f' or >= 'A' and <= 'F');
}
