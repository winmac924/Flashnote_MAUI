using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace Flashnote.Services
{
    public class BlobStorageService
    {
        private readonly BlobServiceClient _blobServiceClient;
        private const string CONTAINER_NAME = "flashnote";
        private bool _isInitialized = false;
        private static readonly string FolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Flashnote");

        public BlobStorageService()
        {
            _blobServiceClient = App.BlobServiceClient;
        }

        private async Task EnsureInitializedAsync()
        {
            if (!_isInitialized)
            {
                await InitializeContainerAsync();
                _isInitialized = true;
            }
        }

        private async Task InitializeContainerAsync()
        {
            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(CONTAINER_NAME);
                
                if (!await containerClient.ExistsAsync())
                {
                    Debug.WriteLine($"コンテナ '{CONTAINER_NAME}' が存在しないため、作成します。");
                    await containerClient.CreateAsync();
                    Debug.WriteLine($"コンテナ '{CONTAINER_NAME}' を作成しました。");
                }
                else
                {
                    Debug.WriteLine($"コンテナ '{CONTAINER_NAME}' は既に存在します。");
                }

                Debug.WriteLine("利用可能なコンテナ一覧:");
                await foreach (var container in _blobServiceClient.GetBlobContainersAsync())
                {
                    Debug.WriteLine($"- {container.Name}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"コンテナの初期化中にエラー: {ex.Message}");
                throw;
            }
        }

        private string GetUserPath(string uid, string subFolder = null)
        {
            return subFolder != null ? $"{uid}/{subFolder}" : uid;
        }

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
                await foreach (var blob in containerClient.GetBlobsAsync(prefix: userPath))
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
            await EnsureInitializedAsync();
            try
            {
                Debug.WriteLine($"ノートの取得開始 - UID: {uid}, ノート名: {noteName}, サブフォルダ: {subFolder ?? "なし"}");
                
                var containerClient = _blobServiceClient.GetBlobContainerClient(CONTAINER_NAME);
                var userPath = GetUserPath(uid, subFolder);
                string fullPath;

                // cards.txtの場合
                if (noteName.EndsWith(".json"))
                {
                    fullPath = $"{userPath}/{noteName}";
                }
                else
                {
                    fullPath = $"{userPath}/{noteName}/cards.txt";
                }

                var blobClient = containerClient.GetBlobClient(fullPath);

                if (await blobClient.ExistsAsync())
                {
                    var response = await blobClient.DownloadAsync();
                    using var streamReader = new StreamReader(response.Value.Content);
                    var content = await streamReader.ReadToEndAsync();
                    Debug.WriteLine($"ノートの取得完了 - サイズ: {content.Length} バイト, パス: {fullPath}");
                    return content;
                }

                Debug.WriteLine($"ノートが見つかりません: {fullPath}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ノートの取得中にエラー: {ex.Message}");
                throw;
            }
        }

        public async Task SaveNoteAsync(string uid, string noteName, string content, string subFolder = null)
        {
            await EnsureInitializedAsync();
            try
            {
                Debug.WriteLine($"ノートの保存開始 - UID: {uid}, ノート名: {noteName}, サブフォルダ: {subFolder ?? "なし"}");
                
                var containerClient = _blobServiceClient.GetBlobContainerClient(CONTAINER_NAME);
                var userPath = GetUserPath(uid, subFolder);
                string fullPath;

                // JSONファイルの場合は直接ファイルとして保存
                if (noteName.EndsWith(".json"))
                {
                    fullPath = $"{userPath}/{noteName}";
                }
                else
                {
                    fullPath = $"{userPath}/{noteName}/cards.txt";
                }

                var blobClient = containerClient.GetBlobClient(fullPath);

                using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
                await blobClient.UploadAsync(stream, overwrite: true);
                Debug.WriteLine($"ノートの保存完了 - サイズ: {content.Length} バイト, パス: {fullPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ノートの保存中にエラー: {ex.Message}");
                throw;
            }
        }

        public async Task UploadImageAsync(string uid, string imageName, string base64Content, string subFolder = null)
        {
            await EnsureInitializedAsync();
            try
            {
                Debug.WriteLine($"画像のアップロード開始 - UID: {uid}, 画像名: {imageName}, サブフォルダ: {subFolder ?? "なし"}");
                
                var containerClient = _blobServiceClient.GetBlobContainerClient(CONTAINER_NAME);
                var userPath = GetUserPath(uid, subFolder);
                var fullPath = $"{userPath}/{imageName}";

                var blobClient = containerClient.GetBlobClient(fullPath);

                // Base64文字列をバイト配列に変換してアップロード
                var imageBytes = Convert.FromBase64String(base64Content);
                using var stream = new MemoryStream(imageBytes);
                await blobClient.UploadAsync(stream, overwrite: true);
                Debug.WriteLine($"画像のアップロード完了 - サイズ: {imageBytes.Length} バイト, パス: {fullPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"画像のアップロード中にエラー: {ex.Message}");
                throw;
            }
        }

        public async Task UploadImageBinaryAsync(string uid, string imageName, byte[] imageBytes, string subFolder = null)
        {
            await EnsureInitializedAsync();
            try
            {
                Debug.WriteLine($"画像のバイナリアップロード開始 - UID: {uid}, 画像名: {imageName}, サブフォルダ: {subFolder ?? "なし"}");
                
                var containerClient = _blobServiceClient.GetBlobContainerClient(CONTAINER_NAME);
                var userPath = GetUserPath(uid, subFolder);
                var fullPath = $"{userPath}/{imageName}";

                var blobClient = containerClient.GetBlobClient(fullPath);

                using var stream = new MemoryStream(imageBytes);
                await blobClient.UploadAsync(stream, overwrite: true);
                Debug.WriteLine($"画像のバイナリアップロード完了 - サイズ: {imageBytes.Length} バイト, パス: {fullPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"画像のバイナリアップロード中にエラー: {ex.Message}");
                throw;
            }
        }

        public async Task<List<string>> GetSubFoldersAsync(string uid)
        {
            await EnsureInitializedAsync();
            try
            {
                Debug.WriteLine($"サブフォルダ一覧の取得開始 - UID: {uid}");
                
                var containerClient = _blobServiceClient.GetBlobContainerClient(CONTAINER_NAME);
                var folders = new HashSet<string>();

                // cards.txtファイルのみを対象とした効率的な検索
                await foreach (var blob in containerClient.GetBlobsAsync(prefix: $"{uid}/"))
                {
                    // cards.txtで終わらないファイルは早期にスキップ（効率化）
                    if (!blob.Name.EndsWith("/cards.txt"))
                        continue;

                    Debug.WriteLine($"見つかったcards.txt: {blob.Name}");
                    var parts = blob.Name.Split('/');
                    if (parts.Length >= 3)
                    {
                        // UIDを除外し、cards.txtの親フォルダまでのパスを取得
                        var remainingParts = parts.Skip(1).Take(parts.Length - 2).ToArray();
                        if (remainingParts.Length > 0)
                        {
                            // 最初のディレクトリがサブフォルダ
                            var subFolder = remainingParts[0];
                            if (!string.IsNullOrEmpty(subFolder))
                            {
                                folders.Add(subFolder);
                                Debug.WriteLine($"サブフォルダを追加: {subFolder}");
                            }
                        }
                    }
                }

                Debug.WriteLine($"取得したサブフォルダ数: {folders.Count}");
                Debug.WriteLine($"サブフォルダ一覧: {string.Join(", ", folders)}");
                return folders.ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"サブフォルダ一覧の取得中にエラー: {ex.Message}");
                throw;
            }
        }

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

                // ローカルの保存先パスを構築
                var localPath = FolderPath;
                if (subFolder != null)
                {
                    localPath = Path.Combine(localPath, subFolder);
                }
                localPath = Path.Combine(localPath, noteName);

                // サブフォルダが存在しない場合は作成
                if (!Directory.Exists(localPath))
                {
                    Directory.CreateDirectory(localPath);
                    Debug.WriteLine($"サブフォルダを作成しました: {localPath}");
                }

                // ノートファイルのパス（サブフォルダを含めたパスで保存）
                var notePath = Path.Combine(localPath, "cards.txt");
                File.WriteAllText(notePath, content);
                Debug.WriteLine($"ローカルノートを作成しました: {notePath}");

                // cards.txtの場合、関連するJSONファイルも取得
                var jsonFiles = await GetNoteListAsync(uid, $"{subFolder}/{noteName}/cards");
                foreach (var jsonFile in jsonFiles)
                {
                    var jsonContent = await GetNoteContentAsync(uid, jsonFile, $"{subFolder}/{noteName}/cards");
                    if (jsonContent != null)
                    {
                        var cardsPath = Path.Combine(localPath, "cards");
                        if (!Directory.Exists(cardsPath))
                        {
                            Directory.CreateDirectory(cardsPath);
                        }
                        var jsonPath = Path.Combine(cardsPath, jsonFile);
                        File.WriteAllText(jsonPath, jsonContent);
                        Debug.WriteLine($"JSONファイルを作成しました: {jsonPath}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ローカルノートの作成中にエラー: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 共有キーからノート情報にアクセスする
        /// </summary>
        public async Task<(string userId, string notePath, bool isFolder)> AccessNoteWithShareKeyAsync(string shareKey)
        {
            await EnsureInitializedAsync();
            try
            {
                Debug.WriteLine($"共有キーによるアクセス開始: {shareKey}");
                
                var containerClient = _blobServiceClient.GetBlobContainerClient(CONTAINER_NAME);
                var mappingPath = $"share_keys/{shareKey}.json";
                var blobClient = containerClient.GetBlobClient(mappingPath);

                if (!await blobClient.ExistsAsync())
                {
                    throw new Exception("共有キーが見つかりません。");
                }

                var response = await blobClient.DownloadAsync();
                using var streamReader = new StreamReader(response.Value.Content);
                var json = await streamReader.ReadToEndAsync();
                
                var mapping = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (!mapping.TryGetValue("userId", out var userId) ||
                    !mapping.TryGetValue("notePath", out var notePath) ||
                    !mapping.TryGetValue("isFolder", out var isFolderStr))
                {
                    throw new Exception("無効な共有キーです。");
                }

                bool.TryParse(isFolderStr, out var isFolder);
                
                Debug.WriteLine($"共有ノートにアクセス成功 - ユーザーID: {userId}, パス: {notePath}, フォルダ: {isFolder}");
                return (userId, notePath, isFolder);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"共有キーによるアクセスに失敗: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 共有ノート用のファイル取得（パスを直接指定）
        /// </summary>
        public async Task<string> GetSharedNoteFileAsync(string userId, string folderPath, string fileName)
        {
            await EnsureInitializedAsync();
            try
            {
                Debug.WriteLine($"共有ファイル取得開始 - UID: {userId}, フォルダ: {folderPath}, ファイル名: {fileName}");
                
                var containerClient = _blobServiceClient.GetBlobContainerClient(CONTAINER_NAME);
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
            await EnsureInitializedAsync();
            try
            {
                Debug.WriteLine($"共有ファイル一覧取得開始 - UID: {userId}, フォルダ: {folderPath}");
                
                var containerClient = _blobServiceClient.GetBlobContainerClient(CONTAINER_NAME);
                var prefix = $"{userId}/{folderPath}/";
                var files = new List<string>();

                Debug.WriteLine($"検索プレフィックス: {prefix}");
                
                await foreach (var blob in containerClient.GetBlobsAsync(prefix: prefix))
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
            await EnsureInitializedAsync();
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
                            Debug.WriteLine($"カードファイル取得結果 {uuid}: {(cardContent != null ? "成功" : "失敗")}");
                            if (cardContent != null)
                            {
                                var cardPath = Path.Combine(cardsDirPath, $"{uuid}.json");
                                
                                // 既存のカードファイルがある場合は上書きしない
                                if (File.Exists(cardPath))
                                {
                                    Debug.WriteLine($"既存のカードファイルが存在するため、ダウンロードをスキップ: {uuid}");
                                }
                                else
                                {
                                    await File.WriteAllTextAsync(cardPath, cardContent);
                                    Debug.WriteLine($"カードファイルをダウンロード: {cardPath}");
                                }
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
                            var imgBytes = await GetImageBinaryAsync(userId, imgFile, $"{notePath}/img");
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
                var localBasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Flashnote");
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
            await EnsureInitializedAsync();
            var downloadedNotes = new List<(string noteName, string subFolder, string fullNotePath)>();
            
            try
            {
                Debug.WriteLine($"共有アイテムのダウンロード開始 - ユーザーID: {userId}, パス: {folderPath}");
                
                var containerClient = _blobServiceClient.GetBlobContainerClient(CONTAINER_NAME);
                
                // まず直下にcards.txtがあるかチェック（単一ノートの場合）
                var directCardsPath = $"{userId}/{folderPath}/cards.txt";
                Debug.WriteLine($"直下のcards.txtをチェック: {directCardsPath}");
                
                var directCardsBlobClient = containerClient.GetBlobClient(directCardsPath);
                var directCardsExists = await directCardsBlobClient.ExistsAsync();
                
                if (directCardsExists.Value)
                {
                    Debug.WriteLine($"直下にcards.txtが存在するため、単一ノートとして処理: {folderPath}");
                    
                    // 単一ノートとしてダウンロード
                    var (subFolder, noteName, _) = ParseBlobPath($"{userId}/{folderPath}/cards.txt");
                    
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
                    await foreach (var blob in containerClient.GetBlobsAsync(prefix: folderPrefix))
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
            await EnsureInitializedAsync();
            try
            {
                Debug.WriteLine($"共有ノートファイルの保存開始 - ユーザーID: {userId}, フォルダパス: {folderPath}, ファイル名: {fileName}");
                
                var containerClient = _blobServiceClient.GetBlobContainerClient(CONTAINER_NAME);
                
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
            await EnsureInitializedAsync();
            try
            {
                Debug.WriteLine($"共有キーのアップロード開始 - UID: {uid}");
                
                var containerClient = _blobServiceClient.GetBlobContainerClient(CONTAINER_NAME);
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
            await EnsureInitializedAsync();
            try
            {
                Debug.WriteLine($"共有キーのダウンロード開始 - UID: {uid}");
                
                var containerClient = _blobServiceClient.GetBlobContainerClient(CONTAINER_NAME);
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
                throw;
            }
        }

        /// <summary>
        /// 共有キーファイルが存在するかチェックする
        /// </summary>
        public async Task<bool> SharedKeysExistsAsync(string uid)
        {
            await EnsureInitializedAsync();
            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(CONTAINER_NAME);
                var fullPath = $"{uid}/share_keys.json";
                var blobClient = containerClient.GetBlobClient(fullPath);
                return await blobClient.ExistsAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"共有キーファイルの存在確認中にエラー: {ex.Message}");
                return false;
            }
        }

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
        /// 学習記録（result.txt）が存在するかチェックする
        /// </summary>
        public async Task<bool> LearningResultExistsAsync(string uid, string noteName, string subFolder = null)
        {
            await EnsureInitializedAsync();
            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(CONTAINER_NAME);
                var userPath = GetUserPath(uid, subFolder);
                var fullPath = $"{userPath}/{noteName}/result.txt";
                var blobClient = containerClient.GetBlobClient(fullPath);
                return await blobClient.ExistsAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"学習記録ファイルの存在確認中にエラー: {ex.Message}");
                return false;
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
                
                await foreach (var blob in containerClient.GetBlobsAsync(prefix: prefix))
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
    }
} 