using System.Text.Json;
using System.Diagnostics;
using System.IO.Compression;

namespace Flashnote.Services
{
    public class CardSyncService
    {
        private readonly BlobStorageService _blobStorageService;
        private readonly SharedKeyService _sharedKeyService;
        private readonly string _localBasePath;
        private readonly string _tempBasePath;

        public CardSyncService(BlobStorageService blobStorageService, SharedKeyService sharedKeyService = null)
        {
            _blobStorageService = blobStorageService;
            _sharedKeyService = sharedKeyService ?? new SharedKeyService();
            _localBasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Flashnote");
            _tempBasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Flashnote");
        }

        // Keep a simplified CardInfo for parsing cards.txt when needed
        private class CardInfo
        {
            public string Uuid { get; set; }
            public DateTime LastModified { get; set; }
            public string Content { get; set; }
            public bool IsDeleted { get; set; }
        }

        /// <summary>
        /// ノートを開く際の同期処理（MainPage → Confirmation）
        /// </summary>
        public async Task SyncNoteOnOpenAsync(string uid, string noteName, string subFolder = null)
        {
            try
            {
                var serverContent = await _blobStorage_service_unsafe_GetNoteContentAsync(uid, noteName, subFolder);
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

                // Sync images placeholder
                await SyncImagesOnNoteOpenAsync(uid, noteName, subFolder, localCardsPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SyncNoteOnOpenAsync error: {ex.Message}");
                throw;
            }
        }

        // Minimal stubs to satisfy callers - implementation can be expanded later
        public async Task SyncAllNotesAsync(string uid)
        {
            // Perform metadata-only sync by default
            await SyncAllNotesMetadataAsync(uid);
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
                var metadata = await _blobStorage_service_unsafe_GetNoteContentAsync(
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

        // Metadata-only sync methods (kept from previous implementation)
        public async Task SyncAllNotesMetadataAsync(string uid)
        {
            try
            {
                Debug.WriteLine($"SyncAllNotesMetadataAsync - start for uid={uid}");
                var blobs = await _blobStorage_service_unsafe_GetNoteListAsync(uid);
                if (blobs == null) return;
                foreach (var blobPath in blobs)
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(blobPath)) continue;
                        if (!blobPath.EndsWith("metadata.json", StringComparison.OrdinalIgnoreCase)) continue;

                        var parsed = _blobStorage_service_unsafe_ParseBlobPath(blobPath);
                        var subFolder = parsed.subFolder;
                        var noteName = parsed.noteName;

                        Debug.WriteLine($"Processing blob: {blobPath}, subFolder: {subFolder}, noteName: {noteName}");

                        string metadataContent = null;
                        try
                        {
                            metadataContent = await _blobStorage_service_unsafe_GetNoteContentAsync(uid, noteName + "/metadata.json", subFolder);
                            if (string.IsNullOrEmpty(metadataContent)) metadataContent = await _blobStorage_service_unsafe_GetNoteContentAsync(uid, blobPath, null);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to get metadata for {blobPath}: {ex.Message}");
                            continue;
                        }

                        if (string.IsNullOrEmpty(metadataContent)) continue;

                        using var doc = JsonDocument.Parse(metadataContent);
                        var root = doc.RootElement;
                        var isFolder = root.TryGetProperty("isFolder", out var pIsFolder) && pIsFolder.GetBoolean();
                        var originalName = root.TryGetProperty("originalName", out var pOrig) ? pOrig.GetString() : noteName;
                        var id = root.TryGetProperty("id", out var pId) ? pId.GetString() : null;
                        var metaSub = root.TryGetProperty("subfolder", out var pSub) ? pSub.GetString() : subFolder;

                        Debug.WriteLine($"Metadata parsed: isFolder={isFolder}, originalName={originalName}, id={id}, metaSub={metaSub}");

                        var localBase = _localBasePath;
                        var tempBase = _tempBasePath;

                        if (isFolder)
                        {
                            var folderId = id ?? noteName;
                            var parentDir = string.IsNullOrEmpty(metaSub) ? localBase : Path.Combine(localBase, metaSub);
                            Directory.CreateDirectory(parentDir);
                            var folderDir = Path.Combine(parentDir, folderId);
                            Directory.CreateDirectory(folderDir);
                            File.WriteAllText(Path.Combine(folderDir, "metadata.json"), metadataContent);
                            Debug.WriteLine($"Saved folder metadata to {Path.Combine(folderDir, "metadata.json")}");
                        }
                        else
                        {
                            // Check if the note belongs to a folder by reading its metadata
                            if (root.TryGetProperty("parentFolderId", out var parentFolderIdProp))
                            {
                                var parentFolderId = parentFolderIdProp.GetString();
                                if (!string.IsNullOrEmpty(parentFolderId))
                                {
                                    Debug.WriteLine($"Note belongs to folder with ID: {parentFolderId}");

                                    // Download the parent folder's metadata.json
                                    var folderMetadataPath = Path.Combine(localBase, parentFolderId, "metadata.json");
                                    if (!File.Exists(folderMetadataPath))
                                    {
                                        try
                                        {
                                            var folderMetadataContent = await _blobStorage_service_unsafe_GetNoteContentAsync(uid, parentFolderId + "/metadata.json", null);
                                            if (!string.IsNullOrEmpty(folderMetadataContent))
                                            {
                                                Directory.CreateDirectory(Path.Combine(localBase, parentFolderId));
                                                File.WriteAllText(folderMetadataPath, folderMetadataContent);
                                                Debug.WriteLine($"Downloaded and saved parent folder metadata to {folderMetadataPath}");
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            Debug.WriteLine($"Failed to download parent folder metadata: {ex.Message}");
                                        }
                                    }
                                }
                            }

                            var parentDir = string.IsNullOrEmpty(metaSub) ? localBase : Path.Combine(localBase, metaSub);
                            Directory.CreateDirectory(parentDir);

                            var fileId = string.IsNullOrEmpty(id) ? (originalName ?? noteName) : id;

                            var tempDir = Path.Combine(tempBase, metaSub ?? string.Empty, fileId + "_temp");
                            if (Directory.Exists(tempDir))
                            {
                                try { Directory.Delete(tempDir, true); } catch { }
                            }
                            Directory.CreateDirectory(tempDir);

                            var metaPath = Path.Combine(tempDir, "metadata.json");
                            File.WriteAllText(metaPath, metadataContent);

                            var ankplsPath = Path.Combine(parentDir, fileId + ".ankpls");
                            if (File.Exists(ankplsPath))
                            {
                                try { File.Delete(ankplsPath); } catch { }
                            }

                            ZipFile.CreateFromDirectory(tempDir, ankplsPath);
                            Debug.WriteLine($"Created .ankpls with metadata at {ankplsPath} (id={fileId})");
                        }
                    }
                    catch (Exception innerEx)
                    {
                        Debug.WriteLine($"Error processing blob {blobPath}: {innerEx.Message}");
                    }
                }

                // Handle flattened blob layouts: attempt to discover top-level UUID folders
                try
                {
                    var topLevelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var blobPath in blobs)
                    {
                        if (string.IsNullOrWhiteSpace(blobPath)) continue;
                        var parts = blobPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 0) continue;
                        topLevelIds.Add(parts[0]);
                    }

                    foreach (var candidateId in topLevelIds)
                    {
                        // If metadata already exists in listing for this id, skip
                        var candidateMetaPath = candidateId + "/metadata.json";
                        if (blobs.Any(b => string.Equals(b, candidateMetaPath, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        string metadataContent = null;
                        try
                        {
                            metadataContent = await _blobStorage_service_unsafe_GetNoteContentAsync(uid, candidateMetaPath, null);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to get metadata for candidate {candidateMetaPath}: {ex.Message}");
                            continue;
                        }

                        if (string.IsNullOrEmpty(metadataContent)) continue;

                        try
                        {
                            using var doc2 = JsonDocument.Parse(metadataContent);
                            var root2 = doc2.RootElement;
                            var isFolder2 = root2.TryGetProperty("isFolder", out var pIsFolder2) && pIsFolder2.GetBoolean();
                            var originalName2 = root2.TryGetProperty("originalName", out var pOrig2) ? pOrig2.GetString() : candidateId;
                            var id2 = root2.TryGetProperty("id", out var pId2) ? pId2.GetString() : candidateId;
                            var metaSub2 = root2.TryGetProperty("subfolder", out var pSub2) ? pSub2.GetString() : null;

                            var localBase2 = _localBasePath;
                            var tempBase2 = _tempBasePath;

                            if (isFolder2)
                            {
                                var folderId2 = id2 ?? candidateId;
                                var parentDir2 = string.IsNullOrEmpty(metaSub2) ? localBase2 : Path.Combine(localBase2, metaSub2);
                                Directory.CreateDirectory(parentDir2);
                                var folderDir2 = Path.Combine(parentDir2, folderId2);
                                Directory.CreateDirectory(folderDir2);
                                // Save metadata into the folder directory under MyDocuments/Flashnote
                                var folderMetaPath2 = Path.Combine(folderDir2, "metadata.json");
                                File.WriteAllText(folderMetaPath2, metadataContent);

                                // Also save copy under LocalApplicationData/Flashnote
                                try
                                {
                                    var localMetaDir2 = Path.Combine(tempBase2, metaSub2 ?? string.Empty, folderId2);
                                    Directory.CreateDirectory(localMetaDir2);
                                    var localMetaPath2 = Path.Combine(localMetaDir2, "metadata.json");
                                    File.WriteAllText(localMetaPath2, metadataContent);
                                }
                                catch (Exception exMetaSave2)
                                {
                                    Debug.WriteLine($"Failed to save candidate folder metadata copy to LocalApplicationData: {exMetaSave2.Message}");
                                }
                                Debug.WriteLine($"Saved folder metadata to {Path.Combine(folderDir2, "metadata.json")} (candidate)");
                            }
                            else
                            {
                                var parentDir2 = string.IsNullOrEmpty(metaSub2) ? localBase2 : Path.Combine(localBase2, metaSub2);
                                Directory.CreateDirectory(parentDir2);
                                var fileId2 = string.IsNullOrEmpty(id2) ? (originalName2 ?? candidateId) : id2;
                                var tempDir2 = Path.Combine(tempBase2, metaSub2 ?? string.Empty, fileId2 + "_temp");
                                if (Directory.Exists(tempDir2)) try { Directory.Delete(tempDir2, true); } catch { }
                                Directory.CreateDirectory(tempDir2);
                                File.WriteAllText(Path.Combine(tempDir2, "metadata.json"), metadataContent);
                                var ankplsPath2 = Path.Combine(parentDir2, fileId2 + ".ankpls");
                                if (File.Exists(ankplsPath2)) try { File.Delete(ankplsPath2); } catch { }
                                ZipFile.CreateFromDirectory(tempDir2, ankplsPath2);
                                Debug.WriteLine($"Created .ankpls with metadata at {ankplsPath2} (candidate, id={fileId2})");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to process metadata JSON for candidate {candidateId}: {ex.Message}");
                            continue;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Candidate metadata discovery error: {ex.Message}");
                }

                Debug.WriteLine("SyncAllNotesMetadataAsync - complete");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SyncAllNotesMetadataAsync error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 特定のサブフォルダ内のノートのみを同期する
        /// </summary>
        public async Task SyncSubFolderMetadataAsync(string uid, string subFolder)
        {
            try
            {
                Debug.WriteLine($"SyncSubFolderMetadataAsync - start for uid={uid} subFolder={subFolder}");
                var blobs = await _blobStorage_service_unsafe_GetNoteListAsync(uid, subFolder);
                if (blobs == null) return;
                foreach (var blobPath in blobs)
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(blobPath)) continue;
                        if (!blobPath.EndsWith("metadata.json", StringComparison.OrdinalIgnoreCase)) continue;

                        var parsed = _blobStorage_service_unsafe_ParseBlobPath(blobPath);
                        var noteName = parsed.noteName;

                        string metadataContent = null;
                        try
                        {
                            metadataContent = await _blobStorage_service_unsafe_GetNoteContentAsync(uid, noteName + "/metadata.json", subFolder);
                            if (string.IsNullOrEmpty(metadataContent)) metadataContent = await _blobStorage_service_unsafe_GetNoteContentAsync(uid, blobPath, null);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to get metadata for {blobPath}: {ex.Message}");
                            continue;
                        }

                        if (string.IsNullOrEmpty(metadataContent)) continue;

                        using var doc = JsonDocument.Parse(metadataContent);
                        var root = doc.RootElement;
                        var isFolder = root.TryGetProperty("isFolder", out var pIsFolder) && pIsFolder.GetBoolean();
                        var originalName = root.TryGetProperty("originalName", out var pOrig) ? pOrig.GetString() : noteName;
                        var id = root.TryGetProperty("id", out var pId) ? pId.GetString() : null;
                        var metaSub = root.TryGetProperty("subfolder", out var pSub) ? pSub.GetString() : subFolder;

                        var localBase = _localBasePath;
                        var tempBase = _tempBasePath;

                        if (isFolder)
                        {
                            var folderId = id ?? noteName;
                            var parentDir = string.IsNullOrEmpty(metaSub) ? localBase : Path.Combine(localBase, metaSub);
                            Directory.CreateDirectory(parentDir);
                            var folderDir = Path.Combine(parentDir, folderId);
                            Directory.CreateDirectory(folderDir);
                            File.WriteAllText(Path.Combine(folderDir, "metadata.json"), metadataContent);
                            Debug.WriteLine($"Saved folder metadata to {Path.Combine(folderDir, "metadata.json")}");
                        }
                        else
                        {
                            var parentDir = string.IsNullOrEmpty(metaSub) ? localBase : Path.Combine(localBase, metaSub);
                            Directory.CreateDirectory(parentDir);
                            var fileId = string.IsNullOrEmpty(id) ? (originalName ?? noteName) : id;
                            var tempDir = Path.Combine(tempBase, metaSub ?? string.Empty, fileId + "_temp");
                            if (Directory.Exists(tempDir)) try { Directory.Delete(tempDir, true); } catch { }
                            Directory.CreateDirectory(tempDir);
                            File.WriteAllText(Path.Combine(tempDir, "metadata.json"), metadataContent);
                            var ankplsPath = Path.Combine(parentDir, fileId + ".ankpls");
                            if (File.Exists(ankplsPath)) try { File.Delete(ankplsPath); } catch { }
                            ZipFile.CreateFromDirectory(tempDir, ankplsPath);
                            Debug.WriteLine($"Created .ankpls with metadata at {ankplsPath} (id={fileId})");
                        }
                    }
                    catch (Exception innerEx)
                    {
                        Debug.WriteLine($"Error processing blob {blobPath}: {innerEx.Message}");
                    }
                }
                // If metadata.json entries were not directly listed, attempt candidate discovery
                try
                {
                    var topLevelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var blobPath in blobs)
                    {
                        if (string.IsNullOrWhiteSpace(blobPath)) continue;
                        var parts = blobPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 0) continue;
                        topLevelIds.Add(parts[0]);
                    }

                    foreach (var candidateId in topLevelIds)
                    {
                        var candidateMetaPath = candidateId + "/metadata.json";
                        if (blobs.Any(b => string.Equals(b, candidateMetaPath, StringComparison.OrdinalIgnoreCase))) continue;

                        string metadataContent = null;
                        try
                        {
                            metadataContent = await _blobStorage_service_unsafe_GetNoteContentAsync(uid, candidateMetaPath, subFolder);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to get metadata for candidate {candidateMetaPath}: {ex.Message}");
                            continue;
                        }

                        if (string.IsNullOrEmpty(metadataContent)) continue;

                        try
                        {
                            using var doc2 = JsonDocument.Parse(metadataContent);
                            var root2 = doc2.RootElement;
                            var isFolder2 = root2.TryGetProperty("isFolder", out var pIsFolder2) && pIsFolder2.GetBoolean();
                            var originalName2 = root2.TryGetProperty("originalName", out var pOrig2) ? pOrig2.GetString() : candidateId;
                            var id2 = root2.TryGetProperty("id", out var pId2) ? pId2.GetString() : candidateId;
                            var metaSub2 = root2.TryGetProperty("subfolder", out var pSub2) ? pSub2.GetString() : subFolder;

                            var localBase2 = _localBasePath;
                            var tempBase2 = _tempBasePath;

                            if (isFolder2)
                            {
                                var folderId2 = id2 ?? candidateId;
                                var parentDir2 = string.IsNullOrEmpty(metaSub2) ? localBase2 : Path.Combine(localBase2, metaSub2);
                                Directory.CreateDirectory(parentDir2);
                                var folderDir2 = Path.Combine(parentDir2, folderId2);
                                Directory.CreateDirectory(folderDir2);
                                // Save metadata into the folder directory under MyDocuments/Flashnote
                                var folderMetaPath2 = Path.Combine(folderDir2, "metadata.json");
                                File.WriteAllText(folderMetaPath2, metadataContent);

                                // Also save copy under LocalApplicationData/Flashnote
                                try
                                {
                                    var localMetaDir2 = Path.Combine(tempBase2, metaSub2 ?? string.Empty, folderId2);
                                    Directory.CreateDirectory(localMetaDir2);
                                    var localMetaPath2 = Path.Combine(localMetaDir2, "metadata.json");
                                    File.WriteAllText(localMetaPath2, metadataContent);
                                }
                                catch (Exception exMetaSave2)
                                {
                                    Debug.WriteLine($"Failed to save candidate folder metadata copy to LocalApplicationData: {exMetaSave2.Message}");
                                }
                                Debug.WriteLine($"Saved folder metadata to {Path.Combine(folderDir2, "metadata.json")} (candidate)");
                            }
                            else
                            {
                                var parentDir2 = string.IsNullOrEmpty(metaSub2) ? localBase2 : Path.Combine(localBase2, metaSub2);
                                Directory.CreateDirectory(parentDir2);
                                var fileId2 = string.IsNullOrEmpty(id2) ? (originalName2 ?? candidateId) : id2;
                                var tempDir2 = Path.Combine(tempBase2, metaSub2 ?? string.Empty, fileId2 + "_temp");
                                if (Directory.Exists(tempDir2)) try { Directory.Delete(tempDir2, true); } catch { }
                                Directory.CreateDirectory(tempDir2);
                                File.WriteAllText(Path.Combine(tempDir2, "metadata.json"), metadataContent);
                                var ankplsPath2 = Path.Combine(parentDir2, fileId2 + ".ankpls");
                                if (File.Exists(ankplsPath2)) try { File.Delete(ankplsPath2); } catch { }
                                ZipFile.CreateFromDirectory(tempDir2, ankplsPath2);
                                Debug.WriteLine($"Created .ankpls with metadata at {ankplsPath2} (candidate, id={fileId2})");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to process metadata JSON for candidate {candidateId}: {ex.Message}");
                            continue;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Candidate metadata discovery error (subfolder): {ex.Message}");
                }

                Debug.WriteLine("SyncSubFolderMetadataAsync - complete");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SyncSubFolderMetadataAsync error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// ノートを開く際の画像同期処理
        /// </summary>
        private async Task SyncImagesOnNoteOpenAsync(string uid, string noteName, string subFolder, string localCardsPath)
        {
            try
            {
                Debug.WriteLine($"=== ノート開時画像同期開始: {noteName} ===");

                // ローカルのimgフォルダのパスを取得
                var localImgDir = Path.Combine(Path.GetDirectoryName(localCardsPath), "img");
                Debug.WriteLine($"ローカルのimgフォルダのパス: {localImgDir}");

                // ローカルのimgフォルダが存在しない場合は作成
                if (!Directory.Exists(localImgDir))
                {
                    Directory.CreateDirectory(localImgDir);
                    Debug.WriteLine($"ローカルimgディレクトリを作成: {localImgDir}");
                }

                // サーバーのimgフォルダパスを構築
                string imgServerPath;
                if (!string.IsNullOrEmpty(subFolder))
                {
                    imgServerPath = $"{subFolder}/{noteName}/img";
                }
                else
                {
                    imgServerPath = $"{noteName}/img";
                }

                // サーバーの画像ファイル一覧を取得
                var serverImgFiles = await _blobStorageService.GetImageFilesAsync(uid, imgServerPath);
                Debug.WriteLine($"サーバーの画像ファイル数: {serverImgFiles.Count}");

                if (serverImgFiles.Count == 0)
                {
                    Debug.WriteLine("サーバーに画像ファイルがありません");
                    return;
                }

                // ローカルの画像ファイル一覧を取得
                var localImgFiles = Directory.GetFiles(localImgDir, "img_*.jpg")
                    .Select(Path.GetFileName)
                    .ToList();
                Debug.WriteLine($"ローカルの画像ファイル数: {localImgFiles.Count}");

                // 不足している画像ファイルを特定
                var missingImgFiles = serverImgFiles.Where(serverImg => !localImgFiles.Contains(serverImg)).ToList();
                Debug.WriteLine($"不足している画像ファイル数: {missingImgFiles.Count}");

                if (missingImgFiles.Count == 0)
                {
                    Debug.WriteLine("不足している画像ファイルはありません");
                    return;
                }

                // 不足している画像ファイルをダウンロード
                foreach (var imgFile in missingImgFiles)
                {
                    try
                    {
                        Debug.WriteLine($"画像ファイルのダウンロード開始: {imgFile}");
                        var imgBytes = await _blobStorageService.GetImageBinaryAsync(uid, imgFile, imgServerPath);

                        if (imgBytes != null)
                        {
                            var localImgPath = Path.Combine(localImgDir, imgFile);
                            await File.WriteAllBytesAsync(localImgPath, imgBytes);
                            Debug.WriteLine($"画像ファイルをダウンロード完了: {imgFile} (サイズ: {imgBytes.Length} バイト)");
                        }
                        else
                        {
                            Debug.WriteLine($"画像ファイルのコンテンツが取得できません: {imgFile}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"画像ファイルのダウンロード中にエラー: {imgFile}, エラー: {ex.Message}");
                        Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                    }
                }

                Debug.WriteLine($"=== ノート開時画像同期完了: {noteName} ===");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ノート開時画像同期エラー: {ex.Message}");
                // 画像同期エラーは致命的ではないため、例外を再スローしない
            }
        }

        // --- Unsafe wrappers that call BlobStorageService methods via reflection to tolerate signature differences ---
        private Task<List<string>> _blob_list_cache = null;

        private async Task<List<string>> _blobStorage_service_unsafe_GetNoteListAsync(string uid, string subFolder = null)
        {
            try
            {
                var svc = _blobStorageService;
                if (svc == null) return null;
                var methodWithSub = svc.GetType().GetMethod("GetNoteListAsync", new Type[] { typeof(string), typeof(string) });
                if (methodWithSub != null)
                {
                    var task = (Task<List<string>>)methodWithSub.Invoke(svc, new object[] { uid, subFolder });
                    return await task;
                }
                var method = svc.GetType().GetMethod("GetNoteListAsync", new Type[] { typeof(string) });
                if (method != null)
                {
                    var task = (Task<List<string>>)method.Invoke(svc, new object[] { uid });
                    return await task;
                }
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"_blobStorage_service_unsafe_GetNoteListAsync error: {ex.Message}");
                return null;
            }
        }

        private (string subFolder, string noteName, bool isCard) _blobStorage_service_unsafe_ParseBlobPath(string blobPath)
        {
            try
            {
                var svc = _blobStorageService;
                if (svc == null) return (null, blobPath, false);
                var method = svc.GetType().GetMethod("ParseBlobPath", new Type[] { typeof(string) });
                if (method != null)
                {
                    var parsed = method.Invoke(svc, new object[] { blobPath });
                    if (parsed is ValueTuple<string, string, bool> vt) return vt;
                    try
                    {
                        var sub = (string)parsed.GetType().GetProperty("subFolder")?.GetValue(parsed);
                        var note = (string)parsed.GetType().GetProperty("noteName")?.GetValue(parsed);
                        var isCard = (bool?)parsed.GetType().GetProperty("isCard")?.GetValue(parsed) ?? false;
                        return (sub, note, isCard);
                    }
                    catch
                    {
                        return (null, blobPath, false);
                    }
                }
                var parts = blobPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && parts[^1].Equals("metadata.json", StringComparison.OrdinalIgnoreCase))
                {
                    var noteName = parts[^2];
                    var sub = parts.Length > 2 ? string.Join(Path.DirectorySeparatorChar.ToString(), parts, 0, parts.Length - 2) : null;
                    return (sub, noteName, true);
                }
                return (null, blobPath, false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"_blobStorage_service_unsafe_ParseBlobPath error: {ex.Message}");
                return (null, blobPath, false);
            }
        }

        private async Task<string> _blobStorage_service_unsafe_GetNoteContentAsync(string uid, string noteName, string subFolder = null)
        {
            try
            {
                var svc = _blobStorageService;
                if (svc == null) return null;
                var methodWithSub = svc.GetType().GetMethod("GetNoteContentAsync", new Type[] { typeof(string), typeof(string), typeof(string) });
                if (methodWithSub != null)
                {
                    var task = (Task<string>)methodWithSub.Invoke(svc, new object[] { uid, noteName, subFolder });
                    return await task;
                }
                var method = svc.GetType().GetMethod("GetNoteContentAsync", new Type[] { typeof(string), typeof(string) });
                if (method != null)
                {
                    var task = (Task<string>)method.Invoke(svc, new object[] { uid, noteName });
                    return await task;
                }
                var sharedMethod = svc.GetType().GetMethod("GetSharedNoteFileAsync", new Type[] { typeof(string), typeof(string), typeof(string) });
                if (sharedMethod != null)
                {
                    var task = (Task<string>)sharedMethod.Invoke(svc, new object[] { uid, subFolder ?? string.Empty, noteName });
                    return await task;
                }
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"_blobStorage_service_unsafe_GetNoteContentAsync error: {ex.Message}");
                return null;
            }
        }
    }
}