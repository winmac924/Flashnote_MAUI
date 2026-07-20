using System;
using System.IO;

namespace Flashnote.Services.Sync
{
    /// <summary>
    /// サーバーからの上書きとローカルの未同期変更が衝突していないかを検出する。
    /// iOS版 ConflictManager.swift に対応。
    /// </summary>
    public class SyncConflict
    {
        public string NoteName { get; set; }
        public string SubFolder { get; set; }
        public string LocalPath { get; set; }
        public DateTime? LocalLastModified { get; set; }
    }

    public class ConflictManager
    {
        private readonly OfflineSyncQueue _offlineSyncQueue;

        public ConflictManager(OfflineSyncQueue offlineSyncQueue)
        {
            _offlineSyncQueue = offlineSyncQueue;
        }

        /// <summary>
        /// 指定ノート/フォルダについて、ローカルに未送信の変更（オフラインキューに滞留中）があるまま
        /// サーバー側の内容で上書きしようとしていないかを検出する。
        /// </summary>
        /// <param name="noteName">ノート名またはフォルダID</param>
        /// <param name="subFolder">サブフォルダ（ルートの場合はnull）</param>
        /// <param name="localPath">上書き対象のローカルファイルパス（存在すれば更新日時を記録）</param>
        /// <returns>競合がなければ null、あれば SyncConflict</returns>
        public SyncConflict DetectConflict(string noteName, string subFolder, string localPath = null)
        {
            if (_offlineSyncQueue == null) return null;
            if (!_offlineSyncQueue.HasPending(noteName, subFolder)) return null;

            DateTime? localModified = null;
            if (!string.IsNullOrEmpty(localPath) && File.Exists(localPath))
            {
                localModified = File.GetLastWriteTimeUtc(localPath);
            }

            return new SyncConflict
            {
                NoteName = noteName,
                SubFolder = subFolder,
                LocalPath = localPath,
                LocalLastModified = localModified
            };
        }
    }
}
