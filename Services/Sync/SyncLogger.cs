using System;
using System.IO;
using System.Text.Json;

namespace Flashnote.Services.Sync
{
    /// <summary>
    /// 同期処理の構造化ログ。iOS版 SyncLogger.swift に対応。
    /// JSON Lines形式で LocalApplicationData/Flashnote/logs/sync-YYYYMMDD.jsonl に出力する。
    /// </summary>
    public static class SyncLogger
    {
        private static readonly object _writeLock = new object();

        private class SyncLogEntry
        {
            public DateTime Timestamp { get; set; }
            public string Level { get; set; }
            public string Phase { get; set; }
            public string NoteId { get; set; }
            public string Message { get; set; }
        }

        public static void Info(string phase, string message, string noteId = null) => Write("info", phase, message, noteId);

        public static void Warn(string phase, string message, string noteId = null) => Write("warn", phase, message, noteId);

        public static void Error(string phase, string message, string noteId = null) => Write("error", phase, message, noteId);

        public static void Error(string phase, Exception ex, string noteId = null) => Write("error", phase, ex?.Message ?? "unknown error", noteId);

        private static void Write(string level, string phase, string message, string noteId)
        {
            try
            {
                var entry = new SyncLogEntry
                {
                    Timestamp = DateTime.UtcNow,
                    Level = level,
                    Phase = phase,
                    NoteId = noteId,
                    Message = message
                };

                var json = JsonSerializer.Serialize(entry);

                lock (_writeLock)
                {
                    var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Flashnote", "logs");
                    if (!Directory.Exists(logDir))
                    {
                        Directory.CreateDirectory(logDir);
                    }

                    var logPath = Path.Combine(logDir, $"sync-{DateTime.UtcNow:yyyyMMdd}.jsonl");
                    File.AppendAllText(logPath, json + Environment.NewLine);
                }
            }
            catch
            {
                // ログ出力自体の失敗で同期処理を止めない
            }
        }
    }
}
