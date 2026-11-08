using System.Text.Json;

namespace HoyoFileClean;

internal static class JsonFileOutput
{
    public static void WriteNew<T>(string outputPath, T value)
    {
        string fullPath = Path.GetFullPath(outputPath);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(value, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
        string partialPath = Path.Combine(
            Path.GetDirectoryName(fullPath)!, $"{Guid.NewGuid():N}.partial");
        using (var output = new FileStream(partialPath, FileMode.CreateNew, FileAccess.Write))
        {
            output.Write(json);
        }
        File.Move(partialPath, fullPath, overwrite: false);
    }
}
