using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Flashnote.Services;

public class ConfigurationService
{
    private readonly ILogger<ConfigurationService> _logger;
    private AppSettings? _settings;

    public ConfigurationService(ILogger<ConfigurationService> logger)
    {
        _logger = logger;
        LoadConfiguration();
    }

    private void LoadConfiguration()
    {
        try
        {
            // まず環境変数から設定を読み込み
            _settings = LoadFromEnvironmentVariables();

            // appsettings.jsonファイルがある場合は上書き
            try
            {
                using var stream = FileSystem.OpenAppPackageFileAsync("appsettings.json").Result;
                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();
                
                var fileSettings = JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                // ファイルの設定で環境変数の設定を上書き
                if (fileSettings != null)
                {
                    _settings = fileSettings;
                }

                _logger.LogInformation("設定ファイルの読み込みが完了しました");
            }
            catch (Exception fileEx)
            {
                _logger.LogWarning(fileEx, "設定ファイルが見つかりません。環境変数の設定を使用します");
            }

            // 設定の検証
            ValidateConfiguration();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "設定の読み込みに失敗しました");
            throw new InvalidOperationException("設定が正しく構成されていません。appsettings.jsonファイルまたは環境変数を確認してください。", ex);
        }
    }

    private AppSettings LoadFromEnvironmentVariables()
    {
        return new AppSettings
        {
            Firebase = new FirebaseSettings
            {
                ApiKey = Environment.GetEnvironmentVariable("FIREBASE_API_KEY") ?? string.Empty,
                AuthDomain = Environment.GetEnvironmentVariable("FIREBASE_AUTH_DOMAIN") ?? string.Empty
            },
            AzureStorage = new AzureStorageSettings
            {
                ConnectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING") ?? string.Empty
            }
        };
    }

    private void ValidateConfiguration()
    {
        if (_settings?.Firebase == null || 
            string.IsNullOrEmpty(_settings.Firebase.ApiKey) || 
            string.IsNullOrEmpty(_settings.Firebase.AuthDomain))
        {
            throw new InvalidOperationException("Firebase設定が不完全です。APIキーとAuthDomainを設定してください。");
        }

        if (_settings?.AzureStorage == null || 
            string.IsNullOrEmpty(_settings.AzureStorage.ConnectionString))
        {
            throw new InvalidOperationException("Azure Storage設定が不完全です。接続文字列を設定してください。");
        }
    }

    public string GetFirebaseApiKey()
    {
        if (string.IsNullOrEmpty(_settings?.Firebase?.ApiKey))
        {
            throw new InvalidOperationException("Firebase APIキーが設定されていません");
        }
        return _settings.Firebase.ApiKey;
    }

    public string GetFirebaseAuthDomain()
    {
        if (string.IsNullOrEmpty(_settings?.Firebase?.AuthDomain))
        {
            throw new InvalidOperationException("Firebase AuthDomainが設定されていません");
        }
        return _settings.Firebase.AuthDomain;
    }

    public string GetAzureStorageConnectionString()
    {
        if (string.IsNullOrEmpty(_settings?.AzureStorage?.ConnectionString))
        {
            throw new InvalidOperationException("Azure Storage接続文字列が設定されていません");
        }
        return _settings.AzureStorage.ConnectionString;
    }
}

public class AppSettings
{
    public FirebaseSettings? Firebase { get; set; }
    public AzureStorageSettings? AzureStorage { get; set; }
}

public class FirebaseSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string AuthDomain { get; set; } = string.Empty;
}

public class AzureStorageSettings
{
    public string ConnectionString { get; set; } = string.Empty;
} 