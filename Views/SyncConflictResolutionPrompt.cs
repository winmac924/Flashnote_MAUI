using System.Diagnostics;
using System.Threading.Tasks;
using Flashnote.Services.Sync;
using Microsoft.Maui.Controls;

namespace Flashnote.Views
{
    public enum SyncConflictResolution
    {
        /// <summary>ローカルの変更を残し、サーバーからの上書きをスキップする</summary>
        KeepLocal,
        /// <summary>サーバーの内容で上書きする（ローカルの変更は破棄、バックアップには残る）</summary>
        KeepServer
    }

    /// <summary>
    /// 同期競合が検出された際に、ローカル/サーバーどちらを残すかをユーザーに確認するダイアログ。
    /// iOS版 SyncConflictResolutionSheet.swift に対応。
    /// </summary>
    public static class SyncConflictResolutionPrompt
    {
        private const string KeepLocalLabel = "ローカルを残す";
        private const string KeepServerLabel = "サーバーを残す";

        public static async Task<SyncConflictResolution> ShowAsync(SyncConflict conflict)
        {
            try
            {
                var page = Application.Current?.MainPage;
                if (page == null)
                {
                    Debug.WriteLine("SyncConflictResolutionPrompt: MainPageが取得できないため、サーバー優先で解決します");
                    return SyncConflictResolution.KeepServer;
                }

                var title = string.IsNullOrEmpty(conflict.SubFolder)
                    ? $"「{conflict.NoteName}」に同期の競合があります"
                    : $"「{conflict.SubFolder}/{conflict.NoteName}」に同期の競合があります";

                var choice = await page.DisplayActionSheet(
                    title,
                    "キャンセル（サーバーを優先）",
                    null,
                    KeepLocalLabel,
                    KeepServerLabel);

                return choice == KeepLocalLabel
                    ? SyncConflictResolution.KeepLocal
                    : SyncConflictResolution.KeepServer;
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine($"SyncConflictResolutionPrompt error: {ex.Message}");
                return SyncConflictResolution.KeepServer;
            }
        }
    }
}
