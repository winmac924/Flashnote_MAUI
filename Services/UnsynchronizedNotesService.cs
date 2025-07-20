using System.Diagnostics;
using System.Text.Json;

namespace Flashnote.Services
{
    /// <summary>
    /// 未同期ノートを管理するサービス
    /// </summary>
    public class UnsynchronizedNotesService
    {
        private readonly string _unsyncFilePath;
        private readonly object _lock = new object();

        public UnsynchronizedNotesService()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var flashnoteDir = Path.Combine(appDataPath, "Flashnote");
            Directory.CreateDirectory(flashnoteDir);
            _unsyncFilePath = Path.Combine(flashnoteDir, "unsynchronized_notes.json");
        }

        /// <summary>
        /// 未同期ノート情報
        /// </summary>
        public class UnsynchronizedNote
        {
            public string NoteName { get; set; }
            public string SubFolder { get; set; }
            public DateTime LastModified { get; set; }
            public string Reason { get; set; } // "offline" or "not_logged_in"
        }

        /// <summary>
        /// 未同期ノートを追加
        /// </summary>
        public void AddUnsynchronizedNote(string noteName, string subFolder = null, string reason = "not_logged_in")
        {
            try
            {
                lock (_lock)
                {
                    var unsyncNotes = LoadUnsynchronizedNotes();
                    
                    // 既存のエントリを検索
                    var existingNote = unsyncNotes.FirstOrDefault(n => 
                        n.NoteName == noteName && 
                        (n.SubFolder ?? "") == (subFolder ?? ""));
                    
                    if (existingNote != null)
                    {
                        // 既存のエントリを更新
                        existingNote.LastModified = DateTime.Now;
                        existingNote.Reason = reason;
                        Debug.WriteLine($"未同期ノート更新: {noteName} (フォルダ: {subFolder ?? "ルート"})");
                    }
                    else
                    {
                        // 新しいエントリを追加
                        unsyncNotes.Add(new UnsynchronizedNote
                        {
                            NoteName = noteName,
                            SubFolder = subFolder,
                            LastModified = DateTime.Now,
                            Reason = reason
                        });
                        Debug.WriteLine($"未同期ノート追加: {noteName} (フォルダ: {subFolder ?? "ルート"})");
                    }
                    
                    SaveUnsynchronizedNotes(unsyncNotes);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"未同期ノート追加エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 未同期ノートを削除（同期完了時）
        /// </summary>
        public void RemoveUnsynchronizedNote(string noteName, string subFolder = null)
        {
            try
            {
                lock (_lock)
                {
                    var unsyncNotes = LoadUnsynchronizedNotes();
                    var noteToRemove = unsyncNotes.FirstOrDefault(n => 
                        n.NoteName == noteName && 
                        (n.SubFolder ?? "") == (subFolder ?? ""));
                    
                    if (noteToRemove != null)
                    {
                        unsyncNotes.Remove(noteToRemove);
                        SaveUnsynchronizedNotes(unsyncNotes);
                        Debug.WriteLine($"未同期ノート削除: {noteName} (フォルダ: {subFolder ?? "ルート"})");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"未同期ノート削除エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 全ての未同期ノートを取得
        /// </summary>
        public List<UnsynchronizedNote> GetUnsynchronizedNotes()
        {
            try
            {
                lock (_lock)
                {
                    return LoadUnsynchronizedNotes();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"未同期ノート取得エラー: {ex.Message}");
                return new List<UnsynchronizedNote>();
            }
        }

        /// <summary>
        /// 未同期ノートがあるかチェック
        /// </summary>
        public bool HasUnsynchronizedNotes()
        {
            try
            {
                var notes = GetUnsynchronizedNotes();
                return notes.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 全ての未同期ノートをクリア
        /// </summary>
        public void ClearAllUnsynchronizedNotes()
        {
            try
            {
                lock (_lock)
                {
                    SaveUnsynchronizedNotes(new List<UnsynchronizedNote>());
                    Debug.WriteLine("全ての未同期ノートをクリアしました");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"未同期ノートクリアエラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 未同期ノートリストを読み込み
        /// </summary>
        private List<UnsynchronizedNote> LoadUnsynchronizedNotes()
        {
            try
            {
                if (!File.Exists(_unsyncFilePath))
                {
                    return new List<UnsynchronizedNote>();
                }

                var json = File.ReadAllText(_unsyncFilePath);
                var notes = JsonSerializer.Deserialize<List<UnsynchronizedNote>>(json);
                return notes ?? new List<UnsynchronizedNote>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"未同期ノート読み込みエラー: {ex.Message}");
                return new List<UnsynchronizedNote>();
            }
        }

        /// <summary>
        /// 未同期ノートリストを保存
        /// </summary>
        private void SaveUnsynchronizedNotes(List<UnsynchronizedNote> notes)
        {
            try
            {
                var json = JsonSerializer.Serialize(notes, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                File.WriteAllText(_unsyncFilePath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"未同期ノート保存エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 統計情報を取得
        /// </summary>
        public string GetSyncStatus()
        {
            try
            {
                var notes = GetUnsynchronizedNotes();
                if (notes.Count == 0)
                {
                    return "全てのノートが同期済みです";
                }

                var offlineCount = notes.Count(n => n.Reason == "offline");
                var notLoggedInCount = notes.Count(n => n.Reason == "not_logged_in");

                return $"未同期ノート: {notes.Count}件 (オフライン: {offlineCount}件, 未ログイン: {notLoggedInCount}件)";
            }
            catch
            {
                return "同期状態を取得できません";
            }
        }
    }
} 