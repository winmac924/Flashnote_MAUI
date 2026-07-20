using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using CommunityToolkit.Maui.Alerts;
using Flashnote.Services.Sync;

namespace Flashnote.Services
{
    /// <summary>
    /// ノート/カード/共有フォルダ等のドメインレベル操作を提供する薄いファサード。
    /// 接続の初期化・疎通確認・ネットワーク復帰時の自動同期は BlobStorageClient に委譲する。
    /// </summary>
    public class BlobStorageService
    {
        private readonly BlobStorageClient _blobClient = new BlobStorageClient();
        private readonly SharingManager _sharingManager;

        // 既存の呼び出し箇所（ドメインメソッド群）を変更せずに済むよう、
        // 旧プライベートメンバー名のまま BlobStorageClient に委譲するラッパーを用意する。
        private BlobServiceClient _blobServiceClient => _blobClient.Client;
        private const string CONTAINER_NAME = BlobStorageClient.ContainerName;

        /// <summary>
        /// 自動同期完了イベント
        /// </summary>
        public event EventHandler AutoSyncCompleted;
        private static readonly string FolderPath = SyncPathResolver.GetLocalNoteRoot();

        public BlobStorageService()
        {
            _blobClient.AutoSyncCompleted += (sender, e) => AutoSyncCompleted?.Invoke(this, e);
            _sharingManager = new SharingManager(_blobClient, this);
        }

        /// <summary>
        /// ネットワーク接続状態をチェック
        /// </summary>
        private bool IsNetworkAvailable() => _blobClient.IsNetworkAvailable();

        /// <summary>
        /// Azure接続状態をリセット（ネットワーク状態変化時など）
        /// </summary>
        public void ResetConnectionState() => _blobClient.ResetConnectionState();

        /// <summary>
        /// 現在の初期化状態を取得
        /// </summary>
        public bool IsInitialized => _blobClient.IsInitialized;

        /// <summary>
        /// Azure Blob Storageへの接続をテスト
        /// </summary>
        private Task<bool> TestBlobConnectionAsync() => _blobClient.TestBlobConnectionAsync();

        private Task EnsureInitializedAsync() => _blobClient.EnsureInitializedAsync();

        private string GetUserPath(string uid, string subFolder = null) => _blobClient.GetUserPath(uid, subFolder);

        public (string subFolder, string noteName, bool isCard) ParseBlobPath(string blobPath)
        {
            var parts = blobPath.Split('/');
            if (parts.Length < 3) return (null, null, false);

            // UIDを除外し、残りのパスを解析
            var remainingParts = parts.Skip(1).ToArray();
            
            // cards.txtの場合、親フォルダをノート名として扱う
            if (remainingParts[remainingParts.Length - 1] == "cards.txt")
            {
                var noteName = remainingParts[remainingParts.Length - 2];
                var subFolder = remainingParts.Length > 2 ? string.Join("/", remainingParts.Take(remainingParts.Length - 2)) : null;
                return (subFolder, noteName, true);
            }

            // cards/ディレクトリ内のJSONファイルの場合
            if (remainingParts.Length > 2 && remainingParts[remainingParts.Length - 2] == "cards")
            {
                var noteName = remainingParts[remainingParts.Length - 1];
                var subFolder = remainingParts.Length > 3 ? string.Join("/", remainingParts.Take(remainingParts.Length - 3)) : null;
                return (subFolder, noteName, true);
            }

            // その他のファイルの場合
            var fileName = remainingParts[remainingParts.Length - 1];
            var folderPath = remainingParts.Length > 1 ? string.Join("/", remainingParts.Take(remainingParts.Length - 1)) : null;
            return (folderPath, fileName, false);
        }

        public async Task<List<string>> GetNoteListAsync(string uid, string subFolder = null)
        {
            await EnsureInitializedAsync();
            try
            {
                Debug.WriteLine($"ノート一覧の取得開始 - UID: {uid}, サブフォルダ: {subFolder ?? "なし"}");
                
                var containerClient = _blobServiceClient.GetBlobContainerClient(CONTAINER_NAME);
                var userPath = GetUserPath(uid, subFolder);
                Debug.WriteLine($"検索パス: {userPath}");
                
                var notes = new List<string>();
                var processedNames = new HashSet<string>();

                // cards.txtファイルのみを対象とした効率的な検索
                // Azure Blob Storageのプレフィックス検索を使用し、cards.txtパターンに一致するもののみを処理
                // 注：Azure Blob Storage .NET SDKでは suffix 検索は未対応のため、早期フィルタリングで最適化
                await foreach (var blob in containerClient.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix: userPath, cancellationToken: default))
                {
                    // cards.txtで終わらないファイルは早期にスキップ（効率化）
                    if (!blob.Name.EndsWith("/cards.txt"))
                        continue;

                    Debug.WriteLine($"見つかったcards.txt: {blob.Name}");
                    var (parsedSubFolder, noteName, isCard) = ParseBlobPath(blob.Name);
                    Debug.WriteLine($"パース結果 - サブフォルダ: {parsedSubFolder}, ノート名: {noteName}");
                    
                    if (noteName != null && !processedNames.Contains(noteName))
                    {
                        // サブフォルダが指定されている場合、そのサブフォルダ内のノートのみを対象とする
                        if (subFolder != null)
                        {
                            if (parsedSubFolder == subFolder)
                            {
                                notes.Add(noteName);
                                processedNames.Add(noteName);
                                Debug.WriteLine($"追加されたノート: {noteName}");
                            }
                        }
                        else
                        {
                            // サブフォルダが指定されていない場合、ルートのノートのみを対象とする
                            if (parsedSubFolder == null)
                            {
                                notes.Add(noteName);
                                processedNames.Add(noteName);
                                Debug.WriteLine($"追加されたルートノート: {noteName}");
                            }
                        }
                    }
                }

                Debug.WriteLine($"取得したノート数: {notes.Count}");
                Debug.WriteLine($"取得したノート一覧: {string.Join(", ", notes)}");
                return notes;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ノート一覧の取得中にエラー: {ex.Message}");
                throw;
            }
        }

        public async Task<string> GetNoteContentAsync(string uid, string noteName, string subFolder = null)
        {
            CancellationTokenSource cancellationTokenSource = null;
            try
            {
                await EnsureInitializedAsync();
                Debug.WriteLine($"ノートの取得開始 - UID: {uid}, ノート名: {noteName}, サブフォルダ: {subFolder ?? "なし"}");

                var containerClient = _blobServiceClient.GetBlobContainerClient(CONTAINER_NAME);
                string fullPath = BlobPathResolver.ResolveNoteFilePath(uid, noteName, subFolder);

                Debug.WriteLine($"検索対象のパス: {fullPath}");
                var blobClient = containerClient.GetBlobClient(fullPath);
                cancellationTokenSource = NetworkOperationCancellationManager.CreateCancellationTokenSource(TimeSpan.FromSeconds(15));

                if (await blobClient.ExistsAsync(cancellationTokenSource.Token))
                {
                    var response = await blobClient.DownloadAsync(cancellationTokenSource.Token);
                    using var streamReader = new StreamReader(response.Value.Content);
                    var content = await streamReader.ReadToEndAsync();
                    Debug.WriteLine($"ノートの取得完了 - サイズ: {content.Length} バイト, パス: {fullPath}");
                    return content;
                }

                Debug.WriteLine($"ノートが見つかりません: {fullPath}");
                return null;
            }
            catch (InvalidOperationException)
            {
                // オンライン・オフライン自動切替時の競合を避けるため、再スロー
                throw;
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("ノート取得がタイムアウトしました。ネットワーク接続を確認してください。");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ノートの取得中にエラー: {ex.Message}");
                throw new InvalidOperationException($"ノートの取得に失敗しました: {ex.Message}", ex);
            }
            finally
            {
                // 安全にCancellationTokenSourceをクリーンアップ
                if (cancellationTokenSource != null)
                {
                    try
                    {
                        NetworkOperationCancellationManager.RemoveCancellationTokenSource(cancellationTokenSource);
                        cancellationTokenSource.Dispose();
                    }
                    catch (Exception disposeEx)
                    {
                        Debug.WriteLine($"CancellationTokenSourceクリーンアップエラー: {disposeEx.Message}");
                    }
                }
            }
        }

        public async Task SaveNoteAsync(string uid, string noteName, string content, string subFolder = null)
        {
            CancellationTokenSource cancellationTokenSource = null;
            try
            {
                await EnsureInitializedAsync();
                Debug.WriteLine($"ノートの保存開始 - UID: {uid}, ノート名: {noteName}, サブフォルダ: {subFolder ?? "なし"}");
                
                var containerClient = _blobServiceClient.GetBlobContainerClient(CONTAINER_NAME);
                string fullPath = noteName.EndsWith(".json")
                    ? BlobPathResolver.ResolveDirectFilePath(uid, noteName, subFolder)
                    : BlobPathResolver.ResolveNoteFilePath(uid, noteName, subFolder);

                var blobClient = containerClient.GetBlobClient(fullPath);
                cancellationTokenSource = NetworkOperationCancellationManager.CreateCancellationTokenSource(TimeSpan.FromSeconds(30));

                using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
                await blobClient.UploadAsync(stream, overwrite: true, cancellationToken: cancellationTokenSource.Token);
                
                Debug.WriteLine($"ノートの保存完了 - サイズ: {content.Length} バイト, パス: {fullPath}");
                
                // 保存成功時に未同期記録から削除
                try
                {
                    if (MauiProgram.Services != null)
                    {
                        var unsyncService = MauiProgram.Services.GetService<UnsynchronizedNotesService>();
                        
                        // ノート名を抽出（JSONファイルでない場合）
                        if (!noteName.EndsWith(".json"))
                        {
                            unsyncService?.RemoveUnsynchronizedNote(noteName, subFolder);
                            Debug.WriteLine($"未同期記録から削除: {noteName}");
                        }
                    }
                }
                catch (Exception unsyncEx)
                {
                    Debug.WriteLine($"未同期記録削除エラー: {unsyncEx.Message}");
                }
            }
            catch (InvalidOperationException)
            {
                // オンライン・オフライン自動切替時の競合を避けるため、再スロー
                throw;
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("ノート保存がタイムアウトしました。ネットワーク接続を確認してください。");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ノートの保存中にエラー: {ex.Message}");
                throw new InvalidOperationException($"ノートの保存に失敗しました: {ex.Message}", ex);
            }
            finally
            {
                // 安全にCancellationTokenSourceをクリーンアップ
                if (cancellationTokenSource != null)
                {
                    try
                    {
                        NetworkOperationCancellationManager.RemoveCancellationTokenSource(cancellationTokenSource);
                        cancellationTokenSource.Dispose();
                    }
                    catch (Exception disposeEx)
                    {
                        Debug.WriteLine($"CancellationTokenSourceクリーンアップエラー: {disposeEx.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// ノート内のimgフォルダに画像ファイルをアップロード
        /// </summary>
        public async Task UploadImageToNoteAsync(string uid, string noteName, string imageName, string base64Content, string subFolder = null)
        {
            await EnsureInitializedAsync();
            try
            {
                Debug.WriteLine($"ノート内画像のアップロード開始 - UID: {uid}, ノート名: {noteName}, 画像名: {imageName}, サブフォルダ: {subFolder ?? "なし"}");
                
                var containerClient = _blobServiceClient.GetBlobContainerClient(CONTAINER_NAME);
                var userPath = GetUserPath(uid, subFolder);
                var fullPath = $"{userPath}/{noteName}/img/{imageName}";

                var blobClient = containerClient.GetBlobClient(fullPath);

                // Base64文字列をバイト配列に変換してアップロード
                var imageBytes = Convert.FromBase64String(base64Content);
                using var stream = new MemoryStream(imageBytes);
                await blobClient.UploadAsync(stream, overwrite: true);
                Debug.WriteLine($"ノート内画像のアップロード完了 - サイズ: {imageBytes.Length} バイト, パス: {fullPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ノート内画像のアップロード中にエラー: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 共有ノートに画像ファイルをアップロード
        /// </summary>
        public Task UploadSharedImageAsync(string userId, string imageName, string base64Content, string folderPath)
            => _sharingManager.UploadSharedImageAsync(userId, imageName, base64Content, folderPath);

        public async Task CreateLocalNoteAsync(string uid, string noteName, string subFolder = null)
        {
            try
            {
                Debug.WriteLine($"ローカルノートの作成開始 - UID: {uid}, ノート名: {noteName}, サブフォルダ: {subFolder ?? "なし"}");
                
                // サーバーからノートの内容を取得
                var content = await GetNoteContentAsync(uid, noteName, subFolder);
                if (content == null)
                {
                    Debug.WriteLine($"サーバーにノートが存在しないため、作成をスキップします: {noteName}");
                    return;
                }

                // Only write files to temp extract path (LocalApplicationData) so UI reads from there.
                var tempBasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Flashnote");
                var tempDir = string.IsNullOrEmpty(subFolder) ? Path.Combine(tempBasePath, noteName + "_temp") : Path.Combine(tempBasePath, subFolder, noteName + "_temp");
                bool anyDownloaded = false; // track whether we downloaded anything new
                try
                {
                    // Do not delete existing tempDir; perform incremental updates
                    if (!Directory.Exists(tempDir))
                    {
                        Directory.CreateDirectory(tempDir);
                        Debug.WriteLine($"tempDir を作成: {tempDir}");
                    }
                    else
                    {
                        Debug.WriteLine($"tempDir は既に存在します: {tempDir}");
                    }

                    var tempNotePath = Path.Combine(tempDir, "cards.txt");

                    // Only overwrite cards.txt if missing or different
                    try
                    {
                        if (!File.Exists(tempNotePath))
                        {
                            File.WriteAllText(tempNotePath, content);
                            anyDownloaded = true;
                            Debug.WriteLine($"temp の cards.txt を作成しました: {tempNotePath}");
                        }
                        else
                        {
                            var existing = File.ReadAllText(tempNotePath);
                            if (!string.Equals(existing, content, StringComparison.Ordinal))
                            {
                                File.WriteAllText(tempNotePath, content);
                                anyDownloaded = true;
                                Debug.WriteLine($"temp の cards.txt を上書きしました（差分あり）: {tempNotePath}");
                            }
                            else
                            {
                                Debug.WriteLine($"既存の cards.txt は最新のため上書きしません: {tempNotePath}");
                            }
                        }
                    }
                    catch (Exception exTemp)
                    {
                        Debug.WriteLine($"temp への cards.txt 書き込みに失敗: {exTemp.Message}");
                    }
                }
                catch (Exception exDir)
                {
                    Debug.WriteLine($"tempDir 処理エラー: {exDir.Message}");
                }

                // cards.txt の内容に基づいて関連ファイルをダウンロード
                try
                {
                    var lines = content.Split('\n')
                                       .Select(line => line.Trim())
                                       .Where(line => !string.IsNullOrEmpty(line))
                                       .ToList();

                    Debug.WriteLine($"cards.txt の行数: {lines.Count}");

                    // 1 行目が数字のみの場合はカード数なのでスキップ
                    int startIndex = 0;
                    if (lines.Count > 0 && int.TryParse(lines[0], out _))
                    {
                        startIndex = 1;
                        Debug.WriteLine($"1行目はカード数のためスキップ: {lines[0]}");
                    }

                    Debug.WriteLine($"カードファイルのダウンロード開始 - 処理対象行数: {lines.Count - startIndex}");

                    var containerClient = _blobServiceClient.GetBlobContainerClient(CONTAINER_NAME);
                    var tempCardsPath = Path.Combine(tempDir, "cards");
                    if (!Directory.Exists(tempCardsPath)) Directory.CreateDirectory(tempCardsPath);

                    // Build set of existing card ids to avoid re-downloading
                    var existingCardIds = new HashSet<string>(Directory.GetFiles(tempCardsPath, "*.json").Select(f => Path.GetFileNameWithoutExtension(f)), StringComparer.OrdinalIgnoreCase);

                    for (int i = startIndex; i < lines.Count; i++)
                    {
                        var line = lines[i];
                        Debug.WriteLine($"処理中の行 {i}: {line}");
                        var parts = line.Split(',');
                        if (parts.Length >= 1)
                        {
                            var uuid = parts[0];
                            Debug.WriteLine($"カードUUID: {uuid}");

                            // If line indicates deletion, remove local card file if exists and continue
                            bool isDeleted = parts.Any(p => p.Trim().Equals("deleted", StringComparison.OrdinalIgnoreCase));
                            var localCardPath = Path.Combine(tempCardsPath, $"{uuid}.json");
                            if (isDeleted)
                            {
                                try
                                {
                                    if (File.Exists(localCardPath))
                                    {
                                        File.Delete(localCardPath);
                                        Debug.WriteLine($"削除フラグ検出: 共有ノートのローカルカードを削除しました: {localCardPath}");
                                    }
                                    else
                                    {
                                        Debug.WriteLine($"削除フラグ検出: ローカルにカードファイルは存在しません: {localCardPath}");
                                    }
                                }
                                catch (Exception exDel)
                                {
                                    Debug.WriteLine($"共有ノートローカルカード削除エラー ({uuid}): {exDel.Message}");
                                }
                                continue;
                            }

                            // If we already have a non-empty file for this card, skip download
                            if (File.Exists(localCardPath) && new FileInfo(localCardPath).Length > 5)
                            {
                                Debug.WriteLine($"既存のカードファイルがあるためダウンロードをスキップ: {localCardPath}");
                                continue;
                            }

                            // Construct possible blob path for card JSON
                            string blobName = BlobPathResolver.ResolveDirectFilePath(uid, $"{noteName}/cards/{uuid}.json", subFolder);

                            try
                            {
                                var blobClient = containerClient.GetBlobClient(blobName);
                                if (await blobClient.ExistsAsync())
                                {
                                    var resp = await blobClient.DownloadAsync();
                                    using var sr = new StreamReader(resp.Value.Content);
                                    var jsonContent = await sr.ReadToEndAsync();
                                    if (!string.IsNullOrEmpty(jsonContent))
                                    {
                                        File.WriteAllText(localCardPath, jsonContent);
                                        anyDownloaded = true;
                                        Debug.WriteLine($"temp JSONファイルを作成しました: {localCardPath}");
                                    }
                                    else
                                    {
                                        Debug.WriteLine($"ダウンロードしたJSONが空です: {blobName}");
                                    }
                                }
                                else
                                {
                                    Debug.WriteLine($"カードJSONがBlob上に存在しません: {blobName}");
                                }
                            }
                            catch (Exception exInner)
                            {
                                Debug.WriteLine($"カードJSON取得エラー ({uuid}): {exInner.Message}");
                            }
                        }
                        else
                        {
                            Debug.WriteLine($"無効な行形式をスキップ: {line}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"カードファイルのダウンロード中にエラー: {ex.Message}");
                }

                // Download img folder (non-shared) - same level as cards
                try
                {
                    var imgDir = Path.Combine(tempDir, "img");
                    if (!Directory.Exists(imgDir)) Directory.CreateDirectory(imgDir);

                    string imageFolderBlobPath = BlobPathResolver.ResolveNoteSubPath(noteName, subFolder, "img");

                    Debug.WriteLine($"画像フォルダをダウンロード: blobFolder={imageFolderBlobPath}");
                    var imageFiles = await GetImageFilesAsync(uid, imageFolderBlobPath);
                    Debug.WriteLine($"取得した画像ファイル数: {imageFiles.Count}");

                    foreach (var imgFile in imageFiles)
                    {
                        try
                        {
                            if (!System.Text.RegularExpressions.Regex.IsMatch(imgFile, @"^img_\d{8}_\d{6}\.jpg$"))
                            {
                                Debug.WriteLine($"画像ファイル形式ではないためスキップ: {imgFile}");
                                continue;
                            }

                            var localImgPath = Path.Combine(imgDir, imgFile);
                            if (File.Exists(localImgPath))
                            {
                                Debug.WriteLine($"既存の画像ファイルがあるためダウンロードをスキップ: {localImgPath}");
                                continue;
                            }

                            var imgBytes = await GetImageBinaryAsync(uid, imgFile, imageFolderBlobPath);
                            if (imgBytes != null && imgBytes.Length > 0)
                            {
                                await File.WriteAllBytesAsync(localImgPath, imgBytes);
                                anyDownloaded = true;
                                Debug.WriteLine($"画像ファイルをダウンロード: {localImgPath} ({imgBytes.Length} バイト)");
                            }
                            else
                            {
                                Debug.WriteLine($"画像ファイルの取得に失敗または空: {imgFile}");
                            }
                        }
                        catch (Exception exImg)
                        {
                            Debug.WriteLine($"画像ファイルダウンロードエラー ({imgFile}): {exImg.Message}");
                        }
                    }
                }
                catch (Exception exImgAll)
                {
                    Debug.WriteLine($"imgフォルダダウンロード中にエラー: {exImgAll.Message}");
                }

                // Recreate .ankpls only if we downloaded new content or file doesn't exist
                try
                {
                    // prepare target .ankpls path
                    var localBasePath = SyncPathResolver.GetLocalNoteRoot();
                    string ankplsPath;
                    if (!string.IsNullOrEmpty(subFolder))
                    {
                        var subFolderPath = Path.Combine(localBasePath, subFolder);
                        if (!Directory.Exists(subFolderPath)) Directory.CreateDirectory(subFolderPath);
                        ankplsPath = Path.Combine(subFolderPath, $"{noteName}.ankpls");
                    }
                    else
                    {
                        if (!Directory.Exists(localBasePath)) Directory.CreateDirectory(localBasePath);
                        ankplsPath = Path.Combine(localBasePath, $"{noteName}.ankpls");
                    }

                    bool shouldCreateAnkpls = anyDownloaded || !File.Exists(ankplsPath);
                    Debug.WriteLine($".ankpls作成判定: anyDownloaded={anyDownloaded}, exists={File.Exists(ankplsPath)} -> create={shouldCreateAnkpls}");

                    if (shouldCreateAnkpls)
                    {
                        if (File.Exists(ankplsPath))
                        {
                            try { File.Delete(ankplsPath); Debug.WriteLine($"既存の.ankplsファイルを削除: {ankplsPath}"); } catch (Exception exDel) { Debug.WriteLine($".ankpls削除失敗: {exDel.Message}"); }
                        }

                        // 一時ディレクトリの内容を確認
                        Debug.WriteLine($"一時ディレクトリの内容確認: {tempDir}");
                        if (Directory.Exists(tempDir))
                        {
                            var tempFiles = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories);
                            Debug.WriteLine($"一時ディレクトリのファイル数: {tempFiles.Length}");
                            foreach (var file in tempFiles)
                            {
                                var relativePath = Path.GetRelativePath(tempDir, file);
                                var fileSize = new FileInfo(file).Length;
                                Debug.WriteLine($"  ファイル: {relativePath} ({fileSize} バイト)");
                            }
                        }

                        System.IO.Compression.ZipFile.CreateFromDirectory(tempDir, ankplsPath);
                        Debug.WriteLine($".ankplsファイルを作成: {ankplsPath}");
                    }
                    else
                    {
                        Debug.WriteLine($".ankplsファイルは最新のため作成をスキップ: {ankplsPath}");
                    }
                }
                catch (Exception exZip)
                {
                    Debug.WriteLine($".ankpls作成中にエラー: {exZip.Message}");
                }
            }
            catch (Exception ex)
            {
                 Debug.WriteLine($"ローカルノートの作成中にエラー: {ex.Message}");
                 throw;
             }
         }

        /// <summary>
        /// 共有キーでノートにアクセスする
        /// </summary>
        public Task<(string userId, string notePath, bool isFolder)> AccessNoteWithShareKeyAsync(string shareKey)
            => _sharingManager.AccessNoteWithShareKeyAsync(shareKey);
        /// <summary>
        /// 共有ノート用のファイル取得（パスを直接指定）
        /// </summary>
        public Task<string> GetSharedNoteFileAsync(string userId, string folderPath, string fileName)
            => _sharingManager.GetSharedNoteFileAsync(userId, folderPath, fileName);

        /// <summary>
        /// 共有ノート用のファイル一覧取得
        /// </summary>
        public Task<List<string>> GetSharedNoteListAsync(string userId, string folderPath)
            => _sharingManager.GetSharedNoteListAsync(userId, folderPath);

        /// <summary>
        /// 共有ノートをダウンロードする
        /// </summary>
        public Task DownloadSharedNoteAsync(string userId, string notePath, string noteName, string subFolder = null)
            => _sharingManager.DownloadSharedNoteAsync(userId, notePath, noteName, subFolder);
        /// <summary>
        /// 共有フォルダをダウンロードする（実際には単一ノートの可能性もある）
        /// </summary>
        public Task<(bool isActuallyFolder, List<(string noteName, string subFolder, string fullNotePath)> downloadedNotes)> DownloadSharedFolderAsync(string userId, string folderPath, string shareKey)
            => _sharingManager.DownloadSharedFolderAsync(userId, folderPath, shareKey);
        /// <summary>
        /// 共有ノート用のファイル保存（パスを直接指定）
        /// </summary>
        public Task SaveSharedNoteFileAsync(string userId, string folderPath, string fileName, string content)
            => _sharingManager.SaveSharedNoteFileAsync(userId, folderPath, fileName, content);
        /// <summary>
        /// 共有キーファイルをアップロードする
        /// </summary>
        public Task UploadSharedKeysAsync(string uid, string sharedKeysJson)
            => _sharingManager.UploadSharedKeysAsync(uid, sharedKeysJson);
        /// <summary>
        /// 共有キーファイルをダウンロードする
        /// </summary>
        public Task<string> DownloadSharedKeysAsync(string uid)
            => _sharingManager.DownloadSharedKeysAsync(uid);
        /// <summary>
        /// 学習記録（result.txt）をアップロードする
        /// </summary>
        public async Task UploadLearningResultAsync(string uid, string noteName, string resultContent, string subFolder = null)
        {
            await EnsureInitializedAsync();
            try
            {
                Debug.WriteLine($"学習記録のアップロード開始 - UID: {uid}, ノート名: {noteName}, サブフォルダ: {subFolder ?? "なし"}");
                
                var containerClient = _blobServiceClient.GetBlobContainerClient(CONTAINER_NAME);
                var userPath = GetUserPath(uid, subFolder);
                var fullPath = $"{userPath}/{noteName}/result.txt";
                var blobClient = containerClient.GetBlobClient(fullPath);

                using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(resultContent));
                await blobClient.UploadAsync(stream, overwrite: true);
                Debug.WriteLine($"学習記録のアップロード完了 - パス: {fullPath}, サイズ: {resultContent.Length} バイト");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"学習記録のアップロード中にエラー: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 学習記録（result.txt）をダウンロードする
        /// </summary>
        public async Task<string> DownloadLearningResultAsync(string uid, string noteName, string subFolder = null)
        {
            await EnsureInitializedAsync();
            try
            {
                Debug.WriteLine($"学習記録のダウンロード開始 - UID: {uid}, ノート名: {noteName}, サブフォルダ: {subFolder ?? "なし"}");
                
                var containerClient = _blobServiceClient.GetBlobContainerClient(CONTAINER_NAME);
                var userPath = GetUserPath(uid, subFolder);
                var fullPath = $"{userPath}/{noteName}/result.txt";
                var blobClient = containerClient.GetBlobClient(fullPath);

                if (await blobClient.ExistsAsync())
                {
                    var response = await blobClient.DownloadAsync();
                    using var streamReader = new StreamReader(response.Value.Content);
                    var content = await streamReader.ReadToEndAsync();
                    Debug.WriteLine($"学習記録のダウンロード完了 - サイズ: {content.Length} バイト");
                    return content;
                }

                Debug.WriteLine($"学習記録ファイルが見つかりません: {fullPath}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"学習記録のダウンロード中にエラー: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 学習記録の最終更新時刻を取得する
        /// </summary>
        public async Task<DateTime?> GetLearningResultLastModifiedAsync(string uid, string noteName, string subFolder = null)
        {
            await EnsureInitializedAsync();
            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(CONTAINER_NAME);
                var userPath = GetUserPath(uid, subFolder);
                var fullPath = $"{userPath}/{noteName}/result.txt";
                var blobClient = containerClient.GetBlobClient(fullPath);

                if (await blobClient.ExistsAsync())
                {
                    var properties = await blobClient.GetPropertiesAsync();
                    return properties.Value.LastModified.DateTime;
                }

                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"学習記録の最終更新時刻取得中にエラー: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 指定されたパスの画像ファイル一覧を取得する
        /// </summary>
        public async Task<List<string>> GetImageFilesAsync(string uid, string folderPath)
        {
            await EnsureInitializedAsync();
            try
            {
                Debug.WriteLine($"画像ファイル一覧の取得開始 - UID: {uid}, フォルダパス: {folderPath}");
                
                var containerClient = _blobServiceClient.GetBlobContainerClient(CONTAINER_NAME);
                var prefix = $"{uid}/{folderPath}/";
                var imageFiles = new List<string>();

                Debug.WriteLine($"検索プレフィックス: {prefix}");
                
                await foreach (var blob in containerClient.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix: prefix, cancellationToken: default))
                {
                    Debug.WriteLine($"見つかったファイル: {blob.Name}");
                    
                    // プレフィックス以降のファイル名のみを抽出
                    var relativePath = blob.Name.Substring(prefix.Length);
                    if (!string.IsNullOrEmpty(relativePath) && !relativePath.Contains("/"))
                    {
                        // iOS版の形式（img_########_######.jpg）をチェック
                        if (System.Text.RegularExpressions.Regex.IsMatch(relativePath, @"^img_\d{8}_\d{6}\.jpg$"))
                        {
                            imageFiles.Add(relativePath);
                            Debug.WriteLine($"追加された画像ファイル: {relativePath}");
                        }
                        else
                        {
                            Debug.WriteLine($"画像ファイル形式ではないためスキップ: {relativePath}");
                        }
                    }
                }

                Debug.WriteLine($"画像ファイル一覧取得完了 - 件数: {imageFiles.Count}");
                return imageFiles;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"画像ファイル一覧取得中にエラー: {ex.Message}");
                return new List<string>();
            }
        }

        /// <summary>
        /// 画像ファイルの内容をバイナリデータとして取得する
        /// </summary>
        public async Task<byte[]> GetImageBinaryAsync(string uid, string imageName, string folderPath)
        {
            await EnsureInitializedAsync();
            try
            {
                Debug.WriteLine($"画像ファイルの取得開始 - UID: {uid}, 画像名: {imageName}, フォルダパス: {folderPath}");
                
                var containerClient = _blobServiceClient.GetBlobContainerClient(CONTAINER_NAME);
                var fullPath = $"{uid}/{folderPath}/{imageName}";
                
                Debug.WriteLine($"画像ファイルの完全パス: {fullPath}");
                var blobClient = containerClient.GetBlobClient(fullPath);

                if (await blobClient.ExistsAsync())
                {
                    var response = await blobClient.DownloadAsync();
                    using var memoryStream = new MemoryStream();
                    await response.Value.Content.CopyToAsync(memoryStream);
                    var imageBytes = memoryStream.ToArray();
                    Debug.WriteLine($"画像ファイルの取得完了 - サイズ: {imageBytes.Length} バイト");
                    return imageBytes;
                }

                Debug.WriteLine($"画像ファイルが見つかりません: {fullPath}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"画像ファイルの取得中にエラー: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// カードをノートに追加（競合回避機能付き）
        /// </summary>
        public async Task AppendCardToNoteAsync(string uid, string noteName, string cardId, string cardContent, string subFolder = null)
        {
            CancellationTokenSource cancellationTokenSource = null;
            try
            {
                await EnsureInitializedAsync();
                Debug.WriteLine($"カード追加開始 - UID: {uid}, ノート名: {noteName}, カードID: {cardId}, サブフォルダ: {subFolder ?? "なし"}");
                
                var containerClient = _blobServiceClient.GetBlobContainerClient(CONTAINER_NAME);
                string fullPath = BlobPathResolver.ResolveNoteFilePath(uid, noteName, subFolder);

                var blobClient = containerClient.GetBlobClient(fullPath);
                cancellationTokenSource = NetworkOperationCancellationManager.CreateCancellationTokenSource(TimeSpan.FromSeconds(30));

                // 既存のコンテンツを取得してカード数を更新
                var existingContent = await GetNoteContentAsync(uid, noteName, subFolder);
                var lines = existingContent?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? new string[0];
                
                int cardCount = 0;
                if (lines.Length > 0 && int.TryParse(lines[0], out cardCount))
                {
                    cardCount++;
                }
                else
                {
                    cardCount = 1;
                }
                
                // 新しいカード行を追加
                var newCardLine = $"\n{cardId},{DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                
                // カード数を更新した新しいコンテンツを作成
                var updatedContent = $"{cardCount}\n{string.Join("\n", lines.Skip(1))}{newCardLine}";
                
                using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(updatedContent));
                await blobClient.UploadAsync(stream, overwrite: true, cancellationToken: cancellationTokenSource.Token);
                Debug.WriteLine($"cards.txtを更新: {fullPath}, カード数: {cardCount}");
                

                // カードのJSONファイルを個別に保存
                string cardJsonPath = BlobPathResolver.ResolveDirectFilePath(uid, $"{noteName}/cards/{cardId}.json", subFolder);
                var cardBlobClient = containerClient.GetBlobClient(cardJsonPath);
                using var cardStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(cardContent));
                await cardBlobClient.UploadAsync(cardStream, overwrite: true, cancellationToken: cancellationTokenSource.Token);
                
                Debug.WriteLine($"カード追加完了 - カードID: {cardId}, パス: {cardJsonPath}");
                
                // 保存成功時に未同期記録から削除
                try
                {
                    var unsyncService = MauiProgram.Services.GetService<UnsynchronizedNotesService>();
                    unsyncService?.RemoveUnsynchronizedNote(noteName, subFolder);
                    Debug.WriteLine($"未同期記録から削除: {noteName}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"未同期記録削除エラー: {ex.Message}");
                }
            }
            catch (InvalidOperationException)
            {
                // オンライン・オフライン自動切替時の競合を避けるため、再スロー
                throw;
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("カード追加がタイムアウトしました。ネットワーク接続を確認してください。");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"カード追加中にエラー: {ex.Message}");
                throw new InvalidOperationException($"カード追加に失敗しました: {ex.Message}", ex);
            }
            finally
            {
                // 安全にCancellationTokenSourceをクリーンアップ
                if (cancellationTokenSource != null)
                {
                    try
                    {
                        NetworkOperationCancellationManager.RemoveCancellationTokenSource(cancellationTokenSource);
                        cancellationTokenSource.Dispose();
                    }
                    catch (Exception disposeEx)
                    {
                        Debug.WriteLine($"CancellationTokenSourceクリーンアップエラー: {disposeEx.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// 共有ノートにカードを追加（競合回避機能付き）
        /// </summary>
        public Task AppendCardToSharedNoteAsync(string originalUserId, string notePath, string cardId, string cardContent)
            => _sharingManager.AppendCardToSharedNoteAsync(originalUserId, notePath, cardId, cardContent);
        /// <summary>
        /// 指定されたユーザー領域にある任意のファイルを取得する（例: metadata.json）
        /// </summary>
        public async Task<string> GetUserFileAsync(string uid, string fileName, string subFolder = null)
        {
            CancellationTokenSource cancellationTokenSource = null;
            try
            {
                await EnsureInitializedAsync();
                Debug.WriteLine($"ユーザーファイル取得開始 - UID: {uid}, ファイル名: {fileName}, サブフォルダ: {subFolder ?? "なし"}");

                var containerClient = _blobServiceClient.GetBlobContainerClient(CONTAINER_NAME);
                string fullPath;
                if (!string.IsNullOrEmpty(subFolder))
                {
                    fullPath = $"{uid}/{subFolder}/{fileName}";
                }
                else
                {
                    fullPath = $"{uid}/{fileName}";
                }

                Debug.WriteLine($"検索対象のパス: {fullPath}");
                var blobClient = containerClient.GetBlobClient(fullPath);
                cancellationTokenSource = NetworkOperationCancellationManager.CreateCancellationTokenSource(TimeSpan.FromSeconds(15));

                if (await blobClient.ExistsAsync(cancellationTokenSource.Token))
                {
                    var response = await blobClient.DownloadAsync(cancellationTokenSource.Token);
                    using var streamReader = new StreamReader(response.Value.Content);
                    var content = await streamReader.ReadToEndAsync();
                    Debug.WriteLine($"ユーザーファイル取得完了 - サイズ: {content.Length} バイト, パス: {fullPath}");
                    return content;
                }

                Debug.WriteLine($"ユーザーファイルが見つかりません: {fullPath}");
                return null;
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("ユーザーファイル取得がタイムアウトしました。ネットワーク接続を確認してください。");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ユーザーファイル取得中にエラー: {ex.Message}");
                return null;
            }
            finally
            {
                if (cancellationTokenSource != null)
                {
                    try
                    {
                        NetworkOperationCancellationManager.RemoveCancellationTokenSource(cancellationTokenSource);
                        cancellationTokenSource.Dispose();
                    }
                    catch (Exception disposeEx)
                    {
                        Debug.WriteLine($"CancellationTokenSourceクリーンアップエラー: {disposeEx.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// ユーザー配下のすべての metadata.json を取得する
        /// </summary>
        public async Task<List<(string blobName, string content)>> GetAllMetadataJsonAsync(string uid)
        {
            await EnsureInitializedAsync();
            var results = new List<(string blobName, string content)>();
            CancellationTokenSource cts = null;
            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(CONTAINER_NAME);
                var prefix = $"{uid}/";
                await foreach (var blobItem in containerClient.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix: prefix, cancellationToken: default))
                {
                    if (!blobItem.Name.EndsWith("/metadata.json", StringComparison.OrdinalIgnoreCase))
                        continue;

                    try
                    {
                        var blobClient = containerClient.GetBlobClient(blobItem.Name);
                        cts = NetworkOperationCancellationManager.CreateCancellationTokenSource(TimeSpan.FromSeconds(20));
                        var resp = await blobClient.DownloadAsync(cts.Token);
                        using var sr = new StreamReader(resp.Value.Content);
                        var json = await sr.ReadToEndAsync();
                        results.Add((blobItem.Name, json));
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"metadata.json ダウンロード失敗: {blobItem.Name} - {ex.Message}");
                    }
                    finally
                    {
                        if (cts != null)
                        {
                            try { NetworkOperationCancellationManager.RemoveCancellationTokenSource(cts); cts.Dispose(); } catch { }
                            cts = null;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetAllMetadataJsonAsync エラー: {ex.Message}");
            }
            finally
            {
                if (cts != null)
                {
                    try { NetworkOperationCancellationManager.RemoveCancellationTokenSource(cts); cts.Dispose(); } catch { }
                }
            }
            return results;
        }
        /// <summary>
        /// 共有キーマッピング(JSON)をルートの share_keys/{shareKey}.json に保存する
        /// </summary>
        public Task UploadShareKeyMappingAsync(string shareKey, string mappingJson)
            => _sharingManager.UploadShareKeyMappingAsync(shareKey, mappingJson);
    }
}