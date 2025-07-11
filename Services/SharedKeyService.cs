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
                var noteName = Path.GetFileName(sharedInfo.NotePath);
                Debug.WriteLine($"単一ノートのインポート: {noteName}");

                // CardSyncServiceを使用してノートを同期
                var cardSyncService = new CardSyncService(_blobStorageService);
                await cardSyncService.SyncSharedNoteAsync(sharedInfo.OriginalUserId, sharedInfo.NotePath, noteName, null);
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

                // CardSyncServiceを使用してフォルダを同期
                var cardSyncService = new CardSyncService(_blobStorageService);
                await cardSyncService.SyncSharedFolderAsync(sharedInfo.OriginalUserId, sharedInfo.NotePath, sharedInfo.ShareKey);
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
    }
} 