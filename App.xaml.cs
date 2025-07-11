using Microsoft.Maui.Controls;
using Firebase.Auth;
using Firebase.Auth.Providers;
using System.Diagnostics;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Flashnote.Services;
using Microsoft.Extensions.Logging;

namespace Flashnote
{
    public partial class App : Application
    {
        public static FirebaseAuthClient AuthClient { get; private set; }
        public static BlobServiceClient BlobServiceClient { get; private set; }
        private static User _currentUser;
        private readonly ConfigurationService _configService;
        private readonly FileWatcherService _fileWatcherService;

        public static User CurrentUser 
        { 
            get => _currentUser;
            set => _currentUser = value;
        }

        public App(ConfigurationService configService, FileWatcherService fileWatcherService)
        {
            InitializeComponent();
            _configService = configService;
            _fileWatcherService = fileWatcherService;

            // 軽量な初期化のみを同期的に実行
            CleanupBackupFiles();
            
            // WebView2の初期化を実行
            _ = WebView2InitializationService.InitializeAsync();
            
            // 重い初期化処理は非同期で実行
            _ = Task.Run(async () =>
            {
                try
                {
                    await InitializeHeavyServicesAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"重い初期化処理でエラー: {ex.Message}");
                }
            });
            
            InitializeMainPage();
        }

        private async Task InitializeHeavyServicesAsync()
        {
            // Firebase認証の初期化
            await Task.Run(() => InitializeFirebase());
            
            // Azure Blob Storageの初期化
            await Task.Run(() => InitializeAzureBlobStorage());
        }

        private void InitializeFirebase()
        {
            try
            {
            var config = new FirebaseAuthConfig
            {
                    ApiKey = _configService.GetFirebaseApiKey(),
                    AuthDomain = _configService.GetFirebaseAuthDomain(),
                Providers = new FirebaseAuthProvider[]
                {
                    new EmailProvider()
                }
            };

            AuthClient = new FirebaseAuthClient(config);
                Debug.WriteLine("Firebase認証が初期化されました");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Firebase初期化中にエラー: {ex.Message}");
                throw;
            }
        }

        private void InitializeAzureBlobStorage()
        {
            try
            {
                string connectionString = _configService.GetAzureStorageConnectionString();
                BlobServiceClient = new BlobServiceClient(connectionString);
                Debug.WriteLine("Azure Blob Storage接続が初期化されました");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Azure Blob Storageの初期化中にエラー: {ex.Message}");
                throw;
            }
        }

        private void InitializeMainPage()
        {
            MainPage = new AppShell();
            // AppShellの初期化後に少し遅延してからMainPageに移動
            _ = Task.Delay(200).ContinueWith(async _ => await CheckSavedLoginAndUpdatesAsync());
        }

        private void CleanupBackupFiles()
        {
            try
            {
                // 現在の実行ファイルのパスを取得
                var currentExePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(currentExePath))
                    return;

                var backupPath = currentExePath + ".backup";
                
                // バックアップファイルが存在する場合は削除
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                    Debug.WriteLine($"バックアップファイルを削除しました: {backupPath}");
                }

                // 一時フォルダのアップデート関連ファイルもクリーンアップ
                var tempPath = Path.GetTempPath();
                var updateBatchFiles = Directory.GetFiles(tempPath, "Flashnote_Update*.bat");
                foreach (var batchFile in updateBatchFiles)
                {
                    try
                    {
                        File.Delete(batchFile);
                        Debug.WriteLine($"アップデートバッチファイルを削除しました: {batchFile}");
                    }
                    catch
                    {
                        // バッチファイルが実行中の場合は削除できないが、問題なし
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"バックアップファイルのクリーンアップ中にエラー: {ex.Message}");
            }
        }

        public static async Task CheckForUpdatesAsync()
        {
            try
            {
                // MainPageから呼び出されるように修正
                Debug.WriteLine("アップデートチェックをスキップしました（実装準備中）");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"アップデートチェック中にエラー: {ex.Message}");
            }
        }

        private async Task CheckSavedLoginAndUpdatesAsync()
        {
            try
            {
                // 少し遅延してからShell.Currentにアクセス
                await Task.Delay(100);
                
                // 保存されたログイン情報で自動ログインを試行
                try
                {
                    var email = await SecureStorage.GetAsync("user_email");
                    var password = await SecureStorage.GetAsync("user_password");

                    if (!string.IsNullOrEmpty(email) && !string.IsNullOrEmpty(password))
                    {
                        Debug.WriteLine("保存されたログイン情報でログインを試行します");
                        var userCredential = await AuthClient.SignInWithEmailAndPasswordAsync(email, password);
                        if (userCredential != null)
                        {
                            CurrentUser = userCredential.User;
                            Debug.WriteLine($"自動ログイン成功: {CurrentUser.Info.Email}");
                        }
                        else
                        {
                            Debug.WriteLine("自動ログイン失敗: ユーザー認証情報が無効");
                        }
                    }
                    else
                    {
                        Debug.WriteLine("保存されたログイン情報がありません");
                    }
                }
                catch (Exception loginEx)
                {
                    Debug.WriteLine($"自動ログイン中にエラー: {loginEx.Message}");
                    // 自動ログインに失敗しても続行
                }

                // Shell.Currentのnullチェックを追加
                if (Shell.Current != null)
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        try
                        {
                            await Shell.Current.GoToAsync("///MainPage");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Shell.Current.GoToAsyncでエラー: {ex.Message}");
                        }
                    });
                }
                else
                {
                    Debug.WriteLine("Shell.Currentがnullです。MainPageへの移動をスキップします。");
                }

                // 初回起動時のみ更新確認を実行
                await CheckForUpdatesOnFirstLaunchAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CheckSavedLoginAndUpdatesAsync中にエラー: {ex.Message}");
            }
        }

        private async Task CheckForUpdatesOnFirstLaunchAsync()
        {
            try
            {
                // UpdateNotificationServiceをDIコンテナから取得
                var updateService = Handler?.MauiContext?.Services?.GetService<UpdateNotificationService>();
                
                if (updateService != null)
                {
                    // アプリ起動時に毎回フラグをクリア（アプリを閉じるたびに更新確認を可能にする）
                    await updateService.ClearFirstLaunchFlagAsync();
                    
                    // 開発中はアップデートチェックをスキップ
                    if (!UpdateNotificationService.IsUpdateCheckEnabled)
                    {
                        Debug.WriteLine("開発モード: アップデートチェックをスキップします");
                        return;
                    }

                    // 少し遅延してから初回起動時の更新確認を実行
                    await Task.Delay(5000);
                    await updateService.CheckForUpdatesOnFirstLaunchAsync();
                }
                else
                {
                    Debug.WriteLine("UpdateNotificationServiceが見つかりません");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"初回起動時のアップデートチェック中にエラー: {ex.Message}");
            }
        }

        public static async Task SaveLoginInfo(string email, string password)
        {
            try
            {
                await SecureStorage.SetAsync("user_email", email);
                await SecureStorage.SetAsync("user_password", password);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ログイン情報の保存中にエラー: {ex.Message}");
            }
        }

        public static async Task ClearLoginInfo()
        {
            try
            {
                SecureStorage.Default.Remove("user_email");
                SecureStorage.Default.Remove("user_password");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ログイン情報の削除中にエラー: {ex.Message}");
            }
        }

        protected override void OnStart()
        {
            base.OnStart();
            Debug.WriteLine("アプリが開始されました");
            

            
            // アプリケーション開始時にファイル監視を開始
            _fileWatcherService?.StartWatching();
        }

        protected override void OnSleep()
        {
            base.OnSleep();
            // アプリケーションがバックグラウンドに移行時にファイル監視を停止
            _fileWatcherService?.StopWatching();
            
            // アプリケーション終了時（または一時停止時）に初回起動フラグをクリア
            _ = ClearFirstLaunchFlagOnExitAsync();
        }

        protected override void OnResume()
        {
            base.OnResume();
            // アプリケーションがフォアグラウンドに戻った時にファイル監視を再開
            _fileWatcherService?.StartWatching();
        }

        private async Task ClearFirstLaunchFlagOnExitAsync()
        {
            try
            {
                var updateService = Handler?.MauiContext?.Services?.GetService<UpdateNotificationService>();
                if (updateService != null)
                {
                    await updateService.ClearFirstLaunchFlagAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"アプリケーション終了時のフラグクリア中にエラー: {ex.Message}");
            }
        }
    }
}