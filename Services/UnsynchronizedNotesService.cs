using Flashnote.Services.Sync;

namespace Flashnote.Services
{
    /// <summary>
    /// 後方互換のためのエイリアス。実体は Services/Sync/OfflineSyncQueue.cs
    /// （iOS版 OfflineSyncQueue.swift に対応する名称）。
    /// DI登録・<c>MauiProgram.Services.GetService&lt;UnsynchronizedNotesService&gt;()</c> による
    /// 既存の呼び出し箇所を変更せずに済むよう、クラス名はそのまま維持している。
    /// </summary>
    public class UnsynchronizedNotesService : OfflineSyncQueue
    {
    }
}
