using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Diagnostics;
using Flashnote.Services;
using SkiaSharp.Views.Maui;
using SkiaSharp;
using System.Web;
using System.Text;
using System.Timers;
using Flashnote_MAUI.Services;
using Microsoft.Maui.Platform;
using Windows.System;

namespace Flashnote
{
    public partial class Qa : ContentPage
    {
        private string cardsFilePath;
        private string tempExtractPath;
        private List<CardData> cards = new List<CardData>();
        private List<CardData> sortedCards = new List<CardData>();
        private int currentIndex = 0;
        private int correctCount = 0;
        private int incorrectCount = 0;
        private System.Timers.Timer reviewTimer;
        private Services.LearningResultSyncService _learningResultSyncService;
        // クラスの先頭で変数を宣言
        private string selectedImagePath = "";
        private List<SKRect> selectionRects = new List<SKRect>();  // 画像座標（ピクセル単位）
        // 各問題ごとの正解・不正解回数を管理
        private Dictionary<int, CardResult> results = new Dictionary<int, CardResult>();
        private Dictionary<string, LearningRecord> learningRecords = new Dictionary<string, LearningRecord>();
        private bool showAnswer = false;  // 解答表示フラグ
        private string frontText = "";
        private bool isTextInputMode = false;  // テキスト入力モードフラグ
        private List<Entry> textInputEntries = new List<Entry>();  // テキスト入力フィールド
        private List<string> correctAnswers = new List<string>();  // 正解リスト
        private List<Label> resultLabels = new List<Label>();  // 結果表示ラベル
        private bool wasTextInputModeEnabled = false;  // テキスト入力モードの前回の状態を記憶
        private bool isDefaultTextInputModeEnabled = false;  // デフォルト設定の状態

        // ダークモード判定プロパティ
        private bool IsDarkMode => Microsoft.Maui.Controls.Application.Current?.RequestedTheme == AppTheme.Dark;

        // ダークモード対応色設定
        private struct ThemeColors
        {
            public static Color BackgroundColor => Microsoft.Maui.Controls.Application.Current?.RequestedTheme == AppTheme.Dark 
                ? Color.FromArgb("#1F1F1F") : Color.FromArgb("#FFFFFF");
            public static Color TextColor => Microsoft.Maui.Controls.Application.Current?.RequestedTheme == AppTheme.Dark 
                ? Color.FromArgb("#FFFFFF") : Color.FromArgb("#1E1E1E");
            public static Color BorderColor => Microsoft.Maui.Controls.Application.Current?.RequestedTheme == AppTheme.Dark 
                ? Color.FromArgb("#404040") : Color.FromArgb("#E0E0E0");
            public static Color CanvasBackground => Microsoft.Maui.Controls.Application.Current?.RequestedTheme == AppTheme.Dark 
                ? Color.FromArgb("#2F2F2F") : Color.FromArgb("#F5F5F5");
        }

        // 新形式用のカードデータクラス
        private class CardData
        {
            public string id { get; set; }
            public string type { get; set; }
            public string front { get; set; }
            public string back { get; set; }
            public string question { get; set; }
            public string explanation { get; set; }
            public List<ChoiceItem> choices { get; set; }
            public List<SelectionRect> selectionRects { get; set; }
            public string imageFileName { get; set; } // 画像穴埋めカード用の画像ファイル名
        }
        private class ChoiceItem
        {
            public string text { get; set; }
            public bool isCorrect { get; set; }
        }
        private class SelectionRect
        {
            public float x { get; set; }
            public float y { get; set; }
            public float width { get; set; }
            public float height { get; set; }
        }

        private class CardResult
        {
            public bool WasCorrect { get; set; }  // 直前の正誤のみを保持
            public DateTime? NextReviewTime { get; set; }
            public int OriginalQuestionNumber { get; set; }  // 元の問題番号を保持
        }

        // iOS版と同じ学習記録構造体
        private class LearningRecord
        {
            public string CardId { get; set; }
            public int CorrectCount { get; set; }
            public int IncorrectCount { get; set; }
            public DateTime NextReviewDate { get; set; }
            public bool? LastResult { get; set; }  // true: 正解, false: 不正解, null: 未学習

            public LearningRecord(string cardId)
            {
                CardId = cardId;
                CorrectCount = 0;
                IncorrectCount = 0;
                NextReviewDate = DateTime.Now;
                LastResult = null;
            }
        }

        public Qa(string cardsPath, string tempPath)
        {
            InitializeComponent();
            // 一時フォルダ
            tempExtractPath = tempPath;
            cardsFilePath = Path.Combine(tempExtractPath, "cards.txt");
            
            // 学習記録同期サービスを初期化
            var blobStorageService = new Services.BlobStorageService();
            _learningResultSyncService = new Services.LearningResultSyncService(blobStorageService);
            
            LoadCards();
            LoadAndSortCards();
            InitializeTheme();
            _ = DisplayCard();
            
            // アプリ起動時の同期を実行
            _ = InitializeLearningResultSync();
        }

        public Qa(List<string> cardsList)
        {
            InitializeComponent();
            // 一時フォルダを作成（結果保存用）
            tempExtractPath = Path.Combine(Path.GetTempPath(), "Flashnote_" + Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempExtractPath);

            // 新形式ではこのコンストラクタは使わない想定ですが、空リストで初期化
            cards = new List<CardData>();
            LoadAndSortCards();
            Debug.WriteLine($"Loaded {cards.Count} cards");
            InitializeTheme();
            _ = DisplayCard();
        }

        private void InitializeTheme()
        {
            // ページのバックグラウンドカラーを設定
            this.BackgroundColor = ThemeColors.BackgroundColor;
            
            // テーマ変更イベントの監視
            Microsoft.Maui.Controls.Application.Current.RequestedThemeChanged += OnRequestedThemeChanged;
        }

        private void OnRequestedThemeChanged(object sender, AppThemeChangedEventArgs e)
        {
            // テーマが変更された時の処理
            this.BackgroundColor = ThemeColors.BackgroundColor;
            
            // 現在表示中のカードを再描画
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await DisplayCard();
                CanvasView?.InvalidateSurface();
            });
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            CanvasView.InvalidateSurface();
            LoadResultsFromFile();
            StartReviewTimer();
            LoadDefaultTextInputModeSetting();
            
            // キーボードイベントハンドラーを登録
            RegisterKeyboardEvents();
            
            // 現在のカードの状態を復元
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Task.Delay(100); // 少し遅延させてから処理を実行
                
                // 問題数を初期化
                UpdateQuestionNumber();
                
                // 現在のカードを再表示して状態を復元
                if (sortedCards != null && sortedCards.Count > 0 && currentIndex < sortedCards.Count)
                {
                    await DisplayCard();
                }
                
                // アクティブなボタンにフォーカスを設定
                SetFocusToActiveButton();
            });
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            // イベントの解除
            if (Microsoft.Maui.Controls.Application.Current != null)
            {
                Microsoft.Maui.Controls.Application.Current.RequestedThemeChanged -= OnRequestedThemeChanged;
            }
            StopReviewTimer();
            
            // キーボードイベントハンドラーを解除
            UnregisterKeyboardEvents();
            
            // 学習セッション終了時の同期
            _ = FinalizeLearningResultSync();
        }

        private async Task InitializeLearningResultSync()
        {
            try
            {
                if (App.CurrentUser != null && _learningResultSyncService != null)
                {
                    // ノート名を取得（tempExtractPathから抽出）
                    var noteName = GetNoteNameFromTempPath();
                    if (!string.IsNullOrEmpty(noteName))
                    {
                        await _learningResultSyncService.SyncOnAppStartAsync(App.CurrentUser.Uid, noteName);
                        Debug.WriteLine($"学習記録同期を初期化: {noteName}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"学習記録同期の初期化エラー: {ex.Message}");
            }
        }

        private async Task FinalizeLearningResultSync()
        {
            try
            {
                if (App.CurrentUser != null && _learningResultSyncService != null)
                {
                    var noteName = GetNoteNameFromTempPath();
                    if (!string.IsNullOrEmpty(noteName))
                    {
                        await _learningResultSyncService.SyncOnSessionEndAsync(App.CurrentUser.Uid, noteName);
                        Debug.WriteLine($"学習セッション終了時の同期完了: {noteName}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"学習セッション終了時の同期エラー: {ex.Message}");
            }
        }

        private string GetNoteNameFromTempPath()
        {
            try
            {
                // tempExtractPathから ノート名_temp の部分を抽出
                var dirName = Path.GetFileName(tempExtractPath);
                if (dirName.EndsWith("_temp"))
                {
                    return dirName.Substring(0, dirName.Length - 5); // "_temp"を除去
                }
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ノート名の抽出エラー: {ex.Message}");
                return null;
            }
        }

        private void UpdateSyncFlag()
        {
            try
            {
                _learningResultSyncService?.OnQuestionAnswered();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"同期フラグ更新エラー: {ex.Message}");
            }
        }

        // カードを読み込む
        private void LoadCards()
        {
            cards.Clear();
            string cardsDir = Path.Combine(tempExtractPath, "cards");
            if (!File.Exists(cardsFilePath) || !Directory.Exists(cardsDir)) return;

            var lines = File.ReadAllLines(cardsFilePath);
            foreach (var line in lines.Skip(1)) // 1行目はカード数
                        {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split(',');
                if (parts.Length < 1) continue;
                string uuid = parts[0];
                string jsonPath = Path.Combine(cardsDir, $"{uuid}.json");
                if (File.Exists(jsonPath))
                {
                    var json = File.ReadAllText(jsonPath);
                    var card = JsonSerializer.Deserialize<CardData>(json);
                    if (card != null) cards.Add(card);
                }
            }
            
            Debug.WriteLine($"Loaded {cards.Count} cards");
        }
        // 結果ファイルを読み込む
        private void LoadResultsFromFile()
        {
            try
            {
                string resultFilePath = Path.Combine(tempExtractPath, "result.txt");

                if (File.Exists(resultFilePath))
                {
                    var lines = File.ReadAllLines(resultFilePath);

                    foreach (var line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        
                        var parts = line.Split('|');
                        if (parts.Length >= 3)
                        {
                            // UUIDから対応するカードを検索
                            if (Guid.TryParse(parts[0].Trim(), out Guid cardGuid))
                            {
                                string cardId = cardGuid.ToString();
                                // カードのIDと一致するものを探す
                                var matchingCard = cards.FirstOrDefault(c => c.id == cardId);
                                if (matchingCard != null)
                                {
                                    int cardIndex = cards.IndexOf(matchingCard);
                                    int questionNumber = cardIndex + 1;

                                    if (!results.ContainsKey(questionNumber))
                                    {
                                        results[questionNumber] = new CardResult { OriginalQuestionNumber = questionNumber };
                                    }

                                    // 正解・不正解の解析
                                    var resultInfo = parts[1].Trim();
                                    results[questionNumber].WasCorrect = resultInfo == "正解";

                                    // 次回表示時間の解析
                                    if (DateTime.TryParse(parts[2].Trim(), out DateTime nextReview))
                                    {
                                        results[questionNumber].NextReviewTime = nextReview;
                                    }

                                    Debug.WriteLine($"カード {cardId} (問題 {questionNumber}) の結果を読み込み: {(results[questionNumber].WasCorrect ? "正解" : "不正解")}");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"結果読み込み中にエラー: {ex.Message}");
            }
        }
        // 問題を表示
        private async Task DisplayCard()
        {
            try
            {
                Debug.WriteLine($"DisplayCard開始 - sortedCards.Count: {sortedCards?.Count ?? 0}, currentIndex: {currentIndex}");
                
                if (sortedCards == null || !sortedCards.Any())
                {
                    Debug.WriteLine("No cards available");
                    return;
                }

                if (currentIndex >= sortedCards.Count)
                {
                    Debug.WriteLine("すべての問題が出題されました");
                    // 復習が必要なカードがあるかチェック
                    await ShowReviewNeededCards();
                    return;
                }

                var card = sortedCards[currentIndex];
                Debug.WriteLine($"Current card id: {card.id}, type: {card.type}");

                // 問題数を更新
                UpdateQuestionNumber();

                // レイアウトの初期化
                BasicCardLayout.IsVisible = false;
                ChoiceCardLayout.IsVisible = false;
                ImageFillCardLayout.IsVisible = false;

                if (card.type.Contains("基本"))
                {
                    Debug.WriteLine("Displaying basic card");
                    DisplayBasicCard(card);
                }
                else if (card.type.Contains("選択肢"))
                {
                    Debug.WriteLine("Displaying choice card");
                    DisplayChoiceCard(card);
                }
                else if (card.type.Contains("画像穴埋め"))
                {
                    Debug.WriteLine("Displaying image fill card");
                    DisplayImageFillCard(card);
                }
                else
                {
                    Debug.WriteLine($"Unknown card type: {card.type}");
                    // 不明なカードタイプの場合は次のカードへ
                    currentIndex++;
                    await DisplayCard();
                }
                
                // ボタンの表示状態を復元
                RestoreButtonStates();
                
                // 「解答を表示」ボタンにフォーカスを設定
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await Task.Delay(100);
                    SetFocusToActiveButton();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in DisplayCard: {ex}");
                await UIThreadHelper.ShowAlertAsync("Error", "Failed to display card", "OK");
            }
        }
        // 基本カードを解析
        private (string FrontText, string BackText) ParseBasicCard(List<string> lines)
        {
            var frontText = new StringBuilder();
            var backText = new StringBuilder();
            bool isFront = false;
            bool isBack = false;

            foreach (var line in lines)
            {
                if (line.StartsWith("表面:"))
                {
                    isFront = true;
                    isBack = false;
                    frontText.AppendLine(line.Substring(3));
                }
                else if (line.StartsWith("裏面:"))
                {
                    isBack = true;
                    isFront = false;
                    backText.AppendLine(line.Substring(3));
                }
                else
                {
                    if (isFront)
                    {
                        frontText.AppendLine(line);
                    }
                    else if (isBack)
                    {
                        backText.AppendLine(line);
                    }
                }
            }

            return (frontText.ToString().Trim(), backText.ToString().Trim());
        }
        // 基本・穴埋めカード表示
        private void DisplayBasicCard(CardData card)
        {
            BasicCardLayout.IsVisible = true;

            string frontText = card.front ?? "";
            string backText = card.back ?? "";

            // デバッグ情報を出力
            Debug.WriteLine($"=== Qa.xaml.cs DisplayBasicCard ===");
            Debug.WriteLine($"tempExtractPath: {tempExtractPath}");
            Debug.WriteLine($"FrontPreviewLabel.ImageFolderPath (設定前): {FrontPreviewLabel.ImageFolderPath}");
            Debug.WriteLine($"frontText: {frontText}");

            // テキスト入力モードの場合、正解を抽出
            if (isTextInputMode)
            {
                correctAnswers = ExtractBlankAnswers(frontText);
                FrontPreviewLabel.TextInputMode = true;
                FrontPreviewLabel.BlankAnswers = correctAnswers;
                
                // blankがない場合はテキスト入力モードを無効化
                if (correctAnswers.Count == 0)
                {
                    wasTextInputModeEnabled = true;  // 状態を記憶
                    isTextInputMode = false;
                    FrontPreviewLabel.TextInputMode = false;
                    FrontPreviewLabel.BlankAnswers = null;
                    showAnswer = false; // 解答表示状態をリセット
                    Debug.WriteLine("blankがないため、テキスト入力モードを無効化しました（状態を記憶）");
                }
                else
                {
                    wasTextInputModeEnabled = false;  // blankがある場合は記憶をクリア
                }
            }
            else
            {
                // テキスト入力モードがOFFの場合、blankがあるかチェック
                correctAnswers = ExtractBlankAnswers(frontText);
                if (correctAnswers.Count > 0 && wasTextInputModeEnabled)
                {
                    // 前回テキスト入力モードが有効だった場合は復元
                    isTextInputMode = true;
                    FrontPreviewLabel.TextInputMode = true;
                    FrontPreviewLabel.BlankAnswers = correctAnswers;
                    showAnswer = false; // 解答表示状態をリセット
                    Debug.WriteLine("blankがあるため、テキスト入力モードを復元しました");
                }
                else
                {
                    FrontPreviewLabel.TextInputMode = false;
                    FrontPreviewLabel.BlankAnswers = null;
                }
            }

            // RichTextLabelにテキストを設定
            FrontPreviewLabel.ImageFolderPath = tempExtractPath;
            FrontPreviewLabel.RichText = frontText;
            FrontPreviewLabel.ShowAnswer = showAnswer; // showAnswerフラグの状態を反映

            Debug.WriteLine($"FrontPreviewLabel.ImageFolderPath (設定後): {FrontPreviewLabel.ImageFolderPath}");

            // テキスト入力フィールドを生成
            CreateTextInputFields();

            // トグル状態を同期
            SyncToggleState();

            // 裏面が空でない場合のみ設定
            if (!string.IsNullOrWhiteSpace(backText))
            {
                BackPreviewLabel.ImageFolderPath = tempExtractPath;
                BackPreviewLabel.RichText = backText;
                BackPreviewLabel.ShowAnswer = showAnswer; // showAnswerフラグの状態を反映

                // 裏面の表示状態を復元
                BackPreviewFrame.IsVisible = showAnswer;
            }
            else
            {
            BackPreviewFrame.IsVisible = false;
            }
        }
        // 選択肢カード表示
        private List<CheckBox> checkBoxes = new List<CheckBox>();  // チェックボックスを保持
        private List<bool> currentCorrectFlags = new List<bool>();  // 現在のカードの正誤情報

        private void DisplayChoiceCard(CardData card)
        {
            ChoiceCardLayout.IsVisible = true;

            var (question, explanation, choices, isCorrectFlags) = ParseChoiceCard(card);

            // デバッグ情報を出力
            Debug.WriteLine($"=== Qa.xaml.cs DisplayChoiceCard ===");
            Debug.WriteLine($"tempExtractPath: {tempExtractPath}");
            Debug.WriteLine($"ChoiceQuestionLabel.ImageFolderPath (設定前): {ChoiceQuestionLabel.ImageFolderPath}");
            Debug.WriteLine($"question: {question}");

            // 選択肢カードの問題Label設定
            ChoiceQuestionLabel.ImageFolderPath = tempExtractPath;
            ChoiceQuestionLabel.RichText = question;
            ChoiceQuestionLabel.ShowAnswer = false;

            Debug.WriteLine($"ChoiceQuestionLabel.ImageFolderPath (設定後): {ChoiceQuestionLabel.ImageFolderPath}");

            ChoiceContainer.Children.Clear();
            checkBoxes.Clear();

            // 選択肢をシャッフル
            var random = new Random();
            var shuffledIndices = Enumerable.Range(0, choices.Count).OrderBy(x => random.Next()).ToList();
            var shuffledChoices = shuffledIndices.Select(i => choices[i]).ToList();
            currentCorrectFlags = shuffledIndices.Select(i => isCorrectFlags[i]).ToList();  // シャッフルされた正誤フラグを保存

            for (int i = 0; i < shuffledChoices.Count; i++)
            {
                var choiceText = shuffledChoices[i];

                var choiceLayout = new HorizontalStackLayout
                {
                    Spacing = 10
                };

                // チェックボックス
                var checkBox = new CheckBox
                {
                    IsChecked = false
                };

                checkBoxes.Add(checkBox);

                // 選択肢のラベル（クリック可能なボタンとして実装）
                var choiceButton = new Microsoft.Maui.Controls.Button
                {
                    Text = choiceText,
                    VerticalOptions = LayoutOptions.Center,
                    BackgroundColor = Colors.Transparent,
                    TextColor = ThemeColors.TextColor,
                    FontSize = 16,
                    Padding = new Thickness(0),
                    Margin = new Thickness(0)
                };

                // ボタンのクリックイベント
                int index = i; // クロージャのためにインデックスを保存
                choiceButton.Clicked += (s, e) =>
                {
                    checkBox.IsChecked = !checkBox.IsChecked;
                };

                // 正誤マークを表示するための Label
                var resultLabel = new Label
                {
                    Text = "",
                    VerticalOptions = LayoutOptions.Center,
                    TextColor = ThemeColors.TextColor,
                    IsVisible = false
                };

                // チェックボックスとボタンを追加
                choiceLayout.Children.Add(checkBox);
                choiceLayout.Children.Add(choiceButton);
                choiceLayout.Children.Add(resultLabel);

                ChoiceContainer.Children.Add(choiceLayout);
            }

            // 解説が空でない場合のみ設定
            if (!string.IsNullOrWhiteSpace(explanation))
            {
                ChoiceExplanationLabel.RichText = explanation;
                ChoiceExplanationLabel.ShowAnswer = showAnswer; // showAnswerフラグの状態を反映
                ChoiceExplanationLabel.ImageFolderPath = tempExtractPath;
                
                // 解説の表示状態を復元
                ChoiceExplanationFrame.IsVisible = showAnswer;
            }
            else
            {
            ChoiceExplanationFrame.IsVisible = false;
            }
        }

        private async void DisplayImageFillCard(CardData card)
        {
            ImageFillCardLayout.IsVisible = true;
            selectionRects.Clear();

            // imageFileNameフィールドから画像ファイル名を取得
            string imageFileName = GetImageFileNameFromCard(card);

            if (!string.IsNullOrWhiteSpace(imageFileName))
            {
                // フルパスを作成
                string imageFolder = Path.Combine(tempExtractPath, "img");
                selectedImagePath = Path.Combine(imageFolder, imageFileName);

                if (File.Exists(selectedImagePath))
                {
                    Debug.WriteLine($"画像読み込み成功: {selectedImagePath}");
                    CanvasView.InvalidateSurface();  // 再描画
                }
                else
                {
                    Debug.WriteLine($"画像が存在しません: {selectedImagePath}");
                    Debug.WriteLine($"探索パス: {selectedImagePath}");
                    Debug.WriteLine($"imgフォルダ内容: {string.Join(", ", Directory.GetFiles(imageFolder))}");
                    await UIThreadHelper.ShowAlertAsync("エラー", "画像が存在しません。", "OK");
                    return;
                }
            }
            else
            {
                Debug.WriteLine($"画像ファイル名が取得できません。card.front: '{card.front}', JSONデータ確認が必要");
                await UIThreadHelper.ShowAlertAsync("エラー", "画像パスが無効です。", "OK");
                return;
            }

            // 範囲の追加（画像座標で保存）
            foreach (var line in card.selectionRects)
            {
                // 既存のデータが正規化座標かどうかを判定
                // x, y, width, heightが全て0.0-1.0の範囲にある場合は正規化座標とみなす
                bool isNormalized = line.x >= 0.0f && line.x <= 1.0f && 
                                   line.y >= 0.0f && line.y <= 1.0f && 
                                   line.width >= 0.0f && line.width <= 1.0f && 
                                   line.height >= 0.0f && line.height <= 1.0f;
                
                SKRect selectionRect;
                if (isNormalized)
                {
                    // 正規化座標の場合は画像座標に変換
                    // 画像サイズは後で取得するため、一時的に0.0-1.0のまま保存
                    selectionRect = new SKRect(line.x, line.y, line.x + line.width, line.y + line.height);
                    Debug.WriteLine($"正規化座標を検出: {selectionRect}");
                }
                else
                {
                    // 画像座標の場合はそのまま使用
                    selectionRect = new SKRect(line.x, line.y, line.x + line.width, line.y + line.height);
                    Debug.WriteLine($"画像座標を検出: {selectionRect}");
                }
                selectionRects.Add(selectionRect);
            }

            CanvasView.InvalidateSurface();
        }

        /// <summary>
        /// カードデータから画像ファイル名を取得
        /// </summary>
        private string GetImageFileNameFromCard(CardData card)
        {
            // 新形式: imageFileNameフィールドから取得
            if (!string.IsNullOrWhiteSpace(card.imageFileName))
            {
                Debug.WriteLine($"新形式の画像ファイル名を使用: {card.imageFileName}");
                return card.imageFileName;
            }

            // 旧形式: frontフィールドから取得（後方互換性のため）
            if (!string.IsNullOrWhiteSpace(card.front))
            {
                Debug.WriteLine($"旧形式のfrontフィールドを使用: {card.front}");
                return card.front;
            }

            Debug.WriteLine("画像ファイル名が見つかりません");
            return null;
        }

        private void OnCanvasViewPaintSurface(object sender, SKPaintSurfaceEventArgs e)
        {
            var surface = e.Surface;
            var canvas = surface.Canvas;
            var info = e.Info;

            // ダークモード対応の背景色
            var backgroundColor = IsDarkMode ? SKColors.Black : SKColors.White;
            canvas.Clear(backgroundColor);

            if (string.IsNullOrWhiteSpace(selectedImagePath) || !File.Exists(selectedImagePath))
            {
                Debug.WriteLine("画像が選択されていないか、存在しません。");
                return;
            }

            try
            {
                // ファイルパスから直接画像を読み込む
                using (var stream = File.OpenRead(selectedImagePath))
                {
                    var bitmap = SKBitmap.Decode(stream);
                    Debug.WriteLine($"画像サイズ: {bitmap.Width} x {bitmap.Height}");
                    Debug.WriteLine($"キャンバスサイズ: {info.Width} x {info.Height}");
                    
                    if (bitmap == null)
                    {
                        Debug.WriteLine("画像のデコードに失敗しました。");
                        return;
                    }

                    // アスペクト比を維持して画像を描画
                    float imageAspect = (float)bitmap.Width / bitmap.Height;
                    float canvasAspect = (float)info.Width / info.Height;
                    
                    SKRect imageRect;
                    float scale;
                    
                    if (imageAspect > canvasAspect)
                    {
                        // 画像の方が横長：幅をキャンバスに合わせ、高さを調整
                        scale = (float)info.Width / bitmap.Width;
                        float scaledHeight = bitmap.Height * scale;
                        float offsetY = (info.Height - scaledHeight) / 2;
                        imageRect = new SKRect(0, offsetY, info.Width, offsetY + scaledHeight);
                    }
                    else
                    {
                        // 画像の方が縦長：高さをキャンバスに合わせ、幅を調整
                        scale = (float)info.Height / bitmap.Height;
                        float scaledWidth = bitmap.Width * scale;
                        float offsetX = (info.Width - scaledWidth) / 2;
                        imageRect = new SKRect(offsetX, 0, offsetX + scaledWidth, info.Height);
                    }
                    
                    canvas.DrawBitmap(bitmap, imageRect);
                    Debug.WriteLine($"描画領域: {imageRect.Left}, {imageRect.Top}, {imageRect.Right}, {imageRect.Bottom}");
                    Debug.WriteLine($"スケール: {scale}");

                    // 画像座標をキャンバス座標に変換して範囲を表示
                    foreach (var selectionRect in selectionRects)
                    {
                        // 既存のデータが正規化座標かどうかを判定
                        bool isNormalized = selectionRect.Left >= 0.0f && selectionRect.Left <= 1.0f && 
                                           selectionRect.Top >= 0.0f && selectionRect.Top <= 1.0f && 
                                           selectionRect.Width >= 0.0f && selectionRect.Width <= 1.0f && 
                                           selectionRect.Height >= 0.0f && selectionRect.Height <= 1.0f;
                        
                        SKRect actualImageRect;
                        if (isNormalized)
                        {
                            // 正規化座標の場合は画像座標に変換
                            actualImageRect = new SKRect(
                                selectionRect.Left * bitmap.Width,
                                selectionRect.Top * bitmap.Height,
                                (selectionRect.Left + selectionRect.Width) * bitmap.Width,
                                (selectionRect.Top + selectionRect.Height) * bitmap.Height
                            );
                            Debug.WriteLine($"正規化座標を画像座標に変換: {selectionRect} → {actualImageRect}");
                        }
                        else
                        {
                            // 画像座標の場合はそのまま使用
                            actualImageRect = selectionRect;
                            Debug.WriteLine($"画像座標を使用: {actualImageRect}");
                        }
                        
                        // 画像座標をキャンバス座標に変換
                        float canvasX = imageRect.Left + (actualImageRect.Left * scale);
                        float canvasY = imageRect.Top + (actualImageRect.Top * scale);
                        float canvasWidth = actualImageRect.Width * scale;
                        float canvasHeight = actualImageRect.Height * scale;
                        
                        var displayRect = new SKRect(canvasX, canvasY, canvasX + canvasWidth, canvasY + canvasHeight);
                        
                        // ダークモード対応の色設定
                        var fillColor = IsDarkMode ? SKColors.DarkRed : SKColors.Red;
                        var borderColor = IsDarkMode ? SKColors.White : SKColors.Black;
                        
                        // 塗りつぶし用のペイント
                        using (var fillPaint = new SKPaint
                        {
                            Color = fillColor,
                            Style = SKPaintStyle.Fill
                        })
                        {
                            canvas.DrawRect(displayRect, fillPaint);
                        }

                        // 枠線表示
                        using (var borderPaint = new SKPaint
                        {
                            Color = borderColor,
                            Style = SKPaintStyle.Stroke,
                            StrokeWidth = 3
                        })
                        {
                            canvas.DrawRect(displayRect, borderPaint);
                        }

                        Debug.WriteLine($"画像座標: {actualImageRect.Left:F1}, {actualImageRect.Top:F1}, {actualImageRect.Width:F1}, {actualImageRect.Height:F1}");
                        Debug.WriteLine($"表示座標: {displayRect.Left:F1}, {displayRect.Top:F1}, {displayRect.Right:F1}, {displayRect.Bottom:F1}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"描画中にエラーが発生しました: {ex.Message}");
            }
        }

        // 選択肢カードを解析
        private (string Question, string Explanation, List<string> Choices, List<bool> IsCorrect) ParseChoiceCard(CardData card)
        {
            string question = card.question ?? "";
            string explanation = card.explanation ?? "";
            var choices = new List<string>();
            var isCorrectFlags = new List<bool>();
            if (card.choices != null)
            {
                foreach (var c in card.choices)
                {
                    choices.Add(c.text);
                    isCorrectFlags.Add(c.isCorrect);
                }
            }
            return (question, explanation, choices, isCorrectFlags);
        }

        private async void OnShowAnswerClicked(object sender, EventArgs e)
        {
            if (BasicCardLayout.IsVisible)
            {
                // テキスト入力モードの場合、入力された回答を判定
                if (isTextInputMode)
                {
                    bool isCorrect = CheckTextInputAnswers();
                    
                    // 解答を表示
                    FrontPreviewLabel.ShowAnswer = true;
                    
                    // 裏面が空でない場合のみ表示
                    var currentCard = sortedCards[currentIndex];
                    if (!string.IsNullOrWhiteSpace(currentCard.back))
                    {
                        BackPreviewFrame.IsVisible = true;
                        BackPreviewLabel.ShowAnswer = true;
                    }
                    else
                    {
                        BackPreviewFrame.IsVisible = false;
                    }
                    
                    // 結果を保存
                    if (!results.ContainsKey(currentIndex + 1))
                    {
                        results[currentIndex + 1] = new CardResult();
                    }
                    var result = results[currentIndex + 1];
                    result.WasCorrect = isCorrect;
                    
                    // 次回表示時間を設定
                    if (isCorrect)
                    {
                        result.NextReviewTime = DateTime.Now.AddDays(1);
                    }
                    else
                    {
                        result.NextReviewTime = DateTime.Now.AddMinutes(1);
                    }
                    
                    // 学習記録を保存
                    SaveLearningRecord(currentCard, isCorrect);
                    UpdateSyncFlag();
                    
                    // blankがない場合は正解・不正解ボタンを表示、ある場合は「次へ」ボタンを表示
                    if (correctAnswers.Count == 0)
                    {
                        // blankがない場合は正解・不正解ボタンを表示
                        Correct.IsVisible = true;
                        Incorrect.IsVisible = true;
                        SeparatorGrid.IsVisible = true;
                        ShowAnswerButton.IsVisible = false;
                    }
                    else
                    {
                        // blankがある場合は「次へ」ボタンを表示
                        ShowAnswerButton.IsVisible = false;
                        NextButton.IsVisible = true;
                    }
                    
                    // 「次へ」ボタンにフォーカスを設定
                    MainThread.BeginInvokeOnMainThread(async () =>
                    {
                        await Task.Delay(50);
                        SetFocusToActiveButton();
                    });
                }
                else
                {
                    // 通常モード
                showAnswer = true;  // 解答表示フラグを有効に
                
                // RichTextLabelで解答を表示
                FrontPreviewLabel.ShowAnswer = true;
                
                // 裏面が空でない場合のみ表示
                var currentCard = sortedCards[currentIndex];
                if (!string.IsNullOrWhiteSpace(currentCard.back))
                {
                BackPreviewFrame.IsVisible = true;
                    BackPreviewLabel.ShowAnswer = true;
                }
                else
                {
                    BackPreviewFrame.IsVisible = false;
                }
                Correct.IsVisible = true;
                Incorrect.IsVisible = true;
                SeparatorGrid.IsVisible = true;
                ShowAnswerButton.IsVisible = false;
                    
                    // 「正解」ボタンにフォーカスを設定
                    MainThread.BeginInvokeOnMainThread(async () =>
                    {
                        await Task.Delay(50);
                        SetFocusToActiveButton();
                    });
                }
            }
            else if (ChoiceCardLayout.IsVisible)
            {
                // 選択肢の正誤を判定
                bool isCorrect = true;
                for (int i = 0; i < checkBoxes.Count; i++)
                {
                    var checkBox = checkBoxes[i];
                    var parentLayout = (HorizontalStackLayout)checkBox.Parent;
                    var resultLabel = (Label)parentLayout.Children[2];

                    if (checkBox.IsChecked != currentCorrectFlags[i])
                    {
                        isCorrect = false;
                    }

                    if (currentCorrectFlags[i])
                    {
                        resultLabel.Text = "正";
                        resultLabel.TextColor = IsDarkMode ? Color.FromArgb("#90EE90") : Colors.Green;
                        resultLabel.IsVisible = true;
                    }
                    else
                    {
                        resultLabel.Text = "誤";
                        resultLabel.TextColor = IsDarkMode ? Color.FromArgb("#FF6B6B") : Colors.Red;
                        resultLabel.IsVisible = true;
                    }
                }

                // 結果を保存
                if (!results.ContainsKey(currentIndex + 1))
                {
                    results[currentIndex + 1] = new CardResult();
                }

                var result = results[currentIndex + 1];
                result.WasCorrect = isCorrect;

                // 次回表示時間を設定
                if (isCorrect)
                {
                    // 2回目以降の正解：1日後、初回正解：10分後
                    result.NextReviewTime = result.WasCorrect ? DateTime.Now.AddDays(1) : DateTime.Now.AddMinutes(10);
                }
                else
                {
                    // 不正解：連続不正解なら1分後、前回正解なら10分後
                    result.NextReviewTime = DateTime.Now.AddMinutes(result.WasCorrect ? 10 : 1);
                }

                                // iOS版と同じ形式でresult.txtに保存
                var currentCard = sortedCards[currentIndex];
                SaveLearningRecord(currentCard, isCorrect);

                // 学習記録同期フラグを更新
                UpdateSyncFlag();

                // 解説が空でない場合のみ表示
                var currentCards = sortedCards[currentIndex];
                if (!string.IsNullOrWhiteSpace(currentCards.explanation))
                {
                    ChoiceExplanationFrame.IsVisible = true;
                }
                else
                {
                    ChoiceExplanationFrame.IsVisible = false;
                }

                // 「次へ」ボタンを表示
                ShowAnswerButton.IsVisible = false;
                NextButton.IsVisible = true;
                
                // 「次へ」ボタンにフォーカスを設定
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await Task.Delay(50);
                    SetFocusToActiveButton();
                });
            }
            else if (ImageFillCardLayout.IsVisible)
            {
                selectionRects.Clear();
                CanvasView.InvalidateSurface();
                Correct.IsVisible = true;
                Incorrect.IsVisible = true;
                SeparatorGrid.IsVisible = true;
                ShowAnswerButton.IsVisible = false;

                // 画像穴埋め問題の結果を保存（正解・不正解ボタンで判定されるため、ここでは初期化のみ）
                if (!results.ContainsKey(currentIndex + 1))
                {
                    results[currentIndex + 1] = new CardResult();
                }
                
                // 「正解」ボタンにフォーカスを設定
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await Task.Delay(50);
                    SetFocusToActiveButton();
                });
            }
        }

        private async void OnNextClicked(object sender, EventArgs e)
        {
            try
            {
                currentIndex++;
                NextButton.IsVisible = false;
                ShowAnswerButton.IsVisible = true;
                
                // テキスト入力モードの場合、結果ラベルをリセット
                if (isTextInputMode)
                {
                    foreach (var label in resultLabels)
                    {
                        label.IsVisible = false;
                        label.Text = "";
                    }
                }
                
                Debug.WriteLine($"次の問題へ移動: {currentIndex + 1}/{cards.Count}");
                await DisplayCard();
                
                // 「解答を表示」ボタンにフォーカスを設定
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await Task.Delay(100);
                    SetFocusToActiveButton();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in OnNextClicked: {ex}");
                await UIThreadHelper.ShowAlertAsync("Error", "Failed to move to next card", "OK");
            }
        }

        // 正解ボタン
        private async void OnCorrectClicked(object sender, EventArgs e)
        {
            try
            {
                var currentCard = sortedCards[currentIndex];
                
                // 現在のカードのインデックスをそのまま利用
                int questionNumber = currentIndex + 1;
                    if (!results.ContainsKey(questionNumber))
                    {
                        results[questionNumber] = new CardResult { OriginalQuestionNumber = questionNumber };
                    }
                    var result = results[questionNumber];
                    result.WasCorrect = true;  // 正解として記録
                    result.OriginalQuestionNumber = questionNumber;  // 元の問題番号を保持
                
                    // 次回表示時間を設定
                if (result.WasCorrect)
                {
                    // 2回目以降の正解：1日後
                    result.NextReviewTime = DateTime.Now.AddDays(1);
                }
                else
                {
                    // 初回正解：10分後
                    result.NextReviewTime = DateTime.Now.AddMinutes(10);
                }
                
                // iOS版と同じ形式でresult.txtに保存
                SaveLearningRecord(currentCard, true);

                // 学習記録同期フラグを更新
                UpdateSyncFlag();

                currentIndex++;
                Correct.IsVisible = false;
                Incorrect.IsVisible = false;
                SeparatorGrid.IsVisible = false;
                ShowAnswerButton.IsVisible = true;
                
                // テキスト入力モードの場合、結果ラベルをリセット
                if (isTextInputMode)
                {
                    foreach (var label in resultLabels)
                    {
                        label.IsVisible = false;
                        label.Text = "";
                    }
                }
                
                // 解答表示状態をリセット
                showAnswer = false;
                
                await DisplayCard();
                
                // 「解答を表示」ボタンにフォーカスを設定
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await Task.Delay(100);
                    SetFocusToActiveButton();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in OnCorrectClicked: {ex}");
                await UIThreadHelper.ShowAlertAsync("Error", "Failed to process correct answer", "OK");
            }
        }

        // 不正解ボタン
        private async void OnIncorrectClicked(object sender, EventArgs e)
        {
            try
            {
                var currentCard = sortedCards[currentIndex];
                
                int questionNumber = currentIndex + 1;
                    if (!results.ContainsKey(questionNumber))
                    {
                        results[questionNumber] = new CardResult { OriginalQuestionNumber = questionNumber };
                    }
                    var result = results[questionNumber];
                    result.WasCorrect = false;  // 不正解として記録
                    result.OriginalQuestionNumber = questionNumber;  // 元の問題番号を保持
                
                    // 次回表示時間を設定
                if (result.WasCorrect)
                {
                    // 前回が正解で今回不正解：10分後
                    result.NextReviewTime = DateTime.Now.AddMinutes(10);
                }
                else
                {
                    // 連続不正解：1分後
                    result.NextReviewTime = DateTime.Now.AddMinutes(1);
                }
                
                // iOS版と同じ形式でresult.txtに保存
                SaveLearningRecord(currentCard, false);

                // 学習記録同期フラグを更新
                UpdateSyncFlag();

                currentIndex++;
                Correct.IsVisible = false;
                Incorrect.IsVisible = false;
                SeparatorGrid.IsVisible = false;
                ShowAnswerButton.IsVisible = true;
                
                // テキスト入力モードの場合、結果ラベルをリセット
                if (isTextInputMode)
                {
                    foreach (var label in resultLabels)
                    {
                        label.IsVisible = false;
                        label.Text = "";
                    }
                }
                
                // 解答表示状態をリセット
                showAnswer = false;
                
                await DisplayCard();
                
                // 「解答を表示」ボタンにフォーカスを設定
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await Task.Delay(100);
                    SetFocusToActiveButton();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in OnIncorrectClicked: {ex}");
                await UIThreadHelper.ShowAlertAsync("Error", "Failed to process incorrect answer", "OK");
            }
        }


        // 学習記録を保存（iOS版と同じ形式）
        private void SaveLearningRecord(CardData card, bool isCorrect)
        {
            try
            {
                string resultFilePath = Path.Combine(tempExtractPath, "result.txt");

                // 結果の文字列
                string result = isCorrect ? "正解" : "不正解";

                // result.txtから既存の記録があるかを確認
                bool hasExistingRecord = CheckExistingRecord(card.id, resultFilePath);

                // 前回の結果を取得（既存記録がある場合）
                bool? previousResult = null;
                if (hasExistingRecord)
                {
                    previousResult = GetPreviousResult(card.id, resultFilePath);
                }

                // 次回復習時間の計算（iOS版と同じロジック）
                DateTime nextReviewDate;
                if (isCorrect)
                {
                    // 正解の場合
                    if (hasExistingRecord)
                    {
                        // 2回目以降の正解：1日後
                        nextReviewDate = DateTime.Now.AddDays(1);
                        Debug.WriteLine($"カード {card.id}: 2回目以降の正解 → 1日後に復習");
                    }
                    else
                    {
                        // 初回正解：10分後
                        nextReviewDate = DateTime.Now.AddMinutes(10);
                        Debug.WriteLine($"カード {card.id}: 初回正解 → 10分後に復習");
                    }
                }
                else
                {
                    // 不正解の場合
                    if (hasExistingRecord && previousResult == true)
                    {
                        // 前回が正解で今回不正解：10分後
                        nextReviewDate = DateTime.Now.AddMinutes(10);
                        Debug.WriteLine($"カード {card.id}: 前回正解→今回不正解 → 10分後に復習");
                    }
                    else
                    {
                        // 初回不正解または連続不正解：1分後
                        nextReviewDate = DateTime.Now.AddMinutes(1);
                        Debug.WriteLine($"カード {card.id}: 初回不正解または連続不正解 → 1分後に復習");
                    }
                }

                // 日時を文字列に変換（iOS版と同じ形式）
                string nextReviewDateStr = nextReviewDate.ToString("yyyy/MM/dd HH:mm:ss");

                // iOS版と同じ形式：{UUID}|{正解/不正解}|{次回復習時間}
                string recordLine = $"{card.id}|{result}|{nextReviewDateStr}";

                // ファイルに追記
                File.AppendAllText(resultFilePath, recordLine + Environment.NewLine);

                // メモリ内の学習記録も更新
                UpdateLearningRecord(card, isCorrect, nextReviewDate);

                Debug.WriteLine($"学習記録を保存: {recordLine}");
                Debug.WriteLine($"保存先: {resultFilePath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"学習記録保存エラー: {ex.Message}");
            }
        }

        // result.txtに既存の記録があるかチェック
        private bool CheckExistingRecord(string cardId, string resultFilePath)
        {
            try
            {
                if (!File.Exists(resultFilePath))
                {
                    Debug.WriteLine($"result.txtが存在しないため、カード {cardId} は初回");
                    return false;
                }

                var lines = File.ReadAllLines(resultFilePath);
                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    
                    var parts = line.Split('|');
                    if (parts.Length >= 1 && parts[0].Trim().Equals(cardId, StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.WriteLine($"カード {cardId} の既存記録を発見");
                        return true;
                    }
                }
                
                Debug.WriteLine($"カード {cardId} の既存記録なし（初回）");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"既存記録チェック中にエラー: {ex.Message}");
                return false;
            }
        }

        // 前回の結果を取得
        private bool? GetPreviousResult(string cardId, string resultFilePath)
        {
            try
            {
                if (!File.Exists(resultFilePath)) return null;

                var lines = File.ReadAllLines(resultFilePath);
                // 最後の記録を取得（最新の結果）
                for (int i = lines.Length - 1; i >= 0; i--)
                {
                    string line = lines[i];
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    
                    var parts = line.Split('|');
                    if (parts.Length >= 2 && parts[0].Trim().Equals(cardId, StringComparison.OrdinalIgnoreCase))
                    {
                        bool wasCorrect = parts[1].Trim() == "正解";
                        Debug.WriteLine($"カード {cardId} の前回結果: {(wasCorrect ? "正解" : "不正解")}");
                        return wasCorrect;
                    }
                }
                
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"前回結果取得中にエラー: {ex.Message}");
                return null;
            }
        }

        // 結果を即時保存（互換性のため残す）
        private void SaveResultsToFile()
        {
            try
            {
                if (currentIndex < sortedCards.Count)
                {
                    var currentCard = sortedCards[currentIndex];
                    if (results.ContainsKey(currentIndex + 1))
                    {
                        SaveLearningRecord(currentCard, results[currentIndex + 1].WasCorrect);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"結果の保存中にエラーが発生: {ex.Message}");
            }
        }

        // iOS版と同じ学習記録の読み込みとカードソート
        private void LoadAndSortCards()
        {
            Debug.WriteLine($"LoadAndSortCards開始 - cards.Count: {cards.Count}");
            LoadLearningRecords();
            
            // 現在時刻
            var now = DateTime.Now;
            
            // カードを優先順位でソート
            sortedCards = cards.OrderBy(card =>
            {
                var record = learningRecords.ContainsKey(card.id) ? learningRecords[card.id] : null;
                
                // 1. 未学習のカードを最優先 (0)
                if (record == null || record.LastResult == null)
                {
                    Debug.WriteLine($"★未学習★カード優先: {card.id}");
                    return 0;
                }
                
                // 2. 復習時間が来ているカードを次に優先 (1)
                if (record.NextReviewDate <= now)
                {
                    Debug.WriteLine($"★復習時間★カード優先: {card.id}");
                    return 1;
                }
                
                // 3. 前回の回答が不正解のカードを優先 (2)
                if (record.LastResult == false)
                {
                    Debug.WriteLine($"★前回不正解★カード優先: {card.id}");
                    return 2;
                }
                
                // 4. その他は復習時間順 (3以降)
                return 3;
            })
            .ThenBy(card =>
            {
                var record = learningRecords.ContainsKey(card.id) ? learningRecords[card.id] : null;
                return record?.NextReviewDate ?? DateTime.MaxValue;
            })
            .ToList();
            
            // カードが空でないことを確認
            if (!sortedCards.Any())
            {
                sortedCards = cards.ToList();
            }
            
            Debug.WriteLine($"ソート後のカード順序: {sortedCards.Count}件");
            for (int i = 0; i < Math.Min(5, sortedCards.Count); i++)
            {
                var card = sortedCards[i];
                var record = learningRecords.ContainsKey(card.id) ? learningRecords[card.id] : null;
                var status = record?.LastResult == null ? "★未学習★" : 
                           record.NextReviewDate <= now ? "★復習時間★" :
                           record.LastResult == false ? "★前回不正解★" : "学習済み";
                Debug.WriteLine($"{i + 1}. {status} カードID: {card.id}");
            }
        }

        // 学習記録を読み込む
        private void LoadLearningRecords()
        {
            Debug.WriteLine("学習記録の読み込み開始");
            learningRecords.Clear();
            
            try
            {
                string resultFilePath = Path.Combine(tempExtractPath, "result.txt");
                
                if (!File.Exists(resultFilePath))
                {
                    Debug.WriteLine("result.txtが存在しません");
                    return;
                }
                
                var lines = File.ReadAllLines(resultFilePath);
                
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    
                    var parts = line.Split('|');
                    if (parts.Length >= 3)
                    {
                        var cardId = parts[0].Trim();
                        var isCorrect = parts[1].Trim() == "正解";
                        
                        if (DateTime.TryParse(parts[2].Trim(), out DateTime nextReviewDate))
                        {
                            // 既存の記録を取得または新規作成
                            if (!learningRecords.ContainsKey(cardId))
                            {
                                learningRecords[cardId] = new LearningRecord(cardId);
                            }
                            
                            var record = learningRecords[cardId];
                            
                            // 記録を更新
                            if (isCorrect)
                            {
                                record.CorrectCount++;
                            }
                            else
                            {
                                record.IncorrectCount++;
                            }
                            
                            record.LastResult = isCorrect;
                            record.NextReviewDate = nextReviewDate;
                        }
                    }
                }
                
                Debug.WriteLine($"読み込んだ学習記録数: {learningRecords.Count}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"学習記録読み込み中にエラー: {ex.Message}");
            }
        }

        // 復習タイマーを開始
        private void StartReviewTimer()
        {
            StopReviewTimer();
            
            Debug.WriteLine("復習タイマーを開始");
            reviewTimer = new System.Timers.Timer(30000); // 30秒間隔
            reviewTimer.Elapsed += (sender, e) => CheckForReviewCards();
            reviewTimer.AutoReset = true;
            reviewTimer.Enabled = true;
        }

        // 復習タイマーを停止
        private void StopReviewTimer()
        {
            if (reviewTimer != null)
            {
                reviewTimer.Stop();
                reviewTimer.Dispose();
                reviewTimer = null;
                Debug.WriteLine("復習タイマーを停止");
            }
        }

        // 復習が必要なカードをチェック
        private void CheckForReviewCards()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    var now = DateTime.Now;
                    
                    var needsReview = cards.Where(card =>
                    {
                        if (!learningRecords.ContainsKey(card.id)) return false;
                        
                        var record = learningRecords[card.id];
                        var isReviewTime = record.NextReviewDate <= now;
                        var isLastIncorrect = record.LastResult == false;
                        var isNotCurrentCard = currentIndex >= sortedCards.Count || card.id != sortedCards[currentIndex].id;
                        var isNotAlreadyInQueue = !sortedCards.Skip(currentIndex + 1).Any(c => c.id == card.id);
                        
                        return (isReviewTime || isLastIncorrect) && isNotCurrentCard && isNotAlreadyInQueue;
                    }).ToList();
                    
                    if (needsReview.Any())
                    {
                        Debug.WriteLine($"復習対象カード: {needsReview.Count}件");
                        foreach (var card in needsReview)
                        {
                            var record = learningRecords[card.id];
                            var reason = record.NextReviewDate <= now ? "復習時間到達" : "前回不正解";
                            Debug.WriteLine($"- カード{card.id}: {reason}");
                        }
                        PrioritizeReviewCards(needsReview);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"復習チェック中にエラー: {ex.Message}");
                }
            });
        }

        // 復習カードを優先配置
        private void PrioritizeReviewCards(List<CardData> reviewCards)
        {
            var now = DateTime.Now;
            
            var sortedReviewCards = reviewCards.OrderBy(card =>
            {
                var record = learningRecords[card.id];
                
                // 復習時間が来ているカードを優先
                var isReviewTime = record.NextReviewDate <= now;
                var isLastIncorrect = record.LastResult == false;
                
                if (isReviewTime && !isLastIncorrect) return 0;
                if (isReviewTime && isLastIncorrect) return 1;
                if (!isReviewTime && isLastIncorrect) return 2;
                return 3;
            })
            .ThenBy(card => learningRecords[card.id].NextReviewDate)
            .ToList();
            
            var remainingCards = sortedCards.Skip(currentIndex + 1).ToList();
            var newSortedCards = sortedCards.Take(currentIndex + 1).ToList();
            newSortedCards.AddRange(sortedReviewCards);
            newSortedCards.AddRange(remainingCards.Where(c => !sortedReviewCards.Any(r => r.id == c.id)));
            
            sortedCards = newSortedCards;
            
            Debug.WriteLine("復習カードを次に配置完了");
            Debug.WriteLine($"新しい総カード数: {sortedCards.Count}");
            
            // 問題数を更新
            UpdateQuestionNumber();
        }

        // 復習が必要なカードを表示
        private async Task ShowReviewNeededCards()
        {
            var now = DateTime.Now;
            var reviewNeededCards = cards.Where(card =>
            {
                if (!learningRecords.ContainsKey(card.id)) return false;
                var record = learningRecords[card.id];
                return record.NextReviewDate <= now;
            }).ToList();
            
            if (reviewNeededCards.Any())
            {
                sortedCards = reviewNeededCards.OrderBy(card => learningRecords[card.id].NextReviewDate).ToList();
                currentIndex = 0;
                showAnswer = false;
                
                Debug.WriteLine($"復習カードで継続: {sortedCards.Count}件");
                
                // 問題数を更新
                UpdateQuestionNumber();
                
                await DisplayCard();
            }
            else
            {
                Debug.WriteLine("すべての問題が完了しました");
                await UIThreadHelper.ShowAlertAsync("完了", "すべての問題が出題されました。", "OK");
                Navigation.PopAsync();
            }
        }

        // メモリ内の学習記録を更新
        private void UpdateLearningRecord(CardData card, bool isCorrect, DateTime nextReviewDate)
        {
            if (!learningRecords.ContainsKey(card.id))
            {
                learningRecords[card.id] = new LearningRecord(card.id);
            }
            
            var record = learningRecords[card.id];
            
            if (isCorrect)
            {
                record.CorrectCount++;
            }
            else
            {
                record.IncorrectCount++;
            }
            
            record.LastResult = isCorrect;
            record.NextReviewDate = nextReviewDate;
            
            Debug.WriteLine($"学習記録を更新: カード{card.id} - {(isCorrect ? "正解" : "不正解")} - 次回: {nextReviewDate}");
        }

        // 画像拡大表示メソッド（必要に応じて使用）
        private async void ShowImagePopup(string imagePath, string imageFileName = null)
        {
            try
            {
                var popupPage = new ImagePopupPage(imagePath, imageFileName);
                var navigationPage = new NavigationPage(popupPage);
                await Navigation.PushModalAsync(navigationPage);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"画像拡大表示エラー: {ex.Message}");
                await UIThreadHelper.ShowAlertAsync("エラー", "画像の拡大表示に失敗しました。", "OK");
            }
        }

        // テキスト入力モードのトグルイベントハンドラ
        private void OnTextInputModeToggled(object sender, ToggledEventArgs e)
        {
            isTextInputMode = e.Value;
            Debug.WriteLine($"テキスト入力モード: {isTextInputMode}");
            
            // 手動でOFFにした場合は記憶をクリア
            if (!isTextInputMode)
            {
                wasTextInputModeEnabled = false;
                Debug.WriteLine("手動でテキスト入力モードをOFFにしたため、記憶をクリアしました");
            }
            
            // テキスト入力モードを変更した時は解答表示状態をリセット
            showAnswer = false;
            
            // 現在のカードを再表示
            _ = DisplayCard();
        }

        // 穴埋め問題の正解を抽出
        private List<string> ExtractBlankAnswers(string text)
        {
            var answers = new List<string>();
            var regex = new Regex(@"<<blank\|(.*?)>>");
            var matches = regex.Matches(text);
            
            foreach (Match match in matches)
            {
                answers.Add(match.Groups[1].Value);
            }
            
            Debug.WriteLine($"抽出された正解: {string.Join(", ", answers)}");
            return answers;
        }

        // テキスト入力フィールドを生成
        private void CreateTextInputFields()
        {
            // 既存のフィールドをクリア
            TextInputFieldsContainer.Children.Clear();
            textInputEntries.Clear();
            resultLabels.Clear(); // 結果ラベルもクリア
            
            if (!isTextInputMode || correctAnswers.Count == 0)
            {
                TextInputContainer.IsVisible = false;
                return;
            }
            
            for (int i = 0; i < correctAnswers.Count; i++)
            {
                var fieldLayout = new HorizontalStackLayout
                {
                    Spacing = 10,
                    VerticalOptions = LayoutOptions.Center
                };
                
                // 番号ラベル
                var numberLabel = new Label
                {
                    Text = $"({i + 1})",
                    FontSize = 16,
                    FontAttributes = FontAttributes.Bold,
                    VerticalOptions = LayoutOptions.Center,
                    WidthRequest = 40
                };
                numberLabel.SetDynamicResource(Label.TextColorProperty, "TextColor");
                
                // テキスト入力フィールド
                var entry = new Entry
                {
                    Placeholder = "回答を入力",
                    FontSize = 16,
                    HorizontalOptions = LayoutOptions.FillAndExpand
                };
                entry.SetDynamicResource(Entry.TextColorProperty, "TextColor");
                entry.SetDynamicResource(Entry.PlaceholderColorProperty, "TextColor");
                
                textInputEntries.Add(entry);
                
                // 結果表示ラベル
                var resultLabel = new Label
                {
                    Text = "",
                    VerticalOptions = LayoutOptions.Center,
                    FontSize = 16,
                    FontAttributes = FontAttributes.Bold,
                    IsVisible = false,
                    WidthRequest = 60
                };
                resultLabels.Add(resultLabel);
                
                fieldLayout.Children.Add(numberLabel);
                fieldLayout.Children.Add(entry);
                fieldLayout.Children.Add(resultLabel);
                
                TextInputFieldsContainer.Children.Add(fieldLayout);
            }
            
            TextInputContainer.IsVisible = true;
        }

        // テキスト入力の正誤判定
        private bool CheckTextInputAnswers()
        {
            if (!isTextInputMode || textInputEntries.Count != correctAnswers.Count) return false;
            
            bool allCorrect = true;
            
            for (int i = 0; i < textInputEntries.Count; i++)
            {
                var userAnswer = textInputEntries[i].Text?.Trim() ?? "";
                var correctAnswer = correctAnswers[i].Trim();
                var resultLabel = resultLabels[i];
                
                // 全角数字を半角数字に変換し、スペースを除去
                userAnswer = NormalizeText(userAnswer);
                correctAnswer = NormalizeText(correctAnswer);
                
                // 大文字小文字を区別しない比較
                bool isCorrect = string.Equals(userAnswer, correctAnswer, StringComparison.OrdinalIgnoreCase);
                
                if (isCorrect)
                {
                    resultLabel.Text = "✓";
                    resultLabel.TextColor = Colors.Green;
                    Debug.WriteLine($"正解: 入力「{userAnswer}」, 正解「{correctAnswer}」");
                }
                else
                {
                    resultLabel.Text = "✗";
                    resultLabel.TextColor = Colors.Red;
                    allCorrect = false;
                    Debug.WriteLine($"不正解: 入力「{userAnswer}」, 正解「{correctAnswer}」");
                }
                
                resultLabel.IsVisible = true;
            }
            
            if (allCorrect)
            {
                Debug.WriteLine("すべて正解です");
            }
            
            return allCorrect;
        }

        // テキストを正規化（全角数字を半角数字に変換し、スペースを除去）
        private string NormalizeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            
            return text
                .Replace('０', '0')
                .Replace('１', '1')
                .Replace('２', '2')
                .Replace('３', '3')
                .Replace('４', '4')
                .Replace('５', '5')
                .Replace('６', '6')
                .Replace('７', '7')
                .Replace('８', '8')
                .Replace('９', '9')
                .Replace(" ", "")  // 半角スペースを除去
                .Replace("　", ""); // 全角スペースを除去
        }

        // Labelを使用した装飾文字表示の実装例

        // キーボードイベントハンドラーを登録
        private void RegisterKeyboardEvents()
        {
#if WINDOWS
            try
            {
                Debug.WriteLine("キーボードイベント登録開始");
                
                // FrameworkElementから取得
                if (this.Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement frameworkElement)
                {
                    Debug.WriteLine("FrameworkElement取得成功");
                    
                    // 既存のイベントハンドラを削除（重複を防ぐ）
                    frameworkElement.KeyDown -= OnKeyDown;
                    
                    frameworkElement.KeyDown += OnKeyDown;
                    
                    // フォーカス可能に設定してキーボードイベントを受け取れるようにする
                    frameworkElement.IsTabStop = true;
                    frameworkElement.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
                    
                    Debug.WriteLine("FrameworkElementにキーボードイベント設定完了");
                }
                else
                {
                    Debug.WriteLine("FrameworkElementが見つかりませんでした");
                }
                
                // 再試行（1秒後に再度試行）
                if (this.Handler?.PlatformView == null)
                {
                    Debug.WriteLine("ハンドラーがまだ準備できていないため、1秒後に再試行");
                    MainThread.BeginInvokeOnMainThread(async () =>
                    {
                        await Task.Delay(1000);
                        RegisterKeyboardEvents();
                    });
                    return;
                }
                
                Debug.WriteLine("キーボードイベント登録完了");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"キーボードイベント登録エラー: {ex.Message}");
                Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
            }
#endif
        }

        // キーボードイベントハンドラーを解除
        private void UnregisterKeyboardEvents()
        {
#if WINDOWS
            try
            {
                if (this.Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement frameworkElement)
                {
                    frameworkElement.KeyDown -= OnKeyDown;
                    Debug.WriteLine("キーボードイベント解除完了");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"キーボードイベント解除エラー: {ex.Message}");
            }
#endif
        }

        // キーボードイベントハンドラー
        private void OnKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
#if WINDOWS
            try
            {
                Debug.WriteLine($"キーボードイベント: {e.Key}");
                
                // スペースキーが押された場合
                if (e.Key == Windows.System.VirtualKey.Space)
                {
                    Debug.WriteLine("スペースキーが押されました");
                    
                    // 現在のページがQaページであることを確認
                    if (this == Navigation.NavigationStack.LastOrDefault())
                    {
                        // テキスト入力中かどうかをチェック
                        if (IsTextInputFocused())
                        {
                            Debug.WriteLine("テキスト入力中なので、スペースキーを通常通り処理");
                            return; // テキスト入力の場合は通常のスペースキー処理を許可
                        }
                        
                        // トグルやチェックボックスにフォーカスがあるかチェック
                        if (IsToggleOrCheckBoxFocused())
                        {
                            Debug.WriteLine("トグルやチェックボックスにフォーカスがあるため、スペースキーを無視");
                            e.Handled = true; // イベントを処理済みとしてマーク（トグルの切り替えを防ぐ）
                            return;
                        }
                        
                        // アクティブなボタンにフォーカスがあるかチェック
                        if (IsActiveButtonFocused())
                        {
                            e.Handled = true; // イベントを処理済みとしてマーク
                            
                            MainThread.BeginInvokeOnMainThread(() =>
                            {
                                HandleSpaceKeyPress();
                            });
                        }
                        else
                        {
                            Debug.WriteLine("アクティブなボタンにフォーカスがないため、ボタンにフォーカスを戻してから処理");
                            e.Handled = true; // イベントを処理済みとしてマーク
                            
                            MainThread.BeginInvokeOnMainThread(() =>
                            {
                                SetFocusToActiveButton();
                                // 少し遅延させてからスペースキー処理を実行
                                MainThread.BeginInvokeOnMainThread(async () =>
                                {
                                    await Task.Delay(50);
                                    HandleSpaceKeyPress();
                                });
                            });
                        }
                    }
                    else
                    {
                        Debug.WriteLine("Qaページが最前面ではないため、スペースキーを無視");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"キーボードイベント処理エラー: {ex.Message}");
            }
#endif
        }

        // ページにフォーカスを設定
        private void SetPageFocus()
        {
#if WINDOWS
            try
            {
                if (this.Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement frameworkElement)
                {
                    frameworkElement.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
                    Debug.WriteLine("Qaページにフォーカスを設定しました");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"フォーカス設定エラー: {ex.Message}");
            }
#endif
        }

        // フォーカスされている要素を取得
        private object GetFocusedElement()
        {
#if WINDOWS
            try
            {
                if (this.Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement frameworkElement)
                {
                    var focusedElement = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(frameworkElement.XamlRoot);
                    Debug.WriteLine($"フォーカスされている要素: {focusedElement?.GetType().Name}");
                    return focusedElement;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"フォーカス要素取得エラー: {ex.Message}");
            }
#endif
            return null;
        }

        // アクティブなボタンにフォーカスがあるかチェック
        private bool IsActiveButtonFocused()
        {
#if WINDOWS
            try
            {
                var focusedElement = GetFocusedElement();
                if (focusedElement == null) return false;
                
                var elementType = focusedElement.GetType().Name;
                Debug.WriteLine($"フォーカス要素タイプ: {elementType}");
                
                // 現在表示されているボタンをチェック
                if (ShowAnswerButton.IsVisible)
                {
                    if (IsElementFocused(ShowAnswerButton))
                    {
                        Debug.WriteLine("「解答を表示」ボタンにフォーカスがあります");
                        return true;
                    }
                }
                else if (Correct.IsVisible && Incorrect.IsVisible)
                {
                    if (IsElementFocused(Correct))
                    {
                        Debug.WriteLine("「正解」ボタンにフォーカスがあります");
                        return true;
                    }
                    if (IsElementFocused(Incorrect))
                    {
                        Debug.WriteLine("「不正解」ボタンにフォーカスがあります");
                        return true;
                    }
                }
                else if (NextButton.IsVisible)
                {
                    if (IsElementFocused(NextButton))
                    {
                        Debug.WriteLine("「次へ」ボタンにフォーカスがあります");
                        return true;
                    }
                }
                
                Debug.WriteLine("アクティブなボタンにフォーカスがありません");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"アクティブボタンフォーカスチェックエラー: {ex.Message}");
                return false;
            }
#endif
            return false;
        }

        // 指定された要素にフォーカスがあるかチェック
        private bool IsElementFocused(VisualElement element)
        {
#if WINDOWS
            try
            {
                if (element?.Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement frameworkElement)
                {
                    var focusedElement = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(frameworkElement.XamlRoot);
                    return focusedElement == frameworkElement;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"要素フォーカスチェックエラー: {ex.Message}");
            }
#endif
            return false;
        }

        // アクティブなボタンにフォーカスを設定
        private void SetFocusToActiveButton()
        {
#if WINDOWS
            try
            {
                // 現在フォーカスされている要素をチェック
                var focusedElement = GetFocusedElement();
                if (focusedElement != null)
                {
                    var elementType = focusedElement.GetType().Name;
                    Debug.WriteLine($"現在のフォーカス要素: {elementType}");
                    
                    // トグルやチェックボックスにフォーカスがある場合は、フォーカスを設定しない
                    if (IsToggleOrCheckBox(focusedElement))
                    {
                        Debug.WriteLine("トグルやチェックボックスにフォーカスがあるため、フォーカス設定をスキップ");
                        return;
                    }
                }
                
                // テキスト入力要素にフォーカスがある場合も、フォーカスを設定しない
                if (IsTextInputFocused())
                {
                    Debug.WriteLine("テキスト入力要素にフォーカスがあるため、フォーカス設定をスキップ");
                    return;
                }
                
                if (ShowAnswerButton.IsVisible)
                {
                    SetElementFocus(ShowAnswerButton);
                    Debug.WriteLine("「解答を表示」ボタンにフォーカスを設定");
                }
                else if (Correct.IsVisible && Incorrect.IsVisible)
                {
                    SetElementFocus(Correct);
                    Debug.WriteLine("「正解」ボタンにフォーカスを設定");
                }
                else if (NextButton.IsVisible)
                {
                    SetElementFocus(NextButton);
                    Debug.WriteLine("「次へ」ボタンにフォーカスを設定");
                }
                else
                {
                    Debug.WriteLine("アクティブなボタンが見つかりません");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"アクティブボタンフォーカス設定エラー: {ex.Message}");
            }
#endif
        }

        // トグルやチェックボックスにフォーカスがあるかチェック
        private bool IsToggleOrCheckBoxFocused()
        {
#if WINDOWS
            try
            {
                var focusedElement = GetFocusedElement();
                if (focusedElement == null) return false;
                
                return IsToggleOrCheckBox(focusedElement);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"トグル/チェックボックスフォーカスチェックエラー: {ex.Message}");
                return false;
            }
#endif
            return false;
        }

        // トグルやチェックボックスかどうかを判定
        private bool IsToggleOrCheckBox(object element)
        {
#if WINDOWS
            if (element == null) return false;
            
            var elementType = element.GetType().Name;
            Debug.WriteLine($"トグル/チェックボックスチェック - 要素タイプ: {elementType}");
            
            // トグルやチェックボックスの要素をチェック
            var toggleTypes = new[]
            {
                "ToggleSwitch", // Switch
                "CheckBox",
                "ToggleButton", // Switchの別名
                "CheckBoxPresenter", // CheckBoxの内部要素
                "ToggleButtonPresenter" // Switchの内部要素
            };
            
            foreach (var type in toggleTypes)
            {
                if (elementType.Contains(type))
                {
                    Debug.WriteLine($"トグル/チェックボックス要素を検出: {elementType}");
                    return true;
                }
            }
            
            // 親要素もチェック（トグルやチェックボックスの内部要素の場合）
            try
            {
                if (element is Microsoft.UI.Xaml.FrameworkElement frameworkElement)
                {
                    var parent = frameworkElement.Parent;
                    while (parent != null)
                    {
                        var parentType = parent.GetType().Name;
                        Debug.WriteLine($"親要素タイプ: {parentType}");
                        
                        foreach (var type in toggleTypes)
                        {
                            if (parentType.Contains(type))
                            {
                                Debug.WriteLine($"親要素でトグル/チェックボックス要素を検出: {parentType}");
                                return true;
                            }
                        }
                        
                        if (parent is Microsoft.UI.Xaml.FrameworkElement parentFrameworkElement)
                        {
                            parent = parentFrameworkElement.Parent;
                        }
                        else
                        {
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"親要素チェックエラー: {ex.Message}");
            }
            
            Debug.WriteLine("トグル/チェックボックス要素ではありません");
            return false;
#endif
            return false;
        }

        // 指定された要素にフォーカスを設定
        private void SetElementFocus(VisualElement element)
        {
#if WINDOWS
            try
            {
                if (element?.Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement frameworkElement)
                {
                    frameworkElement.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"要素フォーカス設定エラー: {ex.Message}");
            }
#endif
        }

        // テキスト入力中かどうかをチェック
        private bool IsTextInputFocused()
        {
#if WINDOWS
            try
            {
                var focusedElement = GetFocusedElement();
                if (focusedElement == null) return false;
                
                var elementType = focusedElement.GetType().Name;
                Debug.WriteLine($"テキスト入力チェック - フォーカス要素タイプ: {elementType}");
                
                // テキスト入力要素をチェック
                var textInputTypes = new[]
                {
                    "TextBox", // Entry
                    "RichEditBox", // Editor
                    "PasswordBox" // PasswordEntry
                };
                
                foreach (var type in textInputTypes)
                {
                    if (elementType.Contains(type))
                    {
                        Debug.WriteLine($"テキスト入力要素を検出: {elementType}");
                        return true;
                    }
                }
                
                // 親要素もチェック（テキスト入力要素の内部要素の場合）
                try
                {
                    if (focusedElement is Microsoft.UI.Xaml.FrameworkElement frameworkElement)
                    {
                        var parent = frameworkElement.Parent;
                        while (parent != null)
                        {
                            var parentType = parent.GetType().Name;
                            Debug.WriteLine($"親要素タイプ: {parentType}");
                            
                            foreach (var type in textInputTypes)
                            {
                                if (parentType.Contains(type))
                                {
                                    Debug.WriteLine($"親要素でテキスト入力要素を検出: {parentType}");
                                    return true;
                                }
                            }
                            
                            if (parent is Microsoft.UI.Xaml.FrameworkElement parentFrameworkElement)
                            {
                                parent = parentFrameworkElement.Parent;
                            }
                            else
                            {
                                break;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"親要素チェックエラー: {ex.Message}");
                }
                
                Debug.WriteLine("テキスト入力要素ではありません");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"テキスト入力チェックエラー: {ex.Message}");
                return false;
            }
#endif
            return false;
        }

        // スペースキーが押されたときの処理
        private void HandleSpaceKeyPress()
        {
            try
            {
                // 現在表示されているボタンに応じて適切なアクションを実行
                if (ShowAnswerButton.IsVisible)
                {
                    // 「解答を表示」ボタンが表示されている場合
                    OnShowAnswerClicked(ShowAnswerButton, EventArgs.Empty);
                }
                else if (Correct.IsVisible && Incorrect.IsVisible)
                {
                    // 「正解」「不正解」ボタンが表示されている場合
                    // デフォルトで「正解」を選択
                    OnCorrectClicked(Correct, EventArgs.Empty);
                }
                else if (NextButton.IsVisible)
                {
                    // 「次へ」ボタンが表示されている場合
                    OnNextClicked(NextButton, EventArgs.Empty);
                }
                
                Debug.WriteLine("スペースキーでボタンを押しました");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"スペースキー処理エラー: {ex.Message}");
            }
        }

        // デフォルト設定を読み込む
        private async void LoadDefaultTextInputModeSetting()
        {
            try
            {
                var defaultTextInputMode = await SecureStorage.GetAsync("default_text_input_mode");
                isDefaultTextInputModeEnabled = defaultTextInputMode == "true";
                Debug.WriteLine($"デフォルトテキスト入力モード設定を読み込み: {isDefaultTextInputModeEnabled}");
                
                // デフォルト設定が有効で、現在blankがあるカードの場合はテキスト入力モードを有効化
                if (isDefaultTextInputModeEnabled && sortedCards != null && sortedCards.Count > 0 && currentIndex < sortedCards.Count)
                {
                    var currentCard = sortedCards[currentIndex];
                    var blankAnswers = ExtractBlankAnswers(currentCard.front ?? "");
                    if (blankAnswers.Count > 0)
                    {
                        isTextInputMode = true;
                        wasTextInputModeEnabled = false; // デフォルト設定による有効化なので記憶をクリア
                        Debug.WriteLine("デフォルト設定により、テキスト入力モードを有効化しました");
                    }
                }
                
                // 設定読み込み後に現在のカードを再表示
                if (sortedCards != null && sortedCards.Count > 0 && currentIndex < sortedCards.Count)
                {
                    await DisplayCard();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"デフォルトテキスト入力モード設定の読み込み中にエラー: {ex.Message}");
                isDefaultTextInputModeEnabled = false;
            }
        }

        // ボタンの表示状態を復元
        private void RestoreButtonStates()
        {
            if (showAnswer)
            {
                // 解答が表示されている場合
                ShowAnswerButton.IsVisible = false;
                
                if (BasicCardLayout.IsVisible)
                {
                    // 基本カードの場合
                    if (isTextInputMode && correctAnswers.Count > 0)
                    {
                        // テキスト入力モードでblankがある場合は「次へ」ボタンを表示
                        NextButton.IsVisible = true;
                        Correct.IsVisible = false;
                        Incorrect.IsVisible = false;
                        SeparatorGrid.IsVisible = false;
                    }
                    else
                    {
                        // 通常モードまたはblankがない場合は正解・不正解ボタンを表示
                        NextButton.IsVisible = false;
                        Correct.IsVisible = true;
                        Incorrect.IsVisible = true;
                        SeparatorGrid.IsVisible = true;
                    }
                }
                else if (ChoiceCardLayout.IsVisible)
                {
                    // 選択肢カードの場合
                    NextButton.IsVisible = true;
                    Correct.IsVisible = false;
                    Incorrect.IsVisible = false;
                    SeparatorGrid.IsVisible = false;
                }
                else if (ImageFillCardLayout.IsVisible)
                {
                    // 画像穴埋めカードの場合
                    NextButton.IsVisible = false;
                    Correct.IsVisible = true;
                    Incorrect.IsVisible = true;
                    SeparatorGrid.IsVisible = true;
                }
            }
            else
            {
                // 解答が表示されていない場合
                ShowAnswerButton.IsVisible = true;
                NextButton.IsVisible = false;
                Correct.IsVisible = false;
                Incorrect.IsVisible = false;
                SeparatorGrid.IsVisible = false;
            }
        }

        // 問題数を更新
        private void UpdateQuestionNumber()
        {
            try
            {
                if (sortedCards != null && sortedCards.Count > 0)
                {
                    int currentQuestionNumber = currentIndex + 1;
                    int totalQuestions = sortedCards.Count;
                    QuestionNumberLabel.Text = $"問題 {currentQuestionNumber} / {totalQuestions}";
                    Debug.WriteLine($"問題数を更新: {currentQuestionNumber} / {totalQuestions}");
                }
                else
                {
                    QuestionNumberLabel.Text = "問題 0 / 0";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"問題数更新エラー: {ex.Message}");
            }
        }

        // トグルの状態をローカル変数と同期
        private void SyncToggleState()
        {
            // イベントハンドラを一時的に無効化
            TextInputModeToggle.Toggled -= OnTextInputModeToggled;
            
            if (TextInputModeToggle.IsToggled != isTextInputMode)
            {
                TextInputModeToggle.IsToggled = isTextInputMode;
            }
            
            // イベントハンドラを再登録
            TextInputModeToggle.Toggled += OnTextInputModeToggled;
        }
    }
}

