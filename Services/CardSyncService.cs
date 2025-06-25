using System.Text.Json;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;

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

        private class CardInfo
        {
            public string Uuid { get; set; }
            public DateTime LastModified { get; set; }
            public string Content { get; set; }
        }

        private async Task<List<CardInfo>> ParseCardsFile(string content)
        {
            try
            {
                Debug.WriteLine($"ParseCardsFile開始 - コンテンツ長: {content.Length}");
                var cards = new List<CardInfo>();
                var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                Debug.WriteLine($"行数: {lines.Length}");

                // 1行目が数字のみの場合はカード数なのでスキップ
                int startIndex = 0;
                if (lines.Length > 0 && int.TryParse(lines[0], out _))
                {
                    startIndex = 1;
                }

                for (int i = startIndex; i < lines.Length; i++)
                {
                    try
                    {
                        // 行の末尾の改行文字を削除
                        var line = lines[i].TrimEnd('\r', '\n');
                        Debug.WriteLine($"行 {i} をパース: {line}");
                        var parts = line.Split(',');
                        if (parts.Length >= 2)
                        {
                            var card = new CardInfo
                            {
                                Uuid = parts[0],
                                LastModified = DateTime.ParseExact(parts[1].Trim(), "yyyy-MM-dd HH:mm:ss", null)
                            };
                            cards.Add(card);
                            Debug.WriteLine($"カード情報をパース: UUID={card.Uuid}, 最終更新={card.LastModified}");
                        }
                        else
                        {
                            Debug.WriteLine($"行 {i} のパースに失敗: カンマ区切りの値が不足しています");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"カードのパースに失敗: {lines[i]}, エラー: {ex.Message}");
                    }
                }

                Debug.WriteLine($"ParseCardsFile完了 - パースしたカード数: {cards.Count}");
                return cards;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"カードファイルのパース中にエラー: {ex.Message}");
                throw;
            }
        }

        private async Task<string> ReadLocalCardsFile(string notePath)
        {
            // 一時フォルダのパスを構築
            string tempDir;
            if (notePath.Contains(Path.DirectorySeparatorChar))
            {
                // サブフォルダ内のノートの場合
                var directoryName = Path.GetDirectoryName(notePath);
                var fileName = Path.GetFileNameWithoutExtension(notePath);
                tempDir = Path.Combine(_tempBasePath, directoryName, fileName + "_temp");
            }
            else
            {
                // ルートのノートの場合
                tempDir = Path.Combine(_tempBasePath, notePath + "_temp");
            }
            
            var cardsPath = Path.Combine(tempDir, "cards.txt");
            Debug.WriteLine($"読み込みファイルのパス: {cardsPath}");

            if (File.Exists(cardsPath))
            {
                return await File.ReadAllTextAsync(cardsPath);
            }
            return string.Empty;
        }

        public async Task SyncNoteAsync(string uid, string noteName, string subFolder = null)
        {
            try
            {
                // サーバーとローカルのパスを取得
                var userPath = subFolder != null ? $"{uid}/{subFolder}" : uid;
                var notePath = Path.Combine(userPath, noteName);

                Debug.WriteLine($"=== ノート同期開始 ===");
                Debug.WriteLine($"同期開始 - UID: {uid}, ノート名: {noteName}, サブフォルダ: {subFolder ?? "なし"}");
                Debug.WriteLine($"ユーザーパス: {userPath}");
                Debug.WriteLine($"ノートパス: {notePath}");

                // サーバーとローカルのcards.txtを取得
                var serverContent = await _blobStorageService.GetNoteContentAsync(uid, noteName, subFolder);
                Debug.WriteLine($"サーバーコンテンツ取得: {(serverContent != null ? "成功" : "失敗")}");
                if (serverContent != null)
                {
                    Debug.WriteLine($"サーバーコンテンツサイズ: {serverContent.Length} 文字");
                }
                
                var localContent = await ReadLocalCardsFile(subFolder != null ? Path.Combine(subFolder, noteName) : noteName);
                Debug.WriteLine($"ローカルコンテンツ取得: {(localContent != null ? "成功" : "失敗")}");
                if (localContent != null)
                {
                    Debug.WriteLine($"ローカルコンテンツサイズ: {localContent.Length} 文字");
                    Debug.WriteLine($"ローカルのcards.txtの内容:");
                    Debug.WriteLine(localContent);
                }

                // 一時ディレクトリの準備
                string tempDir;
                if (!string.IsNullOrEmpty(subFolder))
                {
                    // サブフォルダ内のノートの場合
                    tempDir = Path.Combine(_tempBasePath, subFolder, noteName + "_temp");
                }
                else
                {
                    // ルートのノートの場合
                    tempDir = Path.Combine(_tempBasePath, noteName + "_temp");
                }
                
                Debug.WriteLine($"一時ディレクトリパス: {tempDir}");
                if (!Directory.Exists(tempDir))
                {
                    Directory.CreateDirectory(tempDir);
                    Debug.WriteLine($"一時ディレクトリを作成: {tempDir}");
                }
                else
                {
                    Debug.WriteLine($"一時ディレクトリは既に存在: {tempDir}");
                }

                // ローカルにcards.txtがない場合、サーバーから全てダウンロード
                if (string.IsNullOrEmpty(localContent) && serverContent != null)
                {
                    Debug.WriteLine($"ローカルにcards.txtがないため、ノート全体をダウンロードします");
                    var serverCardsToDownload = await ParseCardsFile(serverContent);
                    
                    // サーバーのcards.txtをそのまま保存
                    var tempCardsPath = Path.Combine(tempDir, "cards.txt");
                    await File.WriteAllTextAsync(tempCardsPath, serverContent);
                    Debug.WriteLine($"一時フォルダにcards.txtを保存: {tempCardsPath}");

                    // カードファイルをダウンロード
                    foreach (var card in serverCardsToDownload)
                    {
                        string cardPath;
                        if (!string.IsNullOrEmpty(subFolder))
                        {
                            // サブフォルダ内のノートの場合
                            cardPath = $"{subFolder}/{noteName}/cards";
                        }
                        else
                        {
                            // ルートのノートの場合
                            cardPath = $"{noteName}/cards";
                        }
                        
                        var cardContent = await _blobStorageService.GetNoteContentAsync(uid, $"{card.Uuid}.json", cardPath);
                        if (cardContent != null)
                        {
                            var tempCardPath = Path.Combine(tempDir, "cards", $"{card.Uuid}.json");
                            var tempCardDir = Path.GetDirectoryName(tempCardPath);
                            if (!Directory.Exists(tempCardDir))
                            {
                                Directory.CreateDirectory(tempCardDir);
                                Debug.WriteLine($"一時カードディレクトリを作成: {tempCardDir}");
                            }
                            await File.WriteAllTextAsync(tempCardPath, cardContent);
                            Debug.WriteLine($"カードファイルを一時フォルダにダウンロード: {tempCardPath}");
                        }
                    }

                    // imgフォルダの同期（カードの同期とは別に実行）
                    var tempImgDir = Path.Combine(tempDir, "img");
                    Debug.WriteLine($"=== 画像ダウンロード処理開始 ===");
                    Debug.WriteLine($"一時画像フォルダのパス: {tempImgDir}");
                    Debug.WriteLine($"一時画像フォルダの存在確認: {Directory.Exists(tempImgDir)}");

                    if (!Directory.Exists(tempImgDir))
                    {
                        Directory.CreateDirectory(tempImgDir);
                        Debug.WriteLine($"一時imgディレクトリを作成: {tempImgDir}");
                    }

                    // imgフォルダ内のファイル一覧を取得
                    string imgPath;
                    if (!string.IsNullOrEmpty(subFolder))
                    {
                        // サブフォルダ内のノートの場合
                        imgPath = $"{subFolder}/{noteName}/img";
                    }
                    else
                    {
                        // ルートのノートの場合
                        imgPath = $"{noteName}/img";
                    }
                    
                    var imgFiles = await _blobStorageService.GetImageFilesAsync(uid, imgPath);
                    Debug.WriteLine($"サーバーの画像ファイル数: {imgFiles.Count}");
                    Debug.WriteLine($"サーバーの画像ファイル一覧:");
                    foreach (var imgFile in imgFiles)
                    {
                        Debug.WriteLine($"- {imgFile}");
                    }

                    foreach (var imgFile in imgFiles)
                    {
                        // iOS版の形式（img_########_######.jpg）をチェック
                        if (Regex.IsMatch(imgFile, @"^img_\d{8}_\d{6}\.jpg$"))
                        {
                            Debug.WriteLine($"画像ファイルの処理開始: {imgFile}");
                            var imgBytes = await _blobStorageService.GetImageBinaryAsync(uid, imgFile, imgPath);
                            if (imgBytes != null)
                            {
                                try
                                {
                                    var tempImgPath = Path.Combine(tempImgDir, imgFile);
                                    Debug.WriteLine($"画像ファイルの保存先: {tempImgPath}");
                                    Debug.WriteLine($"画像ファイルのサイズ: {imgBytes.Length} バイト");
                                    await File.WriteAllBytesAsync(tempImgPath, imgBytes);
                                    Debug.WriteLine($"画像ファイルを一時フォルダにダウンロード: {tempImgPath}");
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"画像ファイルのダウンロード中にエラー: {imgFile}, エラー: {ex.Message}");
                                    Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                                }
                            }
                            else
                            {
                                Debug.WriteLine($"画像ファイルのコンテンツが取得できません: {imgFile}");
                            }
                        }
                    }

                    // .ankplsファイルを作成
                    var localBasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Flashnote");
                    string ankplsPath;
                    if (!string.IsNullOrEmpty(subFolder))
                    {
                        // サブフォルダ内のノートの場合
                        var subFolderPath = Path.Combine(localBasePath, subFolder);
                        if (!Directory.Exists(subFolderPath))
                        {
                            Directory.CreateDirectory(subFolderPath);
                        }
                        ankplsPath = Path.Combine(subFolderPath, $"{noteName}.ankpls");
                    }
                    else
                    {
                        // ルートのノートの場合
                        ankplsPath = Path.Combine(localBasePath, $"{noteName}.ankpls");
                    }
                    
                    if (File.Exists(ankplsPath))
                    {
                        File.Delete(ankplsPath);
                        Debug.WriteLine($"既存の.ankplsファイルを削除: {ankplsPath}");
                    }
                    
                    System.IO.Compression.ZipFile.CreateFromDirectory(tempDir, ankplsPath);
                    Debug.WriteLine($".ankplsファイルを作成: {ankplsPath}");

                    Debug.WriteLine($"ノート '{noteName}' の全体ダウンロードが完了しました。");
                    return;
                }

                // ローカルにcards.txtがある場合
                if (!string.IsNullOrEmpty(localContent))
                {
                    Debug.WriteLine($"=== ローカルにcards.txtが存在する場合の処理開始 ===");
                    var localCards = await ParseCardsFile(localContent);
                    var serverCards = serverContent != null ? await ParseCardsFile(serverContent) : new List<CardInfo>();

                    // サーバーのcards.txtを一時保存
                    if (serverContent != null)
                    {
                        var serverCardsPath = Path.Combine(tempDir, "server_cards.txt");
                        await File.WriteAllTextAsync(serverCardsPath, serverContent);
                        Debug.WriteLine($"サーバーのcards.txtを一時保存: {serverCardsPath}");
                        Debug.WriteLine($"サーバーのcards.txtの内容:");
                        Debug.WriteLine(serverContent);
                    }

                    var cardsToDownload = new List<CardInfo>();
                    var cardsToUpload = new List<CardInfo>();
                    var updatedLocalCards = localCards.ToList();

                    Debug.WriteLine($"ローカルのカード情報:");
                    foreach (var card in localCards)
                    {
                        Debug.WriteLine($"UUID={card.Uuid}, 最終更新={card.LastModified}");
                    }

                    Debug.WriteLine($"サーバーのカード情報:");
                    foreach (var card in serverCards)
                    {
                        Debug.WriteLine($"UUID={card.Uuid}, 最終更新={card.LastModified}");
                    }

                    // サーバーにあるがローカルにない、または更新が必要なカードを特定
                    foreach (var serverCard in serverCards)
                    {
                        var localCard = localCards.FirstOrDefault(c => c.Uuid == serverCard.Uuid);
                        if (localCard == null || localCard.LastModified < serverCard.LastModified)
                        {
                            cardsToDownload.Add(serverCard);
                            Debug.WriteLine($"ダウンロード対象カード: {serverCard.Uuid} (ローカル={localCard?.LastModified}, サーバー={serverCard.LastModified})");
                            
                            // ローカルのリストを更新
                            if (localCard != null)
                            {
                                updatedLocalCards.Remove(localCard);
                            }
                            updatedLocalCards.Add(serverCard);
                        }
                        else if (localCard.LastModified > serverCard.LastModified)
                        {
                            // ローカルが新しい場合、アップロード対象に追加
                            cardsToUpload.Add(localCard);
                            Debug.WriteLine($"ローカルが新しいためアップロード対象に追加: {serverCard.Uuid} (ローカル={localCard.LastModified}, サーバー={serverCard.LastModified})");
                        }
                        else
                        {
                            Debug.WriteLine($"カードは最新: {serverCard.Uuid} (最終更新={serverCard.LastModified})");
                        }
                    }

                    // ローカルにあるがサーバーにないカードを特定
                    foreach (var localCard in localCards)
                    {
                        if (!serverCards.Any(c => c.Uuid == localCard.Uuid))
                        {
                            cardsToUpload.Add(localCard);
                            Debug.WriteLine($"新規アップロード対象カード: {localCard.Uuid}");
                            // 新規カードはupdatedLocalCardsに既に含まれているので追加の処理は不要
                        }
                    }

                    // 削除されたカードを検出（サーバーにあるがローカルにないカード）
                    var deletedCards = new List<CardInfo>();
                    foreach (var serverCard in serverCards)
                    {
                        if (!localCards.Any(c => c.Uuid == serverCard.Uuid))
                        {
                            deletedCards.Add(serverCard);
                            Debug.WriteLine($"削除されたカード: {serverCard.Uuid}");
                        }
                    }

                    // カードのダウンロード
                    if (cardsToDownload.Any())
                    {
                        Debug.WriteLine($"ダウンロードするカード数: {cardsToDownload.Count}");
                        foreach (var card in cardsToDownload)
                        {
                            string cardPath;
                            if (!string.IsNullOrEmpty(subFolder))
                            {
                                // サブフォルダ内のノートの場合
                                cardPath = $"{subFolder}/{noteName}/cards";
                            }
                            else
                            {
                                // ルートのノートの場合
                                cardPath = $"{noteName}/cards";
                            }
                            
                            var cardContent = await _blobStorageService.GetNoteContentAsync(uid, $"{card.Uuid}.json", cardPath);
                            if (cardContent != null)
                            {
                                var tempCardPath = Path.Combine(tempDir, "cards", $"{card.Uuid}.json");
                                var tempCardDir = Path.GetDirectoryName(tempCardPath);
                                if (!Directory.Exists(tempCardDir))
                                {
                                    Directory.CreateDirectory(tempCardDir);
                                    Debug.WriteLine($"一時カードディレクトリを作成: {tempCardDir}");
                                }
                                await File.WriteAllTextAsync(tempCardPath, cardContent);
                                Debug.WriteLine($"カードファイルを一時フォルダにダウンロード: {tempCardPath}");
                            }
                        }
                    }

                    // imgフォルダの同期（カードの同期とは独立して実行）
                    var tempImgDir = Path.Combine(tempDir, "img");
                    Debug.WriteLine($"=== 画像同期処理開始（ローカルにcards.txt存在時） ===");
                    Debug.WriteLine($"一時画像フォルダのパス: {tempImgDir}");

                    if (!Directory.Exists(tempImgDir))
                    {
                        Directory.CreateDirectory(tempImgDir);
                        Debug.WriteLine($"一時imgディレクトリを作成: {tempImgDir}");
                    }

                    // ローカルのimgフォルダのパスを取得
                    var localCardsPath = Path.Combine(_tempBasePath, subFolder ?? "", noteName + "_temp", "cards.txt");
                    var localImgDir = Path.Combine(Path.GetDirectoryName(localCardsPath), "img");
                    Debug.WriteLine($"ローカルのimgフォルダのパス: {localImgDir}");
                    Debug.WriteLine($"ローカルのimgフォルダの存在確認: {Directory.Exists(localImgDir)}");

                    if (Directory.Exists(localImgDir))
                    {
                        var localImgFiles = Directory.GetFiles(localImgDir, "img_*.jpg");
                        Debug.WriteLine($"ローカルの画像ファイル数: {localImgFiles.Length}");
                        Debug.WriteLine($"ローカルの画像ファイル一覧:");
                        foreach (var imgFile in localImgFiles)
                        {
                            Debug.WriteLine($"- {Path.GetFileName(imgFile)}");
                        }

                        foreach (var imgFile in localImgFiles)
                        {
                            try
                            {
                                var fileName = Path.GetFileName(imgFile);
                                // iOS版の形式（img_########_######.jpg）をチェック
                                if (Regex.IsMatch(fileName, @"^img_\d{8}_\d{6}\.jpg$"))
                                {
                                    Debug.WriteLine($"画像ファイルの処理開始: {fileName}");
                                    var imgBytes = await File.ReadAllBytesAsync(imgFile);
                                    Debug.WriteLine($"画像ファイルのサイズ: {imgBytes.Length} バイト");
                                    
                                    string imgPath;
                                    if (!string.IsNullOrEmpty(subFolder))
                                    {
                                        // サブフォルダ内のノートの場合
                                        imgPath = $"{subFolder}/{noteName}/img";
                                    }
                                    else
                                    {
                                        // ルートのノートの場合
                                        imgPath = $"{noteName}/img";
                                    }
                                    
                                    await _blobStorageService.UploadImageBinaryAsync(uid, fileName, imgBytes, imgPath);
                                    Debug.WriteLine($"画像ファイルをアップロード: {fileName}");

                                    // 一時フォルダにもコピー
                                    var tempImgPath = Path.Combine(tempImgDir, fileName);
                                    await File.WriteAllBytesAsync(tempImgPath, imgBytes);
                                    Debug.WriteLine($"画像ファイルを一時フォルダにコピー: {tempImgPath}");
                                }
                                else
                                {
                                    Debug.WriteLine($"画像ファイル名の形式が正しくありません: {fileName}");
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"画像ファイルの処理中にエラー: {imgFile}, エラー: {ex.Message}");
                                Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                            }
                        }
                    }

                    // サーバーの画像ファイルを取得
                    var serverImgFiles = await _blobStorageService.GetImageFilesAsync(uid, $"{subFolder}/{noteName}/img");
                    Debug.WriteLine($"サーバーの画像ファイル数: {serverImgFiles.Count}");
                    Debug.WriteLine($"サーバーの画像ファイル一覧:");
                    foreach (var imgFile in serverImgFiles)
                    {
                        Debug.WriteLine($"- {imgFile}");
                    }

                    foreach (var imgFile in serverImgFiles)
                    {
                        // iOS版の形式（img_########_######.jpg）をチェック
                        if (Regex.IsMatch(imgFile, @"^img_\d{8}_\d{6}\.jpg$"))
                        {
                            Debug.WriteLine($"画像ファイルの処理開始: {imgFile}");
                            var imgBytes = await _blobStorageService.GetImageBinaryAsync(uid, imgFile, $"{subFolder}/{noteName}/img");
                            if (imgBytes != null)
                            {
                                try
                                {
                                    var tempImgPath = Path.Combine(tempImgDir, imgFile);
                                    Debug.WriteLine($"画像ファイルの保存先: {tempImgPath}");
                                    Debug.WriteLine($"画像ファイルのサイズ: {imgBytes.Length} バイト");
                                    await File.WriteAllBytesAsync(tempImgPath, imgBytes);
                                    Debug.WriteLine($"画像ファイルを一時フォルダにダウンロード: {tempImgPath}");
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"画像ファイルのダウンロード中にエラー: {imgFile}, エラー: {ex.Message}");
                                    Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                                }
                            }
                            else
                            {
                                Debug.WriteLine($"画像ファイルのコンテンツが取得できません: {imgFile}");
                            }
                        }
                    }
                    Debug.WriteLine($"=== 画像同期処理完了（ローカルにcards.txt存在時） ===");

                    // カードのアップロード
                    if (cardsToUpload.Any())
                    {
                        Debug.WriteLine($"アップロードするカード数: {cardsToUpload.Count}");
                        foreach (var card in cardsToUpload)
                        {
                            // ローカルのcardsフォルダからJSONファイルを読み込む
                            var localCardPath = Path.Combine(_tempBasePath, subFolder ?? "", noteName + "_temp", "cards", $"{card.Uuid}.json");
                            Debug.WriteLine($"カードファイルのパス: {localCardPath}");
                            
                            if (File.Exists(localCardPath))
                            {
                                var cardContent = await File.ReadAllTextAsync(localCardPath);
                                
                                // カードのJSONファイルを直接アップロード
                                string cardPath;
                                if (!string.IsNullOrEmpty(subFolder))
                                {
                                    // サブフォルダ内のノートの場合
                                    cardPath = $"{subFolder}/{noteName}/cards";
                                }
                                else
                                {
                                    // ルートのノートの場合
                                    cardPath = $"{noteName}/cards";
                                }
                                
                                await _blobStorageService.SaveNoteAsync(uid, $"{card.Uuid}.json", cardContent, cardPath);
                                Debug.WriteLine($"カードファイルをアップロード: {localCardPath}");
                            }
                            else
                            {
                                Debug.WriteLine($"カードファイルが見つかりません: {localCardPath}");
                            }
                        }

                        // ローカルのcards.txtをサーバーにアップロード
                        var newContent = string.Join("\n", updatedLocalCards.Select(c => $"{c.Uuid},{c.LastModified:yyyy-MM-dd HH:mm:ss}"));
                        var contentWithCount = $"{updatedLocalCards.Count}\n{newContent}";
                        await _blobStorageService.SaveNoteAsync(uid, noteName, contentWithCount, subFolder);
                        Debug.WriteLine($"サーバーにcards.txtをアップロード（カードのアップロード時）");
                    }

                    // ローカルのcards.txtを更新（ダウンロードしたカードがある場合）
                    if (cardsToDownload.Any())
                    {
                        var newContent = string.Join("\n", updatedLocalCards.Select(c => $"{c.Uuid},{c.LastModified:yyyy-MM-dd HH:mm:ss}"));
                        var contentWithCount = $"{updatedLocalCards.Count}\n{newContent}";
                        var tempCardsPath = Path.Combine(tempDir, "cards.txt");
                        await File.WriteAllTextAsync(tempCardsPath, contentWithCount);
                        Debug.WriteLine($"一時フォルダにcards.txtを保存: {tempCardsPath}");

                        // サーバーにアップロード
                        await _blobStorageService.SaveNoteAsync(uid, noteName, contentWithCount, subFolder);
                        Debug.WriteLine($"更新されたcards.txtをサーバーにアップロード: {noteName}");
                    }
                    else if (!cardsToUpload.Any())
                    {
                        Debug.WriteLine($"ローカルとサーバーの内容が同じため、cards.txtのアップロードは不要です");
                    }

                    // 更新されたcards.txtを保存
                    var updatedCardsContent = $"{updatedLocalCards.Count}\n{string.Join("\n", updatedLocalCards.Select(c => $"{c.Uuid},{c.LastModified:yyyy-MM-dd HH:mm:ss}"))}";
                    await File.WriteAllTextAsync(localCardsPath, updatedCardsContent);
                    Debug.WriteLine($"更新されたcards.txtを保存: {localCardsPath}");

                    // 画像ファイルの同期（変更がある場合のみ）
                    var imgDir = Path.Combine(tempDir, "img");
                    if (!Directory.Exists(imgDir))
                    {
                        Directory.CreateDirectory(imgDir);
                        Debug.WriteLine($"画像ディレクトリを作成: {imgDir}");
                    }

                    string imgSyncPath;
                    if (!string.IsNullOrEmpty(subFolder))
                    {
                        // サブフォルダ内のノートの場合
                        imgSyncPath = $"{subFolder}/{noteName}/img";
                    }
                    else
                    {
                        // ルートのノートの場合
                        imgSyncPath = $"{noteName}/img";
                    }
                    
                    var imgFiles = await _blobStorageService.GetImageFilesAsync(uid, imgSyncPath);
                                            foreach (var imgFile in imgFiles)
                        {
                            if (Regex.IsMatch(imgFile, @"^img_\d{8}_\d{6}\.jpg$"))
                            {
                                var imgPath = Path.Combine(imgDir, imgFile);
                                if (!File.Exists(imgPath))
                                {
                                    var imgBytes = await _blobStorageService.GetImageBinaryAsync(uid, imgFile, imgSyncPath);
                                    if (imgBytes != null)
                                    {
                                        try
                                        {
                                            await File.WriteAllBytesAsync(imgPath, imgBytes);
                                            Debug.WriteLine($"画像ファイルをダウンロード: {imgFile}");
                                        }
                                        catch (Exception ex)
                                        {
                                            Debug.WriteLine($"画像ファイル同期中にエラー: {imgFile}, エラー: {ex.Message}");
                                        }
                                    }
                                }
                            }
                        }

                    // .ankplsファイルを更新
                    var localBasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Flashnote");
                    string ankplsPath;
                    if (!string.IsNullOrEmpty(subFolder))
                    {
                        // サブフォルダ内のノートの場合
                        var subFolderPath = Path.Combine(localBasePath, subFolder);
                        if (!Directory.Exists(subFolderPath))
                        {
                            Directory.CreateDirectory(subFolderPath);
                        }
                        ankplsPath = Path.Combine(subFolderPath, $"{noteName}.ankpls");
                    }
                    else
                    {
                        // ルートのノートの場合
                        ankplsPath = Path.Combine(localBasePath, $"{noteName}.ankpls");
                    }
                    
                    if (File.Exists(ankplsPath))
                    {
                        File.Delete(ankplsPath);
                        Debug.WriteLine($"既存の.ankplsファイルを削除: {ankplsPath}");
                    }
                    
                    System.IO.Compression.ZipFile.CreateFromDirectory(tempDir, ankplsPath);
                    Debug.WriteLine($".ankplsファイルを更新: {ankplsPath}");

                    Debug.WriteLine($"=== ローカルにcards.txtが存在する場合の処理完了 ===");
                }

                Debug.WriteLine($"ノート '{noteName}' の同期が完了しました。");
                Debug.WriteLine($"=== ノート同期完了 ===");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ノートの同期中にエラー: {ex.Message}");
                throw;
            }
        }

        public async Task SyncAllNotesAsync(string uid)
        {
            try
            {
                Debug.WriteLine($"=== 全ノート同期開始 ===");
                Debug.WriteLine($"同期開始 - UID: {uid}");

                // 通常のノートを同期
                await SyncLocalNotesAsync(uid);

                // 共有ノートを同期
                await SyncSharedNotesAsync(uid);

                Debug.WriteLine($"=== 全ノート同期完了 ===");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"全ノート同期中にエラー: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 共有ノートを同期する
        /// </summary>
        public async Task SyncSharedNotesAsync(string uid)
        {
            try
            {
                Debug.WriteLine($"=== 共有ノート同期開始 ===");
                
                // SharedKeyServiceを取得
                var sharedNotes = _sharedKeyService.GetSharedNotes();
                
                Debug.WriteLine($"共有ノート数: {sharedNotes.Count}");
                
                foreach (var sharedNote in sharedNotes)
                {
                    var noteName = sharedNote.Key;
                    var sharedInfo = sharedNote.Value;
                    
                    Debug.WriteLine($"共有ノート同期開始: {noteName}");
                    Debug.WriteLine($"  元ユーザーID: {sharedInfo.OriginalUserId}");
                    Debug.WriteLine($"  ノートパス: {sharedInfo.NotePath}");
                    Debug.WriteLine($"  フォルダ: {sharedInfo.IsFolder}");
                    
                    try
                    {
                        if (sharedInfo.IsFolder)
                        {
                            // フォルダの場合
                            await SyncSharedFolderAsync(sharedInfo.OriginalUserId, sharedInfo.NotePath, sharedInfo.ShareKey);
                        }
                        else
                        {
                            // 単一ノートの場合
                            await SyncSharedNoteAsync(sharedInfo.OriginalUserId, sharedInfo.NotePath, noteName, null);
                        }
                        
                        Debug.WriteLine($"共有ノート同期完了: {noteName}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"共有ノート同期中にエラー: {noteName}, エラー: {ex.Message}");
                        // 個別のノートでエラーが発生しても他のノートの同期は続行
                    }
                }
                
                Debug.WriteLine($"=== 共有ノート同期完了 ===");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"共有ノート同期中にエラー: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 共有ノートを同期する（単一ノート）
        /// </summary>
        public async Task SyncSharedNoteAsync(string originalUserId, string notePath, string noteName, string subFolder = null)
        {
            try
            {
                Debug.WriteLine($"共有ノート同期開始: {noteName} (パス: {notePath})");
                
                // サーバーから共有ノートの内容を取得
                var serverContent = await _blobStorageService.GetSharedNoteFileAsync(originalUserId, notePath, "cards.txt");
                if (serverContent == null)
                {
                    Debug.WriteLine($"サーバーに共有ノートが存在しません: {notePath}");
                    return;
                }
                
                // ローカルの一時フォルダパスを構築
                string tempDir;
                if (!string.IsNullOrEmpty(subFolder))
                {
                    // サブフォルダ内のノートの場合
                    tempDir = Path.Combine(_tempBasePath, subFolder, $"{noteName}_temp");
                }
                else
                {
                    // ルートのノートの場合
                    tempDir = Path.Combine(_tempBasePath, $"{noteName}_temp");
                }
                var localCardsPath = Path.Combine(tempDir, "cards.txt");
                
                // ローカルのcards.txtを読み込み
                string localContent = string.Empty;
                if (File.Exists(localCardsPath))
                {
                    localContent = await File.ReadAllTextAsync(localCardsPath);
                    Debug.WriteLine($"ローカルのcards.txtを読み込み: {localContent.Length} 文字");
                }

                // 一時ディレクトリの準備
                if (!Directory.Exists(tempDir))
                {
                    Directory.CreateDirectory(tempDir);
                    Debug.WriteLine($"一時ディレクトリを作成: {tempDir}");
                }

                // ローカルにcards.txtがない場合、サーバーから全てダウンロード
                if (string.IsNullOrEmpty(localContent) && serverContent != null)
                {
                    Debug.WriteLine($"ローカルにcards.txtがないため、共有ノート全体をダウンロードします");
                    var serverCardsToDownload = await ParseCardsFile(serverContent);
                    
                    // サーバーのcards.txtをそのまま保存
                    await File.WriteAllTextAsync(localCardsPath, serverContent);
                    Debug.WriteLine($"一時フォルダにcards.txtを保存: {localCardsPath}");

                    // カードファイルをダウンロード
                    foreach (var card in serverCardsToDownload)
                    {
                        string cardPath;
                        if (!string.IsNullOrEmpty(subFolder))
                        {
                            // サブフォルダ内のノートの場合
                            cardPath = $"{subFolder}/{noteName}/cards";
                        }
                        else
                        {
                            // ルートのノートの場合
                            cardPath = $"{noteName}/cards";
                        }
                        
                        var cardContent = await _blobStorageService.GetNoteContentAsync(originalUserId, $"{card.Uuid}.json", cardPath);
                        if (cardContent != null)
                        {
                            var tempCardPath = Path.Combine(tempDir, "cards", $"{card.Uuid}.json");
                            var tempCardDir = Path.GetDirectoryName(tempCardPath);
                            if (!Directory.Exists(tempCardDir))
                            {
                                Directory.CreateDirectory(tempCardDir);
                                Debug.WriteLine($"一時カードディレクトリを作成: {tempCardDir}");
                            }
                            await File.WriteAllTextAsync(tempCardPath, cardContent);
                            Debug.WriteLine($"カードファイルを一時フォルダにダウンロード: {tempCardPath}");
                        }
                    }

                    // 画像ファイルをダウンロード
                    var imgDir = Path.Combine(tempDir, "img");
                    if (!Directory.Exists(imgDir))
                    {
                        Directory.CreateDirectory(imgDir);
                        Debug.WriteLine($"画像ディレクトリを作成: {imgDir}");
                    }

                    var imgFiles = await _blobStorageService.GetSharedNoteListAsync(originalUserId, $"{notePath}/img");
                    foreach (var imgFile in imgFiles)
                    {
                        if (Regex.IsMatch(imgFile, @"^img_\d{8}_\d{6}\.jpg$"))
                        {
                            var imgContent = await _blobStorageService.GetSharedNoteFileAsync(originalUserId, $"{notePath}/img", imgFile);
                            if (imgContent != null)
                            {
                                try
                                {
                                    var imgBytes = Convert.FromBase64String(imgContent);
                                    var imgPath = Path.Combine(imgDir, imgFile);
                                    await File.WriteAllBytesAsync(imgPath, imgBytes);
                                    Debug.WriteLine($"画像ファイルをダウンロード: {imgFile}");
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"画像ファイル同期中にエラー: {imgFile}, エラー: {ex.Message}");
                                }
                            }
                        }
                    }

                    // .ankplsファイルを作成
                    var localBasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Flashnote");
                    string ankplsPath;
                    if (!string.IsNullOrEmpty(subFolder))
                    {
                        // サブフォルダ内のノートの場合
                        var subFolderPath = Path.Combine(localBasePath, subFolder);
                        if (!Directory.Exists(subFolderPath))
                        {
                            Directory.CreateDirectory(subFolderPath);
                        }
                        ankplsPath = Path.Combine(subFolderPath, $"{noteName}.ankpls");
                    }
                    else
                    {
                        // ルートのノートの場合
                        ankplsPath = Path.Combine(localBasePath, $"{noteName}.ankpls");
                    }
                    
                    if (File.Exists(ankplsPath))
                    {
                        File.Delete(ankplsPath);
                        Debug.WriteLine($"既存の.ankplsファイルを削除: {ankplsPath}");
                    }
                    
                    System.IO.Compression.ZipFile.CreateFromDirectory(tempDir, ankplsPath);
                    Debug.WriteLine($".ankplsファイルを作成: {ankplsPath}");
                    
                    Debug.WriteLine($"共有ノート '{noteName}' の全体ダウンロードが完了しました。");
                    return;
                }

                // ローカルにcards.txtがある場合の双方向同期
                if (!string.IsNullOrEmpty(localContent))
                {
                    Debug.WriteLine($"=== ローカルにcards.txtが存在する場合の双方向同期処理開始 ===");
                    var localCards = await ParseCardsFile(localContent);
                    var serverCards = await ParseCardsFile(serverContent);

                    Debug.WriteLine($"ローカルのカード数: {localCards.Count}");
                    Debug.WriteLine($"サーバーのカード数: {serverCards.Count}");

                    // サーバーのcards.txtを一時保存
                    var serverCardsPath = Path.Combine(tempDir, "server_cards.txt");
                    await File.WriteAllTextAsync(serverCardsPath, serverContent);
                    Debug.WriteLine($"サーバーのcards.txtを一時保存: {serverCardsPath}");

                    var cardsToDownload = new List<CardInfo>();
                    var cardsToUpload = new List<CardInfo>();
                    var updatedLocalCards = localCards.ToList();

                    Debug.WriteLine($"ローカルのカード情報:");
                    foreach (var card in localCards)
                    {
                        Debug.WriteLine($"UUID={card.Uuid}, 最終更新={card.LastModified}");
                    }

                    Debug.WriteLine($"サーバーのカード情報:");
                    foreach (var card in serverCards)
                    {
                        Debug.WriteLine($"UUID={card.Uuid}, 最終更新={card.LastModified}");
                    }

                    // サーバーにあるがローカルにない、または更新が必要なカードを特定
                    foreach (var serverCard in serverCards)
                    {
                        var localCard = localCards.FirstOrDefault(c => c.Uuid == serverCard.Uuid);
                        if (localCard == null || localCard.LastModified < serverCard.LastModified)
                        {
                            cardsToDownload.Add(serverCard);
                            Debug.WriteLine($"ダウンロード対象カード: {serverCard.Uuid} (ローカル={localCard?.LastModified}, サーバー={serverCard.LastModified})");
                            
                            // ローカルのリストを更新
                            if (localCard != null)
                            {
                                updatedLocalCards.Remove(localCard);
                            }
                            updatedLocalCards.Add(serverCard);
                        }
                        else if (localCard.LastModified > serverCard.LastModified)
                        {
                            // ローカルが新しい場合、アップロード対象に追加
                            cardsToUpload.Add(localCard);
                            Debug.WriteLine($"ローカルが新しいためアップロード対象に追加: {serverCard.Uuid} (ローカル={localCard.LastModified}, サーバー={serverCard.LastModified})");
                        }
                        else
                        {
                            Debug.WriteLine($"カードは最新: {serverCard.Uuid} (最終更新={serverCard.LastModified})");
                        }
                    }

                    // ローカルにあるがサーバーにないカードを特定
                    foreach (var localCard in localCards)
                    {
                        if (!serverCards.Any(c => c.Uuid == localCard.Uuid))
                        {
                            cardsToUpload.Add(localCard);
                            Debug.WriteLine($"新規アップロード対象カード: {localCard.Uuid}");
                            // 新規カードはupdatedLocalCardsに既に含まれているので追加の処理は不要
                        }
                    }

                    // 削除されたカードを検出（サーバーにあるがローカルにないカード）
                    var deletedCards = new List<CardInfo>();
                    foreach (var serverCard in serverCards)
                    {
                        if (!localCards.Any(c => c.Uuid == serverCard.Uuid))
                        {
                            deletedCards.Add(serverCard);
                            Debug.WriteLine($"削除されたカード: {serverCard.Uuid}");
                        }
                    }

                    // 変更がある場合のみ同期を実行
                    if (cardsToDownload.Any() || cardsToUpload.Any() || deletedCards.Any())
                    {
                        Debug.WriteLine($"共有ノートの内容が変更されています: {noteName}");
                        Debug.WriteLine($"ダウンロード対象カード数: {cardsToDownload.Count}");
                        Debug.WriteLine($"アップロード対象カード数: {cardsToUpload.Count}");
                        Debug.WriteLine($"削除対象カード数: {deletedCards.Count}");
                        
                        // カードディレクトリを準備
                        var cardsDir = Path.Combine(tempDir, "cards");
                        if (!Directory.Exists(cardsDir))
                        {
                            Directory.CreateDirectory(cardsDir);
                            Debug.WriteLine($"カードディレクトリを作成: {cardsDir}");
                        }

                        // 削除されたカードのファイルを削除
                        foreach (var deletedCard in deletedCards)
                        {
                            var cardPath = Path.Combine(cardsDir, $"{deletedCard.Uuid}.json");
                            if (File.Exists(cardPath))
                            {
                                File.Delete(cardPath);
                                Debug.WriteLine($"削除されたカードファイルを削除: {deletedCard.Uuid}");
                            }
                        }

                        // 更新されたカードをダウンロード
                        foreach (var card in cardsToDownload)
                        {
                            var cardContent = await _blobStorageService.GetSharedNoteFileAsync(originalUserId, $"{notePath}/cards", $"{card.Uuid}.json");
                            if (cardContent != null)
                            {
                                var cardPath = Path.Combine(cardsDir, $"{card.Uuid}.json");
                                await File.WriteAllTextAsync(cardPath, cardContent);
                                Debug.WriteLine($"カードファイルを更新: {card.Uuid}");
                            }
                            else
                            {
                                Debug.WriteLine($"カードファイルが見つかりません: {card.Uuid}");
                            }
                        }

                        // ローカルの変更をサーバーにアップロード
                        foreach (var card in cardsToUpload)
                        {
                            var cardPath = Path.Combine(cardsDir, $"{card.Uuid}.json");
                            if (File.Exists(cardPath))
                            {
                                var cardContent = await File.ReadAllTextAsync(cardPath);
                                // パス区切り文字を正規化
                                var normalizedCardPath = notePath.Replace("\\", "/");
                                await _blobStorageService.SaveSharedNoteFileAsync(originalUserId, $"{normalizedCardPath}/cards", $"{card.Uuid}.json", cardContent);
                                Debug.WriteLine($"共有ノートのカードファイルをアップロード: {card.Uuid}");
                            }
                            else
                            {
                                Debug.WriteLine($"アップロード対象のカードファイルが見つかりません: {card.Uuid}");
                            }
                        }

                        // ローカルの画像ファイルをサーバーにアップロード
                        Debug.WriteLine($"=== ローカル画像ファイルのアップロード処理開始 ===");
                        var localImgDir = Path.Combine(tempDir, "img");
                        if (Directory.Exists(localImgDir))
                        {
                            var localImgFiles = Directory.GetFiles(localImgDir, "img_*.jpg");
                            Debug.WriteLine($"ローカルの画像ファイル数: {localImgFiles.Length}");
                            
                            foreach (var imgFile in localImgFiles)
                            {
                                try
                                {
                                    var fileName = Path.GetFileName(imgFile);
                                    // iOS版の形式（img_########_######.jpg）をチェック
                                    if (Regex.IsMatch(fileName, @"^img_\d{8}_\d{6}\.jpg$"))
                                    {
                                        Debug.WriteLine($"画像ファイルの処理開始: {fileName}");
                                        var imgBytes = await File.ReadAllBytesAsync(imgFile);
                                        Debug.WriteLine($"画像ファイルのサイズ: {imgBytes.Length} バイト");
                                        
                                        // Base64エンコードしてアップロード
                                        var base64Content = Convert.ToBase64String(imgBytes);
                                        // パス区切り文字を正規化してUIDの重複を防ぐ
                                        var normalizedPath = notePath.Replace("\\", "/");
                                        await _blobStorageService.SaveSharedNoteFileAsync(originalUserId, $"{normalizedPath}/img", fileName, base64Content);
                                        Debug.WriteLine($"共有ノートの画像ファイルをアップロード: {fileName}");
                                    }
                                    else
                                    {
                                        Debug.WriteLine($"画像ファイル名の形式が正しくありません: {fileName}");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"画像ファイルのアップロード中にエラー: {imgFile}, エラー: {ex.Message}");
                                    Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                                }
                            }
                        }
                        else
                        {
                            Debug.WriteLine($"ローカル画像ディレクトリが見つかりません: {localImgDir}");
                        }
                        Debug.WriteLine($"=== ローカル画像ファイルのアップロード処理完了 ===");

                        // 更新されたcards.txtを保存
                        var updatedCardsContent = $"{updatedLocalCards.Count}\n{string.Join("\n", updatedLocalCards.Select(c => $"{c.Uuid},{c.LastModified:yyyy-MM-dd HH:mm:ss}"))}";
                        await File.WriteAllTextAsync(localCardsPath, updatedCardsContent);
                        Debug.WriteLine($"更新されたcards.txtを保存: {localCardsPath}");

                        // 更新されたcards.txtをサーバーにアップロード（パス区切り文字を正規化してUIDの重複を防ぐ）
                        var normalizedNotePath = notePath.Replace("\\", "/");
                        await _blobStorageService.SaveSharedNoteFileAsync(originalUserId, normalizedNotePath, "cards.txt", updatedCardsContent);
                        Debug.WriteLine($"更新されたcards.txtをサーバーにアップロード: {normalizedNotePath}/cards.txt");

                        // 画像ファイルの同期（変更がある場合のみ）
                        var imgDir = Path.Combine(tempDir, "img");
                        if (!Directory.Exists(imgDir))
                        {
                            Directory.CreateDirectory(imgDir);
                            Debug.WriteLine($"画像ディレクトリを作成: {imgDir}");
                        }

                        var imgFiles = await _blobStorageService.GetSharedNoteListAsync(originalUserId, $"{notePath}/img");
                        foreach (var imgFile in imgFiles)
                        {
                            if (Regex.IsMatch(imgFile, @"^img_\d{8}_\d{6}\.jpg$"))
                            {
                                var imgPath = Path.Combine(imgDir, imgFile);
                                if (!File.Exists(imgPath))
                                {
                                    var imgContent = await _blobStorageService.GetSharedNoteFileAsync(originalUserId, $"{notePath}/img", imgFile);
                                    if (imgContent != null)
                                    {
                                        try
                                        {
                                            var imgBytes = Convert.FromBase64String(imgContent);
                                            await File.WriteAllBytesAsync(imgPath, imgBytes);
                                            Debug.WriteLine($"画像ファイルをダウンロード: {imgFile}");
                                        }
                                        catch (Exception ex)
                                        {
                                            Debug.WriteLine($"画像ファイル同期中にエラー: {imgFile}, エラー: {ex.Message}");
                                        }
                                    }
                                }
                            }
                        }

                        // .ankplsファイルを更新
                        var localBasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Flashnote");
                        string ankplsPath;
                        if (!string.IsNullOrEmpty(subFolder))
                        {
                            var subFolderPath = Path.Combine(localBasePath, subFolder);
                            if (!Directory.Exists(subFolderPath))
                            {
                                Directory.CreateDirectory(subFolderPath);
                            }
                            ankplsPath = Path.Combine(subFolderPath, $"{noteName}.ankpls");
                        }
                        else
                        {
                            ankplsPath = Path.Combine(localBasePath, $"{noteName}.ankpls");
                        }             
                                   
                        if (File.Exists(ankplsPath))
                        {
                            File.Delete(ankplsPath);
                            Debug.WriteLine($"既存の.ankplsファイルを削除: {ankplsPath}");
                        }
                        
                        System.IO.Compression.ZipFile.CreateFromDirectory(tempDir, ankplsPath);
                        Debug.WriteLine($".ankplsファイルを更新: {ankplsPath}");
                    }
                    else
                    {
                        Debug.WriteLine($"共有ノートは最新です: {noteName}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"共有ノート同期中にエラー: {noteName}, エラー: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 共有フォルダを同期する
        /// </summary>
        public async Task SyncSharedFolderAsync(string originalUserId, string folderPath, string shareKey)
        {
            try
            {
                Debug.WriteLine($"共有フォルダ同期開始: {folderPath}");
                
                // 共有フォルダの内容を取得
                var (isActuallyFolder, downloadedNotes) = await _blobStorageService.DownloadSharedFolderAsync(originalUserId, folderPath, shareKey);
                
                Debug.WriteLine($"共有フォルダ同期結果: 実際にフォルダ={isActuallyFolder}, ノート数={downloadedNotes.Count}");
                
                // 各ノートを同期
                foreach (var (noteName, subFolder, fullNotePath) in downloadedNotes)
                {
                    try
                    {
                        await SyncSharedNoteAsync(originalUserId, fullNotePath, noteName, subFolder);
                        Debug.WriteLine($"共有フォルダ内のノート同期完了: {noteName}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"共有フォルダ内のノート同期中にエラー: {noteName}, エラー: {ex.Message}");
                        // 個別のノートでエラーが発生しても他のノートの同期は続行
                    }
                }
                
                Debug.WriteLine($"共有フォルダ同期完了: {folderPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"共有フォルダ同期中にエラー: {folderPath}, エラー: {ex.Message}");
                throw;
            }
        }

        public async Task SyncLocalNotesAsync(string uid)
        {
            try
            {
                Debug.WriteLine($"=== ローカルノートの同期開始 ===");

                // 共有ノートの一覧を取得（除外用）
                var sharedNotes = _sharedKeyService.GetSharedNotes();
                var sharedNoteNames = sharedNotes.Keys.ToHashSet();
                Debug.WriteLine($"共有ノート一覧（除外対象）: {string.Join(", ", sharedNoteNames)}");

                // 1. サーバーとローカル両方にあるノートの同期
                Debug.WriteLine("1. 両方にあるノートの同期開始");
                
                // サーバーのノート一覧を取得（ルートとサブフォルダを含む）
                var allServerNotes = new List<(string noteName, string subFolder)>();
                
                // ルートのノートを取得
                var rootServerNotes = await _blobStorageService.GetNoteListAsync(uid);
                foreach (var serverNote in rootServerNotes)
                {
                    if (!sharedNoteNames.Contains(serverNote))
                    {
                        allServerNotes.Add((serverNote, null));
                        Debug.WriteLine($"ルートのサーバーノートを追加: {serverNote}");
                    }
                }
                
                // サブフォルダのノートを取得
                var subFolders = await _blobStorageService.GetSubFoldersAsync(uid);
                Debug.WriteLine($"取得したサブフォルダ: {string.Join(", ", subFolders)}");
                
                foreach (var subFolder in subFolders)
                {
                    var subFolderNotes = await _blobStorageService.GetNoteListAsync(uid, subFolder);
                    foreach (var noteName in subFolderNotes)
                    {
                        var fullNotePath = $"{subFolder}/{noteName}";
                        if (!sharedNoteNames.Contains(noteName) && !sharedNoteNames.Contains(fullNotePath))
                        {
                            allServerNotes.Add((noteName, subFolder));
                            Debug.WriteLine($"サブフォルダのサーバーノートを追加: {subFolder}/{noteName}");
                        }
                    }
                }
                
                Debug.WriteLine($"全サーバーノート数: {allServerNotes.Count}");
                foreach (var (noteName, subFolder) in allServerNotes)
                {
                    Debug.WriteLine($"  - {noteName} (サブフォルダ: {subFolder ?? "ルート"})");
                }
                
                var localNotes = new List<(string subFolder, string noteName, string cardsPath)>();
                var tempNotes = new List<(string subFolder, string noteName, string cardsPath)>();

                // ローカルノートの収集
                var localBasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Flashnote");
                var tempBasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Flashnote");

                Debug.WriteLine($"ローカルベースパス: {localBasePath}");
                Debug.WriteLine($"tempベースパス: {tempBasePath}");

                // ローカルのノートを収集（共有ノートは除外）
                if (Directory.Exists(localBasePath))
                {
                    Debug.WriteLine($"ローカルフォルダが存在します: {localBasePath}");
                    foreach (var subFolder in Directory.GetDirectories(localBasePath))
                    {
                        var subFolderName = Path.GetFileName(subFolder);
                        Debug.WriteLine($"ローカルサブフォルダを検索中: {subFolderName}");
                        foreach (var noteFolder in Directory.GetDirectories(subFolder))
                        {
                            var noteName = Path.GetFileName(noteFolder);
                            
                            // 共有ノートは除外（フォルダ内のノートも含めてチェック）
                            var fullNotePath = $"{subFolderName}/{noteName}";
                            if (sharedNoteNames.Contains(noteName) || sharedNoteNames.Contains(fullNotePath) || _sharedKeyService.IsInSharedFolder(noteName, subFolderName))
                            {
                                Debug.WriteLine($"共有ノートを除外: {subFolderName}/{noteName}");
                                continue;
                            }
                            
                            var cardsPath = Path.Combine(noteFolder, "cards.txt");
                            if (File.Exists(cardsPath))
                            {
                                localNotes.Add((subFolderName, noteName, cardsPath));
                                Debug.WriteLine($"ローカルノートを追加: {subFolderName}/{noteName}");
                            }
                        }
                    }
                }
                else
                {
                    Debug.WriteLine($"ローカルフォルダが存在しません: {localBasePath}");
                }

                // tempフォルダのノートを収集（共有ノートは除外）
                if (Directory.Exists(tempBasePath))
                {
                    Debug.WriteLine($"tempフォルダが存在します: {tempBasePath}");
                    
                    // tempBasePath直下の_tempで終わるフォルダを検索
                    foreach (var tempFolder in Directory.GetDirectories(tempBasePath, "*_temp"))
                    {
                        var noteName = Path.GetFileName(tempFolder);
                        if (noteName.EndsWith("_temp"))
                        {
                            noteName = noteName.Substring(0, noteName.Length - 5);
                            
                            // 共有ノートは除外
                            if (sharedNoteNames.Contains(noteName))
                            {
                                Debug.WriteLine($"共有ノートを除外（temp）: {noteName}");
                                continue;
                            }
                            
                            var cardsPath = Path.Combine(tempFolder, "cards.txt");
                            if (File.Exists(cardsPath))
                            {
                                tempNotes.Add((null, noteName, cardsPath));
                                Debug.WriteLine($"tempノートを追加（ルート）: {noteName}");
                            }
                        }
                    }
                    
                    // サブフォルダ内の_tempで終わるフォルダを検索
                    foreach (var subFolder in Directory.GetDirectories(tempBasePath))
                    {
                        var subFolderName = Path.GetFileName(subFolder);
                        // _tempで終わるフォルダはスキップ（すでに上で処理済み）
                        if (subFolderName.EndsWith("_temp"))
                            continue;
                            
                        Debug.WriteLine($"tempサブフォルダを検索中: {subFolderName}");
                        foreach (var noteFolder in Directory.GetDirectories(subFolder))
                        {
                            var noteName = Path.GetFileName(noteFolder);
                            if (noteName.EndsWith("_temp"))
                            {
                                noteName = noteName.Substring(0, noteName.Length - 5);
                                
                                // 共有ノートは除外（フォルダ内のノートも含めてチェック）
                                var fullNotePath = $"{subFolderName}/{noteName}";
                                if (sharedNoteNames.Contains(noteName) || sharedNoteNames.Contains(fullNotePath) || _sharedKeyService.IsInSharedFolder(noteName, subFolderName))
                                {
                                    Debug.WriteLine($"共有ノートを除外（tempサブフォルダ）: {subFolderName}/{noteName}");
                                    continue;
                                }
                                
                                var cardsPath = Path.Combine(noteFolder, "cards.txt");
                                if (File.Exists(cardsPath))
                                {
                                    tempNotes.Add((subFolderName, noteName, cardsPath));
                                    Debug.WriteLine($"tempノートを追加: {subFolderName}/{noteName}");
                                }
                            }
                        }
                    }
                }
                else
                {
                    Debug.WriteLine($"tempフォルダが存在しません: {tempBasePath}");
                }

                Debug.WriteLine($"収集したローカルノート数: {localNotes.Count}");
                Debug.WriteLine($"収集したtempノート数: {tempNotes.Count}");
                Debug.WriteLine($"サーバーノート数: {allServerNotes.Count}");

                // 両方にあるノートの同期（共有ノートは除外）
                foreach (var (noteName, subFolder) in allServerNotes)
                {
                    // 共有ノートは除外
                    if (sharedNoteNames.Contains(noteName))
                    {
                        Debug.WriteLine($"サーバーの共有ノートを同期から除外: {noteName}");
                        continue;
                    }
                    
                    var localNote = localNotes.FirstOrDefault(n => n.noteName == noteName && n.subFolder == subFolder);
                    var tempNote = tempNotes.FirstOrDefault(n => n.noteName == noteName && n.subFolder == subFolder);

                    if (localNote.noteName != null || tempNote.noteName != null)
                    {
                        Debug.WriteLine($"両方にあるノートを同期: {noteName}");
                        await SyncNoteAsync(uid, noteName, subFolder);
                    }
                }

                // 2. サーバーにのみあるノートのダウンロード（共有ノートは除外）
                Debug.WriteLine("2. サーバーにのみあるノートのダウンロード開始");
                
                foreach (var (noteName, subFolder) in allServerNotes)
                {
                    // 共有ノートは除外
                    if (sharedNoteNames.Contains(noteName))
                    {
                        Debug.WriteLine($"サーバーの共有ノートをダウンロードから除外: {noteName}");
                        continue;
                    }
                    
                    // ローカルに存在するかチェック
                    var existsLocally = localNotes.Any(n => n.noteName == noteName && n.subFolder == subFolder) ||
                                       tempNotes.Any(n => n.noteName == noteName && n.subFolder == subFolder);
                    
                    if (!existsLocally)
                    {
                        Debug.WriteLine($"サーバーにのみあるノートをダウンロード: {noteName} (サブフォルダ: {subFolder ?? "ルート"})");
                        await SyncNoteAsync(uid, noteName, subFolder);
                    }
                }

                // 3. ローカルにのみあるノートのアップロード（共有ノートは除外）
                Debug.WriteLine("3. ローカルにのみあるノートのアップロード開始");
                var allLocalNotes = localNotes.Concat(tempNotes).DistinctBy(n => (n.noteName, n.subFolder));
                foreach (var localNote in allLocalNotes)
                {
                    // 共有ノートは除外（フォルダ内のノートも含めてチェック）
                    var fullNotePath = localNote.subFolder != null ? $"{localNote.subFolder}/{localNote.noteName}" : localNote.noteName;
                    if (sharedNoteNames.Contains(localNote.noteName) || sharedNoteNames.Contains(fullNotePath) || _sharedKeyService.IsInSharedFolder(localNote.noteName, localNote.subFolder))
                    {
                        Debug.WriteLine($"ローカルの共有ノートをアップロードから除外: {localNote.noteName}");
                        continue;
                    }
                    
                    // サーバーに存在するかチェック（サブフォルダも含めて）
                    var existsOnServer = allServerNotes.Any(n => n.Item1 == localNote.noteName && n.Item2 == localNote.subFolder);
                    
                    if (!existsOnServer)
                    {
                        Debug.WriteLine($"ローカルにのみあるノートをアップロード開始: {localNote.noteName}");
                        Debug.WriteLine($"ノートのパス: {localNote.cardsPath}");
                        Debug.WriteLine($"サブフォルダ: {localNote.subFolder ?? "なし"}");
                        
                        var localContent = await File.ReadAllTextAsync(localNote.cardsPath);
                        Debug.WriteLine($"cards.txtの内容: {localContent}");
                        
                        // cards.txtのパスからcardsディレクトリのパスを取得
                        var cardsDir = Path.Combine(Path.GetDirectoryName(localNote.cardsPath), "cards");
                        Debug.WriteLine($"カードディレクトリのパス: {cardsDir}");

                        if (Directory.Exists(cardsDir))
                        {
                            var jsonFiles = Directory.GetFiles(cardsDir, "*.json");
                            Debug.WriteLine($"見つかったJSONファイル数: {jsonFiles.Length}");
                            var cardCount = jsonFiles.Length;
                            var cardLines = new List<string>();

                            foreach (var jsonFile in jsonFiles)
                            {
                                try
                                {
                                    var jsonContent = await File.ReadAllTextAsync(jsonFile);
                                    var jsonFileName = Path.GetFileName(jsonFile);
                                    var uuid = Path.GetFileNameWithoutExtension(jsonFileName);
                                    var lastModified = File.GetLastWriteTime(jsonFile);
                                    cardLines.Add($"{uuid},{lastModified:yyyy-MM-dd HH:mm:ss}");
                                    Debug.WriteLine($"カード情報を準備: {uuid}, {lastModified:yyyy-MM-dd HH:mm:ss}");

                                    // サブフォルダのパスを正しく構築
                                    var uploadPath = localNote.subFolder != null 
                                        ? $"{localNote.subFolder}/{localNote.noteName}/cards"
                                        : $"{localNote.noteName}/cards";
                                    
                                    await _blobStorageService.SaveNoteAsync(uid, jsonFileName, jsonContent, uploadPath);
                                    Debug.WriteLine($"カードファイルをアップロード: {jsonFileName} -> {uploadPath}");
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"カードファイルのアップロード中にエラー: {jsonFile}, エラー: {ex.Message}");
                                    Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                                }
                            }

                            // 画像ファイルもアップロード
                            var imgDir = Path.Combine(Path.GetDirectoryName(localNote.cardsPath), "img");
                            Debug.WriteLine($"画像ディレクトリのパス: {imgDir}");
                            if (Directory.Exists(imgDir))
                            {
                                var imgFiles = Directory.GetFiles(imgDir, "img_*.jpg");
                                Debug.WriteLine($"見つかった画像ファイル数: {imgFiles.Length}");
                                
                                foreach (var imgFile in imgFiles)
                                {
                                    try
                                    {
                                        var imgFileName = Path.GetFileName(imgFile);
                                        var imgBytes = await File.ReadAllBytesAsync(imgFile);
                                        var uploadImgPath = localNote.subFolder != null 
                                            ? $"{localNote.subFolder}/{localNote.noteName}/img"
                                            : $"{localNote.noteName}/img";
                                        
                                        await _blobStorageService.UploadImageBinaryAsync(uid, imgFileName, imgBytes, uploadImgPath);
                                        Debug.WriteLine($"画像ファイルをアップロード: {imgFileName} -> {uploadImgPath}");
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.WriteLine($"画像ファイルのアップロード中にエラー: {imgFile}, エラー: {ex.Message}");
                                    }
                                }
                            }
                            else
                            {
                                Debug.WriteLine($"画像ディレクトリが見つかりません: {imgDir}");
                            }

                            // cards.txtの内容を正しい形式に変換
                            var formattedContent = $"{cardCount}\n{string.Join("\n", cardLines)}";
                            Debug.WriteLine($"アップロード用のcards.txt内容:");
                            Debug.WriteLine(formattedContent);
                            
                            await _blobStorageService.SaveNoteAsync(uid, localNote.noteName, formattedContent, localNote.subFolder);
                            Debug.WriteLine($"cards.txtをアップロード: {localNote.noteName} -> サブフォルダ: {localNote.subFolder ?? "ルート"}");
                        }
                        else
                        {
                            Debug.WriteLine($"カードディレクトリが見つかりません: {cardsDir}");
                        }
                        
                        Debug.WriteLine($"ローカルにのみあるノートのアップロード完了: {localNote.noteName}");
                    }
                    else
                    {
                        Debug.WriteLine($"ノート '{localNote.noteName}' はサーバーにも存在するため、アップロードをスキップ");
                    }
                }

                Debug.WriteLine($"=== ローカルノートの同期完了 ===");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ローカルノートの同期中にエラー: {ex.Message}");
                throw;
            }
        }
    }
} 