using System;
using System.IO;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Flashnote.Services
{
    /// <summary>
    /// WebView2の初期化とユーザーデータフォルダの設定を行うサービス
    /// </summary>
    public class WebView2InitializationService
    {
        private static bool _isInitialized = false;
        private static readonly object _lockObject = new object();

        /// <summary>
        /// WebView2の初期化を実行
        /// </summary>
        public static async Task InitializeAsync()
        {
            if (_isInitialized) return;

            lock (_lockObject)
            {
                if (_isInitialized) return;
                
                try
                {
                    // ユーザーデータフォルダを設定
                    SetUserDataFolder();
                    
                    // 環境変数を設定
                    SetEnvironmentVariables();
                    
                    _isInitialized = true;
                    Debug.WriteLine("✅ WebView2初期化完了");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"❌ WebView2初期化エラー: {ex.Message}");
                }
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// WebView2のユーザーデータフォルダを設定
        /// </summary>
        private static void SetUserDataFolder()
        {
            try
            {
                // localのFlashnoteフォルダパスを取得
                var localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var webView2DataPath = Path.Combine(localAppDataPath, "Flashnote", "WebView2");
                
                // フォルダが存在しない場合は作成
                if (!Directory.Exists(webView2DataPath))
                {
                    Directory.CreateDirectory(webView2DataPath);
                    Debug.WriteLine($"WebView2ユーザーデータフォルダを作成: {webView2DataPath}");
                }
                else
                {
                    Debug.WriteLine($"WebView2ユーザーデータフォルダは既に存在: {webView2DataPath}");
                }
                
                // 環境変数を設定
                Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", webView2DataPath);
                Debug.WriteLine($"WebView2ユーザーデータフォルダを設定: {webView2DataPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WebView2ユーザーデータフォルダ設定エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// WebView2関連の環境変数を設定
        /// </summary>
        private static void SetEnvironmentVariables()
        {
            try
            {
                // WebView2のログレベルを設定（デバッグ用）
                Environment.SetEnvironmentVariable("WEBVIEW2_LOG_LEVEL", "Info");
                
                // WebView2のプロセス分離を無効化（オプション）
                Environment.SetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", "--disable-features=VizDisplayCompositor");
                
                Debug.WriteLine("WebView2環境変数を設定完了");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WebView2環境変数設定エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// WebView2のユーザーデータフォルダパスを取得
        /// </summary>
        public static string GetUserDataFolderPath()
        {
            var localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppDataPath, "Flashnote", "WebView2");
        }
    }
} 