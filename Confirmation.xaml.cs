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
                // データ取得  
                int totalQuestions = GetTotalQuestions();
                
                // タイトルにサブフォルダ名を含める
                string title = GetNoteTitleWithSubfolder();
                NoteTitleLabel.Text = title;
                
                TotalQuestionsLabel.Text = $"カード枚数: {totalQuestions}";
            }
            else
            {
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
        protected override void OnAppearing()
        {
            base.OnAppearing();
            // カード枚数を再取得して更新
            LoadNote();
        }
        // ノート数取得  
        private int GetTotalQuestions()
        {
            try
            {
                // cardsディレクトリ内のJSONファイル数をカウント
                string cardsDir = Path.Combine(tempExtractPath, "cards");
                if (Directory.Exists(cardsDir))
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
    }
}

