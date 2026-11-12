using System.Text.Json;

namespace HoyoFileClean;

public sealed record NetworkService(string Id, string NameTextId, Uri BranchUrl, Uri BuildUrl, string LauncherId)
{
    internal Uri BranchRequestUrl => new($"{BranchUrl.AbsoluteUri}?{OfficialProtocol.LauncherIdParameter}={Uri.EscapeDataString(LauncherId)}");

    internal bool IsValid => !string.IsNullOrWhiteSpace(Id) && !string.IsNullOrWhiteSpace(NameTextId)
        && !string.IsNullOrWhiteSpace(LauncherId)
        && IsHttpsEndpoint(BranchUrl) && IsHttpsEndpoint(BuildUrl)
        && BranchUrl.Query.Length == 0 && BuildUrl.Query.Length == 0;

    internal static IReadOnlyList<NetworkService> Load(string? configurationPath = null)
    {
        string path = configurationPath ?? Path.Combine(AppContext.BaseDirectory, Maintenance.NetworkServicesRelativePath);
        try
        {
            using Stream input = File.OpenRead(path);
            NetworkService[] services = JsonSerializer.Deserialize<NetworkService[]>(input, new JsonSerializerOptions
            {
                RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true, AllowDuplicateProperties = false
            })
                ?? throw new InvalidDataException();
            if (services.Length == 0 || services.Any(service => service is null)
                || services.Select(service => service.Id).Distinct(StringComparer.Ordinal).Count() != services.Length
                || services.Any(service => !service.IsValid))
                throw new InvalidDataException();
            return Array.AsReadOnly(services);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            throw new CoreException(CoreErrorCode.NetworkConfigurationInvalid, path, innerException: error);
        }
    }

    internal static bool IsHttpsEndpoint(Uri? uri) => uri is { IsAbsoluteUri: true, Scheme: "https", IsDefaultPort: true }
        && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0;
}
