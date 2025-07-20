using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net;

namespace Flashnote.Services
{
    /// <summary>
    /// ネットワーク状態監視サービス
    /// </summary>
    public class NetworkStateService : IDisposable
    {
        private bool _isNetworkAvailable;
        private bool _isDisposed = false;
        private readonly object _lock = new object();
        private System.Timers.Timer _connectionTestTimer;
        private bool _isOfflineMode = false; // オフラインモードフラグ
        private int _consecutiveOfflineCount = 0; // 連続オフライン回数
        private const int MAX_OFFLINE_COUNT = 3; // 最大連続オフライン回数（この回数に達するとpingテストを停止）

        /// <summary>
        /// ネットワーク状態が変化した時のイベント
        /// </summary>
        public event EventHandler<NetworkStateChangedEventArgs> NetworkStateChanged;

        public NetworkStateService()
        {
            try
            {
                // 初期状態をデフォルト値で設定（後で非同期で更新）
                _isNetworkAvailable = NetworkInterface.GetIsNetworkAvailable();
                Debug.WriteLine($"NetworkStateService 初期化完了 - 初期状態: {(_isNetworkAvailable ? "オンライン（仮）" : "オフライン")}");
                
                // ネットワーク状態変化の監視を開始
                NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
                NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
                
                // 定期的な接続テストを開始（10秒間隔）
                StartPeriodicConnectionTest();
                
                // 初期化後すぐに実際の接続テストを非同期で実行
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(1000); // 1秒待機してから実際のテストを実行
                        await InitialNetworkTestAsync();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"初期ネットワークテストエラー: {ex.Message}");
                    }
                });
                
                Debug.WriteLine("ネットワーク状態監視を開始しました");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"NetworkStateService 初期化エラー: {ex.Message}");
                // デフォルト値でフォールバック
                _isNetworkAvailable = false;
            }
        }

        /// <summary>
        /// 定期的な接続テストを開始
        /// </summary>
        private void StartPeriodicConnectionTest()
        {
            try
            {
                _connectionTestTimer = new System.Timers.Timer(30000); // 30秒間隔に変更（負荷軽減）
                _connectionTestTimer.Elapsed += OnConnectionTestTimer;
                _connectionTestTimer.AutoReset = true;
                _connectionTestTimer.Start();
                Debug.WriteLine("定期接続テストを開始しました");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"定期接続テスト開始エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 定期接続テストのタイマーイベント
        /// </summary>
        private async void OnConnectionTestTimer(object sender, System.Timers.ElapsedEventArgs e)
        {
            try
            {
                // オフラインモードの場合は軽量チェックのみ実行
                bool shouldDoFullTest = true;
                lock (_lock)
                {
                    if (_isOfflineMode)
                    {
                        shouldDoFullTest = false;
                        Debug.WriteLine("オフラインモード中: 軽量チェックのみ実行");
                    }
                }

                bool wasAvailable;
                lock (_lock)
                {
                    wasAvailable = _isNetworkAvailable;
                }

                bool isNowAvailable;
                if (shouldDoFullTest)
                {
                    isNowAvailable = await CheckRealNetworkConnectionAsync();
                }
                else
                {
                    // オフラインモード時は軽量チェックのみ
                    isNowAvailable = await CheckLightweightConnectionAsync();
                }
                
                lock (_lock)
                {
                    _isNetworkAvailable = isNowAvailable;
                    
                    // オフライン状態の管理
                    if (!isNowAvailable)
                    {
                        _consecutiveOfflineCount++;
                        if (_consecutiveOfflineCount >= MAX_OFFLINE_COUNT && !_isOfflineMode)
                        {
                            _isOfflineMode = true;
                            Debug.WriteLine($"連続オフライン回数が{MAX_OFFLINE_COUNT}回に達したため、オフラインモードに移行します");
                            
                            // オフラインモード時は定期テストを停止
                            if (_connectionTestTimer != null)
                            {
                                _connectionTestTimer.Stop();
                                Debug.WriteLine("オフラインモード: 定期テストを停止しました");
                            }
                        }
                    }
                    else
                    {
                        // オンライン復帰時
                        if (_isOfflineMode)
                        {
                            _isOfflineMode = false;
                            _consecutiveOfflineCount = 0;
                            Debug.WriteLine("オンライン復帰を検知、オフラインモードを解除します");
                            
                            // オンライン復帰時は定期テストを再開
                            if (_connectionTestTimer != null)
                            {
                                _connectionTestTimer.Start();
                                Debug.WriteLine("オンライン復帰: 定期テストを再開しました");
                            }
                        }
                        _consecutiveOfflineCount = 0;
                    }
                }

                if (wasAvailable != isNowAvailable)
                {
                    Debug.WriteLine($"定期テストによるネットワーク状態変化: {(wasAvailable ? "オンライン" : "オフライン")} → {(isNowAvailable ? "オンライン" : "オフライン")}");
                    
                    NetworkStateChanged?.Invoke(this, new NetworkStateChangedEventArgs
                    {
                        IsAvailable = isNowAvailable,
                        WasAvailable = wasAvailable,
                        ChangeType = NetworkChangeType.PeriodicTest
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"定期接続テストエラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 軽量ネットワーク接続チェック（オフラインモード時用）
        /// </summary>
        private async Task<bool> CheckLightweightConnectionAsync()
        {
            try
            {
                // オフラインモード時はネットワークアダプターの状態のみチェック
                bool networkAvailable = NetworkInterface.GetIsNetworkAvailable();
                if (!networkAvailable)
                {
                    Debug.WriteLine("軽量チェック: ネットワークアダプターが利用不可");
                    return false;
                }

                // オフラインモード時はPingテストをスキップしてネットワークアダプターの状態のみ返す
                Debug.WriteLine("軽量チェック: オフラインモードのためPingテストをスキップ");
                return networkAvailable;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"軽量ネットワークチェックエラー: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 実際のネットワーク接続をテスト
        /// </summary>
        private async Task<bool> CheckRealNetworkConnectionAsync()
        {
            try
            {
                // まずローカルネットワークアダプターをチェック
                if (!NetworkInterface.GetIsNetworkAvailable())
                {
                    Debug.WriteLine("ネットワークアダプターが利用不可");
                    return false;
                }

                // 複数の信頼できるホストに対してpingテスト
                string[] testHosts = {
                    "8.8.8.8",        // Google DNS
                    "1.1.1.1",        // Cloudflare DNS
                    "208.67.222.222"  // OpenDNS
                };

                foreach (string host in testHosts)
                {
                    try
                    {
                        using (var ping = new Ping())
                        {
                            var reply = await ping.SendPingAsync(host, 2000); // 2秒タイムアウトに短縮
                            if (reply.Status == IPStatus.Success)
                            {
                                Debug.WriteLine($"接続テスト成功: {host} (応答時間: {reply.RoundtripTime}ms)");
                                return true;
                            }
                            else
                            {
                                Debug.WriteLine($"接続テスト失敗: {host} - {reply.Status}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Pingテストエラー {host}: {ex.Message}");
                    }
                }

                Debug.WriteLine("全ての接続テストが失敗しました");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ネットワーク接続テストエラー: {ex.Message}");
                
                // フォールバックとして基本チェックを実行
                try
                {
                    return NetworkInterface.GetIsNetworkAvailable();
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// 初回の実際のネットワーク接続テスト
        /// </summary>
        private async Task InitialNetworkTestAsync()
        {
            try
            {
                Debug.WriteLine("初回ネットワーク接続テストを開始");
                
                bool wasAvailable;
                lock (_lock)
                {
                    wasAvailable = _isNetworkAvailable;
                }

                bool actualNetworkState = await CheckRealNetworkConnectionAsync();
                
                lock (_lock)
                {
                    _isNetworkAvailable = actualNetworkState;
                }

                Debug.WriteLine($"初回ネットワークテスト完了: {(actualNetworkState ? "オンライン" : "オフライン")}");

                if (wasAvailable != actualNetworkState)
                {
                    Debug.WriteLine($"初回テストによる状態変化: {(wasAvailable ? "オンライン" : "オフライン")} → {(actualNetworkState ? "オンライン" : "オフライン")}");
                    
                    NetworkStateChanged?.Invoke(this, new NetworkStateChangedEventArgs
                    {
                        IsAvailable = actualNetworkState,
                        WasAvailable = wasAvailable,
                        ChangeType = NetworkChangeType.Manual
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"初回ネットワークテストエラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 現在のネットワーク状態
        /// </summary>
        public bool IsNetworkAvailable
        {
            get
            {
                lock (_lock)
                {
                    return _isNetworkAvailable;
                }
            }
        }

        /// <summary>
        /// ネットワーク利用可能性の変化時の処理
        /// </summary>
        private async void OnNetworkAvailabilityChanged(object sender, NetworkAvailabilityEventArgs e)
        {
            try
            {
                Debug.WriteLine($"NetworkAvailabilityChanged イベント: {e.IsAvailable}");
                
                // イベントの値だけでなく、実際の接続もテスト
                bool realConnectionAvailable = await CheckRealNetworkConnectionAsync();
                
                bool wasAvailable;
                lock (_lock)
                {
                    wasAvailable = _isNetworkAvailable;
                    _isNetworkAvailable = realConnectionAvailable;
                    
                    // オフラインモード管理
                    if (!realConnectionAvailable)
                    {
                        _consecutiveOfflineCount++;
                        if (_consecutiveOfflineCount >= MAX_OFFLINE_COUNT && !_isOfflineMode)
                        {
                            _isOfflineMode = true;
                            Debug.WriteLine($"NetworkAvailabilityChanged: 連続オフライン回数が{MAX_OFFLINE_COUNT}回に達したため、オフラインモードに移行します");
                            
                            // オフラインモード時は定期テストを停止
                            if (_connectionTestTimer != null)
                            {
                                _connectionTestTimer.Stop();
                                Debug.WriteLine("NetworkAvailabilityChanged: オフラインモードで定期テストを停止しました");
                            }
                        }
                    }
                    else
                    {
                        // オンライン復帰時
                        if (_isOfflineMode)
                        {
                            _isOfflineMode = false;
                            _consecutiveOfflineCount = 0;
                            Debug.WriteLine("NetworkAvailabilityChanged: オンライン復帰を検知、オフラインモードを解除します");
                            
                            // オンライン復帰時は定期テストを再開
                            if (_connectionTestTimer != null)
                            {
                                _connectionTestTimer.Start();
                                Debug.WriteLine("NetworkAvailabilityChanged: オンライン復帰で定期テストを再開しました");
                            }
                        }
                        _consecutiveOfflineCount = 0;
                    }
                }

                if (wasAvailable != realConnectionAvailable)
                {
                    Debug.WriteLine($"ネットワーク状態変化: {(wasAvailable ? "オンライン" : "オフライン")} → {(realConnectionAvailable ? "オンライン" : "オフライン")}");
                    
                    // イベントを発火
                    NetworkStateChanged?.Invoke(this, new NetworkStateChangedEventArgs
                    {
                        IsAvailable = realConnectionAvailable,
                        WasAvailable = wasAvailable,
                        ChangeType = NetworkChangeType.Availability
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ネットワーク利用可能性変化処理エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// ネットワークアドレス変化時の処理
        /// </summary>
        private async void OnNetworkAddressChanged(object sender, EventArgs e)
        {
            try
            {
                Debug.WriteLine("NetworkAddressChanged イベント発生");
                
                // アドレス変化時も実際の接続をテスト
                bool wasAvailable;
                bool isNowAvailable = await CheckRealNetworkConnectionAsync();
                
                lock (_lock)
                {
                    wasAvailable = _isNetworkAvailable;
                    _isNetworkAvailable = isNowAvailable;
                    
                    // オフラインモード管理
                    if (!isNowAvailable)
                    {
                        _consecutiveOfflineCount++;
                        if (_consecutiveOfflineCount >= MAX_OFFLINE_COUNT && !_isOfflineMode)
                        {
                            _isOfflineMode = true;
                            Debug.WriteLine($"NetworkAddressChanged: 連続オフライン回数が{MAX_OFFLINE_COUNT}回に達したため、オフラインモードに移行します");
                            
                            // オフラインモード時は定期テストを停止
                            if (_connectionTestTimer != null)
                            {
                                _connectionTestTimer.Stop();
                                Debug.WriteLine("NetworkAddressChanged: オフラインモードで定期テストを停止しました");
                            }
                        }
                    }
                    else
                    {
                        // オンライン復帰時
                        if (_isOfflineMode)
                        {
                            _isOfflineMode = false;
                            _consecutiveOfflineCount = 0;
                            Debug.WriteLine("NetworkAddressChanged: オンライン復帰を検知、オフラインモードを解除します");
                            
                            // オンライン復帰時は定期テストを再開
                            if (_connectionTestTimer != null)
                            {
                                _connectionTestTimer.Start();
                                Debug.WriteLine("NetworkAddressChanged: オンライン復帰で定期テストを再開しました");
                            }
                        }
                        _consecutiveOfflineCount = 0;
                    }
                }

                if (wasAvailable != isNowAvailable)
                {
                    Debug.WriteLine($"ネットワークアドレス変化による状態変化: {(wasAvailable ? "オンライン" : "オフライン")} → {(isNowAvailable ? "オンライン" : "オフライン")}");
                    
                    // イベントを発火
                    NetworkStateChanged?.Invoke(this, new NetworkStateChangedEventArgs
                    {
                        IsAvailable = isNowAvailable,
                        WasAvailable = wasAvailable,
                        ChangeType = NetworkChangeType.Address
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ネットワークアドレス変化処理エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// ネットワーク状態を手動で更新
        /// </summary>
        public async Task RefreshNetworkStateAsync()
        {
            try
            {
                Debug.WriteLine("ネットワーク状態を手動更新中...");
                
                bool wasAvailable;
                bool isNowAvailable = await CheckRealNetworkConnectionAsync();
                
                lock (_lock)
                {
                    wasAvailable = _isNetworkAvailable;
                    _isNetworkAvailable = isNowAvailable;
                    
                    // 手動更新時はオフラインモードをリセットして完全テストを実行
                    if (!isNowAvailable)
                    {
                        _consecutiveOfflineCount++;
                        if (_consecutiveOfflineCount >= MAX_OFFLINE_COUNT && !_isOfflineMode)
                        {
                            _isOfflineMode = true;
                            Debug.WriteLine($"手動更新: 連続オフライン回数が{MAX_OFFLINE_COUNT}回に達したため、オフラインモードに移行します");
                            
                            // オフラインモード時は定期テストを停止
                            if (_connectionTestTimer != null)
                            {
                                _connectionTestTimer.Stop();
                                Debug.WriteLine("手動更新: オフラインモードで定期テストを停止しました");
                            }
                        }
                    }
                    else
                    {
                        // オンライン復帰時
                        if (_isOfflineMode)
                        {
                            _isOfflineMode = false;
                            _consecutiveOfflineCount = 0;
                            Debug.WriteLine("手動更新: オンライン復帰を検知、オフラインモードを解除します");
                            
                            // オンライン復帰時は定期テストを再開
                            if (_connectionTestTimer != null)
                            {
                                _connectionTestTimer.Start();
                                Debug.WriteLine("手動更新: オンライン復帰で定期テストを再開しました");
                            }
                        }
                        _consecutiveOfflineCount = 0;
                    }
                }

                if (wasAvailable != isNowAvailable)
                {
                    Debug.WriteLine($"手動更新によるネットワーク状態変化: {(wasAvailable ? "オンライン" : "オフライン")} → {(isNowAvailable ? "オンライン" : "オフライン")}");
                    
                    NetworkStateChanged?.Invoke(this, new NetworkStateChangedEventArgs
                    {
                        IsAvailable = isNowAvailable,
                        WasAvailable = wasAvailable,
                        ChangeType = NetworkChangeType.Manual
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ネットワーク状態手動更新エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 同期版の手動更新（後方互換性のため）
        /// </summary>
        public void RefreshNetworkState()
        {
            _ = Task.Run(async () => await RefreshNetworkStateAsync());
        }

        /// <summary>
        /// 進行中の操作をキャンセル
        /// </summary>
        public void CancelPendingOperations()
        {
            try
            {
                Debug.WriteLine("進行中のネットワーク操作をキャンセルします");
                
                // グローバルキャンセレーション信号を送る（安全に）
                NetworkOperationCancellationManager.CancelAllOperations();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"進行中操作キャンセルエラー: {ex.Message}");
            }
        }

        /// <summary>
        /// リソースの解放
        /// </summary>
        public void Dispose()
        {
            if (!_isDisposed)
            {
                try
                {
                    NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
                    NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
                    
                    _connectionTestTimer?.Stop();
                    _connectionTestTimer?.Dispose();
                    _connectionTestTimer = null;
                    
                    Debug.WriteLine("NetworkStateService のリソースを解放しました");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"NetworkStateService 解放エラー: {ex.Message}");
                }
                
                _isDisposed = true;
            }
        }
    }

    /// <summary>
    /// ネットワーク状態変化イベント引数
    /// </summary>
    public class NetworkStateChangedEventArgs : EventArgs
    {
        public bool IsAvailable { get; set; }
        public bool WasAvailable { get; set; }
        public NetworkChangeType ChangeType { get; set; }
    }

    /// <summary>
    /// ネットワーク変化タイプ
    /// </summary>
    public enum NetworkChangeType
    {
        Availability,    // 利用可能性の変化
        Address,         // アドレスの変化
        Manual,          // 手動更新
        PeriodicTest     // 定期テスト
    }

    /// <summary>
    /// ネットワーク操作のキャンセレーション管理
    /// </summary>
    public static class NetworkOperationCancellationManager
    {
        private static readonly List<WeakReference<CancellationTokenSource>> _activeCancellationTokens = new();
        private static readonly object _lock = new();

        /// <summary>
        /// 新しいキャンセレーショントークンを登録
        /// </summary>
        public static CancellationTokenSource CreateCancellationTokenSource(TimeSpan timeout)
        {
            lock (_lock)
            {
                try
                {
                    // 古い参照をクリーンアップ
                    CleanupDeadReferences();
                    
                    var cts = new CancellationTokenSource(timeout);
                    _activeCancellationTokens.Add(new WeakReference<CancellationTokenSource>(cts));
                    Debug.WriteLine($"CancellationTokenSource作成: アクティブ数 {_activeCancellationTokens.Count}");
                    return cts;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"CancellationTokenSource作成エラー: {ex.Message}");
                    // フォールバックとして通常のCancellationTokenSourceを返す
                    return new CancellationTokenSource(timeout);
                }
            }
        }

        /// <summary>
        /// 全ての進行中操作をキャンセル
        /// </summary>
        public static void CancelAllOperations()
        {
            lock (_lock)
            {
                try
                {
                    CleanupDeadReferences();
                    Debug.WriteLine($"進行中の操作 {_activeCancellationTokens.Count} 件をキャンセルします");
                    
                    var tokensToCancel = new List<CancellationTokenSource>();
                    
                    foreach (var weakRef in _activeCancellationTokens.ToList())
                    {
                        try
                        {
                            if (weakRef.TryGetTarget(out var cts))
                            {
                                tokensToCancel.Add(cts);
                            }
                        }
                        catch (Exception weakRefEx)
                        {
                            Debug.WriteLine($"WeakReference取得エラー: {weakRefEx.Message}");
                        }
                    }
                    
                    foreach (var cts in tokensToCancel)
                    {
                        try
                        {
                            if (cts != null && !cts.IsCancellationRequested && !cts.Token.IsCancellationRequested)
                            {
                                cts.Cancel();
                                Debug.WriteLine("操作をキャンセルしました");
                            }
                        }
                        catch (ObjectDisposedException)
                        {
                            Debug.WriteLine("既に破棄されたCancellationTokenSourceをスキップ");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"キャンセレーション処理エラー: {ex.Message}");
                        }
                    }
                    
                    // すべてクリア
                    _activeCancellationTokens.Clear();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"CancelAllOperations エラー: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 完了したトークンを登録から削除
        /// </summary>
        public static void RemoveCancellationTokenSource(CancellationTokenSource cts)
        {
            if (cts == null) return;
            
            lock (_lock)
            {
                try
                {
                    for (int i = _activeCancellationTokens.Count - 1; i >= 0; i--)
                    {
                        var weakRef = _activeCancellationTokens[i];
                        if (!weakRef.TryGetTarget(out var target) || ReferenceEquals(target, cts))
                        {
                            _activeCancellationTokens.RemoveAt(i);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"RemoveCancellationTokenSource エラー: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// 無効な参照をクリーンアップ
        /// </summary>
        private static void CleanupDeadReferences()
        {
            for (int i = _activeCancellationTokens.Count - 1; i >= 0; i--)
            {
                if (!_activeCancellationTokens[i].TryGetTarget(out _))
                {
                    _activeCancellationTokens.RemoveAt(i);
                }
            }
        }
    }
} 