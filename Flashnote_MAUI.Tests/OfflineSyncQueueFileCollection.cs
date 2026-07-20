namespace Flashnote_MAUI.Tests
{
    // OfflineSyncQueue と ConflictManager は同じ物理ファイル(unsynchronized_notes.json)を
    // 読み書きするため、xUnitのデフォルトのテストクラス並列実行だとレースコンディションで
    // 互いの書き込みを上書きしてしまう。同一コレクションにまとめて直列実行させる。
    [CollectionDefinition("OfflineSyncQueueFile", DisableParallelization = true)]
    public class OfflineSyncQueueFileCollection
    {
    }
}
