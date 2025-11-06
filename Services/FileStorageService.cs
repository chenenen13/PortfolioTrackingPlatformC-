using System.Text.Json;
using TradingPlatform.Services.Abstractions;

namespace TradingPlatform.Services;

public class FileStorageService : IStorageService
{
    private static readonly JsonSerializerOptions _opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public async Task<T?> LoadAsync<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        await using var fs = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(fs, _opts);
    }

    public async Task SaveAsync<T>(string path, T data) where T : class
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        await using var fs = File.Create(path);
        await JsonSerializer.SerializeAsync(fs, data, _opts);
    }
}
