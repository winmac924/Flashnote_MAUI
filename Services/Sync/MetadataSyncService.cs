using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Flashnote.Services;
using Flashnote.Views;

namespace Flashnote.Services.Sync
{
    /// <summary>
    /// metadata.json のダウンロード/ローカル反映を担当する。iOS版 MetadataSyncService.swift に対応。
    /// </summary>
    public class MetadataSyncService
    {
        private readonly BlobStorageService _blobStorageService;
        private readonly ConflictManager _conflictManager;
        private readonly NoteBackupService _noteBackupService;
        private readonly IncrementalSyncService _incrementalSyncService;
        private readonly string _localBasePath;
        private readonly string _tempBasePath;

        public MetadataSyncService(
            BlobStorageService blobStorageService,
            OfflineSyncQueue offlineSyncQueue = null,
            NoteBackupService noteBackupService = null,
            IncrementalSyncService incrementalSyncService = null)
        {
            _blobStorageService = blobStorageService;
            _conflictManager = new ConflictManager(offlineSyncQueue ?? new OfflineSyncQueue());
            _noteBackupService = noteBackupService ?? new NoteBackupService();
            _incrementalSyncService = incrementalSyncService ?? new IncrementalSyncService();
            _localBasePath = SyncPathResolver.GetLocalNoteRoot();
            _tempBasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Flashnote");
        }

        /// <summary>
        /// ノート本体(.ankpls)をサーバーのメタデータで再構築する。
        /// 上書き前にオフラインキューとの競合を確認し、競合時はユーザーに確認する。
        /// バックアップも同時に取得する。
        /// </summary>
        private async Task WriteNoteAnkplsAsync(string parentDir, string tempBase, string metaSub, string fileId, string metadataContent)
        {
            Directory.CreateDirectory(parentDir);

            var tempDir = Path.Combine(tempBase, metaSub ?? string.Empty, fileId + "_temp");
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "metadata.json"), metadataContent);

            var ankplsPath = Path.Combine(parentDir, fileId + ".ankpls");

            var conflict = _conflictManager.DetectConflict(fileId, metaSub, ankplsPath);
            if (conflict != null)
            {
                var resolution = await SyncConflictResolutionPrompt.ShowAsync(conflict);
                if (resolution == SyncConflictResolution.KeepLocal)
                {
                    Debug.WriteLine($"競合検出: ローカルを優先し、サーバーからの上書きをスキップしました (id={fileId})");
                    return;
                }
                Debug.WriteLine($"競合検出: サーバーを優先して上書きします (id={fileId})");
            }

            _noteBackupService.BackupBeforeOverwrite(ankplsPath, fileId);

            if (File.Exists(ankplsPath))
            {
                try { File.Delete(ankplsPath); } catch { }
            }

            ZipFile.CreateFromDirectory(tempDir, ankplsPath);
            Debug.WriteLine($"Created .ankpls with metadata at {ankplsPath} (id={fileId})");
        }

        public async Task SyncAllNotesMetadataAsync(string uid)
        {
            try
            {
                Debug.WriteLine($"SyncAllNotesMetadataAsync - start for uid={uid}");
                var blobs = await _blobStorageService.GetNoteListAsync(uid);
                if (blobs == null) return;
                foreach (var blobPath in blobs)
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(blobPath)) continue;
                        if (!blobPath.EndsWith("metadata.json", StringComparison.OrdinalIgnoreCase)) continue;

                        var parsed = _blobStorageService.ParseBlobPath(blobPath);
                        var subFolder = parsed.subFolder;
                        var noteName = parsed.noteName;

                        Debug.WriteLine($"Processing blob: {blobPath}, subFolder: {subFolder}, noteName: {noteName}");

                        string metadataContent = null;
                        try
                        {
                            metadataContent = await _blobStorageService.GetNoteContentAsync(uid, noteName + "/metadata.json", subFolder);
                            if (string.IsNullOrEmpty(metadataContent)) metadataContent = await _blobStorageService.GetNoteContentAsync(uid, blobPath, null);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to get metadata for {blobPath}: {ex.Message}");
                            continue;
                        }

                        if (string.IsNullOrEmpty(metadataContent)) continue;

                        if (!_incrementalSyncService.HasChangedSinceLastSync(blobPath, metadataContent))
                        {
                            Debug.WriteLine($"変更なしのためスキップ: {blobPath}");
                            continue;
                        }

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
                                            var folderMetadataContent = await _blobStorageService.GetNoteContentAsync(uid, parentFolderId + "/metadata.json", null);
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
                            var fileId = string.IsNullOrEmpty(id) ? (originalName ?? noteName) : id;

                            await WriteNoteAnkplsAsync(parentDir, tempBase, metaSub, fileId, metadataContent);
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
                            metadataContent = await _blobStorageService.GetNoteContentAsync(uid, candidateMetaPath, null);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to get metadata for candidate {candidateMetaPath}: {ex.Message}");
                            continue;
                        }

                        if (string.IsNullOrEmpty(metadataContent)) continue;

                        if (!_incrementalSyncService.HasChangedSinceLastSync(candidateMetaPath, metadataContent))
                        {
                            Debug.WriteLine($"変更なしのためスキップ (candidate): {candidateMetaPath}");
                            continue;
                        }

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
                                var fileId2 = string.IsNullOrEmpty(id2) ? (originalName2 ?? candidateId) : id2;

                                await WriteNoteAnkplsAsync(parentDir2, tempBase2, metaSub2, fileId2, metadataContent);
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

                await VerifyFolderIntegrityAsync(uid);

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
                var blobs = await _blobStorageService.GetNoteListAsync(uid, subFolder);
                if (blobs == null) return;
                foreach (var blobPath in blobs)
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(blobPath)) continue;
                        if (!blobPath.EndsWith("metadata.json", StringComparison.OrdinalIgnoreCase)) continue;

                        var parsed = _blobStorageService.ParseBlobPath(blobPath);
                        var noteName = parsed.noteName;

                        string metadataContent = null;
                        try
                        {
                            metadataContent = await _blobStorageService.GetNoteContentAsync(uid, noteName + "/metadata.json", subFolder);
                            if (string.IsNullOrEmpty(metadataContent)) metadataContent = await _blobStorageService.GetNoteContentAsync(uid, blobPath, null);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to get metadata for {blobPath}: {ex.Message}");
                            continue;
                        }

                        if (string.IsNullOrEmpty(metadataContent)) continue;

                        if (!_incrementalSyncService.HasChangedSinceLastSync(blobPath, metadataContent))
                        {
                            Debug.WriteLine($"変更なしのためスキップ: {blobPath}");
                            continue;
                        }

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
                            var fileId = string.IsNullOrEmpty(id) ? (originalName ?? noteName) : id;

                            await WriteNoteAnkplsAsync(parentDir, tempBase, metaSub, fileId, metadataContent);
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
                            metadataContent = await _blobStorageService.GetNoteContentAsync(uid, candidateMetaPath, subFolder);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to get metadata for candidate {candidateMetaPath}: {ex.Message}");
                            continue;
                        }

                        if (string.IsNullOrEmpty(metadataContent)) continue;

                        if (!_incrementalSyncService.HasChangedSinceLastSync(candidateMetaPath, metadataContent))
                        {
                            Debug.WriteLine($"変更なしのためスキップ (candidate): {candidateMetaPath}");
                            continue;
                        }

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
                                var fileId2 = string.IsNullOrEmpty(id2) ? (originalName2 ?? candidateId) : id2;

                                await WriteNoteAnkplsAsync(parentDir2, tempBase2, metaSub2, fileId2, metadataContent);
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

                await VerifyFolderIntegrityAsync(uid);

                Debug.WriteLine("SyncSubFolderMetadataAsync - complete");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SyncSubFolderMetadataAsync error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 指定したサブフォルダパスの各階層について、metadata.json の isFolder かつ
        /// trashInfo/isDeleted が設定されていないかを確認する。iOS版 isFolderInTrash に対応。
        /// 現状Windows側にはゴミ箱機能(TrashManager)自体は未移植だが、iOS側で削除された
        /// 共有フォルダ等のtrashInfoフィールドを尊重できるよう、読み取りのみ実装する。
        /// </summary>
        public async Task<bool> IsFolderInTrashAsync(string uid, string subfolder)
        {
            if (string.IsNullOrEmpty(subfolder)) return false;

            var parts = subfolder.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var folderId in parts)
            {
                try
                {
                    var metadataContent = await _blobStorageService.GetNoteContentAsync(uid, folderId + "/metadata.json", null);
                    if (string.IsNullOrEmpty(metadataContent)) continue;

                    using var doc = JsonDocument.Parse(metadataContent);
                    var root = doc.RootElement;

                    var hasTrashInfo = root.TryGetProperty("trashInfo", out var pTrash) && pTrash.ValueKind != JsonValueKind.Null;
                    var isDeleted = root.TryGetProperty("isDeleted", out var pDeleted) && pDeleted.ValueKind == JsonValueKind.True;

                    if (hasTrashInfo || isDeleted)
                    {
                        Debug.WriteLine($"IsFolderInTrashAsync: フォルダ {folderId} はゴミ箱に入っています");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"IsFolderInTrashAsync: {folderId} の確認中にエラー: {ex.Message}");
                }
            }

            return false;
        }

        /// <summary>
        /// サブフォルダパスの各階層について、サーバー側のmetadata.jsonを確認し、
        /// ローカルの2つのディレクトリツリー（Documents/Flashnote と LocalApplicationData/Flashnote）に
        /// フォルダを作成する。iOS版 ensureFolderStructure に対応。
        /// </summary>
        public async Task EnsureFolderStructureAsync(string uid, string subfolder)
        {
            if (string.IsNullOrEmpty(subfolder)) return;

            var parts = subfolder.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            var accumulated = string.Empty;

            foreach (var folderId in parts)
            {
                accumulated = string.IsNullOrEmpty(accumulated) ? folderId : $"{accumulated}/{folderId}";

                try
                {
                    var metadataContent = await _blobStorageService.GetNoteContentAsync(uid, folderId + "/metadata.json", null);
                    if (string.IsNullOrEmpty(metadataContent)) continue;

                    using var doc = JsonDocument.Parse(metadataContent);
                    var root = doc.RootElement;
                    var isFolder = root.TryGetProperty("isFolder", out var pIsFolder) && pIsFolder.GetBoolean();
                    if (!isFolder) continue;

                    // Documents/Flashnote 側（ankplsツリー）
                    var docsFolderDir = Path.Combine(_localBasePath, accumulated);
                    Directory.CreateDirectory(docsFolderDir);

                    // LocalApplicationData/Flashnote 側（同期用一時ツリー）。metadata.jsonが未作成なら作成する
                    var appDataFolderDir = Path.Combine(_tempBasePath, accumulated);
                    Directory.CreateDirectory(appDataFolderDir);
                    var appDataMetaPath = Path.Combine(appDataFolderDir, "metadata.json");
                    if (!File.Exists(appDataMetaPath))
                    {
                        File.WriteAllText(appDataMetaPath, metadataContent);
                    }

                    // Documents側にもmetadata.jsonが無ければ作成（フォルダ自体の色分け等が参照するため）
                    var docsMetaPath = Path.Combine(docsFolderDir, "metadata.json");
                    if (!File.Exists(docsMetaPath))
                    {
                        File.WriteAllText(docsMetaPath, metadataContent);
                    }

                    // このフォルダが認識している子ノートのうち、まだ.ankplsが実体化していないものは
                    // 空のプレースホルダーを置いておく（実内容はこの後の通常同期で上書きされる）
                    foreach (var childNoteId in ReadStringArray(root, "childNotes"))
                    {
                        var childAnkplsPath = Path.Combine(docsFolderDir, childNoteId + ".ankpls");
                        if (!File.Exists(childAnkplsPath))
                        {
                            CreateAnkplsPlaceholder(childAnkplsPath);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"EnsureFolderStructureAsync: {folderId} の処理中にエラー: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 空の .ankpls プレースホルダーファイルを作成する。iOS版 createAnkplsFile に対応
        /// （実体の圧縮生成は WriteNoteAnkplsAsync が担当し、こちらは存在確認のためのプレースホルダー作成のみ）。
        /// EnsureFolderStructureAsync でフォルダ階層を辿る際、まだ実体化していない子ノートの
        /// .ankpls を「後で確実に上書きされる空ファイル」として先に置いておくために使う。
        /// </summary>
        private static void CreateAnkplsPlaceholder(string ankplsPath)
        {
            try
            {
                var dir = Path.GetDirectoryName(ankplsPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                if (!File.Exists(ankplsPath))
                {
                    File.WriteAllBytes(ankplsPath, Array.Empty<byte>());
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CreateAnkplsPlaceholder error: {ex.Message}");
            }
        }

        /// <summary>
        /// 共有フォルダの共有元ユーザーのmetadata.jsonからchildNotes/childFoldersを取得する。
        /// iOS版 sourceFolderMetadata に対応するが、共有機能(SharingManager)がWindows側では
        /// 未移植のため、現時点では常にnullを返すスタブとして用意しておく。
        /// 将来SharingManagerを移植する際、共有元ユーザーIDの解決ロジックをここに実装する。
        /// </summary>
        private Task<(List<string> childNotes, List<string> childFolders)?> GetSourceFolderChildrenAsync(string folderId)
        {
            return Task.FromResult<(List<string>, List<string>)?>(null);
        }

        /// <summary>
        /// ローカルに保存済みの全フォルダについて、metadata.json の childNotes/childFolders が
        /// 実態（実際のサブディレクトリ・孤立した.ankpls・親フォルダID逆引き）と一致しているかを検証し、
        /// 不足があればローカル・サーバー双方のmetadata.jsonを補完してアップロードし直す。
        /// iOS版 verifyFolderIntegrity / serverNeedsChildRepair に対応。
        /// 共有フォルダの共有元メタデータ参照(sourceFolderMetadata)は現状スタブのため利用しない。
        /// </summary>
        public async Task VerifyFolderIntegrityAsync(string uid)
        {
            if (!Directory.Exists(_localBasePath)) return;

            try
            {
                // 親フォルダID逆引きインデックスを構築（全metadata.jsonのparentFolderId/subfolderを走査）
                var childrenByParentFolderId = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                var allMetadataFiles = Directory.GetFiles(_localBasePath, "metadata.json", SearchOption.AllDirectories);
                foreach (var metaFile in allMetadataFiles)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(metaFile));
                        var root = doc.RootElement;
                        if (!root.TryGetProperty("parentFolderId", out var pParent) || pParent.ValueKind != JsonValueKind.String) continue;
                        var parentFolderId = pParent.GetString();
                        if (string.IsNullOrEmpty(parentFolderId)) continue;

                        var childId = root.TryGetProperty("id", out var pId) ? pId.GetString() : Path.GetFileName(Path.GetDirectoryName(metaFile));
                        if (string.IsNullOrEmpty(childId)) continue;

                        if (!childrenByParentFolderId.TryGetValue(parentFolderId, out var set))
                        {
                            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            childrenByParentFolderId[parentFolderId] = set;
                        }
                        set.Add(childId);
                    }
                    catch { /* 個別の読み込み失敗はスキップ */ }
                }

                foreach (var metaFile in allMetadataFiles)
                {
                    try
                    {
                        var folderDir = Path.GetDirectoryName(metaFile);
                        if (string.IsNullOrEmpty(folderDir)) continue;

                        var localJson = File.ReadAllText(metaFile);
                        using var doc = JsonDocument.Parse(localJson);
                        var root = doc.RootElement;
                        var isFolder = root.TryGetProperty("isFolder", out var pIsFolder) && pIsFolder.GetBoolean();
                        if (!isFolder) continue;

                        var folderId = root.TryGetProperty("id", out var pId) ? pId.GetString() : Path.GetFileName(folderDir);
                        if (string.IsNullOrEmpty(folderId)) continue;

                        // 記録済みのchildNotes/childFolders
                        var recordedNotes = ReadStringArray(root, "childNotes");
                        var recordedFolders = ReadStringArray(root, "childFolders");

                        // 実態: サブディレクトリ(metadata.json持ち=子フォルダ)
                        var actualFolders = new HashSet<string>(recordedFolders, StringComparer.OrdinalIgnoreCase);
                        foreach (var subDir in Directory.GetDirectories(folderDir))
                        {
                            if (File.Exists(Path.Combine(subDir, "metadata.json")))
                            {
                                actualFolders.Add(Path.GetFileName(subDir));
                            }
                        }

                        // 実態: 直下の.ankplsファイル(孤立ノートを含む)
                        var actualNotes = new HashSet<string>(recordedNotes, StringComparer.OrdinalIgnoreCase);
                        foreach (var ankplsFile in Directory.GetFiles(folderDir, "*.ankpls"))
                        {
                            actualNotes.Add(Path.GetFileNameWithoutExtension(ankplsFile));
                        }

                        // 実態: 親フォルダID逆引きインデックス
                        if (childrenByParentFolderId.TryGetValue(folderId, out var reverseChildren))
                        {
                            foreach (var childId in reverseChildren)
                            {
                                // フォルダかノートかは既存の記録・実ディレクトリ有無から判定
                                if (Directory.Exists(Path.Combine(folderDir, childId)))
                                {
                                    actualFolders.Add(childId);
                                }
                                else
                                {
                                    actualNotes.Add(childId);
                                }
                            }
                        }

                        // 共有フォルダの場合は共有元メタデータも情報源に含める（現状スタブ、常にnull）
                        var sourceChildren = await GetSourceFolderChildrenAsync(folderId);
                        if (sourceChildren.HasValue)
                        {
                            foreach (var n in sourceChildren.Value.childNotes) actualNotes.Add(n);
                            foreach (var f in sourceChildren.Value.childFolders) actualFolders.Add(f);
                        }

                        var notesMissing = actualNotes.Except(recordedNotes, StringComparer.OrdinalIgnoreCase).Any();
                        var foldersMissing = actualFolders.Except(recordedFolders, StringComparer.OrdinalIgnoreCase).Any();

                        if (!notesMissing && !foldersMissing) continue;

                        Debug.WriteLine($"VerifyFolderIntegrityAsync: フォルダ {folderId} の子要素に不足を検出。ローカル/サーバーのmetadata.jsonを補完します");

                        // ローカルmetadata.jsonを補完
                        var updatedLocalJson = MergeChildrenIntoMetadataJson(localJson, actualNotes, actualFolders);
                        File.WriteAllText(metaFile, updatedLocalJson);

                        // サーバー側も同様に補完（サーバー限定の子要素を消さないよう、サーバー版とマージしてからアップロード）
                        try
                        {
                            var serverJson = await _blobStorageService.GetNoteContentAsync(uid, folderId + "/metadata.json", null);
                            if (!string.IsNullOrEmpty(serverJson))
                            {
                                using var serverDoc = JsonDocument.Parse(serverJson);
                                var serverRoot = serverDoc.RootElement;
                                var serverNotes = ReadStringArray(serverRoot, "childNotes");
                                var serverFolders = ReadStringArray(serverRoot, "childFolders");
                                foreach (var n in serverNotes) actualNotes.Add(n);
                                foreach (var f in serverFolders) actualFolders.Add(f);

                                var mergedServerJson = MergeChildrenIntoMetadataJson(serverJson, actualNotes, actualFolders);
                                await _blobStorageService.SaveNoteAsync(uid, $"{folderId}/metadata.json", mergedServerJson, null);
                                Debug.WriteLine($"VerifyFolderIntegrityAsync: サーバーのmetadata.jsonを補完しアップロードしました (folderId={folderId})");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"VerifyFolderIntegrityAsync: サーバー側の補完に失敗しました (folderId={folderId}): {ex.Message}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"VerifyFolderIntegrityAsync: {metaFile} の検証中にエラー: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"VerifyFolderIntegrityAsync error: {ex.Message}");
                // フォルダ整合性検証の失敗は同期全体を止める理由にしない
            }
        }

        private static List<string> ReadStringArray(JsonElement root, string propertyName)
        {
            var list = new List<string>();
            if (root.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in prop.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var v = item.GetString();
                        if (!string.IsNullOrEmpty(v)) list.Add(v);
                    }
                }
            }
            return list;
        }

        private static string MergeChildrenIntoMetadataJson(string originalJson, IEnumerable<string> childNotes, IEnumerable<string> childFolders)
        {
            var obj = JsonSerializer.Deserialize<Dictionary<string, object?>>(originalJson) ?? new Dictionary<string, object?>();
            obj["childNotes"] = childNotes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            obj["childFolders"] = childFolders.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            obj["updatedAt"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            return JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
        }
    }
}
