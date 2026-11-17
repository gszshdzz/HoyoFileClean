using System.Text.Json;

namespace HoyoFileClean;

public static class JsonFileOutput
{
    private static readonly JsonSerializerOptions writerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static void WriteNew<T>(string outputPath, T value)
    {
        string fullPath = Path.GetFullPath(outputPath);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(value, writerOptions);
        string partialPath = Path.Combine(
            Path.GetDirectoryName(fullPath)!, $"{Guid.NewGuid():N}.partial");
        using (var output = new FileStream(partialPath, FileMode.CreateNew, FileAccess.Write))
        {
            output.Write(json);
        }
        File.Move(partialPath, fullPath, overwrite: false);
    }
}
