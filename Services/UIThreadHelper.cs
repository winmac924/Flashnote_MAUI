using Microsoft.Maui.Controls;

namespace Flashnote_MAUI.Services
{
    /// <summary>
    /// UIスレッドでアラートを表示するためのヘルパークラス
    /// </summary>
    public static class UIThreadHelper
    {
        /// <summary>
        /// UIスレッドでアラートを表示
        /// </summary>
        public static async Task<bool> ShowAlertAsync(string title, string message, string accept = "OK", string cancel = null)
        {
            try
            {
                if (Application.Current?.MainPage == null)
                {
                    return false;
                }

                return await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    try
                    {
                        if (cancel != null)
                        {
                            return await Application.Current.MainPage.DisplayAlert(title, message, accept, cancel);
                        }
                        else
                        {
                            await Application.Current.MainPage.DisplayAlert(title, message, accept);
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"アラート表示中にエラー: {ex.Message}");
                        return false;
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UIThreadHelper.ShowAlertAsyncでエラー: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// UIスレッドで安全にナビゲーションを実行
        /// </summary>
        public static async Task<bool> NavigateToModalAsync(Page page)
        {
            try
            {
                if (Application.Current?.MainPage?.Navigation == null)
                {
                    return false;
                }

                return await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    try
                    {
                        await Application.Current.MainPage.Navigation.PushModalAsync(page);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"モーダルナビゲーション中にエラー: {ex.Message}");
                        return false;
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UIThreadHelper.NavigateToModalAsyncでエラー: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// UIスレッドで安全にモーダルを閉じる
        /// </summary>
        public static async Task<bool> PopModalAsync()
        {
            try
            {
                if (Application.Current?.MainPage?.Navigation == null)
                {
                    return false;
                }

                return await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    try
                    {
                        if (Application.Current.MainPage.Navigation.ModalStack.Count > 0)
                        {
                            await Application.Current.MainPage.Navigation.PopModalAsync();
                            return true;
                        }
                        return false;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"モーダルを閉じる処理中にエラー: {ex.Message}");
                        return false;
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UIThreadHelper.PopModalAsyncでエラー: {ex.Message}");
                return false;
            }
        }
    }
} 