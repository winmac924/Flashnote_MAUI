using Microsoft.Extensions.Logging;
namespace Flashnote.Services;

public class UpdateNotificationService
{
    private readonly GitHubUpdateService _updateService;
    private readonly ILogger<UpdateNotificationService> _logger;
    private bool _isCheckingForUpdates = false;
    private const string FirstLaunchKey = "FirstLaunchCompleted";

    public UpdateNotificationService(GitHubUpdateService updateService, ILogger<UpdateNotificationService> logger)
    {
        _updateService = updateService;
        _logger = logger;
    }

    /// <summary>
    /// 初回起動時のみ更新確認を実行
    /// </summary>
    public async Task CheckForUpdatesOnFirstLaunchAsync()
    {
        try
        {
            // 初回起動かどうかをチェック
            var isFirstLaunch = await IsFirstLaunchAsync();
            
            if (!isFirstLaunch)
            {
                _logger.LogInformation("初回起動ではないため、更新確認をスキップします");
                return;
            }

            _logger.LogInformation("初回起動を検出しました。更新確認を実行します");
            
            // 更新確認を実行
            await CheckForUpdatesAsync();
            
            // 初回起動完了をマーク
            await MarkFirstLaunchCompletedAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初回起動時の更新確認中にエラーが発生しました");
        }
    }

    /// <summary>
    /// 初回起動かどうかを判定
    /// </summary>
    private async Task<bool> IsFirstLaunchAsync()
    {
        try
        {
            var firstLaunchCompleted = await SecureStorage.GetAsync(FirstLaunchKey);
            return string.IsNullOrEmpty(firstLaunchCompleted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初回起動判定中にエラーが発生しました");
            return true; // エラーの場合は初回起動として扱う
        }
    }

    /// <summary>
    /// 初回起動完了をマーク
    /// </summary>
    private async Task MarkFirstLaunchCompletedAsync()
    {
        try
        {
            await SecureStorage.SetAsync(FirstLaunchKey, DateTime.Now.ToString());
            _logger.LogInformation("初回起動完了をマークしました");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初回起動完了のマーク中にエラーが発生しました");
        }
    }

    /// <summary>
    /// 初回起動フラグをクリア（アプリケーション終了時に使用）
    /// </summary>
    public async Task ClearFirstLaunchFlagAsync()
    {
        try
        {
            SecureStorage.Remove(FirstLaunchKey);
            _logger.LogInformation("初回起動フラグをクリアしました");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初回起動フラグのクリア中にエラーが発生しました");
        }
    }

    public async Task CheckForUpdatesAsync()
    {
        if (_isCheckingForUpdates)
        {
            _logger.LogInformation("アップデート確認が既に実行中です");
            return;
        }

        try
        {
            _isCheckingForUpdates = true;
            _logger.LogInformation("アップデート確認を開始します");

            var updateInfo = await _updateService.CheckForUpdatesAsync();

            if (updateInfo == null)
            {
                _logger.LogInformation("アップデート確認が完了しました（リリース情報なし）");
                return;
            }

            if (updateInfo.IsUpdateAvailable)
            {
                _logger.LogInformation("新しいアップデートが利用可能です: {Version}", updateInfo.LatestVersion);
                await ShowUpdateNotificationAsync(updateInfo);
            }
            else
            {
                _logger.LogInformation("アプリは最新バージョンです");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "アップデート確認中にエラーが発生しました");
        }
        finally
        {
            _isCheckingForUpdates = false;
        }
    }

    private async Task ShowUpdateNotificationAsync(UpdateInfo updateInfo)
    {
        try
        {
            var title = "🚀 新しいバージョンが利用可能です";
            var message = $"Flashnote {updateInfo.LatestVersion} がリリースされました。\n\n" +
                         $"📋 更新内容:\n{updateInfo.ReleaseNotes}\n\n" +
                         $"今すぐダウンロードしますか？";

            var result = await Application.Current.MainPage.DisplayAlert(
                title,
                message,
                "ダウンロード",
                "後で"
            );

            if (result && !string.IsNullOrEmpty(updateInfo.DownloadUrl))
            {
                _logger.LogInformation("ユーザーがアップデートのダウンロードを選択しました");
                await StartUpdateDownloadAsync(updateInfo);
            }
            else
            {
                _logger.LogInformation("ユーザーがアップデートを後回しにしました");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "アップデート通知の表示中にエラーが発生しました");
        }
    }

    private async Task StartUpdateDownloadAsync(UpdateInfo updateInfo)
    {
        try
        {
            // 最終確認
            var confirmResult = await Application.Current.MainPage.DisplayAlert(
                "🔄 アップデート実行",
                "アップデートを実行します。\n\n処理内容：\n1. 新しいバージョンをダウンロード\n2. アプリケーションを終了\n3. ファイルを自動更新\n4. 新しいバージョンを起動\n\n実行しますか？",
                "実行する",
                "キャンセル"
            );

            if (!confirmResult)
            {
                _logger.LogInformation("ユーザーがアップデートをキャンセルしました");
                return;
            }

            // 進捗表示ページを作成して表示
            var progressPage = new UpdateProgressPage();
            await Application.Current.MainPage.Navigation.PushModalAsync(progressPage);

            _logger.LogInformation("アップデートのダウンロードを開始: {Url}", updateInfo.DownloadUrl);
            
            bool success = false;
            try
            {
                // 進捗報告用のProgressオブジェクトを作成
                var progress = new Progress<DownloadProgress>(p =>
                {
                    try
                    {
                        _logger.LogInformation("進捗報告: {Progress:P1} - {Status} - {Detail}", 
                            p.ProgressPercentage, p.Status, p.Detail);
                        progressPage.UpdateProgress(p.ProgressPercentage, p.Status, p.Detail);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "進捗報告中にエラーが発生しました");
                    }
                });

                success = await _updateService.DownloadAndInstallUpdateAsync(updateInfo.DownloadUrl, progress);
            }
            catch (Exception downloadEx)
            {
                _logger.LogError(downloadEx, "アップデートのダウンロード中に例外が発生しました");
                progressPage.ShowError("ダウンロード中にエラーが発生しました", downloadEx.Message);
                success = false;
            }

            if (success)
            {
                // 成功の場合は、アプリが自動終了するので通知は不要
                _logger.LogInformation("アップデート処理が正常に開始されました - アプリを終了します");
                progressPage.ShowComplete(true, "アップデートが完了しました。アプリケーションを再起動します。");
                
                // 少し待ってからページを閉じる（ユーザーがメッセージを読む時間を確保）
                await Task.Delay(2000);
            }
            else
            {
                progressPage.ShowError(
                    "アップデートに失敗しました",
                    "手動でアップデートしてください：\n1. GitHubリリースページにアクセス\n2. 最新の .exe ファイルをダウンロード\n3. 現在のファイルを置き換え"
                );
                
                // エラーの場合は3秒後にページを閉じる
                await Task.Delay(3000);
                await Application.Current.MainPage.Navigation.PopModalAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "アップデートのダウンロード中にエラーが発生しました");
            
            // 進捗ページが表示されている場合はエラーを表示
            if (Application.Current.MainPage.Navigation.ModalStack.LastOrDefault() is UpdateProgressPage errorPage)
            {
                errorPage.ShowError(
                    "アップデート中にエラーが発生しました",
                    $"手動でアップデートしてください：\n1. https://github.com/winmac924/Flashnote_MAUI/releases\n2. 最新の .exe ファイルをダウンロード\n3. 現在のファイルを置き換え\n\nエラー詳細: {ex.Message}"
                );
                
                await Task.Delay(3000);
                await Application.Current.MainPage.Navigation.PopModalAsync();
            }
            else
            {
                // 進捗ページが表示されていない場合は従来の方法でエラーを表示
                await Application.Current.MainPage.DisplayAlert(
                    "❌ アップデートエラー",
                    $"アップデート中にエラーが発生しました。\n\n手動でアップデートしてください：\n1. https://github.com/winmac924/Flashnote_MAUI/releases\n2. 最新の .exe ファイルをダウンロード\n3. 現在のファイルを置き換え\n\nエラー詳細: {ex.Message}",
                    "OK"
                );
            }
        }
    }

    /// <summary>
    /// 開発中のテスト用：アップデートチェックを無効化
    /// </summary>
    public static bool IsUpdateCheckEnabled => 
#if DEBUG
        true; // デバッグモードでは無効
#else
        true;  // リリースモードでは有効
#endif
} 