using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace Flashnote.Services;

/// <summary>
/// 差分更新機能を提供するサービス
/// </summary>
public class DifferentialUpdateService
{
    private readonly ILogger<DifferentialUpdateService> _logger;
    private readonly HttpClient _httpClient;

    public DifferentialUpdateService(HttpClient httpClient, ILogger<DifferentialUpdateService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// 差分更新ファイルの存在を確認
    /// </summary>
    public async Task<bool> CheckDifferentialUpdateAvailableAsync(string currentVersion, string targetVersion)
    {
        try
        {
            var diffFileName = $"Flashnote_MAUI_{currentVersion}_to_{targetVersion}.diff";
            var diffUrl = $"https://github.com/winmac924/Flashnote_MAUI/releases/download/v{targetVersion}/{diffFileName}";
            
            using var response = await _httpClient.HeadAsync(diffUrl);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "差分更新ファイルの確認に失敗: {CurrentVersion} → {TargetVersion}", currentVersion, targetVersion);
            return false;
        }
    }

    /// <summary>
    /// 差分更新をダウンロードして適用
    /// </summary>
    public async Task<bool> ApplyDifferentialUpdateAsync(string currentVersion, string targetVersion, IProgress<DownloadProgress>? progress = null)
    {
        try
        {
            _logger.LogInformation("差分更新を開始: {CurrentVersion} → {TargetVersion}", currentVersion, targetVersion);

            // 1. 差分ファイルをダウンロード
            var diffFileName = $"Flashnote_MAUI_{currentVersion}_to_{targetVersion}.diff";
            var diffUrl = $"https://github.com/winmac924/Flashnote_MAUI/releases/download/v{targetVersion}/{diffFileName}";
            
            progress?.Report(new DownloadProgress 
            { 
                Status = "差分ファイルをダウンロード中...",
                ProgressPercentage = 0.0,
                Detail = $"ファイル: {diffFileName}"
            });

            var diffPath = await DownloadDiffFileAsync(diffUrl, progress);
            if (string.IsNullOrEmpty(diffPath))
            {
                _logger.LogError("差分ファイルのダウンロードに失敗しました");
                return false;
            }

            // 2. 現在のEXEファイルのバックアップを作成
            var currentExePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(currentExePath))
            {
                _logger.LogError("現在の実行ファイルパスを取得できませんでした");
                return false;
            }

            var backupPath = currentExePath + ".backup";
            File.Copy(currentExePath, backupPath, true);
            _logger.LogInformation("バックアップファイルを作成: {BackupPath}", backupPath);

            // 3. 差分を適用して新しいEXEファイルを生成
            progress?.Report(new DownloadProgress 
            { 
                Status = "差分を適用中...",
                ProgressPercentage = 0.5,
                Detail = "新しいバージョンを生成中"
            });

            var success = await ApplyDiffAsync(currentExePath, diffPath, targetVersion);
            if (!success)
            {
                _logger.LogError("差分の適用に失敗しました");
                // バックアップから復元
                File.Copy(backupPath, currentExePath, true);
                return false;
            }

            // 4. 新しいバージョンの整合性を確認
            progress?.Report(new DownloadProgress 
            { 
                Status = "更新を確認中...",
                ProgressPercentage = 0.8,
                Detail = "ファイルの整合性を確認"
            });

            if (!await VerifyUpdatedFileAsync(currentExePath, targetVersion))
            {
                _logger.LogError("更新されたファイルの整合性確認に失敗しました");
                // バックアップから復元
                File.Copy(backupPath, currentExePath, true);
                return false;
            }

            // 5. 更新用バッチファイルを作成して実行
            progress?.Report(new DownloadProgress 
            { 
                Status = "更新を完了中...",
                ProgressPercentage = 1.0,
                Detail = "アプリケーションを再起動"
            });

            return await CreateAndExecuteUpdateBatchAsync(currentExePath, backupPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "差分更新中にエラーが発生しました");
            return false;
        }
    }

    /// <summary>
    /// 差分ファイルをダウンロード
    /// </summary>
    private async Task<string?> DownloadDiffFileAsync(string diffUrl, IProgress<DownloadProgress>? progress)
    {
        try
        {
            var fileName = Path.GetFileName(new Uri(diffUrl).LocalPath);
            var tempPath = Path.Combine(Path.GetTempPath(), fileName);

            using var response = await _httpClient.GetAsync(diffUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            var downloadedBytes = 0L;

            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write);

            var buffer = new byte[8192];
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead);
                downloadedBytes += bytesRead;

                if (totalBytes > 0)
                {
                    var progressPercentage = (double)downloadedBytes / totalBytes * 0.4; // 全体の40%まで
                    progress?.Report(new DownloadProgress 
                    { 
                        Status = "差分ファイルをダウンロード中...",
                        ProgressPercentage = progressPercentage,
                        Detail = $"サイズ: {FormatBytes(downloadedBytes)} / {FormatBytes(totalBytes)}"
                    });
                }
            }

            _logger.LogInformation("差分ファイルのダウンロード完了: {Path}", tempPath);
            return tempPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "差分ファイルのダウンロードに失敗しました");
            return null;
        }
    }

    /// <summary>
    /// 差分を適用して新しいEXEファイルを生成
    /// </summary>
    private async Task<bool> ApplyDiffAsync(string currentExePath, string diffPath, string targetVersion)
    {
        try
        {
            // 差分ファイルを解凍
            var extractPath = Path.Combine(Path.GetTempPath(), "Flashnote_Diff_Extract");
            if (Directory.Exists(extractPath))
                Directory.Delete(extractPath, true);

            ZipFile.ExtractToDirectory(diffPath, extractPath);

            // 差分情報を読み込み
            var diffInfoPath = Path.Combine(extractPath, "diff_info.json");
            if (!File.Exists(diffInfoPath))
            {
                _logger.LogError("差分情報ファイルが見つかりません");
                return false;
            }

            var diffInfo = System.Text.Json.JsonSerializer.Deserialize<DiffInfo>(
                await File.ReadAllTextAsync(diffInfoPath));

            if (diffInfo == null)
            {
                _logger.LogError("差分情報の解析に失敗しました");
                return false;
            }

            // 現在のEXEファイルを読み込み
            var currentExeBytes = await File.ReadAllBytesAsync(currentExePath);

            // 差分を適用
            var newExeBytes = ApplyBinaryDiff(currentExeBytes, diffInfo, extractPath);

            // 新しいEXEファイルを一時保存
            var newExePath = currentExePath + ".new";
            await File.WriteAllBytesAsync(newExePath, newExeBytes);

            // 元のファイルを置き換え
            File.Delete(currentExePath);
            File.Move(newExePath, currentExePath);

            _logger.LogInformation("差分の適用が完了しました");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "差分の適用に失敗しました");
            return false;
        }
    }

    /// <summary>
    /// バイナリ差分を適用
    /// </summary>
    private byte[] ApplyBinaryDiff(byte[] currentExeBytes, DiffInfo diffInfo, string extractPath)
    {
        var newExeBytes = new List<byte>();

        foreach (var chunk in diffInfo.Chunks)
        {
            switch (chunk.Type)
            {
                case "copy":
                    // 現在のファイルからコピー
                    var copyData = new byte[chunk.Length];
                    Array.Copy(currentExeBytes, chunk.Offset, copyData, 0, chunk.Length);
                    newExeBytes.AddRange(copyData);
                    break;

                case "insert":
                    // 新しいデータを挿入
                    var insertPath = Path.Combine(extractPath, chunk.FileName);
                    if (File.Exists(insertPath))
                    {
                        var insertData = File.ReadAllBytes(insertPath);
                        newExeBytes.AddRange(insertData);
                    }
                    break;

                case "replace":
                    // データを置き換え
                    var replacePath = Path.Combine(extractPath, chunk.FileName);
                    if (File.Exists(replacePath))
                    {
                        var replaceData = File.ReadAllBytes(replacePath);
                        newExeBytes.AddRange(replaceData);
                    }
                    break;
            }
        }

        return newExeBytes.ToArray();
    }

    /// <summary>
    /// 更新されたファイルの整合性を確認
    /// </summary>
    private async Task<bool> VerifyUpdatedFileAsync(string exePath, string targetVersion)
    {
        try
        {
            // ファイルの存在確認
            if (!File.Exists(exePath))
                return false;

            // ファイルサイズの確認（最小サイズ）
            var fileInfo = new FileInfo(exePath);
            if (fileInfo.Length < 1024 * 1024) // 1MB未満は異常
                return false;

            // バージョン情報の確認（簡易チェック）
            var exeBytes = await File.ReadAllBytesAsync(exePath);
            var versionString = targetVersion.Replace(".", "");
            var versionBytes = System.Text.Encoding.UTF8.GetBytes(versionString);
            
            // ファイル内にバージョン文字列が含まれているかチェック
            for (int i = 0; i <= exeBytes.Length - versionBytes.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < versionBytes.Length; j++)
                {
                    if (exeBytes[i + j] != versionBytes[j])
                    {
                        found = false;
                        break;
                    }
                }
                if (found)
                {
                    _logger.LogInformation("更新されたファイルの整合性確認が完了しました");
                    return true;
                }
            }

            _logger.LogWarning("更新されたファイルにバージョン情報が見つかりませんでした");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新されたファイルの整合性確認に失敗しました");
            return false;
        }
    }

    /// <summary>
    /// 更新用バッチファイルを作成して実行
    /// </summary>
    private async Task<bool> CreateAndExecuteUpdateBatchAsync(string exePath, string backupPath)
    {
        try
        {
            var batchPath = Path.Combine(Path.GetTempPath(), "Flashnote_DiffUpdate.bat");
            var batchContent = $@"@echo off
title Flashnote Differential Update
echo.
echo   Flashnote Differential Update in Progress...
echo.

REM Wait for application to completely exit
echo Waiting for application to exit...
timeout /t 2 /nobreak >nul

REM Start new version
echo Starting updated version...
start """" ""{exePath}""

REM Clean up backup after successful start
timeout /t 3 /nobreak >nul
if exist ""{backupPath}"" (
    del ""{backupPath}""
    echo Backup file cleaned up.
)

REM Delete batch file itself
del ""%~f0""
";

            await File.WriteAllTextAsync(batchPath, batchContent, System.Text.Encoding.UTF8);

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = batchPath,
                UseShellExecute = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Normal
            };

            System.Diagnostics.Process.Start(startInfo);

            // アプリケーションを終了
            await Task.Delay(500);
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Task.Delay(100);
                Application.Current?.Quit();
            });

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新用バッチファイルの作成・実行に失敗しました");
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
}

/// <summary>
/// 差分情報を表すクラス
/// </summary>
public class DiffInfo
{
    public string CurrentVersion { get; set; } = string.Empty;
    public string TargetVersion { get; set; } = string.Empty;
    public List<DiffChunk> Chunks { get; set; } = new();
}

/// <summary>
/// 差分チャンクを表すクラス
/// </summary>
public class DiffChunk
{
    public string Type { get; set; } = string.Empty; // "copy", "insert", "replace"
    public int Offset { get; set; }
    public int Length { get; set; }
    public string? FileName { get; set; } // 新しいデータのファイル名
} 