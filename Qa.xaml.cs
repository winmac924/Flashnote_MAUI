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
        private List<SKRect> selectionRects = new List<SKRect>();
        // 各問題ごとの正解・不正解回数を管理
        private Dictionary<int, CardResult> results = new Dictionary<int, CardResult>();
        private Dictionary<string, LearningRecord> learningRecords = new Dictionary<string, LearningRecord>();
        private bool showAnswer = false;  // 解答表示フラグ
        private string frontText = "";

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

            // RichTextLabelにテキストを設定
            FrontPreviewLabel.ImageFolderPath = tempExtractPath;
            FrontPreviewLabel.RichText = frontText;
            FrontPreviewLabel.ShowAnswer = false;

            Debug.WriteLine($"FrontPreviewLabel.ImageFolderPath (設定後): {FrontPreviewLabel.ImageFolderPath}");

            // 裏面が空でない場合のみ設定
            if (!string.IsNullOrWhiteSpace(backText))
            {
                BackPreviewLabel.ImageFolderPath = tempExtractPath;
                BackPreviewLabel.RichText = backText;
                BackPreviewLabel.ShowAnswer = false;
            }

            BackPreviewFrame.IsVisible = false;
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
                ChoiceExplanationLabel.ShowAnswer = false;
                ChoiceExplanationLabel.ImageFolderPath = tempExtractPath;
            }
            ChoiceExplanationFrame.IsVisible = false;
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

            // 範囲の追加
            foreach (var line in card.selectionRects)
                {
                selectionRects.Add(new SKRect(line.x, line.y, line.x + line.width, line.y + line.height));
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

                    // 正規化座標を実際の座標に変換して範囲を表示
                    foreach (var normalizedRect in selectionRects)
                    {
                        // 正規化座標を実際の画像座標に変換
                        float actualX = normalizedRect.Left * bitmap.Width;
                        float actualY = normalizedRect.Top * bitmap.Height;
                        float actualWidth = normalizedRect.Width * bitmap.Width;
                        float actualHeight = normalizedRect.Height * bitmap.Height;
                        
                        // 画像座標をキャンバス座標に変換
                        float canvasX = imageRect.Left + (actualX * scale);
                        float canvasY = imageRect.Top + (actualY * scale);
                        float canvasWidth = actualWidth * scale;
                        float canvasHeight = actualHeight * scale;
                        
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

                        Debug.WriteLine($"正規化座標: {normalizedRect.Left:F3}, {normalizedRect.Top:F3}, {normalizedRect.Width:F3}, {normalizedRect.Height:F3}");
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
            }
        }

        private async void OnNextClicked(object sender, EventArgs e)
        {
            try
            {
                currentIndex++;
                NextButton.IsVisible = false;
                Debug.WriteLine($"次の問題へ移動: {currentIndex + 1}/{cards.Count}");
                await DisplayCard();
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
                await DisplayCard();
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
                await DisplayCard();
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

        // Labelを使用した装飾文字表示の実装例

    }
}

