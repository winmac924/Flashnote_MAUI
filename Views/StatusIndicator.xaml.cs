using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Timers;
using Flashnote.Services;

namespace Flashnote.Views
{
    public partial class StatusIndicator : ContentView
    {
        private System.Timers.Timer _statusTimer;
        private NetworkStateService _networkStateService;
        private bool _isNetworkAvailable = true;
        private bool _isLoggedIn = false;

        public StatusIndicator()
        {
            try
            {
                Debug.WriteLine("StatusIndicator コンストラクタ開始");
                InitializeComponent();
                Debug.WriteLine("StatusIndicator InitializeComponent完了");
                
                // NetworkStateServiceを取得
                try
                {
                    _networkStateService = MauiProgram.Services?.GetService<NetworkStateService>();
                    if (_networkStateService != null)
                    {
                        _networkStateService.NetworkStateChanged += OnNetworkStateServiceChanged;
                        _isNetworkAvailable = _networkStateService.IsNetworkAvailable;
                        Debug.WriteLine("NetworkStateService からの監視を開始");
                    }
                }
                catch (Exception netEx)
                {
                    Debug.WriteLine($"NetworkStateService 取得エラー: {netEx.Message}");
                }
                
                StartStatusMonitoring();
                Debug.WriteLine("StatusIndicator 監視開始完了");
                
                // ログイン状態変更イベントを監視
                App.OnCurrentUserChanged += OnCurrentUserChanged;
                Debug.WriteLine("StatusIndicator イベント購読完了");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"StatusIndicator コンストラクタでエラー: {ex.Message}");
                Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                // 例外を再スローせずに、基本的な初期化のみ実行
                try
                {
                    InitializeComponent();
                }
                catch (Exception initEx)
                {
                    Debug.WriteLine($"StatusIndicator InitializeComponent でエラー: {initEx.Message}");
                }
            }
        }

        /// <summary>
        /// ステータス監視を開始
        /// </summary>
        private void StartStatusMonitoring()
        {
            try
            {
                // 初期状態をチェック
                CheckStatus();

                // 5秒ごとにステータスをチェック
                _statusTimer = new System.Timers.Timer(5000);
                _statusTimer.Elapsed += OnStatusTimerElapsed;
                _statusTimer.AutoReset = true;
                _statusTimer.Start();
                
                Debug.WriteLine("StatusIndicator タイマー開始完了");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"StatusIndicator 監視開始でエラー: {ex.Message}");
                Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                // タイマーエラーは無視（後で手動更新可能）
            }
        }

        /// <summary>
        /// タイマーイベント：ステータスをチェック
        /// </summary>
        private void OnStatusTimerElapsed(object sender, ElapsedEventArgs e)
        {
            // NetworkStateServiceがある場合は、それが定期テストを行っているので
            // 基本的なチェックのみ実行
            if (_networkStateService != null)
            {
                // ログイン状態のみチェック（ネットワーク状態はNetworkStateServiceが管理）
                CheckLoginStatusOnly();
            }
            else
            {
                // フォールバックとして従来のチェックを実行
                CheckStatus();
            }
        }

        /// <summary>
        /// ネットワークとログイン状態をチェックして表示を更新
        /// </summary>
        private void CheckStatus()
        {
            try
            {
                // ネットワーク状態をチェック
                bool networkAvailable = false;
                bool loggedIn = false;
                
                try
                {
                    // NetworkStateServiceがある場合はそれを使用
                    if (_networkStateService != null)
                    {
                        networkAvailable = _networkStateService.IsNetworkAvailable;
                    }
                    else
                    {
                        networkAvailable = NetworkInterface.GetIsNetworkAvailable();
                    }
                }
                catch (Exception netEx)
                {
                    Debug.WriteLine($"ネットワーク状態チェックエラー: {netEx.Message}");
                }
                
                try
                {
                    loggedIn = App.CurrentUser != null;
                }
                catch (Exception loginEx)
                {
                    Debug.WriteLine($"ログイン状態チェックエラー: {loginEx.Message}");
                }

                // UIスレッドで更新
                try
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        try
                        {
                            UpdateNetworkStatus(networkAvailable);
                            UpdateLoginStatus(loggedIn);
                        }
                        catch (Exception uiEx)
                        {
                            Debug.WriteLine($"UI更新エラー: {uiEx.Message}");
                        }
                    });
                }
                catch (Exception mainThreadEx)
                {
                    Debug.WriteLine($"メインスレッド実行エラー: {mainThreadEx.Message}");
                }

                _isNetworkAvailable = networkAvailable;
                _isLoggedIn = loggedIn;

                Debug.WriteLine($"ステータス更新 - ネットワーク: {networkAvailable}, ログイン: {loggedIn}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ステータスチェックエラー: {ex.Message}");
                Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// ログイン状態のみをチェック（ネットワーク状態はNetworkStateServiceが管理）
        /// </summary>
        private async void CheckLoginStatusOnly()
        {
            try
            {
                bool loggedIn = false;
                
                try
                {
                    // ログイン状態を検証
                    loggedIn = await App.ValidateLoginStatusAsync();
                }
                catch (Exception loginEx)
                {
                    Debug.WriteLine($"ログイン状態チェックエラー: {loginEx.Message}");
                }

                // UIスレッドで更新
                try
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        try
                        {
                            UpdateLoginStatus(loggedIn);
                        }
                        catch (Exception uiEx)
                        {
                            Debug.WriteLine($"ログイン状態UI更新エラー: {uiEx.Message}");
                        }
                    });
                }
                catch (Exception mainThreadEx)
                {
                    Debug.WriteLine($"メインスレッド実行エラー: {mainThreadEx.Message}");
                }

                _isLoggedIn = loggedIn;

                Debug.WriteLine($"ログイン状態更新: {loggedIn}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ログイン状態チェックエラー: {ex.Message}");
            }
        }

        /// <summary>
        /// ネットワーク状態の表示を更新
        /// </summary>
        private void UpdateNetworkStatus(bool isAvailable)
        {
            try
            {
                if (!isAvailable)
                {
                    NetworkStatusBorder.IsVisible = true;
                    NetworkStatusLabel.Text = "ネットワークオフライン";
                    NetworkStatusBorder.BackgroundColor = Colors.Red;
                }
                else
                {
                    NetworkStatusBorder.IsVisible = false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ネットワーク状態表示更新エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// ログイン状態の表示を更新
        /// </summary>
        private void UpdateLoginStatus(bool isLoggedIn)
        {
            try
            {
                if (!isLoggedIn)
                {
                    LoginStatusBorder.IsVisible = true;
                    LoginStatusLabel.Text = "未ログイン";
                    LoginStatusBorder.BackgroundColor = Colors.Orange;
                }
                else
                {
                    LoginStatusBorder.IsVisible = false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ログイン状態表示更新エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 手動でステータスを更新
        /// </summary>
        public void RefreshStatus()
        {
            CheckStatus();
        }

        /// <summary>
        /// 非同期でステータスを更新（精度の高いネットワークチェック）
        /// </summary>
        public async Task RefreshStatusAsync()
        {
            try
            {
                // NetworkStateServiceに手動更新を要求
                if (_networkStateService != null)
                {
                    await _networkStateService.RefreshNetworkStateAsync();
                }
                else
                {
                    CheckStatus();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"非同期ステータス更新エラー: {ex.Message}");
                // フォールバックとして通常のチェックを実行
                CheckStatus();
            }
        }

        /// <summary>
        /// NetworkStateService からのネットワーク状態変化時の処理
        /// </summary>
        private void OnNetworkStateServiceChanged(object sender, NetworkStateChangedEventArgs e)
        {
            try
            {
                Debug.WriteLine($"StatusIndicator: NetworkStateService からネットワーク状態変化通知 - {(e.IsAvailable ? "オンライン" : "オフライン")}");
                
                // 即座にネットワーク状態を更新
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    try
                    {
                        _isNetworkAvailable = e.IsAvailable;
                        UpdateNetworkStatus(e.IsAvailable);
                        
                        // オフラインになった場合はログイン状態を検証
                        if (!e.IsAvailable)
                        {
                            await App.ValidateLoginStatusAsync();
                        }
                    }
                    catch (Exception uiEx)
                    {
                        Debug.WriteLine($"ネットワーク状態UI更新エラー: {uiEx.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"NetworkStateService イベント処理エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// ログイン状態変更時の処理
        /// </summary>
        private void OnCurrentUserChanged()
        {
            // 即座にステータスを更新
            CheckStatus();
        }

        /// <summary>
        /// リソースの解放
        /// </summary>
        protected override void OnHandlerChanged()
        {
            base.OnHandlerChanged();
            
            if (Handler == null)
            {
                // コントロールが破棄される時にタイマーを停止
                _statusTimer?.Stop();
                _statusTimer?.Dispose();
                _statusTimer = null;
                
                // イベント購読を解除
                App.OnCurrentUserChanged -= OnCurrentUserChanged;
                
                if (_networkStateService != null)
                {
                    _networkStateService.NetworkStateChanged -= OnNetworkStateServiceChanged;
                }
            }
        }

        /// <summary>
        /// 現在のネットワーク状態を取得
        /// </summary>
        public bool IsNetworkAvailable => _isNetworkAvailable;

        /// <summary>
        /// 現在のログイン状態を取得
        /// </summary>
        public bool IsLoggedIn => _isLoggedIn;
    }
} 