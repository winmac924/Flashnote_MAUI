using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Linq;

namespace Flashnote.Services
{
    public class SharedKeyInfo
    {
        [JsonPropertyName("originalUserId")]
        public string OriginalUserId { get; set; }
        
        [JsonPropertyName("notePath")]
        public string NotePath { get; set; }
        
        [JsonPropertyName("shareKey")]
        public string ShareKey { get; set; }
        
        [JsonPropertyName("isFolder")]
        public bool IsFolder { get; set; }
        
        [JsonPropertyName("isOwnedByMe")]
        public bool IsOwnedByMe { get; set; }
    }

    public class SharedKeyService
    {
        private readonly string _sharedKeysFilePath;
        private Dictionary<string, SharedKeyInfo> _sharedNotes;
        private readonly BlobStorageService _blobStorageService;

        public SharedKeyService()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var flashnotePath = Path.Combine(appDataPath, "Flashnote");
            
            // フォルダが存在しない場合は作成
            if (!Directory.Exists(flashnotePath))
            {
                Directory.CreateDirectory(flashnotePath);
            }

            _sharedKeysFilePath = Path.Combine(flashnotePath, "share_keys.json");
            _sharedNotes = new Dictionary<string, SharedKeyInfo>();
            _blobStorageService = new BlobStorageService();
            LoadSharedKeys();
        }

        public Dictionary<string, SharedKeyInfo> GetSharedNotes()
        {
            return new Dictionary<string, SharedKeyInfo>(_sharedNotes);
        }

        public void AddSharedNote(string noteName, SharedKeyInfo sharedInfo)
        {
            _sharedNotes[noteName] = sharedInfo;
            SaveSharedKeys();
            Debug.WriteLine($"共有ノートを追加: {noteName}");
        }

        public void RemoveSharedNote(string noteName)
        {
            if (_sharedNotes.Remove(noteName))
            {
                SaveSharedKeys();
                Debug.WriteLine($"共有ノートを削除: {noteName}");
            }
        }

        public bool IsSharedNote(string noteName)
        {
            return _sharedNotes.ContainsKey(noteName);
        }

        public SharedKeyInfo GetSharedNoteInfo(string noteName)
        {
            _sharedNotes.TryGetValue(noteName, out var info);
            return info;
        }

        /// <summary>
        /// 共有フォルダの一覧を取得する
        /// </summary>
        public List<string> GetSharedFolders()
        {
            return _sharedNotes
                .Where(kvp => kvp.Value.IsFolder)
                .Select(kvp => kvp.Key)
                .ToList();
        }

        /// <summary>
        /// 指定されたノートが共有フォルダ内のノートかどうかをチェックする
        /// </summary>
        public bool IsInSharedFolder(string noteName, string subFolder)
        {
            // 共有フォルダの一覧を取得
            var sharedFolders = GetSharedFolders();
            
            // サブフォルダが共有フォルダの場合は、その中のノートは共有ノート
            if (!string.IsNullOrEmpty(subFolder) && sharedFolders.Contains(subFolder))
            {
                return true;
            }
            
            // 完全パス（サブフォルダ/ノート名）が共有フォルダの場合は、その中のノートは共有ノート
            if (!string.IsNullOrEmpty(subFolder))
            {
                var fullPath = $"{subFolder}/{noteName}";
                if (sharedFolders.Contains(fullPath))
                {
                    return true;
                }
            }
            
            return false;
        }

        /// <summary>
        /// 外部ファイルから共有キーをインポートする
        /// </summary>
        public async Task<(int imported, int skipped)> ImportSharedKeysFromFileAsync(string filePath)
        {
            try
            {
                Debug.WriteLine($"外部ファイルからの共有キーインポート開始: {filePath}");
                
                if (!File.Exists(filePath))
                {
                    throw new FileNotFoundException($"ファイルが見つかりません: {filePath}");
                }

                var json = await File.ReadAllTextAsync(filePath);
                Debug.WriteLine($"読み込んだJSON: {json}");
                
                var jsonOptions = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                
                var importedSharedNotes = JsonSerializer.Deserialize<Dictionary<string, SharedKeyInfo>>(json, jsonOptions);
                
                if (importedSharedNotes == null)
                {
                    throw new Exception("ファイルの解析に失敗しました。");
                }

                Debug.WriteLine($"インポート対象の共有キー数: {importedSharedNotes.Count}");

                int imported = 0;
                int skipped = 0;

                foreach (var (noteName, sharedInfo) in importedSharedNotes)
                {
                    Debug.WriteLine($"処理中の共有キー: {noteName}, OriginalUserId: {sharedInfo?.OriginalUserId}, NotePath: {sharedInfo?.NotePath}, ShareKey: {sharedInfo?.ShareKey}, IsFolder: {sharedInfo?.IsFolder}");
                    
                    if (sharedInfo == null)
                    {
                        Debug.WriteLine($"共有キー情報がnullのためスキップ: {noteName}");
                        skipped++;
                        continue;
                    }
                    
                    if (_sharedNotes.ContainsKey(noteName))
                    {
                        Debug.WriteLine($"既存の共有キーをスキップ: {noteName}");
                        skipped++;
                    }
                    else
                    {
                        Debug.WriteLine($"新しい共有キーを追加: {noteName}");
                        _sharedNotes[noteName] = sharedInfo;
                        imported++;
                        
                        // 共有ノートをインポート
                        try
                        {
                            await ImportSharedNoteAsync(sharedInfo);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"共有ノートのインポート中にエラー: {noteName}, エラー: {ex.Message}");
                            // インポートに失敗しても共有キーは保持する
                        }
                    }
                }

                // 新しい共有キーが追加された場合は保存
                if (imported > 0)
                {
                    SaveSharedKeys();
                    Debug.WriteLine($"共有キーのインポート完了 - 追加: {imported}件, スキップ: {skipped}件");
                }

                return (imported, skipped);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"外部ファイルからの共有キーインポート中にエラー: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 共有キーをサーバーと同期する
        /// </summary>
        public async Task SyncSharedKeysAsync(string uid)
        {
            try
            {
                Debug.WriteLine($"共有キーの同期開始 - UID: {uid}");

                // 1. サーバーから共有キーをダウンロード
                var serverSharedKeysJson = await _blobStorageService.DownloadSharedKeysAsync(uid);
                if (string.IsNullOrEmpty(serverSharedKeysJson))
                {
                    Debug.WriteLine("サーバーに共有キーファイルが存在しません");
                    // サーバーにファイルがない場合は、ローカルの共有キーをアップロード
                    if (_sharedNotes.Count > 0)
                    {
                        var jsonOption = new JsonSerializerOptions 
                        { 
                            WriteIndented = true,
                            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                        };
                        var currentShareKeysJson = JsonSerializer.Serialize(_sharedNotes, jsonOption);
                        await _blobStorageService.UploadSharedKeysAsync(uid, currentShareKeysJson);
                        Debug.WriteLine("ローカルの共有キーをサーバーにアップロード完了");
                    }
                    return;
                }

                var serverSharedNotes = JsonSerializer.Deserialize<Dictionary<string, SharedKeyInfo>>(serverSharedKeysJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                if (serverSharedNotes == null)
                {
                    Debug.WriteLine("サーバーの共有キーファイルの解析に失敗しました");
                    return;
                }

                Debug.WriteLine($"サーバーの共有キー数: {serverSharedNotes.Count}");

                // 2. ローカルに共有キーがない場合、サーバーの内容で初期化
                if (_sharedNotes.Count == 0)
                {
                    Debug.WriteLine("ローカルに共有キーが存在しないため、サーバーの内容で初期化します");
                    _sharedNotes = new Dictionary<string, SharedKeyInfo>(serverSharedNotes);
                    SaveSharedKeys();
                    
                    // すべての共有ノートをインポート
                    foreach (var (noteName, sharedInfo) in serverSharedNotes)
                    {
                        Debug.WriteLine($"共有ノートをインポート: {noteName}");
                        await ImportSharedNoteAsync(sharedInfo);
                    }
                    
                    Debug.WriteLine($"サーバーの共有キーで初期化完了: {_sharedNotes.Count}件");
                    return;
                }

                // 3. サーバーの共有キーが空の場合、ローカルの共有キーをアップロード
                if (serverSharedNotes.Count == 0)
                {
                    Debug.WriteLine("サーバーの共有キーが空のため、ローカルの共有キーをアップロードします");
                    var jsonOption = new JsonSerializerOptions 
                    { 
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    };
                    var currentShareKeysJson = JsonSerializer.Serialize(_sharedNotes, jsonOption);
                    await _blobStorageService.UploadSharedKeysAsync(uid, currentShareKeysJson);
                    Debug.WriteLine("ローカルの共有キーをサーバーにアップロード完了");
                    return;
                }

                // 4. ローカルとサーバーの両方に共有キーがある場合の同期処理
                var jsonOptions = new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                var currentSharedKeysJson = JsonSerializer.Serialize(_sharedNotes, jsonOptions);
                await _blobStorageService.UploadSharedKeysAsync(uid, currentSharedKeysJson);
                Debug.WriteLine("現在の共有キーをサーバーにアップロード完了");

                // 5. 不足しているIDを追加し、ノートをインポート
                var newNotesAdded = false;
                foreach (var (noteName, sharedInfo) in serverSharedNotes)
                {
                    if (!_sharedNotes.ContainsKey(noteName))
                    {
                        Debug.WriteLine($"新しい共有ノートを発見: {noteName}");
                        _sharedNotes[noteName] = sharedInfo;
                        newNotesAdded = true;

                        // ノートをインポート
                        await ImportSharedNoteAsync(sharedInfo);
                    }
                }

                // 6. 新しいノートが追加された場合は保存
                if (newNotesAdded)
                {
                    SaveSharedKeys();
                    Debug.WriteLine("新しい共有ノートを追加して保存完了");
                }

                Debug.WriteLine($"共有キーの同期完了 - ローカル: {_sharedNotes.Count}件, サーバー: {serverSharedNotes.Count}件");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"共有キーの同期中にエラー: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 共有ノートをインポートする
        /// </summary>
        private async Task ImportSharedNoteAsync(SharedKeyInfo sharedInfo)
        {
            try
            {
                Debug.WriteLine($"共有ノートのインポート開始: {sharedInfo.NotePath}");

                if (sharedInfo.IsFolder)
                {
                    // フォルダの場合
                    await ImportSharedFolderAsync(sharedInfo);
                }
                else
                {
                    // 単一ノートの場合
                    await ImportSingleSharedNoteAsync(sharedInfo);
                }

                Debug.WriteLine($"共有ノートのインポート完了: {sharedInfo.NotePath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"共有ノートのインポート中にエラー: {ex.Message}");
                // インポートに失敗しても共有キーは保持する
            }
        }

        /// <summary>
        /// 単一の共有ノートをインポートする
        /// </summary>
        private async Task ImportSingleSharedNoteAsync(SharedKeyInfo sharedInfo)
        {
            try
            {
                Debug.WriteLine($"単一ノートのインポート: {sharedInfo.NotePath}");
                
                // NotePath から noteName と subFolder を抽出
                var parts = sharedInfo.NotePath.Split('/');
                string noteId;
                string subFolder = null;
                
                if (parts.Length == 1)
                {
                    // ルート直下のノート（新形式の場合はUUID）
                    noteId = parts[0];
                }
                else
                {
                    // サブフォルダ内のノート
                    noteId = parts[parts.Length - 1];
                    subFolder = string.Join("/", parts.Take(parts.Length - 1));
                }
                
                Debug.WriteLine($"noteId: {noteId}, subFolder: {subFolder ?? "(なし)"}");
                
                // metadata.json を取得（新形式・旧形式両対応）
                string metadataContent = null;
                try
                {
                    // まず noteId/metadata.json として取得を試みる（新形式）
                    metadataContent = await _blobStorageService.GetUserFileAsync(sharedInfo.OriginalUserId, "metadata.json", noteId);
                    
                    if (string.IsNullOrEmpty(metadataContent) && !string.IsNullOrEmpty(subFolder))
                    {
                        // 旧形式の場合：subFolder/noteId/metadata.json
                        var oldPath = $"{subFolder}/{noteId}";
                        metadataContent = await _blobStorageService.GetUserFileAsync(sharedInfo.OriginalUserId, "metadata.json", oldPath);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"metadata.json の取得に失敗: {ex.Message}");
                }
                
                if (string.IsNullOrEmpty(metadataContent))
                {
                    Debug.WriteLine($"metadata.json が取得できませんでした。ノートID: {noteId}");
                    // metadata がない場合でも、cards.txt をダウンロードして .ankpls を作成する
                    await _blobStorageService.DownloadSharedNoteAsync(
                        sharedInfo.OriginalUserId, 
                        sharedInfo.NotePath, 
                        noteId, 
                        subFolder);
                    Debug.WriteLine($"単一ノートのインポート完了（フォールバック）: {noteId}");
                    return;
                }
                
                // metadata.json を解析して .ankpls を作成
                using var doc = System.Text.Json.JsonDocument.Parse(metadataContent);
                var root = doc.RootElement;
                var originalName = root.TryGetProperty("originalName", out var pOrig) ? pOrig.GetString() : noteId;
                var displayName = root.TryGetProperty("displayName", out var pDisp) ? pDisp.GetString() : originalName;
                
                Debug.WriteLine($"metadata 解析: originalName={originalName}, displayName={displayName}");
                
                // ローカルの保存先パスを決定
                var localBasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Flashnote");
                var tempBasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Flashnote");
                
                string ankplsPath;
                string tempDir;
                
                if (!string.IsNullOrEmpty(subFolder))
                {
                    var subFolderPath = Path.Combine(localBasePath, subFolder);
                    if (!Directory.Exists(subFolderPath))
                    {
                        Directory.CreateDirectory(subFolderPath);
                    }
                    ankplsPath = Path.Combine(subFolderPath, $"{displayName ?? noteId}.ankpls");
                    tempDir = Path.Combine(tempBasePath, subFolder, $"{displayName ?? noteId}_temp");
                }
                else
                {
                    if (!Directory.Exists(localBasePath))
                    {
                        Directory.CreateDirectory(localBasePath);
                    }
                    ankplsPath = Path.Combine(localBasePath, $"{displayName ?? noteId}.ankpls");
                    tempDir = Path.Combine(tempBasePath, $"{displayName ?? noteId}_temp");
                }
                
                // 一時ディレクトリを作成
                if (Directory.Exists(tempDir))
                {
                    try
                    {
                        Directory.Delete(tempDir, true);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"既存の一時ディレクトリの削除に失敗: {ex.Message}");
                    }
                }
                Directory.CreateDirectory(tempDir);
                
                // metadata.json を一時ディレクトリに保存
                var metadataPath = Path.Combine(tempDir, "metadata.json");
                await File.WriteAllTextAsync(metadataPath, metadataContent);
                Debug.WriteLine($"metadata.json を保存: {metadataPath}");
                
                // .ankpls を作成
                if (File.Exists(ankplsPath))
                {
                    try
                    {
                        File.Delete(ankplsPath);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"既存の .ankpls の削除に失敗: {ex.Message}");
                    }
                }
                
                System.IO.Compression.ZipFile.CreateFromDirectory(tempDir, ankplsPath);
                Debug.WriteLine($".ankpls を作成: {ankplsPath}");
                
                Debug.WriteLine($"単一ノートのインポート完了: {noteId}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"単一ノートのインポート中にエラー: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 共有フォルダをインポートする
        /// </summary>
        private async Task ImportSharedFolderAsync(SharedKeyInfo sharedInfo)
        {
            try
            {
                Debug.WriteLine($"共有フォルダのインポート: {sharedInfo.NotePath}");

                // NotePath からフォルダIDを抽出
                var parts = sharedInfo.NotePath?.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts == null || parts.Length == 0)
                {
                    Debug.WriteLine("ImportSharedFolderAsync: folderPath が不正です");
                    return;
                }
                var folderId = parts[^1];
                Debug.WriteLine($"フォルダID: {folderId}");
                
                // フォルダの metadata.json を取得
                string metadataContent = null;
                try
                {
                    // 新形式: uid/{folderId}/metadata.json
                    metadataContent = await _blobStorageService.GetUserFileAsync(sharedInfo.OriginalUserId, "metadata.json", folderId);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"フォルダの metadata.json 取得に失敗: {ex.Message}");
                }
                
                if (string.IsNullOrEmpty(metadataContent))
                {
                    Debug.WriteLine($"フォルダの metadata.json が取得できませんでした。フォルダID: {folderId}");
                    // フォールバック: 旧形式のダウンロードを試みる
                    var (isActuallyFolder, downloadedNotes) = await _blobStorageService.DownloadSharedFolderAsync(
                        sharedInfo.OriginalUserId, 
                        sharedInfo.NotePath, 
                        sharedInfo.ShareKey);
                    
                    if (isActuallyFolder)
                    {
                        Debug.WriteLine($"共有フォルダのインポート完了（フォールバック）: {downloadedNotes.Count}個のノートをダウンロード");
                    }
                    else
                    {
                        Debug.WriteLine($"共有フォルダは実際には単一ノートでした（フォールバック）");
                    }
                    return;
                }
                
                // metadata.json を解析
                using var doc = System.Text.Json.JsonDocument.Parse(metadataContent);
                var root = doc.RootElement;
                var isFolder = root.TryGetProperty("isFolder", out var pIsFolder) && pIsFolder.GetBoolean();
                var originalName = root.TryGetProperty("originalName", out var pOrig) ? pOrig.GetString() : folderId;
                var displayName = root.TryGetProperty("displayName", out var pDisp) ? pDisp.GetString() : originalName;
                
                if (!isFolder)
                {
                    Debug.WriteLine($"metadata によると、これはフォルダではありません。単一ノートとして処理します。");
                    // 単一ノートとして処理
                    await ImportSingleSharedNoteAsync(sharedInfo);
                    return;
                }
                
                Debug.WriteLine($"フォルダ metadata 解析: originalName={originalName}, displayName={displayName}, isFolder={isFolder}");
                
                // ローカルの保存先パスを決定
                var localBasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Flashnote");
                var tempBasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Flashnote");
                
                // フォルダの metadata.json を保存（Documents）
                var folderDir = Path.Combine(localBasePath, folderId);
                if (!Directory.Exists(folderDir))
                {
                    Directory.CreateDirectory(folderDir);
                }
                var folderMetaPath = Path.Combine(folderDir, "metadata.json");
                await File.WriteAllTextAsync(folderMetaPath, metadataContent);
                Debug.WriteLine($"フォルダ metadata.json を保存: {folderMetaPath}");
                
                // LocalApplicationData にもコピー
                try
                {
                    var tempMetaDir = Path.Combine(tempBasePath, folderId);
                    if (!Directory.Exists(tempMetaDir))
                    {
                        Directory.CreateDirectory(tempMetaDir);
                    }
                    var tempMetaPath = Path.Combine(tempMetaDir, "metadata.json");
                    await File.WriteAllTextAsync(tempMetaPath, metadataContent);
                    Debug.WriteLine($"一時フォルダ metadata.json を保存: {tempMetaPath}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"一時フォルダ metadata 保存に失敗: {ex.Message}");
                }
                
                Debug.WriteLine($"共有フォルダのインポート完了: {folderId}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"共有フォルダのインポート中にエラー: {ex.Message}");
                throw;
            }
        }

        private void LoadSharedKeys()
        {
            try
            {
                if (File.Exists(_sharedKeysFilePath))
                {
                    var json = File.ReadAllText(_sharedKeysFilePath);
                    var jsonOptions = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    };
                    _sharedNotes = JsonSerializer.Deserialize<Dictionary<string, SharedKeyInfo>>(json, jsonOptions) ?? new Dictionary<string, SharedKeyInfo>();
                    Debug.WriteLine($"共有キーを読み込み: {_sharedNotes.Count}件");
                }
                else
                {
                    Debug.WriteLine("共有キーファイルが存在しません");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"共有キーの読み込みに失敗: {ex.Message}");
                _sharedNotes = new Dictionary<string, SharedKeyInfo>();
            }
        }

        private void SaveSharedKeys()
        {
            try
            {
                var jsonOptions = new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                var json = JsonSerializer.Serialize(_sharedNotes, jsonOptions);
                File.WriteAllText(_sharedKeysFilePath, json, System.Text.Encoding.UTF8);
                Debug.WriteLine($"共有キーを保存: {_sharedNotes.Count}件");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"共有キーの保存に失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 共有キーを生成してサーバーにアップロードする
        /// 返り値:生成した shareKey
        /// </summary>
        public async Task<string> CreateAndUploadShareKeyAsync(string noteName, string notePath, bool isFolder)
        {
            try
            {
                Debug.WriteLine($"共有キー作成開始: noteName={noteName}, notePath={notePath}, isFolder={isFolder}");

                // ハイフンを含む標準形式 (D)で生成し大文字化
                var uuid = Guid.NewGuid().ToString("D").ToUpperInvariant();

                var sharedInfo = new SharedKeyInfo
                {
                    OriginalUserId = App.CurrentUser?.Uid ?? string.Empty,
                    NotePath = notePath,
                    ShareKey = uuid,
                    IsFolder = isFolder,
                    IsOwnedByMe = true
                };

                // マッピングJSONを作成（isFolderはboolean、sharedByUserIdを含める）
                var mappingObj = new
                {
                    userId = sharedInfo.OriginalUserId,
                    isFolder = sharedInfo.IsFolder,
                    sharedByUserId = App.CurrentUser?.Uid ?? string.Empty,
                    notePath = sharedInfo.NotePath
                };

                var jsonOptions = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                var mappingJson = System.Text.Json.JsonSerializer.Serialize(mappingObj, jsonOptions);

                // サーバーにアップロード（ファイル名は {UUID}.json 大文字）
                await _blobStorageService.UploadShareKeyMappingAsync(uuid, mappingJson);

                // ローカルにも共有キーを保存
                var displayName = Path.GetFileName(notePath);
                // displayName が空の場合は UUID をキーに使う（ルート共有などで notePath が空になる場合に備える）
                var localKey = !string.IsNullOrEmpty(displayName) ? displayName : uuid;
                _sharedNotes[localKey] = sharedInfo;
                SaveSharedKeys();

                Debug.WriteLine($"共有キー作成完了: {uuid}");
                return uuid;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"共有キー作成中にエラー: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 共有キー（UUID）を使って共有ノート・フォルダにアクセスする
        /// </summary>
        public async Task<(bool success, string message)> AccessWithShareKeyAsync(string shareKey)
        {
            try
            {
                Debug.WriteLine($"共有キーによるアクセス開始: {shareKey}");
                
                // UUIDの形式チェック（大文字・小文字を正規化）
                var normalizedKey = shareKey.Trim().ToUpperInvariant();
                if (!Guid.TryParse(normalizedKey, out _))
                {
                    return (false, "無効な共有キー形式です。");
                }
                
                // share_keys/{UUID}.json をダウンロード
                var (userId, notePath, isFolder) = await _blobStorageService.AccessNoteWithShareKeyAsync(normalizedKey);
                
                if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(notePath))
                {
                    return (false, "共有キーが見つかりません。キーが正しいか確認してください。");
                }
                
                Debug.WriteLine($"共有キー情報取得成功 - userId: {userId}, notePath: {notePath}, isFolder: {isFolder}");
                
                // 共有情報を作成
                var sharedInfo = new SharedKeyInfo
                {
                    OriginalUserId = userId,
                    NotePath = notePath,
                    ShareKey = normalizedKey,
                    IsFolder = isFolder,
                    IsOwnedByMe = false // 他人から共有されたノート
                };
                
                // ローカルキーを決定（notePathの最後の部分、またはUUID）
                var displayName = Path.GetFileName(notePath);
                var localKey = !string.IsNullOrEmpty(displayName) ? displayName : normalizedKey;
                
                // 既に存在する場合はスキップ
                if (_sharedNotes.ContainsKey(localKey))
                {
                    Debug.WriteLine($"共有キーは既に登録されています: {localKey}");
                    return (true, $"共有キーは既に登録されています: {localKey}");
                }
                
                // 共有ノートをインポート
                await ImportSharedNoteAsync(sharedInfo);
                
                // ローカルに共有キーを保存
                _sharedNotes[localKey] = sharedInfo;
                SaveSharedKeys();
                
                Debug.WriteLine($"共有キーによるアクセス完了: {localKey}");
                
                var itemType = isFolder ? "フォルダ" : "ノート";
                return (true, $"共有{itemType}を正常にインポートしました: {localKey}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"共有キーによるアクセス中にエラー: {ex.Message}");
                return (false, $"エラーが発生しました: {ex.Message}");
            }
        }
    }
}