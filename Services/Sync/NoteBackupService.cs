using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Flashnote.Services.Sync
{
    /// <summary>
    /// サーバーからの上書き直前にローカルファイルを世代バックアップする。
    /// iOS版 NoteBackupService.swift に対応。ユーザー向けのゴミ箱(TrashManager相当)とは別の、
    /// 破損/誤上書き復旧用の隠しバックアップ。
    /// </summary>
    public class NoteBackupService
    {
        private const int MaxGenerations = 3;

        private static string BackupRoot =>
            Path.Combine(SyncPathResolver.GetLocalNoteRoot(), ".backups");

        /// <summary>
        /// 指定したファイルが存在すれば、上書き前に世代バックアップへコピーする。
        /// 直近 <see cref="MaxGenerations"/> 世代のみ保持し、それより古い世代は削除する。
        /// </summary>
        /// <param name="filePath">これから上書きされるローカルファイルのフルパス</param>
        /// <param name="noteId">バックアップの分類キー（ノートID/フォルダIDなど。省略時はファイル名を使用）</param>
        public void BackupBeforeOverwrite(string filePath, string noteId = null)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                {
                    return; // 上書き対象が存在しない＝バックアップ不要
                }

                var key = noteId ?? Path.GetFileNameWithoutExtension(filePath);
                var backupDir = Path.Combine(BackupRoot, SanitizeForPath(key));
                Directory.CreateDirectory(backupDir);

                var fileName = Path.GetFileName(filePath);
                var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
                var backupPath = Path.Combine(backupDir, $"{fileName}.{timestamp}.bak");

                File.Copy(filePath, backupPath, overwrite: true);
                Debug.WriteLine($"NoteBackupService: バックアップを作成しました -> {backupPath}");

                PruneOldGenerations(backupDir, fileName);
            }
            catch (Exception ex)
            {
                // バックアップ失敗は同期処理を止める理由にしない
                Debug.WriteLine($"NoteBackupService: バックアップ作成に失敗しました: {ex.Message}");
            }
        }

        private void PruneOldGenerations(string backupDir, string originalFileName)
        {
            try
            {
                var generations = Directory.GetFiles(backupDir, $"{originalFileName}.*.bak")
                    .Select(p => new FileInfo(p))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .ToList();

                foreach (var stale in generations.Skip(MaxGenerations))
                {
                    try
                    {
                        stale.Delete();
                        Debug.WriteLine($"NoteBackupService: 古い世代を削除しました -> {stale.FullName}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"NoteBackupService: 古い世代の削除に失敗しました: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"NoteBackupService: 世代整理に失敗しました: {ex.Message}");
            }
        }

        private static string SanitizeForPath(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
            return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
        }
    }
}
