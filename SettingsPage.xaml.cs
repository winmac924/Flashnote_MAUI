using Microsoft.Maui.Controls;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Firebase.Auth;
using Flashnote_MAUI.Services;

namespace Flashnote
{
    public partial class SettingsPage : ContentPage
    {
        public SettingsPage()
        {
            InitializeComponent();
            _ = LoadSavedLoginInfo();
            LoadAppVersion();
            UpdateLoginStatus();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await LoadSavedLoginInfo();
            UpdateLoginStatus();
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

        private void LoadAppVersion()
        {
            try
            {
                var version = AppInfo.VersionString;
                VersionLabel.Text = version;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"アプリバージョンの取得中にエラー: {ex.Message}");
                VersionLabel.Text = "不明";
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
    }
} 