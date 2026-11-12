using System.Text.Json;
using System.Text.Json.Serialization;

namespace HoyoFileClean;

// Wire names and numeric meanings belong to the official protocol, not UI policy.
internal sealed record OfficialResponse(
    [property: JsonPropertyName("retcode")] long Retcode,
    [property: JsonPropertyName("data")] JsonElement Data = default);

internal sealed record OfficialBranches(
    [property: JsonPropertyName("game_branches")] IReadOnlyList<OfficialGameBranch> Games);

internal sealed record OfficialGameIdentity(
    [property: JsonPropertyName("biz")] string Biz,
    [property: JsonPropertyName("id")] string? Id = null);

internal sealed record OfficialGameBranch(
    [property: JsonPropertyName("game")] OfficialGameIdentity Game,
    [property: JsonPropertyName("main")] OfficialBuildRequest? Main = null);

internal sealed record OfficialBuildRequest(
    [property: JsonPropertyName("tag"), JsonPropertyOrder(3)] string Tag,
    [property: JsonPropertyName("branch"), JsonPropertyOrder(0)] string? Branch = null,
    [property: JsonPropertyName("password"), JsonPropertyOrder(1)] string? Password = null,
    [property: JsonPropertyName("package_id"), JsonPropertyOrder(2)] string? PackageId = null);

internal sealed record OfficialBuild(
    [property: JsonPropertyName("tag")] string Tag,
    [property: JsonPropertyName("build_id")] string BuildId,
    [property: JsonPropertyName("manifests")] IReadOnlyList<OfficialManifest> Manifests);

internal sealed record OfficialManifest(
    [property: JsonPropertyName("matching_field")] string MatchingField,
    [property: JsonPropertyName("category_name")] string CategoryName,
    [property: JsonPropertyName("manifest")] OfficialManifestFile Manifest,
    [property: JsonPropertyName("manifest_download")] OfficialManifestDownload Download);

internal sealed record OfficialManifestFile(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("checksum")] string Checksum,
    [property: JsonPropertyName("uncompressed_size")] long UncompressedSize,
    [property: JsonPropertyName("compressed_size")] long? CompressedSize = null);

internal sealed record OfficialManifestDownload(
    [property: JsonPropertyName("url_prefix")] string UrlPrefix,
    [property: JsonPropertyName("url_suffix")] string? UrlSuffix,
    [property: JsonPropertyName("compression")] long Compression,
    [property: JsonPropertyName("encryption")] long Encryption);

internal enum OfficialCompression { None = 0, Zstandard = 1 }
internal static class OfficialProtocol
{
    internal const long NoEncryption = 0;
    internal const int FileAsset = 0;
    internal const string LauncherIdParameter = "launcher_id";
    internal static string BuildQuery(OfficialBuildRequest request)
    {
        JsonElement fields = JsonSerializer.SerializeToElement(request);
        return string.Join("&", fields.EnumerateObject().Select(field =>
            field.Name + "=" + Uri.EscapeDataString(field.Value.GetString() is { Length: > 0 } value
                ? value : throw new FormatException())));
    }
}
