namespace HoyoFileClean;

public sealed record NetworkService(string Id, string NameTextId, Uri BranchUrl, Uri BuildUrl, string LauncherId)
{
    internal Uri BranchRequestUrl => new($"{BranchUrl.AbsoluteUri}?{OfficialProtocol.LauncherIdParameter}={Uri.EscapeDataString(LauncherId)}");

    internal bool IsValid => !string.IsNullOrWhiteSpace(Id) && !string.IsNullOrWhiteSpace(NameTextId)
        && !string.IsNullOrWhiteSpace(LauncherId)
        && IsHttpsEndpoint(BranchUrl) && IsHttpsEndpoint(BuildUrl)
        && BranchUrl.Query.Length == 0 && BuildUrl.Query.Length == 0;

    internal static IReadOnlyList<NetworkService> Load(IEnumerable<NetworkService>? configuration = null)
    {
        NetworkService[] services = (configuration ?? NetworkServices.Create()).ToArray();
        if (services.Length == 0 || services.Any(service => service is null)
            || services.Select(service => service.Id).Distinct(StringComparer.Ordinal).Count() != services.Length
            || services.Any(service => !service.IsValid))
            throw new CoreException(CoreErrorCode.NetworkConfigurationInvalid, Maintenance.NetworkServicesRelativePath);
        return Array.AsReadOnly(services);
    }

    internal static bool IsHttpsEndpoint(Uri? uri) => uri is { IsAbsoluteUri: true, Scheme: "https", IsDefaultPort: true }
        && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0;
}
