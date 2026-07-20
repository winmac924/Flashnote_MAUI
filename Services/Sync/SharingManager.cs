using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs.Models;
using Flashnote.Services;

namespace Flashnote.Services.Sync
{
    /// <summary>
    /// 共有ノート/共有フォルダ/共有キーに関するBlob操作を担当する。iOS版 SharingManager.swift に対応。
    /// これまで Services/BlobStorageService.cs に混在していた共有関連メソッドをここへ分離した。
    /// 接続ライフサイクルは BlobStorageClient に委譲し、画像バイナリ取得など一部の非共有系プリミティブのみ
    /// BlobStorageService（呼び出し元）へコールバックする。
    /// </summary>
    public class SharingManager
    {
        private readonly BlobStorageClient _blobStorageClient;
        private readonly BlobStorageService _blobStorageService;

        public SharingManager(BlobStorageClient blobStorageClient, BlobStorageService blobStorageService)
        {
            _blobStorageClient = blobStorageClient;
            _blobStorageService = blobStorageService;
        }

        /// <summary>
        /// 共有ノートに画像ファイルをアップロード
        /// </summary>
        public async Task UploadSharedImageAsync(string userId, string imageName, string base64Content, string folderPath)
        {
            await _blobStorageClient.EnsureInitializedAsync();
            try
            {
                Debug.WriteLine($"共有ノート画像のアップロード開始 - ユーザーID: {userId}, 画像名: {imageName}, フォルダパス: {folderPath}");
                
                var containerClient = _blobStorageClient.Client.GetBlobContainerClient(BlobStorageClient.ContainerName);
                var fullPath = $"{userId}/{folderPath}/img/{imageName}";

                var blobClient = containerClient.GetBlobClient(fullPath);

                // Base64文字列をバイト配列に変換してアップロード
                var imageBytes = Convert.FromBase64String(base64Content);
                using var stream = new MemoryStream(imageBytes);
                await blobClient.UploadAsync(stream, overwrite: true);
                Debug.WriteLine($"共有ノート画像のアップロード完了 - サイズ: {imageBytes.Length} バイト, パス: {fullPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"共有ノート画像のアップロード中にエラー: {ex.Message}");
                throw;
            }
        }


        /// <summary>
        /// 共有キーからノート情報にアクセスする
        /// </summary>
        public async Task<(string userId, string notePath, bool isFolder)> AccessNoteWithShareKeyAsync(string shareKey)
        {
            await _blobStorageClient.EnsureInitializedAsync();
            try
            {
                Debug.WriteLine($"=== AccessNoteWithShareKeyAsync 開始 ===");
                Debug.WriteLine($"入力された共有キー: {shareKey}");
                
                var containerClient = _blobStorageClient.Client.GetBlobContainerClient(BlobStorageClient.ContainerName);
                var mappingPath = $"share_keys/{shareKey}.json";
                
                Debug.WriteLine($"検索対象のパス: {mappingPath}");
                
                var blobClient = containerClient.GetBlobClient(mappingPath);
                
                var exists = await blobClient.ExistsAsync();
                Debug.WriteLine($"ファイルの存在確認結果: {exists.Value}");

                if (!exists.Value)
                {
                    Debug.WriteLine($"共有キーファイルが見つかりません: {mappingPath}");
                    
                    // デバッグ: share_keys フォルダ内のファイルを列挙
                    Debug.WriteLine("=== share_keys フォルダ内のファイル一覧 ===");
                    await foreach (var blob in containerClient.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix: "share_keys/", cancellationToken: default))
                    {
                        Debug.WriteLine($"  - {blob.Name}");
                    }
                    
                    throw new Exception("共有キーが見つかりません。");
                }

                var response = await blobClient.DownloadAsync();
                using var streamReader = new StreamReader(response.Value.Content);
                var json = await streamReader.ReadToEndAsync();
                
                Debug.WriteLine($"共有キーJSON取得成功: {json}");
                
                // JsonDocumentを使って正しくデシリアライズ（boolean型を扱える）
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
                
                // プロパティ名の両方をチェック（userId または originalUserId）
                string userId = null;
                if (root.TryGetProperty("userId", out var userIdProp))
                {
                    userId = userIdProp.GetString();
                    Debug.WriteLine($"userId プロパティから取得: {userId}");
                }
                else if (root.TryGetProperty("originalUserId", out var originalUserIdProp))
                {
                    userId = originalUserIdProp.GetString();
                    Debug.WriteLine($"originalUserId プロパティから取得: {userId}");
                }
                
                if (!root.TryGetProperty("notePath", out var notePathProp))
                {
                    Debug.WriteLine("notePath プロパティが存在しません");
                    throw new Exception("無効な共有キーです。");
                }
                
                if (string.IsNullOrEmpty(userId))
                {
                    Debug.WriteLine("userId/originalUserId プロパティが不足しています");
                    Debug.WriteLine($"JSON全体: {json}");
                    throw new Exception("無効な共有キーです。");
                }
                
                var notePath = notePathProp.GetString();
                
                Debug.WriteLine($"userId取得: {userId}");
                Debug.WriteLine($"notePath取得: {notePath}");
                
                // isFolder は boolean として取得（文字列の場合もフォールバック）
                bool isFolder = false;
                if (root.TryGetProperty("isFolder", out var isFolderProp))
                {
                    Debug.WriteLine($"isFolder プロパティの型: {isFolderProp.ValueKind}");
                    
                    if (isFolderProp.ValueKind == System.Text.Json.JsonValueKind.True)
                    {
                        isFolder = true;
                    }
                    else if (isFolderProp.ValueKind == System.Text.Json.JsonValueKind.False)
                    {
                        isFolder = false;
                    }
                    else if (isFolderProp.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        // フォールバック: 文字列の場合
                        bool.TryParse(isFolderProp.GetString(), out isFolder);
                    }
                }
                else
                {
                    Debug.WriteLine("isFolder プロパティが存在しません");
                }
                
                Debug.WriteLine($"isFolder取得: {isFolder}");
                Debug.WriteLine($"共有ノートにアクセス成功 - ユーザーID: {userId}, パス: {notePath}, フォルダ: {isFolder}");
                Debug.WriteLine($"=== AccessNoteWithShareKeyAsync 完了 ===");
                
                return (userId, notePath, isFolder);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"=== AccessNoteWithShareKeyAsync エラー ===");
                Debug.WriteLine($"エラーの種類: {ex.GetType().Name}");
                Debug.WriteLine($"エラーメッセージ: {ex.Message}");
                Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                throw;
            }
        }


        /// <summary>
        /// 共有ノート用のファイル取得（パスを直接指定）
        /// </summary>
        public async Task<string> GetSharedNoteFileAsync(string userId, string folderPath, string fileName)
        {
            await _blobStorageClient.EnsureInitializedAsync();
            try
            {
                Debug.WriteLine($"共有ファイル取得開始 - UID: {userId}, フォルダ: {folderPath}, ファイル名: {fileName}");
                
                var containerClient = _blobStorageClient.Client.GetBlobContainerClient(BlobStorageClient.ContainerName);
                var fullPath = $"{userId}/{folderPath}/{fileName}";
                
                Debug.WriteLine($"完全パス: {fullPath}");
                var blobClient = containerClient.GetBlobClient(fullPath);

                if (await blobClient.ExistsAsync())
                {
                    var response = await blobClient.DownloadAsync();
                    using var streamReader = new StreamReader(response.Value.Content);
                    var content = await streamReader.ReadToEndAsync();
                    Debug.WriteLine($"共有ファイル取得成功 - サイズ: {content.Length} バイト");
                    return content;
                }

                Debug.WriteLine($"共有ファイルが見つかりません: {fullPath}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"共有ファイル取得中にエラー: {ex.Message}");
                return null;
            }
        }


        /// <summary>
        /// 共有ノート用のファイル一覧取得
        /// </summary>
        public async Task<List<string>> GetSharedNoteListAsync(string userId, string folderPath)
        {
            await _blobStorageClient.EnsureInitializedAsync();
            try
            {
                Debug.WriteLine($"共有ファイル一覧取得開始 - UID: {userId}, フォルダ: {folderPath}");
                
                var containerClient = _blobStorageClient.Client.GetBlobContainerClient(BlobStorageClient.ContainerName);
                var prefix = $"{userId}/{folderPath}/";
                var files = new List<string>();

                Debug.WriteLine($"検索プレフィックス: {prefix}");
                
                await foreach (var blob in containerClient.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix: prefix, cancellationToken: default))
                {
                    // プレフィックス以降のファイル名のみを抽出
                    var relativePath = blob.Name.Substring(prefix.Length);
                    if (!string.IsNullOrEmpty(relativePath) && !relativePath.Contains("/"))
                    {
                        files.Add(relativePath);
                        Debug.WriteLine($"見つかった共有ファイル: {relativePath}");
                    }
                }

                Debug.WriteLine($"共有ファイル一覧取得完了 - 件数: {files.Count}");
                return files;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"共有ファイル一覧取得中にエラー: {ex.Message}");
                return new List<string>();
            }
        }


        /// <summary>
        /// 共有ノートをダウンロードする
        /// </summary>
        public async Task DownloadSharedNoteAsync(string userId, string notePath, string noteName, string subFolder = null)
        {
            await _blobStorageClient.EnsureInitializedAsync();
            try
            {
                Debug.WriteLine($"共有ノートのダウンロード開始 - ユーザーID: {userId}, パス: {notePath}");
                
                // 一時フォルダのパス構築
                var tempBasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Flashnote");
                string tempDir;
                if (!string.IsNullOrEmpty(subFolder))
                {
                    // サブフォルダ内のノートの場合
                    tempDir = Path.Combine(tempBasePath, subFolder, $"{noteName}_temp");
                }
                else
                {
                    // ルートのノートの場合
                    tempDir = Path.Combine(tempBasePath, $"{noteName}_temp");
                }

                if (!Directory.Exists(tempDir))
                {
                    Directory.CreateDirectory(tempDir);
                    Debug.WriteLine($"一時ディレクトリを作成: {tempDir}");
                }

                // cards.txtをダウンロード
                Debug.WriteLine($"cards.txtのダウンロードを開始 - パス: {notePath}");
                var cardsContent = await GetSharedNoteFileAsync(userId, notePath, "cards.txt");
                Debug.WriteLine($"cards.txtの取得結果: {(cardsContent != null ? "成功" : "失敗")}");
                
                if (cardsContent != null)
                {
                    var cardsPath = Path.Combine(tempDir, "cards.txt");

                    // 既存のcards.txtがある場合は上書きしない
                    if (File.Exists(cardsPath))
                    {
                        Debug.WriteLine($"既存のcards.txtが存在するため、ダウンロードをスキップ: {cardsPath}");
                    }
                    else
                    {
                        await File.WriteAllTextAsync(cardsPath, cardsContent);
                        Debug.WriteLine($"cards.txtをダウンロード: {cardsPath}");
                        Debug.WriteLine($"cards.txtの内容 ({cardsContent.Length} 文字):");
                        Debug.WriteLine(cardsContent);
                    }

                    // cards.txtの内容を解析してカードファイルをダウンロード
                    var lines = cardsContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    Debug.WriteLine($"cards.txtの行数: {lines.Length}");
                    var cardsDirPath = Path.Combine(tempDir, "cards");

                    if (!Directory.Exists(cardsDirPath))
                    {
                        Directory.CreateDirectory(cardsDirPath);
                        Debug.WriteLine($"cardsディレクトリを作成: {cardsDirPath}");
                    }

                    // 1行目が数字のみの場合はカード数なのでスキップ
                    int startIndex = 0;
                    if (lines.Length > 0 && int.TryParse(lines[0], out _))
                    {
                        startIndex = 1;
                        Debug.WriteLine($"1行目はカード数のためスキップ: {lines[0]}");
                    }

                    Debug.WriteLine($"カードファイルのダウンロード開始 - 処理対象行数: {lines.Length - startIndex}");
                    for (int i = startIndex; i < lines.Length; i++)
                    {
                        var line = lines[i].TrimEnd('\r', '\n');
                        Debug.WriteLine($"処理中の行 {i}: {line}");
                        var parts = line.Split(',');
                        if (parts.Length >= 2)
                        {
                            var uuid = parts[0];
                            Debug.WriteLine($"カードUUID: {uuid}");
                            var cardContent = await GetSharedNoteFileAsync(userId, $"{notePath}/cards", $"{uuid}.json");
                            
                            // If line indicates deletion, remove local card file if exists and continue
                            bool isDeleted = parts.Any(p => p.Trim().Equals("deleted", StringComparison.OrdinalIgnoreCase));
                            var localCardPath = Path.Combine(cardsDirPath, $"{uuid}.json");
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

                            if (cardContent != null)
                            {
                                // 既存のカードファイルがある場合は上書きしない
                                if (File.Exists(localCardPath))
                                {
                                    Debug.WriteLine($"既存のカードファイルが存在するためダウンロードをスキップ: {uuid}");
                                }
                                else
                                {
                                    await File.WriteAllTextAsync(localCardPath, cardContent);
                                    Debug.WriteLine($"カードファイルをダウンロード: {localCardPath}");
                                }
                            }
                            else
                            {
                                Debug.WriteLine($"カードファイルの取得に失敗: {uuid}");
                            }
                        }
                        else
                        {
                            Debug.WriteLine($"無効な行形式をスキップ: {line}");
                        }
                    }
                }
                else
                {
                    Debug.WriteLine($"cards.txtが取得できませんでした - パス: {notePath}");
                }

                // 画像ファイルをダウンロード
                Debug.WriteLine($"画像ファイルのダウンロード開始 - パス: {notePath}/img");
                var imgDirPath = Path.Combine(tempDir, "img");
                if (!Directory.Exists(imgDirPath))
                {
                    Directory.CreateDirectory(imgDirPath);
                    Debug.WriteLine($"imgディレクトリを作成: {imgDirPath}");
                }

                var imgFiles = await GetSharedNoteListAsync(userId, $"{notePath}/img");
                Debug.WriteLine($"画像ファイル一覧取得結果: {imgFiles.Count} 個");
                foreach (var imgFile in imgFiles)
                {
                    Debug.WriteLine($"画像ファイル: {imgFile}");
                }

                foreach (var imgFile in imgFiles)
                {
                    if (System.Text.RegularExpressions.Regex.IsMatch(imgFile, @"^img_\d{8}_\d{6}\.jpg$"))
                    {
                        Debug.WriteLine($"画像ファイル形式チェック OK: {imgFile}");
                        try
                        {
                            // バイナリデータとして直接取得
                            var imgBytes = await _blobStorageService.GetImageBinaryAsync(userId, imgFile, $"{notePath}/img");
                            Debug.WriteLine($"画像ファイル取得結果 {imgFile}: {(imgBytes != null ? "成功" : "失敗")}");
                            if (imgBytes != null)
                            {
                                var imgPath = Path.Combine(imgDirPath, imgFile);
                                await File.WriteAllBytesAsync(imgPath, imgBytes);
                                Debug.WriteLine($"画像ファイルをダウンロード: {imgPath} ({imgBytes.Length} バイト)");
                            }
                            else
                            {
                                Debug.WriteLine($"画像ファイルの取得に失敗: {imgFile}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"画像ファイルのダウンロード中にエラー: {imgFile}, エラー: {ex.Message}");
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"画像ファイル形式チェック NG: {imgFile}");
                    }
                }

                // .ankplsファイルを作成
                Debug.WriteLine($".ankplsファイル作成開始");
                var localBasePath = SyncPathResolver.GetLocalNoteRoot();
                string ankplsPath;
                if (!string.IsNullOrEmpty(subFolder))
                {
                    // サブフォルダ内のノートの場合
                    var subFolderPath = Path.Combine(localBasePath, subFolder);
                    if (!Directory.Exists(subFolderPath))
                    {
                        Directory.CreateDirectory(subFolderPath);
                        Debug.WriteLine($".ankplsディレクトリを作成: {subFolderPath}");
                    }
                    ankplsPath = Path.Combine(subFolderPath, $"{noteName}.ankpls");
                }
                else
                {
                    // ルートのノートの場合
                    ankplsPath = Path.Combine(localBasePath, $"{noteName}.ankpls");
                }
                
                Debug.WriteLine($"ローカルベースパス: {localBasePath}");
                Debug.WriteLine($".ankplsファイルパス: {ankplsPath}");
                Debug.WriteLine($".ankplsディレクトリ: {Path.GetDirectoryName(ankplsPath)}");
                
                if (!Directory.Exists(Path.GetDirectoryName(ankplsPath)))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(ankplsPath));
                    Debug.WriteLine($".ankplsディレクトリを作成: {Path.GetDirectoryName(ankplsPath)}");
                }

                if (File.Exists(ankplsPath))
                {
                    File.Delete(ankplsPath);
                    Debug.WriteLine($"既存の.ankplsファイルを削除: {ankplsPath}");
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

                // 作成した.ankplsファイルのサイズを確認
                if (File.Exists(ankplsPath))
                {
                    var ankplsSize = new FileInfo(ankplsPath).Length;
                    Debug.WriteLine($"作成した.ankplsファイルのサイズ: {ankplsSize} バイト");
                }

                Debug.WriteLine($"共有ノートのダウンロード完了: {noteName}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"共有ノートのダウンロード中にエラー: {ex.Message}");
                throw;
            }
        }


        /// <summary>
        /// 共有フォルダをダウンロードする（実際には単一ノートの可能性もある）
        /// </summary>
        public async Task<(bool isActuallyFolder, List<(string noteName, string subFolder, string fullNotePath)> downloadedNotes)> DownloadSharedFolderAsync(string userId, string folderPath, string shareKey)
        {
            await _blobStorageClient.EnsureInitializedAsync();
            var downloadedNotes = new List<(string noteName, string subFolder, string fullNotePath)>();
            
            try
            {
                Debug.WriteLine($"共有アイテムのダウンロード開始 - ユーザーID: {userId}, パス: {folderPath}");
                
                var containerClient = _blobStorageClient.Client.GetBlobContainerClient(BlobStorageClient.ContainerName);
                
                // まず直下にcards.txtがあるかチェック（単一ノートの場合）
                var directCardsPath = $"{userId}/{folderPath}/cards.txt";
                Debug.WriteLine($"直下のcards.txtをチェック: {directCardsPath}");
                
                var directCardsBlobClient = containerClient.GetBlobClient(directCardsPath);
                var directCardsExists = await directCardsBlobClient.ExistsAsync();
                
                if (directCardsExists.Value)
                {
                    Debug.WriteLine($"直下にcards.txtが存在するため、単一ノートとして処理: {folderPath}");
                    
                    // 単一ノートとしてダウンロード
                    var (subFolder, noteName, _) = _blobStorageService.ParseBlobPath($"{userId}/{folderPath}/cards.txt");
                    
                    Debug.WriteLine($"パース結果 - サブフォルダ: {subFolder}, ノート名: {noteName}");
                    
                    await DownloadSharedNoteAsync(userId, folderPath, noteName, subFolder);
                    downloadedNotes.Add((noteName, subFolder, folderPath));
                    
                    Debug.WriteLine($"単一ノートのダウンロード完了: {folderPath}");
                    return (false, downloadedNotes); // 実際にはフォルダではない
                }
                else
                {
                    Debug.WriteLine($"直下にcards.txtが存在しないため、フォルダとして処理");
                    
                    // フォルダとして中身を検索
                    var folderPrefix = $"{userId}/{folderPath}/";
                    Debug.WriteLine($"フォルダ内容を検索: {folderPrefix}");

                    // フォルダ内のcards.txtファイルのみを効率的に検索
                    await foreach (var blob in containerClient.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix: folderPrefix, cancellationToken: default))
                    {
                        // cards.txtで終わらないファイルは早期にスキップ（効率化）
                        if (!blob.Name.EndsWith("/cards.txt"))
                            continue;

                        Debug.WriteLine($"検出したcards.txt: {blob.Name}");
                        // パスからノート名とサブフォルダを抽出
                        var relativePath = blob.Name.Substring(folderPrefix.Length);
                        var pathParts = relativePath.Split('/');
                        
                        if (pathParts.Length >= 2)
                        {
                            var noteName = pathParts[pathParts.Length - 2]; // cards.txtの親フォルダ
                            var subFolderParts = pathParts.Take(pathParts.Length - 2);
                            var innerSubFolder = subFolderParts.Any() ? string.Join("/", subFolderParts) : null;
                            
                            // フォルダ共有の場合は、{共有フォルダ名}\{ノート名}_tempとして保存
                            var targetSubFolder = folderPath;
                            var fullNotePath = $"{folderPath}/{(innerSubFolder != null ? innerSubFolder + "/" : "")}{noteName}";
                            
                            Debug.WriteLine($"フォルダ内のノートを処理: {noteName}");
                            Debug.WriteLine($"内部サブフォルダ: {innerSubFolder ?? "なし"}");
                            Debug.WriteLine($"保存先サブフォルダ: {targetSubFolder}");
                            Debug.WriteLine($"完全ノートパス: {fullNotePath}");

                            // ノートをダウンロード（保存先は{共有フォルダ名}\{ノート名}_tempとなる）
                            await DownloadSharedNoteAsync(userId, fullNotePath, noteName, targetSubFolder);
                            
                            // ダウンロードしたノート情報を記録
                            downloadedNotes.Add((noteName, targetSubFolder, fullNotePath));
                        }
                    }

                    Debug.WriteLine($"フォルダのダウンロード完了: {folderPath}, ダウンロードしたノート数: {downloadedNotes.Count}");
                    return (true, downloadedNotes); // 実際にフォルダ
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"共有アイテムのダウンロード中にエラー: {ex.Message}");
                throw;
            }
        }


        /// <summary>
        /// 共有ノート用のファイル保存（パスを直接指定）
        /// </summary>
        public async Task SaveSharedNoteFileAsync(string userId, string folderPath, string fileName, string content)
        {
            await _blobStorageClient.EnsureInitializedAsync();
            try
            {
                Debug.WriteLine($"共有ノートファイルの保存開始 - ユーザーID: {userId}, フォルダパス: {folderPath}, ファイル名: {fileName}");
                
                var containerClient = _blobStorageClient.Client.GetBlobContainerClient(BlobStorageClient.ContainerName);
                
                // パス区切り文字を正規化
                var normalizedFolderPath = folderPath.Replace("\\", "/");
                
                // 既にuserIdが含まれている場合は重複を避ける
                string fullPath;
                if (normalizedFolderPath.StartsWith($"{userId}/"))
                {
                    fullPath = $"{normalizedFolderPath}/{fileName}";
                    Debug.WriteLine($"既にUserIDが含まれているため、重複を回避: {fullPath}");
                }
                else
                {
                    fullPath = $"{userId}/{normalizedFolderPath}/{fileName}";
                }
                
                var blobClient = containerClient.GetBlobClient(fullPath);

                using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
                await blobClient.UploadAsync(stream, overwrite: true);
                Debug.WriteLine($"共有ノートファイルの保存完了 - パス: {fullPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"共有ノートファイルの保存中にエラー: {ex.Message}");
                throw;
            }
        }


        /// <summary>
        /// 共有キーファイルをアップロードする
        /// </summary>
        public async Task UploadSharedKeysAsync(string uid, string sharedKeysJson)
        {
            await _blobStorageClient.EnsureInitializedAsync();
            try
            {
                Debug.WriteLine($"共有キーのアップロード開始 - UID: {uid}");
                
                var containerClient = _blobStorageClient.Client.GetBlobContainerClient(BlobStorageClient.ContainerName);
                var fullPath = $"{uid}/share_keys.json";
                var blobClient = containerClient.GetBlobClient(fullPath);

                using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(sharedKeysJson));
                await blobClient.UploadAsync(stream, overwrite: true);
                Debug.WriteLine($"共有キーのアップロード完了 - パス: {fullPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"共有キーのアップロード中にエラー: {ex.Message}");
                throw;
            }
        }


        /// <summary>
        /// 共有キーファイルをダウンロードする
        /// </summary>
        public async Task<string> DownloadSharedKeysAsync(string uid)
        {
            await _blobStorageClient.EnsureInitializedAsync();
            try
            {
                Debug.WriteLine($"共有キーのダウンロード開始 - UID: {uid}");
                
                var containerClient = _blobStorageClient.Client.GetBlobContainerClient(BlobStorageClient.ContainerName);
                var fullPath = $"{uid}/share_keys.json";
                var blobClient = containerClient.GetBlobClient(fullPath);

                if (await blobClient.ExistsAsync())
                {
                    var response = await blobClient.DownloadAsync();
                    using var streamReader = new StreamReader(response.Value.Content);
                    var content = await streamReader.ReadToEndAsync();
                    Debug.WriteLine($"共有キーのダウンロード完了 - サイズ: {content.Length} バイト");
                    return content;
                }

                Debug.WriteLine($"共有キーファイルが見つかりません: {fullPath}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"共有キーのダウンロード中にエラー: {ex.Message}");
                return null;
            }
        }


        /// <summary>
        /// 共有ノートにカードを追加（競合回避機能付き）
        /// </summary>
        public async Task AppendCardToSharedNoteAsync(string originalUserId, string notePath, string cardId, string cardContent)
        {
            CancellationTokenSource cancellationTokenSource = null;
            try
            {
                await _blobStorageClient.EnsureInitializedAsync();
                Debug.WriteLine($"共有ノートカード追加開始 - 元UID: {originalUserId}, ノートパス: {notePath}, カードID: {cardId}");
                
                var containerClient = _blobStorageClient.Client.GetBlobContainerClient(BlobStorageClient.ContainerName);
                // Build full path for cards.txt and card JSON depending on layout
                string fullPath;
                if (Guid.TryParse(notePath, out _))
                {
                    // flat layout: {originalUserId}/{notePath}/cards.txt
                    fullPath = $"{originalUserId}/{notePath}/cards.txt";
                }
                else
                {
                    // hierarchical: notePath may contain subfolders (e.g. "sub/folder/note")
                    fullPath = $"{originalUserId}/{notePath}/cards.txt";
                }

                var blobClient = containerClient.GetBlobClient(fullPath);
                cancellationTokenSource = NetworkOperationCancellationManager.CreateCancellationTokenSource(TimeSpan.FromSeconds(30));

                // 既存ファイルに追記
                var existingContent = await GetSharedNoteFileAsync(originalUserId, notePath, "cards.txt");
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
                Debug.WriteLine($"共有ノートcards.txtを更新: {fullPath}, カード数: {cardCount}");

                // カードのJSONファイルを個別に保存
                string cardJsonPath;
                if (Guid.TryParse(notePath, out _))
                {
                    cardJsonPath = $"{originalUserId}/{notePath}/cards/{cardId}.json";
                }
                else
                {
                    cardJsonPath = $"{originalUserId}/{notePath}/cards/{cardId}.json";
                }
                var cardBlobClient = containerClient.GetBlobClient(cardJsonPath);
                using var cardStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(cardContent));
                await cardBlobClient.UploadAsync(cardStream, overwrite: true, cancellationToken: cancellationTokenSource.Token);
                
                Debug.WriteLine($"共有ノートカード追加完了 - カードID: {cardId}, パス: {cardJsonPath}");
            }
            catch (InvalidOperationException)
            {
                // オンライン・オフライン自動切替時の競合を避けるため、再スロー
                throw;
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("共有ノートカード追加がタイムアウトしました。ネットワーク接続を確認してください。");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"共有ノートカード追加中にエラー: {ex.Message}");
                throw new InvalidOperationException($"共有ノートカード追加に失敗しました: {ex.Message}", ex);
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
        /// 共有キーマッピング(JSON)をルートの share_keys/{shareKey}.json に保存する
        /// </summary>
        public async Task UploadShareKeyMappingAsync(string shareKey, string mappingJson)
        {
            await _blobStorageClient.EnsureInitializedAsync();
            try
            {
                Debug.WriteLine($"共有キーマッピングのアップロード開始 - shareKey: {shareKey}");

                var containerClient = _blobStorageClient.Client.GetBlobContainerClient(BlobStorageClient.ContainerName);
                var normalizedKey = shareKey.Replace("\\", "/");
                var fullPath = $"share_keys/{normalizedKey}.json";
                var blobClient = containerClient.GetBlobClient(fullPath);

                using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(mappingJson));
                await blobClient.UploadAsync(stream, overwrite: true);
                Debug.WriteLine($"共有キーマッピングのアップロード完了 - パス: {fullPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"共有キーマッピングのアップロード中にエラー: {ex.Message}");
                throw;
            }
        }
    }
}
