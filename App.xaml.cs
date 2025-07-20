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
        private NetworkStateService _networkStateService;
        private bool _hasShownOfflineToast = false; // オフライン通知表示フラグ

        public static User CurrentUser 
        { 
            get => _currentUser;
            set 
            { 
                var wasLoggedIn = _currentUser != null;
                var isNowLoggedIn = value != null;
                
                _currentUser = value;
                
                // ログイン状態が変更された時に通知
                OnCurrentUserChanged?.Invoke();
                
                // ログイン復帰時に未同期ノートを同期
                if (!wasLoggedIn && isNowLoggedIn)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await SyncUnsynchronizedNotesAsync();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"未同期ノート同期エラー: {ex.Message}");
                        }
                    });
                }
            }
        }

        /// <summary>
        /// ネットワーク状態に基づいてログイン状態を検証
        /// </summary>
        public static async Task<bool> ValidateLoginStatusAsync()
        {
            try
            {
                if (_currentUser == null)
                {
                    return false;
                }

                // ネットワーク状態をチェック
                var networkStateService = MauiProgram.Services?.GetService<NetworkStateService>();
                if (networkStateService != null && !networkStateService.IsNetworkAvailable)
                {
                    Debug.WriteLine("ネットワークがオフラインのため、ログイン状態を無効化します");
                    _currentUser = null;
                    OnCurrentUserChanged?.Invoke();
                    return false;
                }

                // Firebase認証状態を検証
                try
                {
                    var user = AuthClient.User;
                    if (user == null || user.Uid != _currentUser.Uid)
                    {
                        Debug.WriteLine("Firebase認証状態が無効のため、ログイン状態をクリアします");
                        _currentUser = null;
                        OnCurrentUserChanged?.Invoke();
                        return false;
                    }
                    return true;
                }
                catch (Exception authEx)
                {
                    Debug.WriteLine($"Firebase認証検証エラー: {authEx.Message}");
                    _currentUser = null;
                    OnCurrentUserChanged?.Invoke();
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ログイン状態検証エラー: {ex.Message}");
                return false;
            }
        }

        public static event Action OnCurrentUserChanged;

        /// <summary>
        /// 未同期ノートを同期
        /// </summary>
        private static async Task SyncUnsynchronizedNotesAsync()
        {
            try
            {
                Debug.WriteLine("=== 未同期ノート同期開始 ===");
                
                // ServiceProviderが初期化されていない場合は処理をスキップ
                if (MauiProgram.Services == null)
                {
                    Debug.WriteLine("ServiceProviderが初期化されていないため、未同期ノート同期をスキップ");
                    return;
                }
                
                var unsyncService = MauiProgram.Services.GetService<UnsynchronizedNotesService>();
                var cardSyncService = MauiProgram.Services.GetService<CardSyncService>();
                
                if (unsyncService == null || cardSyncService == null)
                {
                    Debug.WriteLine("必要なサービスが見つからないため、未同期ノート同期をスキップ");
                    return;
                }
                
                var unsyncNotes = unsyncService.GetUnsynchronizedNotes();
                if (unsyncNotes.Count == 0)
                {
                    Debug.WriteLine("未同期ノートがありません");
                    return;
                }
                
                Debug.WriteLine($"未同期ノート数: {unsyncNotes.Count}");
                
                var uid = CurrentUser?.Uid;
                if (string.IsNullOrEmpty(uid))
                {
                    Debug.WriteLine("ユーザーIDが取得できません");
                    return;
                }
                
                int syncedCount = 0;
                int failedCount = 0;
                
                foreach (var unsyncNote in unsyncNotes.ToList())
                {
                    try
                    {
                        Debug.WriteLine($"未同期ノート同期中: {unsyncNote.NoteName} (フォルダ: {unsyncNote.SubFolder ?? "ルート"})");
                        
                        // 双方向同期処理を実行
                        await cardSyncService.SyncNoteOnOpenAsync(uid, unsyncNote.NoteName, unsyncNote.SubFolder);
                        
                        // 同期成功時は未同期リストから削除
                        unsyncService.RemoveUnsynchronizedNote(unsyncNote.NoteName, unsyncNote.SubFolder);
                        syncedCount++;
                        
                        Debug.WriteLine($"未同期ノート同期完了: {unsyncNote.NoteName}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"未同期ノート同期失敗: {unsyncNote.NoteName} - {ex.Message}");
                        failedCount++;
                        
                        // ネットワークエラーの場合は理由を更新
                        if (ex.Message.Contains("オフライン") || ex.Message.Contains("ネットワーク"))
                        {
                            unsyncService.AddUnsynchronizedNote(unsyncNote.NoteName, unsyncNote.SubFolder, "offline");
                        }
                    }
                    
                    // 次のノート同期前に少し待機
                    await Task.Delay(500);
                }
                
                Debug.WriteLine($"=== 未同期ノート同期完了 ===");
                Debug.WriteLine($"成功: {syncedCount}件, 失敗: {failedCount}件");
                
                // 同期結果を通知（UIスレッドで）
                if (syncedCount > 0)
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        try
                        {
                            string message = failedCount > 0 
                                ? $"未同期ノート {syncedCount}件を同期しました。{failedCount}件は同期に失敗しました。"
                                : $"未同期ノート {syncedCount}件を同期しました。";
                            
                            await Application.Current.MainPage.DisplayAlert("同期完了", message, "OK");
                        }
                        catch (Exception uiEx)
                        {
                            Debug.WriteLine($"同期完了通知エラー: {uiEx.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"未同期ノート同期処理エラー: {ex.Message}");
                Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
            }
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
            
            // NetworkStateServiceの初期化とイベント監視
            await Task.Run(() => InitializeNetworkStateService());
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
                Debug.WriteLine("InitializeAzureBlobStorage: Azure Blob Storage初期化開始");
                string connectionString = _configService.GetAzureStorageConnectionString();
                
                if (string.IsNullOrEmpty(connectionString))
                {
                    Debug.WriteLine("InitializeAzureBlobStorage: 接続文字列が空またはnullです");
                    throw new InvalidOperationException("Azure Storage接続文字列が設定されていません");
                }
                
                // 接続文字列の最初と最後の20文字のみを表示（セキュリティのため）
                var maskedConnectionString = connectionString.Length > 40 
                    ? $"{connectionString.Substring(0, 20)}...{connectionString.Substring(connectionString.Length - 20)}"
                    : "***（短い接続文字列）***";
                Debug.WriteLine($"InitializeAzureBlobStorage: 接続文字列を取得: {maskedConnectionString}");
                
                BlobServiceClient = new BlobServiceClient(connectionString);
                Debug.WriteLine("InitializeAzureBlobStorage: BlobServiceClient作成成功");
                
                // 接続文字列の妥当性をチェック
                if (BlobServiceClient.Uri == null)
                {
                    Debug.WriteLine("InitializeAzureBlobStorage: BlobServiceClient.Uriがnullです");
                    throw new InvalidOperationException("Azure Storage接続文字列が無効です");
                }
                
                Debug.WriteLine($"InitializeAzureBlobStorage: Azure Storage URI: {BlobServiceClient.Uri}");
                Debug.WriteLine("InitializeAzureBlobStorage: Azure Blob Storage接続が初期化されました");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"InitializeAzureBlobStorage: Azure Blob Storageの初期化中にエラー - タイプ: {ex.GetType().Name}, メッセージ: {ex.Message}");
                Debug.WriteLine($"InitializeAzureBlobStorage: スタックトレース: {ex.StackTrace}");
                throw;
            }
        }

        private void InitializeNetworkStateService()
        {
            try
            {
                Debug.WriteLine("InitializeNetworkStateService: NetworkStateService初期化開始");
                
                // ServiceProviderからNetworkStateServiceを取得
                if (MauiProgram.Services != null)
                {
                    _networkStateService = MauiProgram.Services.GetService<NetworkStateService>();
                    if (_networkStateService != null)
                    {
                        // ネットワーク状態変化イベントを監視
                        _networkStateService.NetworkStateChanged += OnNetworkStateChanged;
                        Debug.WriteLine("InitializeNetworkStateService: NetworkStateServiceイベント監視を開始しました");
                    }
                    else
                    {
                        Debug.WriteLine("InitializeNetworkStateService: NetworkStateServiceが見つかりません");
                    }
                }
                else
                {
                    Debug.WriteLine("InitializeNetworkStateService: ServiceProviderが初期化されていません");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"InitializeNetworkStateService: NetworkStateService初期化エラー: {ex.Message}");
            }
        }

        private async void OnNetworkStateChanged(object sender, NetworkStateChangedEventArgs e)
        {
            try
            {
                Debug.WriteLine($"OnNetworkStateChanged: ネットワーク状態変化 - {(e.WasAvailable ? "オンライン" : "オフライン")} → {(e.IsAvailable ? "オンライン" : "オフライン")}");
                
                // オンライン → オフラインの変化時のみ処理
                if (e.WasAvailable && !e.IsAvailable && !_hasShownOfflineToast)
                {
                    _hasShownOfflineToast = true;
                    
                    // UIスレッドでトースト表示
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        try
                        {
                            string offlineMessage = "オフラインになりました。カードはローカルに保存され、オンライン復帰時に自動同期されます。";
                            
                            // 現在のページでトースト表示を試行
                            if (Application.Current?.MainPage != null)
                            {
                                // トースト表示の実装（Add.xaml.csのShowToastメソッドを参考）
                                await ShowOfflineToast(offlineMessage);
                            }
                            
                            Debug.WriteLine($"オフライン通知を表示: {offlineMessage}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"オフライン通知表示エラー: {ex.Message}");
                        }
                    });
                }
                // オフライン → オンラインの変化時
                else if (!e.WasAvailable && e.IsAvailable)
                {
                    _hasShownOfflineToast = false; // フラグをリセット
                    Debug.WriteLine("オンライン復帰を検知、オフライン通知フラグをリセットしました");
                    
                    // オンライン復帰時に自動ログインを試行
                    await TryAutoLoginOnOnlineReturnAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OnNetworkStateChangedエラー: {ex.Message}");
            }
        }

        private async Task ShowOfflineToast(string message)
        {
            try
            {
                // シンプルなトースト表示（Add.xaml.csのShowToastメソッドを参考）
                if (Application.Current?.MainPage != null)
                {
                    // トースト表示用のラベルを作成
                    var toastLabel = new Label
                    {
                        Text = message,
                        BackgroundColor = Colors.Black,
                        TextColor = Colors.White,
                        HorizontalOptions = LayoutOptions.Center,
                        VerticalOptions = LayoutOptions.End,
                        Margin = new Thickness(20, 0, 20, 100),
                        Padding = new Thickness(15, 10, 15, 10),
                        HorizontalTextAlignment = TextAlignment.Center,
                        FontSize = 14
                    };

                    // 現在のページにトーストを追加
                    if (Application.Current.MainPage is ContentPage contentPage)
                    {
                        contentPage.Content = new Grid
                        {
                            Children = 
                            {
                                contentPage.Content,
                                toastLabel
                            }
                        };

                        // 3秒後にトーストを削除
                        await Task.Delay(3000);
                        
                        if (contentPage.Content is Grid grid && grid.Children.Count > 1)
                        {
                            grid.Children.RemoveAt(1);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ShowOfflineToastエラー: {ex.Message}");
            }
        }

        /// <summary>
        /// オンライン復帰時の自動ログイン試行
        /// </summary>
        private async Task TryAutoLoginOnOnlineReturnAsync()
        {
            try
            {
                Debug.WriteLine("=== オンライン復帰時の自動ログイン試行開始 ===");
                
                // 既にログイン済みの場合はスキップ
                if (CurrentUser != null)
                {
                    Debug.WriteLine("既にログイン済みのため、自動ログインをスキップします");
                    return;
                }
                
                // 保存されたログイン情報を確認
                var email = await SecureStorage.GetAsync("user_email");
                var password = await SecureStorage.GetAsync("user_password");
                
                if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
                {
                    Debug.WriteLine("保存されたログイン情報がありません");
                    return;
                }
                
                Debug.WriteLine($"保存されたログイン情報を確認: {email}");
                
                // ネットワーク接続を再確認
                if (!System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
                {
                    Debug.WriteLine("ネットワーク接続が不安定なため、自動ログインをスキップします");
                    return;
                }
                
                // タイムアウト付きでログイン試行
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                {
                    try
                    {
                        Debug.WriteLine("Firebase認証でログインを試行します");
                        var userCredential = await AuthClient.SignInWithEmailAndPasswordAsync(email, password);
                        
                        if (userCredential != null && userCredential.User != null)
                        {
                            CurrentUser = userCredential.User;
                            Debug.WriteLine($"オンライン復帰時の自動ログイン成功: {CurrentUser.Info.Email}");
                            
                            // ログイン成功時のトースト表示
                            await MainThread.InvokeOnMainThreadAsync(async () =>
                            {
                                try
                                {
                                    string loginMessage = "オンライン復帰時に自動ログインしました。";
                                    await ShowOfflineToast(loginMessage);
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"ログイン成功通知表示エラー: {ex.Message}");
                                }
                            });
                            
                            // 未同期ノートの同期を実行
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await Task.Delay(2000); // 少し待ってから同期
                                    await SyncUnsynchronizedNotesAsync();
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"オンライン復帰時の未同期ノート同期エラー: {ex.Message}");
                                }
                            });
                        }
                        else
                        {
                            Debug.WriteLine("オンライン復帰時の自動ログイン失敗: ユーザー認証情報が無効");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        Debug.WriteLine("オンライン復帰時の自動ログインがタイムアウトしました");
                    }
                    catch (Exception loginEx)
                    {
                        Debug.WriteLine($"オンライン復帰時の自動ログイン中にエラー: {loginEx.Message}");
                        // ネットワークエラーの詳細ログを抑制
                        if (!(loginEx.Message.Contains("No such host") || loginEx.Message.Contains("network") || loginEx.Message.Contains("Exception occured during Firebase Http request")))
                        {
                            Debug.WriteLine($"詳細: {loginEx}");
                        }
                    }
                }
                
                Debug.WriteLine("=== オンライン復帰時の自動ログイン試行完了 ===");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TryAutoLoginOnOnlineReturnAsyncエラー: {ex.Message}");
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
                    // ネットワーク接続をチェック
                    if (System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
                {
                    var email = await SecureStorage.GetAsync("user_email");
                    var password = await SecureStorage.GetAsync("user_password");

                    if (!string.IsNullOrEmpty(email) && !string.IsNullOrEmpty(password))
                    {
                        Debug.WriteLine("保存されたログイン情報でログインを試行します");
                            
                            // タイムアウト付きでログイン試行
                            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                            {
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
                        }
                        else
                        {
                            Debug.WriteLine("保存されたログイン情報がありません");
                        }
                    }
                    else
                    {
                        Debug.WriteLine("ネットワーク接続がないため、自動ログインをスキップ");
                    }
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine("自動ログインがタイムアウトしました");
                }
                catch (Exception loginEx)
                {
                    Debug.WriteLine($"自動ログイン中にエラー: {loginEx.Message}");
                    // ネットワークエラーの詳細ログを抑制
                    if (!(loginEx.Message.Contains("No such host") || loginEx.Message.Contains("network") || loginEx.Message.Contains("Exception occured during Firebase Http request")))
                    {
                        Debug.WriteLine($"詳細: {loginEx}");
                    }
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