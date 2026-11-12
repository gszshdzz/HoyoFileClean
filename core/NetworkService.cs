using System.Text.Json;

namespace HoyoFileClean;

public sealed record NetworkService(GenshinChannel Channel, string Name, Uri BranchUrl, Uri BuildUrl, string DefaultBiz)
{
    internal static IReadOnlyList<NetworkService> Load(string? configurationPath = null)
    {
        string path = configurationPath ?? Path.Combine(AppContext.BaseDirectory, "config", "network-services.json");
        try
        {
            using Stream input = File.OpenRead(path);
            NetworkService[] services = JsonSerializer.Deserialize<NetworkService[]>(input)
                ?? throw new InvalidDataException("网络服务配置为空。");
            if (services.Length == 0 || services.Any(service => service is null)
                || services.Select(service => service.Channel).Distinct().Count() != services.Length
                || services.Any(service => string.IsNullOrWhiteSpace(service.Name) || string.IsNullOrWhiteSpace(service.DefaultBiz)
                    || !IsHttpsEndpoint(service.BranchUrl) || !IsHttpsEndpoint(service.BuildUrl) || service.BuildUrl.Query.Length != 0))
                throw new InvalidDataException("网络服务配置无效，请检查服务标识、名称及 HTTPS 接口地址。");
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
