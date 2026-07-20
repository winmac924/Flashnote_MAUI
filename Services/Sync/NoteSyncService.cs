using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Flashnote.Services;

namespace Flashnote.Services.Sync
{
    /// <summary>
    /// ノート単位の同期オーケストレーション。iOS版 NoteSyncService.swift に対応。
    /// Metadata → Card(本体) → Material(画像) の順で各サービスを呼び出す。
    /// </summary>
    public class NoteSyncService
    {
        private readonly BlobStorageService _blobStorageService;
        private readonly SharedKeyService _sharedKeyService;
        private readonly MetadataSyncService _metadataSyncService;
        private readonly MaterialSyncService _materialSyncService;
        private readonly string _localBasePath;
        private readonly string _tempBasePath;

        public NoteSyncService(
            BlobStorageService blobStorageService,
            SharedKeyService sharedKeyService,
            MetadataSyncService metadataSyncService,
            MaterialSyncService materialSyncService)
        {
            _blobStorageService = blobStorageService;
            _sharedKeyService = sharedKeyService ?? new SharedKeyService();
            _metadataSyncService = metadataSyncService;
            _materialSyncService = materialSyncService;
            _localBasePath = SyncPathResolver.GetLocalNoteRoot();
            _tempBasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Flashnote");
        }

        /// <summary>
        /// ノートを開く際の同期処理（MainPage → Confirmation）
        /// </summary>
        public async Task SyncNoteOnOpenAsync(string uid, string noteName, string subFolder = null)
        {
            try
            {
                var serverContent = await _blobStorageService.GetNoteContentAsync(uid, noteName, subFolder);
                if (serverContent == null) return;

                // Ensure temp dir
                string localCardsPath;
                if (!string.IsNullOrEmpty(subFolder))
                    localCardsPath = Path.Combine(_tempBasePath, subFolder, noteName + "_temp", "cards.txt");
                else
                    localCardsPath = Path.Combine(_tempBasePath, noteName + "_temp", "cards.txt");

                var localContent = string.Empty;
                if (File.Exists(localCardsPath)) localContent = await File.ReadAllTextAsync(localCardsPath);

                // simple two-way placeholder behaviour: save server cards.txt to temp when local missing
                if (string.IsNullOrEmpty(localContent) && !string.IsNullOrEmpty(serverContent))
                {
                    var tempDir = Path.GetDirectoryName(localCardsPath);
                    if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);
                    await File.WriteAllTextAsync(localCardsPath, serverContent);
                }

                // 画像同期
                await _materialSyncService.SyncImagesOnNoteOpenAsync(uid, noteName, subFolder, localCardsPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SyncNoteOnOpenAsync error: {ex.Message}");
                throw;
            }
        }

        public async Task SyncAllNotesAsync(string uid)
        {
            // Perform metadata-only sync by default
            await _metadataSyncService.SyncAllNotesMetadataAsync(uid);
            await SyncSharedNotesAsync(uid);
        }

        /// <summary>
        /// 共有ノートを同期する
        /// </summary>
        public async Task SyncSharedNotesAsync(string uid)
        {
            try
            {
                var sharedNotes = _sharedKeyService?.GetSharedNotes();
                if (sharedNotes == null) return;

                foreach (var kv in sharedNotes)
                {
                    var noteName = kv.Key;
                    var sharedInfo = kv.Value;
                    try
                    {
                        if (sharedInfo.IsFolder)
                        {
                            // Download folder metadata only
                            await SyncSharedFolderAsync(sharedInfo.OriginalUserId, sharedInfo.NotePath, sharedInfo.ShareKey);
                        }
                        else
                        {
                            await SyncSharedNoteAsync(sharedInfo.OriginalUserId, sharedInfo.NotePath, noteName, null);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error syncing shared note {noteName}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SyncSharedNotesAsync error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 共有ノートを同期する（単一ノート）
        /// </summary>
        public async Task SyncSharedNoteAsync(string originalUserId, string notePath, string noteName, string subFolder = null)
        {
            // Minimal: download metadata.json if exists
            try
            {
                var metadata = await _blobStorageService.GetSharedNoteFileAsync(originalUserId, notePath, "metadata.json");
                if (!string.IsNullOrEmpty(metadata))
                {
                    var parentDir = Path.Combine(_localBasePath, subFolder ?? string.Empty);
                    Directory.CreateDirectory(parentDir);
                    var tempDir = Path.Combine(_tempBasePath, subFolder ?? string.Empty, noteName + "_temp");
                    if (Directory.Exists(tempDir)) try { Directory.Delete(tempDir, true); } catch { }
                    Directory.CreateDirectory(tempDir);
                    File.WriteAllText(Path.Combine(tempDir, "metadata.json"), metadata);
                    ZipFile.CreateFromDirectory(tempDir, Path.Combine(parentDir, noteName + ".ankpls"));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SyncSharedNoteAsync error: {ex.Message}");
            }
        }

        /// <summary>
        /// 共有フォルダを同期する
        /// </summary>
        public async Task SyncSharedFolderAsync(string originalUserId, string folderPath, string shareKey)
        {
            try
            {
                // フォルダID(UUID)を取得
                var parts = folderPath?.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts == null || parts.Length == 0)
                {
                    Debug.WriteLine("SyncSharedFolderAsync: folderPath が不正です");
                    return;
                }
                var folderId = parts[^1];

                // 通常フォルダと同様のアクセス方法:
                // uid だけが originalUserId に変わる
                // subFolder=folderId, noteName="metadata.json"
                var metadata = await _blobStorageService.GetNoteContentAsync(
                    originalUserId,      // ← 通常は uid、共有では originalUserId
                    "metadata.json",     // ← noteName
                    folderId);           // ← subFolder (UUID)

                if (string.IsNullOrEmpty(metadata))
                {
                    Debug.WriteLine($"SyncSharedFolderAsync: metadata が取得できませんでした (user={originalUserId}, folderId={folderId})");
                    return;
                }

                // ローカル保存 (Documents\Flashnote\{FolderUUID}\metadata.json)
                var parentDir = _localBasePath;
                Directory.CreateDirectory(parentDir);
                var folderDir = Path.Combine(parentDir, folderId);
                Directory.CreateDirectory(folderDir);
                var localMetaPath = Path.Combine(folderDir, "metadata.json");
                File.WriteAllText(localMetaPath, metadata);
                Debug.WriteLine($"SyncSharedFolderAsync: フォルダメタデータを保存しました -> {localMetaPath}");

                // LocalApplicationData 側にもコピー
                try
                {
                    var tempMetaDir = Path.Combine(_tempBasePath, folderId);
                    Directory.CreateDirectory(tempMetaDir);
                    var tempMetaPath = Path.Combine(tempMetaDir, "metadata.json");
                    File.WriteAllText(tempMetaPath, metadata);
                    Debug.WriteLine($"SyncSharedFolderAsync: 一時メタデータも保存しました -> {tempMetaPath}");
                }
                catch (Exception exMeta)
                {
                    Debug.WriteLine($"SyncSharedFolderAsync: 一時メタデータ保存に失敗しました: {exMeta.Message}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SyncSharedFolderAsync error: {ex.Message}");
            }
        }
    }
}
