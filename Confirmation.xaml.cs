using Microsoft.Maui.Controls;
using System.Diagnostics;
using System.IO.Compression;
using Microsoft.Maui.Devices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flashnote.Services;
using Microsoft.Maui.Storage;
using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using Flashnote.Models;

#if WINDOWS
using Windows.Storage.Pickers;
using Windows.Storage;
using Microsoft.UI.Xaml;
using WinRT.Interop;
#endif

namespace Flashnote
{
    public partial class Confirmation : ContentPage
    {
        private string tempExtractPath; // 一時展開パス  
        private string ankplsFilePath;  // .ankplsファイルのパス  

        public Confirmation(string note)
        {
            try
            {
                Debug.WriteLine("Confirmation コンストラクタ開始");
            InitializeComponent();
                Debug.WriteLine("Confirmation InitializeComponent完了");
                
                // StatusIndicatorを遅延初期化
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(500); // 500ms遅延
                        await MainThread.InvokeOnMainThreadAsync(() =>
                        {
                            try
                            {
                                StatusIndicator?.RefreshStatus();
                                Debug.WriteLine("StatusIndicator 遅延初期化完了");
                            }
                            catch (Exception statusEx)
                            {
                                Debug.WriteLine($"StatusIndicator 遅延初期化でエラー: {statusEx.Message}");
                            }
                        });
                    }
                    catch (Exception delayEx)
                    {
                        Debug.WriteLine($"StatusIndicator 遅延処理でエラー: {delayEx.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Confirmation コンストラクタでエラー: {ex.Message}");
                Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                throw; // Confirmationの基本初期化エラーは再スロー
            }

            // パス設定  
            string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            ankplsFilePath = note;

            // フォルダ構造を維持した一時ディレクトリのパスを生成  
            string relativePath = Path.GetRelativePath(Path.Combine(documentsPath, "Flashnote"), Path.GetDirectoryName(ankplsFilePath));
            tempExtractPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Flashnote",
                relativePath,
                $"{Path.GetFileNameWithoutExtension(ankplsFilePath)}_temp"
            );

            // 一時ディレクトリが存在しない場合は作成  
            if (!Directory.Exists(tempExtractPath))
            {
                Directory.CreateDirectory(tempExtractPath);
            }

            // モバイルデバイスの場合は「ノートモードへ」ボタンを非表示
            if (DeviceInfo.Platform == DevicePlatform.Android || DeviceInfo.Platform == DevicePlatform.iOS)
            {
                ToNoteButton.IsVisible = false;
            }

            LoadNote();
        }
        // データロード  
        private void LoadNote()
        {
            if (File.Exists(ankplsFilePath))
            {
                Debug.WriteLine($"LoadNote: ankplsFilePathが存在します: {ankplsFilePath}");

                // Ensure metadata.json from the .ankpls is extracted to tempExtractPath so we can read originalName/ id
                try
                {
                    using var archive = ZipFile.OpenRead(ankplsFilePath);
                    var metaEntry = archive.Entries.FirstOrDefault(e => e.Name.Equals("metadata.json", StringComparison.OrdinalIgnoreCase));
                    if (metaEntry != null)
                    {
                        var destMetaPath = Path.Combine(tempExtractPath, "metadata.json");
                        using var s = metaEntry.Open();
                        using var sr = new StreamReader(s);
                        var metaJson = sr.ReadToEnd();
                        try
                        {
                            File.WriteAllText(destMetaPath, metaJson);
                            Debug.WriteLine($"Extracted metadata.json to: {destMetaPath}");
                        }
                        catch (Exception exw)
                        {
                            Debug.WriteLine($"Failed to write extracted metadata: {exw.Message}");
                        }
                    }
                }
                catch (Exception exExtract)
                {
                    Debug.WriteLine($"metadata extraction error: {exExtract.Message}");
                }

                // Extract UUID from metadata or file structure (if available)
                string uuid = ExtractUuidFromMetadata();
                Debug.WriteLine($"LoadNote: 抽出されたUUID: {uuid}");

                // Determine the correct path for cards.txt
                string cardsPath = GetCorrectPath(tempExtractPath, "cards.txt", uuid);

                if (cardsPath != null)
                {
                    Debug.WriteLine($"LoadNote: cards.txtのパスが見つかりました: {cardsPath}");

                    // Read and compare cards.txt
                    var existingCards = new HashSet<string>();
                    var cardsDir = Path.Combine(tempExtractPath, "cards");

                    if (!Directory.Exists(cardsDir))
                    {
                        Directory.CreateDirectory(cardsDir);
                        Debug.WriteLine($"cardsディレクトリを作成しました: {cardsDir}");
                    }

                    foreach (var line in File.ReadLines(cardsPath)
                    .Where(line => !string.IsNullOrWhiteSpace(line))) // 空行をスキップ
                    {
                        var parts = line.Split(',');
                        if (parts.Length >= 2)
                        {
                            var cardId = parts[0];
                            var cardFilePath = Path.Combine(cardsDir, cardId + ".json");

                            // If card file missing or empty/placeholder, try to recover from UUID subfolder
                            bool needPlaceholder = false;
                            if (!File.Exists(cardFilePath) || new FileInfo(cardFilePath).Length < 5)
                            {
                                // look for uuid-based location: tempExtractPath/{uuid}/cards/{id}.json
                                if (!string.IsNullOrEmpty(uuid))
                                {
                                    var altPath = Path.Combine(tempExtractPath, uuid, "cards", cardId + ".json");
                                    if (File.Exists(altPath) && new FileInfo(altPath).Length > 5)
                                    {
                                        try
                                        {
                                            File.Copy(altPath, cardFilePath, true);
                                            Debug.WriteLine($"UUIDサブフォルダからカードを復元: {altPath} -> {cardFilePath}");
                                        }
                                        catch (Exception ex)
                                        {
                                            Debug.WriteLine($"カード復元に失敗: {ex.Message}");
                                        }
                                    }
                                    else
                                    {
                                        // try alternate without cards folder
                                        var altPath2 = Path.Combine(tempExtractPath, uuid, cardId + ".json");
                                        if (File.Exists(altPath2) && new FileInfo(altPath2).Length > 5)
                                        {
                                            try
                                            {
                                                File.Copy(altPath2, cardFilePath, true);
                                                Debug.WriteLine($"UUID直下からカードを復元: {altPath2} -> {cardFilePath}");
                                            }
                                            catch (Exception ex)
                                            {
                                                Debug.WriteLine($"カード復元に失敗: {ex.Message}");
                                            }
                                        }
                                        else
                                        {
                                            needPlaceholder = true;
                                        }
                                    }
                                }
                                else
                                {
                                    needPlaceholder = true;
                                }
                            }

                            if (!File.Exists(cardFilePath) || new FileInfo(cardFilePath).Length < 5)
                            {
                                if (needPlaceholder)
                                {
                                    Debug.WriteLine($"新しいカードを保存(プレースホルダー): {cardFilePath}");
                                    File.WriteAllText(cardFilePath, "{}" /* カードデータをここに保存 */);
                                }
                                else
                                {
                                    Debug.WriteLine($"カードを復元したためプレースホルダーは不要: {cardFilePath}");
                                }
                            }
                            else
                            {
                                Debug.WriteLine($"既存のカードをスキップ: {cardFilePath}");
                            }

                            existingCards.Add(cardId);
                        }
                    }

                    // Data retrieval logic
                    int totalQuestions = existingCards.Count;

                    // Title with subfolder name
                    string title = GetNoteTitleWithSubfolder();
                    try
                    {
                        var metaPath = Path.Combine(tempExtractPath, "metadata.json");
                        if (File.Exists(metaPath))
                        {
                            var metaJson = File.ReadAllText(metaPath);
                            using var metaDoc = JsonDocument.Parse(metaJson);
                            if (metaDoc.RootElement.TryGetProperty("originalName", out var origProp))
                            {
                                var origName = origProp.GetString();
                                if (!string.IsNullOrEmpty(origName))
                                {
                                    // Determine subfolder name (if any)
                                    try
                                    {
                                        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                                        var flashnotePath = Path.Combine(documentsPath, "Flashnote");
                                        var noteDirectory = Path.GetDirectoryName(ankplsFilePath);
                                        if (!string.IsNullOrEmpty(noteDirectory) && !noteDirectory.Equals(flashnotePath, StringComparison.OrdinalIgnoreCase))
                                        {
                                            // Determine display name for subfolder by checking its metadata.json originalName first
                                            var subfolderName = Path.GetFileName(noteDirectory);
                                            try
                                            {
                                                // 1) Try metadata.json inside the folder itself
                                                var folderMeta = Path.Combine(noteDirectory, "metadata.json");
                                                if (File.Exists(folderMeta))
                                                {
                                                    var fmJson = File.ReadAllText(folderMeta);
                                                    using var fmDoc = JsonDocument.Parse(fmJson);
                                                    if (fmDoc.RootElement.TryGetProperty("originalName", out var sfOrig) && !string.IsNullOrEmpty(sfOrig.GetString()))
                                                    {
                                                        subfolderName = sfOrig.GetString();
                                                    }
                                                }
                                                else
                                                {
                                                    // 2) Try local temp metadata (materialized UUID-based data may be stored under LocalApplicationData/Flashnote)
                                                    var localTempMeta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Flashnote", subfolderName + "_temp", "metadata.json");
                                                    if (File.Exists(localTempMeta))
                                                    {
                                                        var ltJson = File.ReadAllText(localTempMeta);
                                                        using var ltDoc = JsonDocument.Parse(ltJson);
                                                        if (ltDoc.RootElement.TryGetProperty("originalName", out var ltOrig) && !string.IsNullOrEmpty(ltOrig.GetString()))
                                                        {
                                                            subfolderName = ltOrig.GetString();
                                                        }
                                                    }
                                                }
                                            }
                                            catch (Exception exSub)
                                            {
                                                Debug.WriteLine($"Subfolder originalName read error: {exSub.Message}");
                                            }
                                            if (!string.IsNullOrEmpty(subfolderName) && subfolderName != ".")
                                            {
                                                title = $"{subfolderName}・{origName}";
                                            }
                                        }
                                        else
                                        {
                                            title = origName;
                                        }
                                    }
                                    catch (Exception exPref)
                                    {
                                        Debug.WriteLine($"Subfolder title compose error: {exPref.Message}");
                                        title = origName;
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception exTitle)
                    {
                        Debug.WriteLine($"Note title originalName read error: {exTitle.Message}");
                    }

                     NoteTitleLabel.Text = title;

                    TotalQuestionsLabel.Text = $"カード枚数: {totalQuestions}";
                }
                else
                {
                    Debug.WriteLine("LoadNote: cards.txtが見つかりませんでした");
                    DisplayAlert("エラー", "cards.txtが見つかりませんでした", "OK");
                }
            }
            else
            {
                Debug.WriteLine("LoadNote: ankplsFilePathが存在しません");
                DisplayAlert("エラー", "データが見つかりませんでした", "OK");
            }
        }

        // サブフォルダ名を含むタイトルを取得
        private string GetNoteTitleWithSubfolder()
        {
            try
            {
                var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var flashnotePath = Path.Combine(documentsPath, "Flashnote");
                var noteDirectory = Path.GetDirectoryName(ankplsFilePath);
                var noteName = Path.GetFileNameWithoutExtension(ankplsFilePath);
                
                // ルートフォルダの場合はノート名のみ
                if (noteDirectory.Equals(flashnotePath, StringComparison.OrdinalIgnoreCase))
                {
                    return noteName;
                }
                
                // サブフォルダの場合は「サブフォルダ名・ノート名」の形式
                var subfolderName = Path.GetFileName(noteDirectory);
                try
                {
                    // 1) Try metadata.json inside the folder itself
                    var folderMeta = Path.Combine(noteDirectory, "metadata.json");
                    if (File.Exists(folderMeta))
                    {
                        var fmJson = File.ReadAllText(folderMeta);
                        using var fmDoc = JsonDocument.Parse(fmJson);
                        if (fmDoc.RootElement.TryGetProperty("originalName", out var sfOrig) && !string.IsNullOrEmpty(sfOrig.GetString()))
                        {
                            subfolderName = sfOrig.GetString();
                        }
                    }
                    else
                    {
                        // 2) Try local temp metadata (materialized UUID-based data may be stored under LocalApplicationData/Flashnote)
                        var localTempMeta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Flashnote", subfolderName + "_temp", "metadata.json");
                        if (File.Exists(localTempMeta))
                        {
                            var ltJson = File.ReadAllText(localTempMeta);
                            using var ltDoc = JsonDocument.Parse(ltJson);
                            if (ltDoc.RootElement.TryGetProperty("originalName", out var ltOrig) && !string.IsNullOrEmpty(ltOrig.GetString()))
                            {
                                subfolderName = ltOrig.GetString();
                            }
                        }
                    }
                }
                catch (Exception exSub)
                {
                    Debug.WriteLine($"Subfolder originalName read error: {exSub.Message}");
                }
                return $"{subfolderName}・{noteName}";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"タイトル生成中にエラー: {ex.Message}");
                // エラーが発生した場合はノート名のみを返す
                return Path.GetFileNameWithoutExtension(ankplsFilePath);
            }
        }

        // ページが表示されるたびに呼ばれるメソッド
        protected override async void OnAppearing()
        {
            base.OnAppearing();

            // uid の設定
            string uid;
            var sharedKeyService = new SharedKeyService();

            if (sharedKeyService.IsSharedNote(ankplsFilePath)) // 共有されたノートかどうかを判定
            {
                var sharedInfo = sharedKeyService.GetSharedNoteInfo(ankplsFilePath);
                uid = sharedInfo?.OriginalUserId ?? throw new Exception("共有元のユーザーIDが取得できませんでした");
            }
            else
            {
                uid = App.CurrentUser?.Uid ?? throw new Exception("現在のユーザーIDが取得できませんでした");
            }

            string noteName = Path.GetFileNameWithoutExtension(ankplsFilePath);

            // compute relative subfolder (relative to Documents/Flashnote) for remote calls
            string subFolder = null;
            try
            {
                var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var flashnotePath = Path.Combine(documentsPath, "Flashnote");
                var noteDirectory = Path.GetDirectoryName(ankplsFilePath);
                if (!string.IsNullOrEmpty(noteDirectory))
                {
                    var rel = Path.GetRelativePath(flashnotePath, noteDirectory);
                    if (!string.IsNullOrEmpty(rel) && rel != ".")
                    {
                        // normalize to forward slashes for blob paths
                        subFolder = rel.Replace("\\", "/");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"サブフォルダ計算中にエラー: {ex.Message}");
                subFolder = null;
            }

            try
            {
                var blobService = new BlobStorageService();

                // If this note is shared (exists in shared keys), use shared download API so that cards/img are fetched
                if (sharedKeyService.IsSharedNote(ankplsFilePath))
                {
                    var info = sharedKeyService.GetSharedNoteInfo(ankplsFilePath);
                    try
                    {
                        Debug.WriteLine($"Downloading shared note: user={info?.OriginalUserId}, path={info?.NotePath}, note={noteName}");
                        await blobService.DownloadSharedNoteAsync(info.OriginalUserId, info.NotePath, noteName, subFolder);
                        Debug.WriteLine("DownloadSharedNoteAsync 完了");
                    }
                    catch (Exception dex)
                    {
                        Debug.WriteLine($"共有ノートのダウンロード中にエラー: {dex.Message}");
                    }
                }
                else
                {
                    // Non-shared note: create local note (downloads cards.txt and card JSONs)
                    try
                    {
                        Debug.WriteLine($"Creating local note from server: uid={uid}, note={noteName}, subFolder={subFolder}");
                        var cardSyncService = new CardSyncService(new BlobStorageService());
                        await cardSyncService.SyncNoteOnOpenAsync(uid, noteName, subFolder);
                        Debug.WriteLine("SyncNoteOnOpenAsync が正常に呼び出されました。");
                        await blobService.CreateLocalNoteAsync(uid, noteName, subFolder);
                        Debug.WriteLine("CreateLocalNoteAsync 完了");
                    }
                    catch (Exception cex)
                    {
                        Debug.WriteLine($"ローカルノート作成中にエラー: {cex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ノートダウンロード処理中にエラー: {ex.Message}");
            }

            // カード枚数を再取得して更新
            LoadNote();
         }
        // ノート数取得  
        private int GetTotalQuestions(string uuid = null)
        {
            try
            {
                // Determine the correct path for the cards directory
                string cardsDir = GetCorrectPath(tempExtractPath, "cards", uuid);
                if (cardsDir != null && Directory.Exists(cardsDir))
                {
                    var jsonFiles = Directory.GetFiles(cardsDir, "*.json");
                    Debug.WriteLine($"cardsディレクトリ内のJSONファイル数: {jsonFiles.Length}");
                    return jsonFiles.Length;
                }

                Debug.WriteLine("cardsディレクトリが見つかりません");
                return 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"カード数取得中にエラー: {ex.Message}");
                return 0;
            }
        }

        // 学習開始  
        private void OnStartLearningClicked(object sender, EventArgs e)
        {
            // Qa.xaml に遷移（tempファイルのパスを渡す）  
            Navigation.PushAsync(new Qa(ankplsFilePath, tempExtractPath));
        }
        // Add  
        private async void AddCardClicked(object sender, EventArgs e)
        {
            try
            {
                Debug.WriteLine("AddCardClicked開始");
                Debug.WriteLine($"ankplsFilePath: {ankplsFilePath}");
                Debug.WriteLine($"tempExtractPath: {tempExtractPath}");

                if (Navigation != null)
                {
                    Debug.WriteLine("Navigationが利用可能です");
                    // Add.xaml に遷移（tempファイルのパスを渡す）  
                    await Navigation.PushAsync(new Add(ankplsFilePath, tempExtractPath));
                    Debug.WriteLine("Add.xamlへの遷移が完了しました");
                }
                else
                {
                    Debug.WriteLine("Navigationが利用できません");
                    await DisplayAlert("Error", "Navigation is not available.", "OK");
                }
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("オフライン") || ex.Message.Contains("ネットワーク"))
            {
                Debug.WriteLine($"オフラインエラー: {ex.Message}");
                await DisplayAlert("ネットワークエラー", "オフラインのため、カードの追加ができません。ネットワーク接続を確認してください。", "OK");
            }
            catch (TimeoutException ex)
            {
                Debug.WriteLine($"タイムアウトエラー: {ex.Message}");
                await DisplayAlert("タイムアウトエラー", "サーバーへの接続がタイムアウトしました。ネットワーク接続を確認してください。", "OK");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AddCardClickedでエラー: {ex.Message}");
                Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                await DisplayAlert("エラー", $"カードの追加中にエラーが発生しました: {ex.Message}", "OK");
            }
        }
        // NotePage  
        private async void ToNoteClicked(object sender, EventArgs e)
        {
            await Navigation.PushAsync(new NotePage(ankplsFilePath, tempExtractPath));
        }
        private async void EditCardsClicked(object sender, EventArgs e)
        {
            try
            {
                Debug.WriteLine("EditCardsClicked開始");
                Debug.WriteLine($"ankplsFilePath: {ankplsFilePath}");
                Debug.WriteLine($"tempExtractPath: {tempExtractPath}");

                if (Navigation != null)
                {
                    Debug.WriteLine("Navigationが利用可能です");
                    // Edit.xaml に遷移
                    await Navigation.PushAsync(new Edit(ankplsFilePath, tempExtractPath));
                    Debug.WriteLine("Edit.xamlへの遷移が完了しました");
                }
                else
                {
                    Debug.WriteLine("Navigationが利用できません");
                    await DisplayAlert("Error", "Navigation is not available.", "OK");
                }
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("オフライン") || ex.Message.Contains("ネットワーク"))
            {
                Debug.WriteLine($"オフラインエラー: {ex.Message}");
                await DisplayAlert("ネットワークエラー", "オフラインのため、カードの編集ができません。ネットワーク接続を確認してください。", "OK");
            }
            catch (TimeoutException ex)
            {
                Debug.WriteLine($"タイムアウトエラー: {ex.Message}");
                await DisplayAlert("タイムアウトエラー", "サーバーへの接続がタイムアウトしました。ネットワーク接続を確認してください。", "OK");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EditCardsClickedでエラー: {ex.Message}");
                Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                await DisplayAlert("エラー", $"カードの編集画面を開く際にエラーが発生しました: {ex.Message}", "OK");
            }
        }

        private async void OnExportToAnkiClicked(object sender, EventArgs e)
        {
            try
            {
                string savePath = null;

                // プラットフォーム固有の処理
                if (DeviceInfo.Platform == DevicePlatform.WinUI)
                {
#if WINDOWS
                    savePath = await GetWindowsSavePath();
#else
                    await DisplayAlert("エラー", "このプラットフォームではサポートされていません", "OK");
                    return;
#endif
                }
                else
                {
                    // 他のプラットフォーム用の処理
                    var result = await FilePicker.PickAsync(new PickOptions
                    {
                        PickerTitle = "APKGファイルの保存先を選択",
                        FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                        {
                            { DevicePlatform.WinUI, new[] { ".apkg" } },
                            { DevicePlatform.macOS, new[] { "apkg" } }
                        })
                    });

                    if (result != null)
                    {
                        savePath = result.FullPath;
                    }
                }

                if (savePath != null)
                {
                    // カードデータの収集
                    var cards = new List<Models.CardData>();
                    var cardsDir = Path.Combine(tempExtractPath, "cards");
                    if (Directory.Exists(cardsDir))
                    {
                        foreach (var jsonFile in Directory.GetFiles(cardsDir, "*.json"))
                        {
                            try
                            {
                                var jsonContent = await File.ReadAllTextAsync(jsonFile);
                                var card = JsonSerializer.Deserialize<Models.CardData>(jsonContent);
                                if (card != null)
                                {
                                    cards.Add(card);
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"カードデータの読み込み中にエラー: {ex.Message}");
                                continue;
                            }
                        }
                    }

                    if (cards.Count == 0)
                    {
                        await DisplayAlert("エラー", "カードデータが見つかりませんでした", "OK");
                        return;
                    }

                    // APKGファイルの作成
                    string subfolder = Path.GetFileName(Path.GetDirectoryName(ankplsFilePath));
                    string notename = Path.GetFileNameWithoutExtension(ankplsFilePath);
                    string deckName = $"{subfolder}::{notename}";
                    var exporter = new AnkiExporter(tempExtractPath, cards, deckName);
                    try
                    {
                        await exporter.GenerateApkg(savePath);
                        await DisplayAlert("成功", "APKGファイルを作成しました", "OK");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"APKGファイル生成中にエラー: {ex.Message}");
                        await DisplayAlert("エラー", $"APKGファイルの作成に失敗しました: {ex.Message}", "OK");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"APKGエクスポート中にエラー: {ex.Message}");
                Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                await DisplayAlert("エラー", $"APKGファイルの作成に失敗しました: {ex.Message}", "OK");
            }
        }

#if WINDOWS
        private async Task<string> GetWindowsSavePath()
        {
            try
            {
                var savePicker = new FileSavePicker();
                savePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                savePicker.SuggestedFileName = Path.GetFileNameWithoutExtension(ankplsFilePath);
                savePicker.FileTypeChoices.Add("APKGファイル", new List<string>() { ".apkg" });

                // ウィンドウハンドルの取得と設定
                var windowHandle = WindowNative.GetWindowHandle(Microsoft.Maui.Controls.Application.Current.Windows[0].Handler.PlatformView);
                InitializeWithWindow.Initialize(savePicker, windowHandle);

                var file = await savePicker.PickSaveFileAsync();
                return file?.Path;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ファイル保存ダイアログでエラー: {ex.Message}");
                Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                throw;
            }
        }
#endif

        // Helper method to determine the correct path for cards.txt or cards directory
        private string GetCorrectPath(string basePath, string fileName, string uuid = null)
        {
            // Check new UUID-based flat structure (basePath/{fileName})
            if (!string.IsNullOrEmpty(uuid))
            {
                var newPath = Path.Combine(basePath, fileName);
                Debug.WriteLine($"UUID形式のパスを確認: {newPath}");
                if (File.Exists(newPath) || Directory.Exists(newPath))
                {
                    return newPath;
                }
                else
                {
                    Debug.WriteLine($"UUID形式のパスが見つかりません: {newPath}");

                    // Ensure the UUID folder exists and cards subfolder exists for future downloads
                    var uuidDir = Path.Combine(basePath, uuid);
                    var cardsDirPath = Path.Combine(uuidDir, "cards");
                    if (!Directory.Exists(cardsDirPath))
                    {
                        try
                        {
                            Directory.CreateDirectory(cardsDirPath);
                            Debug.WriteLine($"cardsディレクトリを作成しました: {cardsDirPath}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"cardsディレクトリ作成エラー: {ex.Message}");
                        }
                    }
                }
            }

            // Check old hierarchical structure
            var oldPath = Path.Combine(basePath, fileName);
            Debug.WriteLine($"旧形式のパスを確認: {oldPath}");
            if (File.Exists(oldPath) || Directory.Exists(oldPath))
            {
                return oldPath;
            }
            else
            {
                Debug.WriteLine($"旧形式のパスが見つかりません: {oldPath}");
            }
 
             return null; // Not found
         }

        // Helper method to extract UUID from metadata (if available)
        private string ExtractUuidFromMetadata()
        {
            try
            {
                string metadataPath = Path.Combine(tempExtractPath, "metadata.json");
                if (File.Exists(metadataPath))
                {
                    string metadataContent = File.ReadAllText(metadataPath);
                    using var doc = JsonDocument.Parse(metadataContent);
                    if (doc.RootElement.TryGetProperty("id", out var idProperty))
                    {
                        return idProperty.GetString();
                    }
                }
                // Fallback: try to extract from tempExtractPath folder name (e.g. "{UUID}_temp")
                try
                {
                    var dirName = Path.GetFileName(tempExtractPath);
                    if (!string.IsNullOrEmpty(dirName) && dirName.EndsWith("_temp"))
                    {
                        var candidate = dirName.Substring(0, dirName.Length - 5);
                        if (Guid.TryParse(candidate, out _))
                        {
                            Debug.WriteLine($"フォルダ名からUUIDを抽出: {candidate}");
                            return candidate;
                        }
                    }
                }
                catch (Exception exDir)
                {
                    Debug.WriteLine($"フォルダ名からのUUID抽出エラー: {exDir.Message}");
                }
             }
             catch (Exception ex)
             {
                 Debug.WriteLine($"UUIDの抽出中にエラー: {ex.Message}");
             }
 
             return null; // UUID not found
         }
    }
}

