using Microsoft.Maui.Controls;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Firebase.Auth;
using Flashnote.Services;
using Flashnote.Models;
using Flashnote_MAUI.Services;
using System.Collections.ObjectModel;
using System.Linq;

namespace Flashnote
{
    public partial class SettingsPage : ContentPage
    {
        private readonly SharedKeyService _sharedKeyService;

        public SettingsPage()
        {
            InitializeComponent();
            _sharedKeyService = new SharedKeyService();
            
            _ = LoadSavedLoginInfo();
            LoadAppVersion();
            UpdateLoginStatus();
            LoadSharedKeysStatus();
            LoadDefaultTextInputModeSetting();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await LoadSavedLoginInfo();
            UpdateLoginStatus();
            LoadSharedKeysStatus();
            LoadDefaultTextInputModeSetting();
            await LoadUpdateStatusAsync();
        }

        private async Task LoadSavedLoginInfo()
        {
            try
            {
                var email = await SecureStorage.GetAsync("user_email");
                var password = await SecureStorage.GetAsync("user_password");

                if (!string.IsNullOrEmpty(email))
                {
                    EmailEntry.Text = email;
                }

                if (!string.IsNullOrEmpty(password))
                {
                    PasswordEntry.Text = password;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"保存されたログイン情報の読み込み中にエラー: {ex.Message}");
            }
        }

        private async void LoadAppVersion()
        {
            try
            {
                var version = AppInfo.VersionString;
                VersionLabel.Text = version;
                
                // アップデート状態も確認
                await LoadUpdateStatusAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"アプリバージョンの取得中にエラー: {ex.Message}");
                VersionLabel.Text = "不明";
            }
        }

        private async Task LoadUpdateStatusAsync()
        {
            try
            {
                // UpdateNotificationServiceをDIコンテナから取得
                var updateService = Handler?.MauiContext?.Services?.GetService<UpdateNotificationService>();
                
                if (updateService != null)
                {
                    // ネットワーク接続をチェック
                    if (!System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
                    {
                        UpdateStatusLabel.Text = "オフライン";
                        UpdateStatusLabel.TextColor = Colors.Gray;
                        return;
                    }

                    // アップデート情報を取得（通知は表示しない）
                    var updateInfo = await GetUpdateInfoWithoutNotificationAsync();
                    
                    if (updateInfo == null)
                    {
                        UpdateStatusLabel.Text = "確認できません";
                        UpdateStatusLabel.TextColor = Colors.Gray;
                    }
                    else if (updateInfo.IsUpdateAvailable)
                    {
                        UpdateStatusLabel.Text = $"アップデート可能 ({updateInfo.LatestVersion})";
                        UpdateStatusLabel.TextColor = Colors.Orange;
                    }
                    else
                    {
                        UpdateStatusLabel.Text = "最新版です";
                        UpdateStatusLabel.TextColor = Colors.Green;
                    }
                }
                else
                {
                    UpdateStatusLabel.Text = "確認できません";
                    UpdateStatusLabel.TextColor = Colors.Gray;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"アップデート状態の確認中にエラー: {ex.Message}");
                UpdateStatusLabel.Text = "確認できません";
                UpdateStatusLabel.TextColor = Colors.Gray;
            }
        }

        private async Task<UpdateInfo?> GetUpdateInfoWithoutNotificationAsync()
        {
            try
            {
                // GitHubUpdateServiceを直接使用してアップデート情報を取得（通知なし）
                var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Flashnote-MAUI-UpdateClient");
                
                var logger = Handler?.MauiContext?.Services?.GetService<ILogger<GitHubUpdateService>>();
                if (logger == null)
                {
                    // ロガーが利用できない場合は簡易版で実装
                    return await GetUpdateInfoSimpleAsync(httpClient);
                }
                
                var updateService = new GitHubUpdateService(httpClient, logger);
                return await updateService.CheckForUpdatesAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"アップデート情報の取得中にエラー: {ex.Message}");
                return null;
            }
        }

        private async Task<UpdateInfo?> GetUpdateInfoSimpleAsync(HttpClient httpClient)
        {
            try
            {
                var currentVersion = AppInfo.VersionString;
                var url = "https://api.github.com/repos/winmac924/Flashnote_MAUI/releases/latest";
                
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var response = await httpClient.GetStringAsync(url, cts.Token);
                
                // 簡易的なJSON解析（TagNameのみ取得）
                if (response.Contains("\"tag_name\""))
                {
                    var tagNameStart = response.IndexOf("\"tag_name\"") + 12;
                    var tagNameEnd = response.IndexOf("\"", tagNameStart);
                    var latestVersion = response.Substring(tagNameStart, tagNameEnd - tagNameStart);
                    
                    // バージョン比較
                    var cleanCurrent = currentVersion.TrimStart('v');
                    var cleanLatest = latestVersion.TrimStart('v');
                    
                    if (Version.TryParse(cleanCurrent, out var current) && 
                        Version.TryParse(cleanLatest, out var latest))
                    {
                        return new UpdateInfo
                        {
                            IsUpdateAvailable = latest > current,
                            LatestVersion = latestVersion,
                            ReleaseNotes = "最新バージョンが利用可能です"
                        };
                    }
                }
                
                return new UpdateInfo { IsUpdateAvailable = false };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"簡易アップデート確認中にエラー: {ex.Message}");
                return null;
            }
        }

        private async void LoadDefaultTextInputModeSetting()
        {
            try
            {
                var defaultTextInputMode = await SecureStorage.GetAsync("default_text_input_mode");
                bool isEnabled = defaultTextInputMode == "true";
                DefaultTextInputModeToggle.IsToggled = isEnabled;
                Debug.WriteLine($"デフォルトテキスト入力モード設定を読み込み: {isEnabled}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"デフォルトテキスト入力モード設定の読み込み中にエラー: {ex.Message}");
                DefaultTextInputModeToggle.IsToggled = false;
            }
        }

        private async void OnDefaultTextInputModeToggled(object sender, ToggledEventArgs e)
        {
            try
            {
                var value = e.Value ? "true" : "false";
                await SecureStorage.SetAsync("default_text_input_mode", value);
                Debug.WriteLine($"デフォルトテキスト入力モード設定を保存: {value}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"デフォルトテキスト入力モード設定の保存中にエラー: {ex.Message}");
            }
        }

        private void UpdateLoginStatus()
        {
            try
            {
                if (App.CurrentUser != null)
                {
                    LoginStatusLabel.Text = $"ログイン状態: ログイン済み ({App.CurrentUser.Info.Email})";
                    LoginStatusLabel.TextColor = Colors.Green;
                }
                else
                {
                    LoginStatusLabel.Text = "ログイン状態: 未ログイン";
                    LoginStatusLabel.TextColor = Colors.Gray;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ログイン状態の更新中にエラー: {ex.Message}");
                LoginStatusLabel.Text = "ログイン状態: 不明";
                LoginStatusLabel.TextColor = Colors.Red;
            }
        }

        private async void OnSaveClicked(object sender, EventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(EmailEntry.Text) || string.IsNullOrWhiteSpace(PasswordEntry.Text))
                {
                    await UIThreadHelper.ShowAlertAsync("エラー", "メールアドレスとパスワードを入力してください。", "OK");
                    return;
                }

                await App.SaveLoginInfo(EmailEntry.Text.Trim(), PasswordEntry.Text);
                await UIThreadHelper.ShowAlertAsync("成功", "ログイン情報を保存しました。", "OK");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ログイン情報の保存中にエラー: {ex.Message}");
                await UIThreadHelper.ShowAlertAsync("エラー", "ログイン情報の保存に失敗しました。", "OK");
            }
        }

        private async void OnLoginClicked(object sender, EventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(EmailEntry.Text) || string.IsNullOrWhiteSpace(PasswordEntry.Text))
                {
                    await UIThreadHelper.ShowAlertAsync("エラー", "メールアドレスとパスワードを入力してください。", "OK");
                    return;
                }

                // ローディング表示
                LoginButton.IsEnabled = false;
                LoginButton.Text = "ログイン中...";

                var userCredential = await App.AuthClient.SignInWithEmailAndPasswordAsync(
                    EmailEntry.Text.Trim(), 
                    PasswordEntry.Text);

                if (userCredential != null)
                {
                    App.CurrentUser = userCredential.User;
                    await App.SaveLoginInfo(EmailEntry.Text.Trim(), PasswordEntry.Text);
                    UpdateLoginStatus();
                    await UIThreadHelper.ShowAlertAsync("成功", "ログインに成功しました。", "OK");
                }
                else
                {
                    await UIThreadHelper.ShowAlertAsync("エラー", "ログインに失敗しました。", "OK");
                }
            }
            catch (FirebaseAuthException authEx)
            {
                Debug.WriteLine($"Firebase認証エラー: {authEx.Message}");
                string errorMessage = "ログインに失敗しました。";
                
                switch (authEx.Reason)
                {
                    case AuthErrorReason.InvalidEmailAddress:
                        errorMessage = "無効なメールアドレスです。";
                        break;
                    case AuthErrorReason.WrongPassword:
                        errorMessage = "パスワードが間違っています。";
                        break;
                    case AuthErrorReason.UserNotFound:
                        errorMessage = "ユーザーが見つかりません。";
                        break;
                    default:
                        errorMessage = $"ログインエラー: {authEx.Message}";
                        break;
                }
                
                await UIThreadHelper.ShowAlertAsync("ログインエラー", errorMessage, "OK");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ログイン中にエラー: {ex.Message}");
                await UIThreadHelper.ShowAlertAsync("エラー", "ログイン処理中にエラーが発生しました。", "OK");
            }
            finally
            {
                // ローディング表示を元に戻す
                LoginButton.IsEnabled = true;
                LoginButton.Text = "ログイン";
            }
        }

        private async void OnClearClicked(object sender, EventArgs e)
        {
            try
            {
                bool result = await UIThreadHelper.ShowAlertAsync("確認", "ログイン情報をクリアしますか？", "はい", "いいえ");
                if (result)
                {
                    EmailEntry.Text = string.Empty;
                    PasswordEntry.Text = string.Empty;
                    await App.ClearLoginInfo();
                    App.CurrentUser = null;
                    UpdateLoginStatus();
                    await UIThreadHelper.ShowAlertAsync("完了", "ログイン情報をクリアしました。", "OK");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ログイン情報のクリア中にエラー: {ex.Message}");
                await UIThreadHelper.ShowAlertAsync("エラー", "ログイン情報のクリアに失敗しました。", "OK");
            }
        }

        private async void OnBackClicked(object sender, EventArgs e)
        {
            try
            {
                await Shell.Current.GoToAsync("///MainPage");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainPageへの移動中にエラー: {ex.Message}");
                await UIThreadHelper.ShowAlertAsync("エラー", "MainPageへの移動に失敗しました。", "OK");
            }
        }

        private void LoadSharedKeysStatus()
        {
            try
            {
                var sharedNotes = _sharedKeyService.GetSharedNotes();
                int count = sharedNotes.Count;
                
                if (count > 0)
                {
                    SharedKeysStatusLabel.Text = $"共有キー: {count}件";
                    SharedKeysStatusLabel.TextColor = Colors.Green;
                }
                else
                {
                    SharedKeysStatusLabel.Text = "共有キーがありません";
                    SharedKeysStatusLabel.TextColor = Colors.Gray;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"共有キー状態の読み込み中にエラー: {ex.Message}");
                SharedKeysStatusLabel.Text = "共有キーの読み込みに失敗しました";
                SharedKeysStatusLabel.TextColor = Colors.Red;
            }
        }

        private async void OnShowSharedKeysClicked(object sender, EventArgs e)
        {
            try
            {
                var sharedKeysSheet = new SharedKeysSheet();
                await Navigation.PushModalAsync(sharedKeysSheet);
                await sharedKeysSheet.ShowAsync();
                await Navigation.PopModalAsync();
                
                // シートが閉じられた後にステータスを更新
                LoadSharedKeysStatus();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"共有キーシートの表示中にエラー: {ex.Message}");
                await UIThreadHelper.ShowAlertAsync("エラー", "共有キーシートの表示に失敗しました。", "OK");
            }
        }

        private async void OnCheckUpdateClicked(object sender, EventArgs e)
        {
            try
            {
                // ボタンを無効化してローディング状態に
                CheckUpdateButton.IsEnabled = false;
                CheckUpdateButton.Text = "確認中...";
                UpdateStatusLabel.Text = "確認中...";
                UpdateStatusLabel.TextColor = Colors.Gray;

                // UpdateNotificationServiceをDIコンテナから取得してアップデート確認を実行
                var updateService = Handler?.MauiContext?.Services?.GetService<UpdateNotificationService>();
                
                if (updateService != null)
                {
                    // 初回起動時と同じアップデート確認機能を実行
                    await updateService.CheckForUpdatesAsync();
                }
                else
                {
                    // UpdateNotificationServiceが利用できない場合は、現在のバージョンを表示
                    var currentVersion = AppInfo.VersionString;
                    await UIThreadHelper.ShowAlertAsync("アップデート確認", 
                        $"現在のバージョン: {currentVersion}\n\n手動でアップデートを確認するには、GitHubリポジトリをご確認ください。", "OK");
                }
                
                // アップデート状態を再確認
                await LoadUpdateStatusAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"アップデート確認中にエラー: {ex.Message}");
                await UIThreadHelper.ShowAlertAsync("エラー", "アップデートの確認に失敗しました。", "OK");
                UpdateStatusLabel.Text = "確認できません";
                UpdateStatusLabel.TextColor = Colors.Gray;
            }
            finally
            {
                // ボタンを元の状態に戻す
                CheckUpdateButton.IsEnabled = true;
                CheckUpdateButton.Text = "アップデート確認";
            }
        }
    }
} 