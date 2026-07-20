using System;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using CommunityToolkit.Maui.Alerts;
using Flashnote.Services;
using Microsoft.Maui.ApplicationModel;

namespace Flashnote.Services.Sync
{
    /// <summary>
    /// Azure Blob Storageへの接続ライフサイクル（初期化・疎通確認・ネットワーク復帰時の自動同期）を
    /// 一元管理する純粋な接続層。iOS版 BlobStorageService.swift の接続まわりの責務に対応。
    /// ドメインレベルの操作（ノート/カードの読み書き等）は Services.BlobStorageService（薄いファサード）が担当する。
    /// </summary>
    public class BlobStorageClient
    {
        public const string ContainerName = "flashnote";

        private readonly BlobServiceClient _blobServiceClient;
        private NetworkStateService _networkStateService;
        private CardSyncService _cardSyncService;
        private SharedKeyService _sharedKeyService;
        private bool _isInitialized = false;
        private readonly object _lock = new object();

        /// <summary>
        /// ネットワーク復帰による自動同期が完了した際に発火する。
        /// </summary>
        public event EventHandler AutoSyncCompleted;

        public BlobServiceClient Client => _blobServiceClient;

        public BlobStorageClient()
        {
            _blobServiceClient = App.BlobServiceClient;

            // 初期化時にサービスを取得せず、必要時に遅延取得する
            Debug.WriteLine("BlobStorageClient を初期化しました（サービスは遅延取得）");
        }

        /// <summary>
        /// 依存サービスを遅延取得
        /// </summary>
        private void EnsureServicesInitialized()
        {
            if (_networkStateService == null && MauiProgram.Services != null)
            {
                try
                {
                    _networkStateService = MauiProgram.Services.GetService<NetworkStateService>();
                    _cardSyncService = MauiProgram.Services.GetService<CardSyncService>();
                    _sharedKeyService = MauiProgram.Services.GetService<SharedKeyService>();

                    if (_networkStateService != null)
                    {
                        _networkStateService.NetworkStateChanged += OnNetworkStateChanged;
                        Debug.WriteLine("BlobStorageClient でネットワーク状態監視を開始");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"サービス取得エラー: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// ネットワーク状態変化時の処理
        /// </summary>
        private void OnNetworkStateChanged(object sender, NetworkStateChangedEventArgs e)
        {
            try
            {
                if (!e.IsAvailable && e.WasAvailable)
                {
                    // オンライン → オフライン
                    Debug.WriteLine("ネットワークがオフラインになりました。進行中の操作をキャンセルし、Azure接続をリセットします。");

                    // 進行中の操作をキャンセル（安全に）
                    try
                    {
                        _networkStateService?.CancelPendingOperations();
                    }
                    catch (Exception cancelEx)
                    {
                        Debug.WriteLine($"操作キャンセルエラー: {cancelEx.Message}");
                    }

                    // Azure接続状態をリセット
                    lock (_lock)
                    {
                        _isInitialized = false;
                    }
                    Debug.WriteLine("Azure接続状態をリセットしました（オフライン）");

                    // ログイン状態を検証
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await App.ValidateLoginStatusAsync();
                        }
                        catch (Exception validateEx)
                        {
                            Debug.WriteLine($"ログイン状態検証エラー: {validateEx.Message}");
                        }
                    });
                }
                else if (e.IsAvailable && !e.WasAvailable)
                {
                    // オフライン → オンライン
                    Debug.WriteLine("ネットワークがオンラインになりました。Azure接続を再初期化します。");

                    // Azure接続を再初期化
                    lock (_lock)
                    {
                        _isInitialized = false;
                    }

                    // 自動同期を実行（エラーハンドリング付き）
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await PerformAutoSyncOnNetworkRecovery();
                        }
                        catch (Exception autoSyncEx)
                        {
                            Debug.WriteLine($"自動同期エラー: {autoSyncEx.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ネットワーク状態変化処理エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// ネットワーク接続状態をチェック
        /// </summary>
        public bool IsNetworkAvailable()
        {
            try
            {
                // NetworkStateServiceが利用可能な場合はそれを使用
                if (_networkStateService != null)
                {
                    return _networkStateService.IsNetworkAvailable;
                }

                // フォールバックとして直接チェック
                return NetworkInterface.GetIsNetworkAvailable();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Azure接続状態をリセット（ネットワーク状態変化時など）
        /// </summary>
        public void ResetConnectionState()
        {
            lock (_lock)
            {
                _isInitialized = false;
            }
            Debug.WriteLine("BlobStorageClient: Azure接続状態をリセットしました");
        }

        /// <summary>
        /// 現在の初期化状態を取得
        /// </summary>
        public bool IsInitialized
        {
            get
            {
                lock (_lock)
                {
                    return _isInitialized;
                }
            }
        }

        /// <summary>
        /// ネットワーク復帰時の自動同期処理
        /// </summary>
        private async Task PerformAutoSyncOnNetworkRecovery()
        {
            try
            {
                Debug.WriteLine("ネットワーク復帰時の自動同期処理を開始");

                // 依存サービスを初期化
                EnsureServicesInitialized();

                // 接続安定化待機
                await Task.Delay(3000);

                // ログインチェック
                if (App.CurrentUser == null)
                {
                    Debug.WriteLine("ユーザーがログインしていないため、自動同期をスキップします");
                    return;
                }

                var uid = App.CurrentUser.Uid;
                Debug.WriteLine($"自動同期開始 - UID: {uid}");

                // Azure接続を再テスト
                try
                {
                    if (await TestBlobConnectionAsync())
                    {
                        Debug.WriteLine("ネットワーク復帰後のAzure接続テスト成功");
                        lock (_lock)
                        {
                            _isInitialized = true;
                        }
                    }
                    else
                    {
                        Debug.WriteLine("ネットワーク復帰後のAzure接続テスト失敗");
                        return;
                    }
                }
                catch (Exception azureEx)
                {
                    Debug.WriteLine($"ネットワーク復帰後のAzure接続テストエラー: {azureEx.Message}");
                    return;
                }

                // 自動同期を実行
                try
                {
                    Debug.WriteLine("自動同期: ノート同期を開始");

                    // 1. 通常のノート同期
                    if (_cardSyncService != null)
                    {
                        await _cardSyncService.SyncAllNotesAsync(uid);
                        Debug.WriteLine("自動同期: ノート同期完了");
                    }
                    else
                    {
                        Debug.WriteLine("自動同期: CardSyncServiceが利用できません");
                    }

                    // 2. 共有キーの同期
                    if (_sharedKeyService != null)
                    {
                        await _sharedKeyService.SyncSharedKeysAsync(uid);
                        Debug.WriteLine("自動同期: 共有キー同期完了");
                    }
                    else
                    {
                        Debug.WriteLine("自動同期: SharedKeyServiceが利用できません");
                    }

                    Debug.WriteLine("ネットワーク復帰時の自動同期処理が完了しました");

                    // 自動同期完了を通知（UI更新のため）
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        try
                        {
                            // トースト通知で自動同期完了を表示
                            var toast = Toast.Make("ネットワーク復帰により自動同期が完了しました", CommunityToolkit.Maui.Core.ToastDuration.Short);
                            toast?.Show();

                            // イベントを発火
                            AutoSyncCompleted?.Invoke(this, EventArgs.Empty);
                        }
                        catch (Exception toastEx)
                        {
                            Debug.WriteLine($"トースト通知エラー: {toastEx.Message}");
                        }
                    });
                }
                catch (Exception syncEx)
                {
                    Debug.WriteLine($"自動同期処理エラー: {syncEx.Message}");

                    // エラー通知
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        try
                        {
                            var toast = Toast.Make("自動同期中にエラーが発生しました", CommunityToolkit.Maui.Core.ToastDuration.Short);
                            toast?.Show();
                        }
                        catch (Exception toastEx)
                        {
                            Debug.WriteLine($"エラートースト通知エラー: {toastEx.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ネットワーク復帰時処理エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// Azure Blob Storageへの接続をテスト
        /// </summary>
        public async Task<bool> TestBlobConnectionAsync()
        {
            CancellationTokenSource cancellationTokenSource = null;
            try
            {
                if (!IsNetworkAvailable())
                {
                    Debug.WriteLine("TestBlobConnectionAsync: ネットワーク接続がありません");
                    return false;
                }

                if (_blobServiceClient == null)
                {
                    Debug.WriteLine("TestBlobConnectionAsync: BlobServiceClientがnullです");
                    return false;
                }

                Debug.WriteLine($"TestBlobConnectionAsync: コンテナ'{ContainerName}'への接続テスト開始");
                var containerClient = _blobServiceClient.GetBlobContainerClient(ContainerName);
                cancellationTokenSource = NetworkOperationCancellationManager.CreateCancellationTokenSource(TimeSpan.FromSeconds(10));

                var exists = await containerClient.ExistsAsync(cancellationTokenSource.Token);
                Debug.WriteLine($"TestBlobConnectionAsync: コンテナ存在確認結果: {exists.Value}");
                Debug.WriteLine("TestBlobConnectionAsync: 接続テスト成功");
                return true;
            }
            catch (Azure.RequestFailedException azureEx)
            {
                Debug.WriteLine($"TestBlobConnectionAsync: Azure要求エラー - ステータス: {azureEx.Status}, エラーコード: {azureEx.ErrorCode}, メッセージ: {azureEx.Message}");
                return false;
            }
            catch (System.Net.Http.HttpRequestException httpEx)
            {
                Debug.WriteLine($"TestBlobConnectionAsync: HTTP要求エラー: {httpEx.Message}");
                return false;
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("TestBlobConnectionAsync: 接続テストがタイムアウトしました");
                return false;
            }
            catch (ObjectDisposedException)
            {
                Debug.WriteLine("TestBlobConnectionAsync: CancellationTokenSourceが既に破棄されています");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TestBlobConnectionAsync: 予期しないエラー - タイプ: {ex.GetType().Name}, メッセージ: {ex.Message}");
                Debug.WriteLine($"TestBlobConnectionAsync: スタックトレース: {ex.StackTrace}");
                return false;
            }
            finally
            {
                if (cancellationTokenSource != null)
                {
                    try
                    {
                        NetworkOperationCancellationManager.RemoveCancellationTokenSource(cancellationTokenSource);
                        cancellationTokenSource.Dispose();
                    }
                    catch (Exception disposeEx)
                    {
                        Debug.WriteLine($"TestBlobConnectionAsync: CancellationTokenSource破棄エラー: {disposeEx.Message}");
                    }
                }
            }
        }

        public async Task EnsureInitializedAsync()
        {
            lock (_lock)
            {
                if (_isInitialized)
                {
                    return; // 既に初期化済み
                }
            }

            // 早期ネットワークチェック
            if (!IsNetworkAvailable())
            {
                Debug.WriteLine("EnsureInitializedAsync: ネットワーク接続がありません");
                throw new InvalidOperationException("オフラインのため、サーバーに接続できません。インターネット接続を確認してください。");
            }

            // BlobServiceClientの状態をチェック
            if (_blobServiceClient == null)
            {
                Debug.WriteLine("EnsureInitializedAsync: BlobServiceClientがnullです");
                throw new InvalidOperationException("Azure Blob Storage接続が初期化されていません。");
            }

            Debug.WriteLine($"EnsureInitializedAsync: BlobServiceClient準備完了、接続テスト開始");

            if (!await TestBlobConnectionAsync())
            {
                Debug.WriteLine("EnsureInitializedAsync: Azure Blob Storage接続テストに失敗");
                throw new InvalidOperationException("オフラインのため、サーバーに接続できません。インターネット接続を確認してください。");
            }

            Debug.WriteLine("EnsureInitializedAsync: Azure Blob Storage接続テスト成功、コンテナ初期化開始");
            await InitializeContainerAsync();

            lock (_lock)
            {
                _isInitialized = true;
            }
            Debug.WriteLine("EnsureInitializedAsync: 初期化完了");
        }

        private async Task InitializeContainerAsync()
        {
            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(ContainerName);

                var cancellationTokenSource = NetworkOperationCancellationManager.CreateCancellationTokenSource(TimeSpan.FromSeconds(10));
                try
                {
                    if (!await containerClient.ExistsAsync(cancellationTokenSource.Token))
                    {
                        Debug.WriteLine($"コンテナ '{ContainerName}' が存在しないため、作成します。");
                        await containerClient.CreateAsync(cancellationToken: cancellationTokenSource.Token);
                        Debug.WriteLine($"コンテナ '{ContainerName}' を作成しました。");
                    }
                    else
                    {
                        Debug.WriteLine($"コンテナ '{ContainerName}' は既に存在します。");
                    }
                }
                finally
                {
                    NetworkOperationCancellationManager.RemoveCancellationTokenSource(cancellationTokenSource);
                    cancellationTokenSource.Dispose();
                }

                // 新しいCancellationTokenSourceでコンテナ一覧を取得
                Debug.WriteLine("利用可能なコンテナ一覧:");
                var listCancellationTokenSource = NetworkOperationCancellationManager.CreateCancellationTokenSource(TimeSpan.FromSeconds(10));
                try
                {
                    await foreach (var container in _blobServiceClient.GetBlobContainersAsync().WithCancellation(listCancellationTokenSource.Token))
                    {
                        Debug.WriteLine($"- {container.Name}");
                    }
                }
                finally
                {
                    NetworkOperationCancellationManager.RemoveCancellationTokenSource(listCancellationTokenSource);
                    listCancellationTokenSource.Dispose();
                }
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("Azure Blob Storageへの接続がタイムアウトしました。ネットワーク接続を確認してください。");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"コンテナの初期化中にエラー: {ex.Message}");
                throw new InvalidOperationException($"Azure Blob Storageの初期化に失敗しました: {ex.Message}", ex);
            }
        }

        public string GetUserPath(string uid, string subFolder = null)
        {
            return subFolder != null ? $"{uid}/{subFolder}" : uid;
        }
    }
}
