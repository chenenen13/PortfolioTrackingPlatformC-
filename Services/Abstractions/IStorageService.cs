namespace TradingPlatform.Services.Abstractions;

public interface IStorageService
{
    Task<T?> LoadAsync<T>(string path) where T : class;
    Task SaveAsync<T>(string path, T data) where T : class;
}
