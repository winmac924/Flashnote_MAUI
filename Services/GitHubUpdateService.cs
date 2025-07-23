using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Flashnote.Services;

public class GitHubUpdateService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GitHubUpdateService> _logger;
    
    // GitHubリポジトリの設定（実際の値に変更してください）
    private const string GITHUB_OWNER = "winmac924"; // GitHubユーザー名
    private const string GITHUB_REPO = "Flashnote_MAUI"; // リポジトリ名
    private const string GITHUB_API_BASE = "https://api.github.com";

    public GitHubUpdateService(HttpClient httpClient, ILogger<GitHubUpdateService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        
        // GitHub API用のUser-Agentヘッダーを設定（必須）
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Flashnote-MAUI-UpdateClient");
    }

    public async Task<UpdateInfo?> CheckForUpdatesAsync()
    {
        try
        {
            var currentVersion = VersionHelper.GetCurrentVersion();
            var latestRelease = await GetLatestReleaseAsync();

            if (latestRelease != null && IsNewVersionAvailable(currentVersion, latestRelease.TagName))
            {
                return new UpdateInfo
                {
                    IsUpdateAvailable = true,
                    LatestVersion = latestRelease.TagName,
                    DownloadUrl = GetExeDownloadUrl(latestRelease),
                    ReleaseNotes = latestRelease.Body ?? "新しいバージョンが利用可能です",
                    ReleaseDate = latestRelease.PublishedAt
                };
            }

            return new UpdateInfo { IsUpdateAvailable = false };
        }
        catch (Exception ex)
        {
            // ネットワークエラーは警告レベルでログ出力
            if (ex.Message.Contains("No such host") || ex.Message.Contains("network") || ex is System.Net.Http.HttpRequestException)
            {
                _logger.LogWarning("アップデート確認でネットワークエラー: オフライン状態のため、アップデート確認をスキップします");
            }
            else
            {
            _logger.LogError(ex, "GitHub からのアップデート確認中にエラーが発生しました");
            }
            return null;
        }
    }

    private async Task<GitHubRelease?> GetLatestReleaseAsync()
    {
        try
        {
            var url = $"{GITHUB_API_BASE}/repos/{GITHUB_OWNER}/{GITHUB_REPO}/releases/latest";
            _logger.LogInformation("GitHub API リクエスト: {Url}", url);
            
            // タイムアウト付きでリクエスト
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                var response = await _httpClient.GetStringAsync(url, cts.Token);
            
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            };
            
            var release = JsonSerializer.Deserialize<GitHubRelease>(response, options);
            _logger.LogInformation("最新リリース取得成功: {TagName}", release?.TagName);
            
            return release;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("GitHub API リクエストがタイムアウトしました。");
            return null;
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("404"))
        {
            _logger.LogInformation("GitHubリポジトリにリリースがまだ作成されていません。初回リリースを作成してください。");
            return null;
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("No such host"))
        {
            _logger.LogWarning("ネットワーク接続エラー: オフライン状態のため、アップデート確認をスキップします。");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "GitHub API リクエストが失敗しました。ネットワーク接続またはリポジトリ設定を確認してください。");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "予期しないエラーが発生しました");
            return null;
        }
    }

    private string? GetExeDownloadUrl(GitHubRelease release)
    {
        // .exe ファイルを最優先で検索
        var exeAsset = release.Assets?.FirstOrDefault(asset => 
            asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

        if (exeAsset != null)
        {
            _logger.LogInformation("EXEファイルを発見: {FileName}", exeAsset.Name);
            return exeAsset.BrowserDownloadUrl;
        }

        // .msix ファイルを検索（後方互換性）
        var msixAsset = release.Assets?.FirstOrDefault(asset => 
            asset.Name.EndsWith(".msix", StringComparison.OrdinalIgnoreCase));

        if (msixAsset != null)
        {
            _logger.LogInformation("MSIXファイルを発見: {FileName}", msixAsset.Name);
            return msixAsset.BrowserDownloadUrl;
        }

        // .zip ファイルも検索（実行ファイルが含まれている可能性）
        var zipAsset = release.Assets?.FirstOrDefault(asset => 
            asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
            asset.Name.Contains("windows", StringComparison.OrdinalIgnoreCase));

        if (zipAsset != null)
        {
            _logger.LogInformation("ZIPファイルを発見: {FileName}", zipAsset.Name);
            return zipAsset.BrowserDownloadUrl;
        }

        _logger.LogWarning("ダウンロード可能なファイルが見つかりませんでした。利用可能なアセット: {Assets}", 
            string.Join(", ", release.Assets?.Select(a => a.Name) ?? new List<string>()));
        
        return null;
    }

    private bool IsNewVersionAvailable(string currentVersion, string latestVersion)
    {
        try
        {
            // "v1.0.0" -> "1.0.0" の形式変換
            var cleanCurrent = currentVersion.TrimStart('v');
            var cleanLatest = latestVersion.TrimStart('v');
            
            var current = Version.Parse(cleanCurrent);
            var latest = Version.Parse(cleanLatest);
            
            var isNewer = latest > current;
            _logger.LogInformation("バージョン比較: 現在={Current}, 最新={Latest}, 新しい={IsNewer}", 
                currentVersion, latestVersion, isNewer);
            
            return isNewer;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "バージョン比較中にエラー: current={Current}, latest={Latest}", 
                currentVersion, latestVersion);
            return false;
        }
    }

    public async Task<bool> DownloadAndInstallUpdateAsync(string downloadUrl, IProgress<DownloadProgress>? progress = null)
    {
        try
        {
            _logger.LogInformation("アップデートをダウンロード中: {Url}", downloadUrl);
            
            var fileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
            var tempPath = Path.Combine(Path.GetTempPath(), fileName);
            
            progress?.Report(new DownloadProgress 
            { 
                Status = "ダウンロードを開始しています...",
                ProgressPercentage = 0,
                Detail = $"ファイル: {fileName}"
            });
            
            // タイムアウト設定（5分）
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            
            // プログレス付きダウンロード
            using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            response.EnsureSuccessStatusCode();
            
            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            var downloadedBytes = 0L;
            
            progress?.Report(new DownloadProgress 
            { 
                Status = "ファイルをダウンロード中...",
                ProgressPercentage = 0,
                Detail = $"サイズ: {FormatBytes(totalBytes)}"
            });
            
            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write);
            
            var buffer = new byte[8192];
            int bytesRead;
            var lastProgressReport = DateTime.Now;
            
            _logger.LogInformation("ダウンロード開始: 合計サイズ {TotalBytes} bytes", totalBytes);
            
            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cts.Token)) > 0)
            {
                try
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, cts.Token);
                    downloadedBytes += bytesRead;
                    
                    // 進捗報告を1秒に1回に制限してパフォーマンスを改善
                    var now = DateTime.Now;
                    if ((now - lastProgressReport).TotalSeconds >= 1.0 || downloadedBytes == totalBytes)
                    {
                        if (totalBytes > 0)
                        {
                            var progressPercentage = (double)downloadedBytes / totalBytes;
                            _logger.LogInformation("ダウンロード進行状況: {Progress:F1}% ({Downloaded}/{Total})", 
                                progressPercentage * 100, FormatBytes(downloadedBytes), FormatBytes(totalBytes));
                            
                            progress?.Report(new DownloadProgress 
                            { 
                                Status = "ファイルをダウンロード中...",
                                ProgressPercentage = progressPercentage,
                                Detail = $"{FormatBytes(downloadedBytes)} / {FormatBytes(totalBytes)}"
                            });
                        }
                        else
                        {
                            _logger.LogInformation("ダウンロード進行中: {Downloaded} bytes", FormatBytes(downloadedBytes));
                            
                            progress?.Report(new DownloadProgress 
                            { 
                                Status = "ファイルをダウンロード中...",
                                ProgressPercentage = 0.5, // サイズ不明の場合は50%で固定
                                Detail = $"{FormatBytes(downloadedBytes)} ダウンロード済み"
                            });
                        }
                        lastProgressReport = now;
                    }
                }
                catch (Exception writeEx)
                {
                    _logger.LogError(writeEx, "ファイル書き込み中にエラーが発生しました");
                    throw;
                }
            }
            
            _logger.LogInformation("ダウンロードループ完了: {TotalDownloaded} bytes", downloadedBytes);
            
            _logger.LogInformation("ダウンロード完了: {Path}", tempPath);
            
            progress?.Report(new DownloadProgress 
            { 
                Status = "ダウンロード完了。インストールを準備中...",
                ProgressPercentage = 1.0,
                Detail = "ファイルの準備中"
            });
            
            // EXEファイルの場合、直接実行
            if (fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                progress?.Report(new DownloadProgress 
                { 
                    Status = "アップデートをインストール中...",
                    ProgressPercentage = 1.0,
                    Detail = "アプリケーションを更新しています"
                });
                return await InstallExeAsync(tempPath);
            }

            // MSIXファイルの場合、直接インストール
            if (fileName.EndsWith(".msix", StringComparison.OrdinalIgnoreCase))
            {
                progress?.Report(new DownloadProgress 
                { 
                    Status = "MSIXパッケージをインストール中...",
                    ProgressPercentage = 1.0,
                    Detail = "パッケージを更新しています"
                });
                return await InstallMsixAsync(tempPath);
            }
            
            // ZIPファイルの場合、解凍して実行ファイルを探す
            if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                progress?.Report(new DownloadProgress 
                { 
                    Status = "ZIPファイルを展開中...",
                    ProgressPercentage = 1.0,
                    Detail = "ファイルを展開しています"
                });
                return await ExtractAndInstallFromZipAsync(tempPath);
            }
            
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "アップデートのダウンロード・インストール中にエラーが発生しました");
            return false;
        }
    }

    private string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    private async Task<bool> InstallExeAsync(string exePath)
    {
        try
        {
            _logger.LogInformation("EXEアップデートを準備中: {Path}", exePath);
            
            // 現在の実行ファイルのパスを取得
            var currentExePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(currentExePath))
            {
                _logger.LogError("現在の実行ファイルパスを取得できませんでした");
                return false;
            }

            // ダウンロードしたファイルの存在確認
            if (!File.Exists(exePath))
            {
                _logger.LogError("ダウンロードしたファイルが見つかりません: {Path}", exePath);
                return false;
            }

            // バックアップファイル名
            var backupPath = currentExePath + ".backup";
            
            // アップデート用バッチファイルを作成（英語メッセージでエンコーディング問題を回避）
            var batchPath = Path.Combine(Path.GetTempPath(), "Flashnote_Update.bat");
            var batchContent = $@"@echo off
title Flashnote Update
echo.
echo   Flashnote Update in Progress...
echo.

REM Wait for application to completely exit
echo Waiting for application to exit...
timeout /t 2 /nobreak >nul

REM Backup current file
if exist ""{currentExePath}"" (
    echo Backing up current version...
    move ""{currentExePath}"" ""{backupPath}""
    if errorlevel 1 (
        echo ERROR: Failed to backup current file
        pause
        exit /b 1
    )
)

REM Deploy new file
echo Deploying new version...
move ""{exePath}"" ""{currentExePath}""
if errorlevel 1 (
    echo ERROR: Failed to deploy new file
    echo Restoring backup file...
    move ""{backupPath}"" ""{currentExePath}""
    pause
    exit /b 1
)

echo Update completed! Starting new version...
timeout /t 1 /nobreak >nul

REM Start new version
start """" ""{currentExePath}""

REM Delete batch file itself
del ""%~f0""
";

            await File.WriteAllTextAsync(batchPath, batchContent, System.Text.Encoding.UTF8);
            _logger.LogInformation("アップデートバッチファイルを作成: {BatchPath}", batchPath);

            // バッチファイルを実行
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = batchPath,
                UseShellExecute = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Normal, // ユーザーに進行状況を表示
                CreateNoWindow = false
            };
            
            _logger.LogInformation("アップデート用バッチファイルを実行します");
            
            var process = System.Diagnostics.Process.Start(startInfo);
            if (process == null)
            {
                _logger.LogError("バッチファイルの実行に失敗しました");
                return false;
            }
            
            _logger.LogInformation("アプリケーションを終了します");
            
            // 少し待ってからアプリケーションを終了
            await Task.Delay(500);
            
            // UIスレッドで安全にアプリケーションを終了
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    // 少し待ってから終了
                    await Task.Delay(100);
                    Application.Current?.Quit();
                }
                catch
                {
                    // フォールバック: 少し待ってから強制終了
                    await Task.Delay(200);
                    Environment.Exit(0);
                }
            });
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EXE インストール中にエラーが発生しました");
            return false;
        }
    }

    private async Task<bool> InstallMsixAsync(string msixPath)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = msixPath,
                UseShellExecute = true,
                Verb = "runas" // 管理者権限で実行
            };
            
            System.Diagnostics.Process.Start(startInfo);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MSIX インストール中にエラーが発生しました");
            return false;
        }
    }

    private async Task<bool> ExtractAndInstallFromZipAsync(string zipPath)
    {
        try
        {
            var extractPath = Path.Combine(Path.GetTempPath(), "Flashnote_Update");
            if (Directory.Exists(extractPath))
                Directory.Delete(extractPath, true);
                
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractPath);
            
            // MSIXファイルを検索
            var msixFiles = Directory.GetFiles(extractPath, "*.msix", SearchOption.AllDirectories);
            if (msixFiles.Length > 0)
            {
                return await InstallMsixAsync(msixFiles[0]);
            }
            
            _logger.LogWarning("ZIP内にMSIXファイルが見つかりませんでした");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ZIP展開中にエラーが発生しました");
            return false;
        }
    }
}

// GitHub Releases API のレスポンス構造
public class GitHubRelease
{
    public string TagName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool Prerelease { get; set; }
    public DateTime PublishedAt { get; set; }
    public List<GitHubAsset>? Assets { get; set; }
}

public class GitHubAsset
{
    public string Name { get; set; } = string.Empty;
    public string BrowserDownloadUrl { get; set; } = string.Empty;
    public long Size { get; set; }
    public string ContentType { get; set; } = string.Empty;
}

public class DownloadProgress
{
    public string Status { get; set; } = string.Empty;
    public double ProgressPercentage { get; set; }
    public string Detail { get; set; } = string.Empty;
} 