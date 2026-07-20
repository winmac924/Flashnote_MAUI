using System.Threading.Tasks;
using Flashnote.Services.Sync;

namespace Flashnote.Services
{
    /// <summary>
    /// ノート/カード同期の後方互換ファサード。
    /// 実体は Services/Sync 配下の NoteSyncService・MetadataSyncService・MaterialSyncService に委譲する
    /// （iOS版の Core/Sync ディレクトリ構成に対応）。
    /// DI登録・コンストラクタ注入されている既存の呼び出し箇所を変更せずに済むよう、
    /// クラス名・公開メソッドのシグネチャは維持している。
    /// </summary>
    public class CardSyncService
    {
        private readonly NoteSyncService _noteSyncService;
        private readonly MetadataSyncService _metadataSyncService;

        public CardSyncService(BlobStorageService blobStorageService, SharedKeyService sharedKeyService = null, OfflineSyncQueue offlineSyncQueue = null)
        {
            var sharedKey = sharedKeyService ?? new SharedKeyService();
            var materialSyncService = new MaterialSyncService(blobStorageService);
            _metadataSyncService = new MetadataSyncService(blobStorageService, offlineSyncQueue);
            _noteSyncService = new NoteSyncService(blobStorageService, sharedKey, _metadataSyncService, materialSyncService);
        }

        /// <summary>
        /// ノートを開く際の同期処理（MainPage → Confirmation）
        /// </summary>
        public Task SyncNoteOnOpenAsync(string uid, string noteName, string subFolder = null)
            => _noteSyncService.SyncNoteOnOpenAsync(uid, noteName, subFolder);

        public Task SyncAllNotesAsync(string uid)
            => _noteSyncService.SyncAllNotesAsync(uid);

        /// <summary>
        /// 共有ノートを同期する
        /// </summary>
        public Task SyncSharedNotesAsync(string uid)
            => _noteSyncService.SyncSharedNotesAsync(uid);

        /// <summary>
        /// 共有ノートを同期する（単一ノート）
        /// </summary>
        public Task SyncSharedNoteAsync(string originalUserId, string notePath, string noteName, string subFolder = null)
            => _noteSyncService.SyncSharedNoteAsync(originalUserId, notePath, noteName, subFolder);

        /// <summary>
        /// 共有フォルダを同期する
        /// </summary>
        public Task SyncSharedFolderAsync(string originalUserId, string folderPath, string shareKey)
            => _noteSyncService.SyncSharedFolderAsync(originalUserId, folderPath, shareKey);

        public Task SyncAllNotesMetadataAsync(string uid)
            => _metadataSyncService.SyncAllNotesMetadataAsync(uid);

        /// <summary>
        /// 特定のサブフォルダ内のノートのみを同期する
        /// </summary>
        public Task SyncSubFolderMetadataAsync(string uid, string subFolder)
            => _metadataSyncService.SyncSubFolderMetadataAsync(uid, subFolder);
    }
}
