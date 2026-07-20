using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Flashnote.Services.Sync
{
    /// <summary>
    /// 前回同期時のメタデータ内容をローカルインデックスとして保持し、変更がないノートの
    /// 再処理（一時ディレクトリ再構築・.ankpls再作成）をスキップする。
    /// iOS版 BackgroundSyncManager.swift 内の IncrementalSyncService に対応
    /// （プッシュ通知トリガーは対象外。既存の呼び出しトリガー：アプリ起動時・ノートオープン時・
    /// 手動同期ボタンに対して差分判定を挟むのみ）。
    /// バックエンドのBlob一覧APIがETag/更新日時を返さないため、コンテンツハッシュで差分検出する。
    /// </summary>
    public class IncrementalSyncService
    {
        private readonly string _indexFilePath;
        private readonly object _lock = new object();

        public IncrementalSyncService()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var flashnoteDir = Path.Combine(appDataPath, "Flashnote");
            Directory.CreateDirectory(flashnoteDir);
            _indexFilePath = Path.Combine(flashnoteDir, "sync-index.json");
        }

        /// <summary>
        /// 指定キー（blobPathなど、ノート/フォルダを一意に識別する文字列）のコンテンツが
        /// 前回同期時から変化しているかどうかを判定し、インデックスを更新する。
        /// 変化がなければ false を返し、呼び出し側はローカルへの再書き込みをスキップできる。
        /// </summary>
        public bool HasChangedSinceLastSync(string key, string content)
        {
            if (string.IsNullOrEmpty(key)) return true; // キーが特定できない場合は安全側に倒して常に処理する

            try
            {
                var hash = ComputeHash(content);

                lock (_lock)
                {
                    var index = LoadIndex();
                    if (index.TryGetValue(key, out var previousHash) && previousHash == hash)
                    {
                        return false;
                    }

                    index[key] = hash;
                    SaveIndex(index);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"IncrementalSyncService: 差分判定に失敗したため処理を継続します: {ex.Message}");
                return true; // 判定に失敗した場合は安全側に倒して常に処理する
            }
        }

        /// <summary>
        /// 指定キーのインデックスを削除する（強制的に次回フル処理させたい場合に使用）。
        /// </summary>
        public void Reset(string key)
        {
            try
            {
                lock (_lock)
                {
                    var index = LoadIndex();
                    if (index.Remove(key))
                    {
                        SaveIndex(index);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"IncrementalSyncService: インデックスリセットに失敗しました: {ex.Message}");
            }
        }

        /// <summary>
        /// インデックス全体をクリアする（次回同期を全件フル処理させたい場合に使用）。
        /// </summary>
        public void ResetAll()
        {
            try
            {
                lock (_lock)
                {
                    SaveIndex(new Dictionary<string, string>());
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"IncrementalSyncService: インデックス全体のリセットに失敗しました: {ex.Message}");
            }
        }

        private Dictionary<string, string> LoadIndex()
        {
            try
            {
                if (!File.Exists(_indexFilePath))
                {
                    return new Dictionary<string, string>();
                }

                var json = File.ReadAllText(_indexFilePath);
                var index = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                return index ?? new Dictionary<string, string>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"IncrementalSyncService: インデックス読み込みに失敗しました: {ex.Message}");
                return new Dictionary<string, string>();
            }
        }

        private void SaveIndex(Dictionary<string, string> index)
        {
            var json = JsonSerializer.Serialize(index);
            File.WriteAllText(_indexFilePath, json);
        }

        private static string ComputeHash(string content)
        {
            var bytes = Encoding.UTF8.GetBytes(content ?? string.Empty);
            var hashBytes = SHA256.HashData(bytes);
            return Convert.ToHexString(hashBytes);
        }
    }
}
