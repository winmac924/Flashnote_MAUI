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
            if (Application.Current?.MainPage == null)
            {
                return false;
            }

            return await MainThread.InvokeOnMainThreadAsync(async () =>
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
            });
        }
    }
} 