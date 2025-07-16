using System.Text.Json;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.IO.Compression;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using System.Web;
using System.Reflection;
using Flashnote.Views;
using Flashnote.Models;

namespace Flashnote
{
    public partial class Edit : ContentPage
    {
        private string tempExtractPath;
        private string ankplsFilePath;
        private List<CardInfo> cards = new List<CardInfo>();
        private List<CardInfo> filteredCards = new List<CardInfo>(); // 検索結果用
        private string editCardId = null;    // 編集対象のカードID
        private bool isDirty = false;        // 変更があるかどうかのフラグ
        private System.Timers.Timer autoSaveTimer;  // 自動保存用タイマー
        private List<SKRect> selectionRects = new List<SKRect>();  // 画像穴埋め用の選択範囲（画像座標）
        private SKBitmap imageBitmap;         // 画像を表示するためのビットマップ
        private string selectedImagePath;     // 選択された画像のパス
        private SKPoint startPoint, endPoint;
        private bool isDragging = false;
        private bool isMoving = false;
        private bool isResizing = false;
        private int selectedRectIndex = -1;
        private int resizeHandle = -1; // 0:左上, 1:右上, 2:左下, 3:右下
        private SKPoint dragOffset;
        private const float HANDLE_SIZE = 25; // ハンドルの表示サイズ
        private const float HANDLE_HIT_SIZE = 35; // ハンドルのクリック判定サイズ（より大きい）
        private const float MAX_CANVAS_WIDTH = 600f;  // キャンバスの最大幅
        private const float MAX_CANVAS_HEIGHT = 800f; // キャンバスの最大高さ

        /// <summary>
        /// 画像座標をキャンバス座標に変換する（iOS版に合わせて絶対座標を使用）
        /// </summary>
        private SKRect ImageToCanvasRect(SKRect imageRect, float canvasWidth, float canvasHeight)
        {
            // 画像の実際のサイズとキャンバスサイズの比率を計算
            float scaleX = 1.0f;
            float scaleY = 1.0f;
            
            if (imageBitmap != null)
            {
                scaleX = canvasWidth / imageBitmap.Width;
                scaleY = canvasHeight / imageBitmap.Height;
            }
            
            var result = new SKRect(
                imageRect.Left * scaleX,
                imageRect.Top * scaleY,
                imageRect.Right * scaleX,
                imageRect.Bottom * scaleY
            );
            
            Debug.WriteLine($"画像座標変換: 入力={imageRect}, 画像サイズ={imageBitmap?.Width}x{imageBitmap?.Height}, キャンバスサイズ={canvasWidth}x{canvasHeight}, 拡大率=({scaleX:F3},{scaleY:F3}), 出力={result}");
            return result;
        }

        /// <summary>
        /// キャンバス座標を画像座標に変換する（iOS版に合わせて絶対座標を使用）
        /// </summary>
        private SKRect CanvasToImageRect(SKRect canvasRect, float canvasWidth, float canvasHeight)
        {
            // 画像の実際のサイズとキャンバスサイズの比率を計算
            float scaleX = 1.0f;
            float scaleY = 1.0f;
            
            if (imageBitmap != null)
            {
                scaleX = canvasWidth / imageBitmap.Width;
                scaleY = canvasHeight / imageBitmap.Height;
            }
            
            return new SKRect(
                canvasRect.Left / scaleX,
                canvasRect.Top / scaleY,
                canvasRect.Right / scaleX,
                canvasRect.Bottom / scaleY
            );
        }

        /// <summary>
        /// 画像のアスペクト比に合わせてキャンバスサイズを調整する
        /// </summary>
        private void AdjustCanvasSizeToImage()
        {
            if (imageBitmap == null)
            {
                // 画像がない場合はデフォルトサイズ
                CanvasView.WidthRequest = 400;
                CanvasView.HeightRequest = 300;
                return;
            }

            float imageAspect = (float)imageBitmap.Width / imageBitmap.Height;
            float canvasWidth, canvasHeight;

            if (imageAspect > 1.0f)
            {
                // 横長の画像
                canvasWidth = Math.Min(MAX_CANVAS_WIDTH, imageBitmap.Width);
                canvasHeight = canvasWidth / imageAspect;
            }
            else
            {
                // 縦長の画像
                canvasHeight = Math.Min(MAX_CANVAS_HEIGHT, imageBitmap.Height);
                canvasWidth = canvasHeight * imageAspect;
            }

            // 最小サイズを確保
            canvasWidth = Math.Max(canvasWidth, 200);
            canvasHeight = Math.Max(canvasHeight, 150);

            Debug.WriteLine($"キャンバスサイズ調整: 画像={imageBitmap.Width}x{imageBitmap.Height}, アスペクト比={imageAspect:F2}, キャンバス={canvasWidth:F0}x{canvasHeight:F0}");

            CanvasView.WidthRequest = canvasWidth;
            CanvasView.HeightRequest = canvasHeight;
            
            // キャンバスを再描画してサイズ変更を反映
            CanvasView.InvalidateSurface();
        }

        public class CardInfo
        {
            public string Id { get; set; }
            public string FrontText { get; set; }
            public string ImageInfo { get; set; }
            public bool HasImage { get; set; }
            public string LastModified { get; set; }
        }

        public Edit(string notePath, string tempPath)
        {
            try
            {
                Debug.WriteLine("Edit.xaml.cs コンストラクタ開始");
                InitializeComponent();

                tempExtractPath = tempPath;
                ankplsFilePath = notePath;

                // ノート名を設定
                NoteTitleLabel.Text = Path.GetFileNameWithoutExtension(ankplsFilePath);

                // カード情報を読み込む
                LoadCards();

                // 自動保存タイマーの設定
                autoSaveTimer = new System.Timers.Timer(5000); // 5秒ごとに自動保存
                autoSaveTimer.Elapsed += AutoSaveTimer_Elapsed;
                autoSaveTimer.AutoReset = false; // 一度だけ実行

                // テキスト変更イベントの設定
                FrontTextEditor.TextChanged += OnTextChanged;
                BackTextEditor.TextChanged += OnTextChanged;
                ChoiceQuestion.TextChanged += OnChoiceQuestionTextChanged;
                ChoiceQuestionExplanation.TextChanged += OnChoiceExplanationTextChanged;

                // カードタイプの初期設定
                CardTypePicker.SelectedIndex = 0;

                // 削除ボタンを初期状態で非表示にする
                UpdateDeleteButtonVisibility(false);

                Debug.WriteLine(tempExtractPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Edit.xaml.cs コンストラクタでエラー: {ex.Message}");
                Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                throw;
            }
        }

        private void LoadCards()
        {
            try
            {
                Debug.WriteLine("LoadCards開始");
                var cardsFilePath = Path.Combine(tempExtractPath, "cards.txt");
                var cardsDirPath = Path.Combine(tempExtractPath, "cards");

                if (File.Exists(cardsFilePath))
                {
                    var lines = File.ReadAllLines(cardsFilePath);
                    if (lines.Length > 1) // 1行目はカード数
                    {
                        cards.Clear();
                        for (int i = 1; i < lines.Length; i++)
                        {
                            var parts = lines[i].Split(',');
                            if (parts.Length >= 2)
                            {
                                var cardId = parts[0];
                                
                                // 削除フラグが付いているカードは読み込まない
                                if (parts.Length >= 3 && parts[2].Trim() == "deleted")
                                {
                                    Debug.WriteLine($"削除フラグ付きカードをスキップ: {cardId}");
                                    continue;
                                }
                                
                                var lastModified = DateTime.ParseExact(parts[1].Trim(), "yyyy-MM-dd HH:mm:ss", null);
                                var jsonPath = Path.Combine(cardsDirPath, $"{cardId}.json");

                                if (File.Exists(jsonPath))
                                {
                                    var jsonContent = File.ReadAllText(jsonPath);
                                    var cardData = JsonSerializer.Deserialize<CardData>(jsonContent);

                                    var cardInfo = new CardInfo
                                    {
                                        Id = cardId,
                                        FrontText = cardData.type == "選択肢" ? cardData.question : cardData.front,
                                        LastModified = lastModified.ToString("yyyy-MM-dd HH:mm:ss")
                                    };

                                    // 画像情報を取得
                                    var imageMatches = System.Text.RegularExpressions.Regex.Matches(cardInfo.FrontText, @"<<img_\d{8}_\d{6}\.jpg>>");
                                    if (imageMatches.Count > 0)
                                    {
                                        cardInfo.HasImage = true;
                                        cardInfo.ImageInfo = $"画像: {string.Join(", ", imageMatches.Select(m => m.Value))}";
                                    }

                                    cards.Add(cardInfo);
                                }
                            }
                        }

                        // カードを最終更新日時の降順でソート
                        cards = cards.OrderByDescending(c => DateTime.Parse(c.LastModified)).ToList();
                        filteredCards = new List<CardInfo>(cards);
                        CardsCollectionView.ItemsSource = filteredCards;
                        TotalCardsLabel.Text = $"カード枚数: {cards.Count}";
                        UpdateSearchResult();
                    }
                }
                Debug.WriteLine("LoadCards完了");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadCardsでエラー: {ex.Message}");
                Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                throw;
            }
        }

        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            Debug.WriteLine($"OnTextChanged: {sender.GetType().Name} - isDirtyをtrueに設定");
            isDirty = true;
            // タイマーをリセット
            autoSaveTimer.Stop();
            autoSaveTimer.Start();
        }

        private void FrontOnTextChanged(object sender, TextChangedEventArgs e)
        {
            isDirty = true;
            autoSaveTimer.Stop();
            autoSaveTimer.Start();

            // JavaScript手法を使用してプレビューを更新（デバウンス付き）
            UpdateFrontPreviewWithDebounce(e.NewTextValue ?? "");
        }

        private void BackOnTextChanged(object sender, TextChangedEventArgs e)
        {
            isDirty = true;
            autoSaveTimer.Stop();
            autoSaveTimer.Start();

            // JavaScript手法を使用してプレビューを更新（デバウンス付き）
            UpdateBackPreviewWithDebounce(e.NewTextValue ?? "");
        }

        /// <summary>
        /// 表面プレビューを更新（デバウンス付き）
        /// </summary>
        private System.Timers.Timer _frontPreviewTimer;
        private void UpdateFrontPreviewWithDebounce(string text)
        {
            // 既存のタイマーを停止
            _frontPreviewTimer?.Stop();
            _frontPreviewTimer?.Dispose();
            
            // 新しいタイマーを作成（300ms後に実行）
            _frontPreviewTimer = new System.Timers.Timer(300);
            _frontPreviewTimer.Elapsed += (s, e) =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        // デバッグ情報を出力
                        Debug.WriteLine($"=== Edit.xaml.cs UpdateFrontPreviewWithDebounce ===");
                        Debug.WriteLine($"tempExtractPath: {tempExtractPath}");
                        Debug.WriteLine($"FrontPreviewLabel.ImageFolderPath (設定前): {FrontPreviewLabel.ImageFolderPath}");
                        
                        FrontPreviewLabel.ImageFolderPath = tempExtractPath;
                        FrontPreviewLabel.RichText = text;
                        FrontPreviewLabel.ShowAnswer = false;
                        
                        Debug.WriteLine($"FrontPreviewLabel.ImageFolderPath (設定後): {FrontPreviewLabel.ImageFolderPath}");
                        Debug.WriteLine($"FrontPreviewLabel.RichText: {FrontPreviewLabel.RichText}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"表面プレビュー更新エラー: {ex.Message}");
                    }
                });
                _frontPreviewTimer?.Stop();
                _frontPreviewTimer?.Dispose();
                _frontPreviewTimer = null;
            };
            _frontPreviewTimer.AutoReset = false;
            _frontPreviewTimer.Start();
        }

        /// <summary>
        /// 裏面プレビューを更新（デバウンス付き）
        /// </summary>
        private System.Timers.Timer _backPreviewTimer;
        private void UpdateBackPreviewWithDebounce(string text)
        {
            // 既存のタイマーを停止
            _backPreviewTimer?.Stop();
            _backPreviewTimer?.Dispose();
            
            // 新しいタイマーを作成（300ms後に実行）
            _backPreviewTimer = new System.Timers.Timer(300);
            _backPreviewTimer.Elapsed += (s, e) =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        // デバッグ情報を出力
                        Debug.WriteLine($"=== Edit.xaml.cs UpdateBackPreviewWithDebounce ===");
                        Debug.WriteLine($"tempExtractPath: {tempExtractPath}");
                        Debug.WriteLine($"BackPreviewLabel.ImageFolderPath (設定前): {BackPreviewLabel.ImageFolderPath}");
                        
                        BackPreviewLabel.ImageFolderPath = tempExtractPath;
                        BackPreviewLabel.RichText = text;
                        BackPreviewLabel.ShowAnswer = false;
                        
                        Debug.WriteLine($"BackPreviewLabel.ImageFolderPath (設定後): {BackPreviewLabel.ImageFolderPath}");
                        Debug.WriteLine($"BackPreviewLabel.RichText: {BackPreviewLabel.RichText}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"裏面プレビュー更新エラー: {ex.Message}");
                    }
                });
                _backPreviewTimer?.Stop();
                _backPreviewTimer?.Dispose();
                _backPreviewTimer = null;
            };
            _backPreviewTimer.AutoReset = false;
            _backPreviewTimer.Start();
        }

        private async void AutoSaveTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            Debug.WriteLine($"自動保存タイマー: isDirty={isDirty}, editCardId={editCardId}");
            
            if (isDirty && !string.IsNullOrEmpty(editCardId))
            {
                Debug.WriteLine("自動保存タイマー: 内容が変更されているため保存を実行");
                await SaveCurrentCard();
            }
            else
            {
                if (string.IsNullOrEmpty(editCardId))
                {
                    Debug.WriteLine("自動保存タイマー: editCardIdが空のため保存をスキップ");
                }
                else
                {
                    Debug.WriteLine("自動保存タイマー: 内容に変更がないため保存をスキップ");
                }
            }
        }

        private async Task SaveCurrentCard()
        {
            try
            {
                if (string.IsNullOrEmpty(editCardId)) return;

                string cardType = CardTypePicker.SelectedItem as string;
                string frontText = FrontTextEditor.Text?.Replace("\r\n", "\n").Replace("\r", "\n") ?? "";
                string backText = BackTextEditor.Text?.Replace("\r\n", "\n").Replace("\r", "\n") ?? "";
                string choiceQuestion = ChoiceQuestion.Text?.Replace("\r\n", "\n").Replace("\r", "\n") ?? "";
                string choiceExplanation = ChoiceQuestionExplanation.Text?.Replace("\r\n", "\n").Replace("\r", "\n") ?? "";

                var choices = new List<object>();
                foreach (var stack in ChoicesContainer.Children.OfType<StackLayout>())
                {
                    var entry = stack.Children.OfType<Editor>().FirstOrDefault();
                    var checkBox = stack.Children.OfType<CheckBox>().FirstOrDefault();

                    if (entry != null && !string.IsNullOrWhiteSpace(entry.Text))
                    {
                        string cleanText = Regex.Replace(entry.Text, @"^\d+\.\s*", "").Trim()
                            .Replace("\r\n", "\n")
                            .Replace("\r", "\n");
                        bool isCorrect = checkBox?.IsChecked == true;
                        choices.Add(new
                        {
                            isCorrect = isCorrect,
                            text = cleanText
                        });
                    }
                }

                // カード情報をJSONとして保存
                var cardData = new
                {
                    id = editCardId,
                    type = cardType,
                    front = cardType == "画像穴埋め" ? selectedImagePath : frontText,  // 画像穴埋めの場合は画像ファイル名を保存
                    back = backText,
                    question = choiceQuestion,
                    explanation = choiceExplanation,
                    choices = choices,
                    selectionRects = selectionRects.Select(r => new
                    {
                        x = r.Left,  // 画像座標（ピクセル単位）
                        y = r.Top,   // 画像座標（ピクセル単位）
                        width = r.Width,  // 画像座標（ピクセル単位）
                        height = r.Height // 画像座標（ピクセル単位）
                    }).ToList(),
                    imagePath = selectedImagePath  // 画像穴埋め用の画像パス
                };

                string jsonPath = Path.Combine(tempExtractPath, "cards", $"{editCardId}.json");
                string jsonContent = JsonSerializer.Serialize(cardData, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                });
                await File.WriteAllTextAsync(jsonPath, jsonContent);

                // 変更があった場合のみcards.txtの更新日時を更新
                if (isDirty)
                {
                    string cardsFilePath = Path.Combine(tempExtractPath, "cards.txt");
                    if (File.Exists(cardsFilePath))
                    {
                        var lines = await File.ReadAllLinesAsync(cardsFilePath);
                        var newLines = new List<string>();
                        bool cardUpdated = false;

                        // 1行目は画像番号なのでそのまま保持
                        if (lines.Length > 0)
                        {
                            newLines.Add(lines[0]);
                        }

                        // カード情報の更新
                        for (int i = 1; i < lines.Length; i++)
                        {
                            var parts = lines[i].Split(',');
                            if (parts.Length >= 2 && parts[0] == editCardId)
                            {
                                // 更新日時を現在時刻に更新
                                newLines.Add($"{editCardId},{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                                cardUpdated = true;
                            }
                            else
                            {
                                newLines.Add(lines[i]);
                            }
                        }

                        // カードが存在しない場合は新規追加
                        if (!cardUpdated)
                        {
                            newLines.Add($"{editCardId},{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                        }

                        await File.WriteAllLinesAsync(cardsFilePath, newLines);
                        Debug.WriteLine($"cards.txtの更新日時を更新しました: {editCardId}");
                    }
                }
                else
                {
                    Debug.WriteLine("内容に変更がないため、cards.txtの更新日時は更新しません");
                }

                // .ankpls を更新（内容が変更された場合のみ）
                if (isDirty)
                {
                    if (File.Exists(ankplsFilePath))
                    {
                        File.Delete(ankplsFilePath);
                    }
                    ZipFile.CreateFromDirectory(tempExtractPath, ankplsFilePath);
                    Debug.WriteLine(".ankplsファイルを更新しました");
                }
                else
                {
                    Debug.WriteLine("内容に変更がないため、.ankplsファイルは更新しません");
                }

                isDirty = false;
                Debug.WriteLine("カードを自動保存しました");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"自動保存でエラー: {ex.Message}");
                Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
            }
        }

        private async void OnCardSelected(object sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is CardInfo selectedCard)
            {
                // カード選択時は保存処理を行わない（内容が変更されていても）
                // 前のカードの変更は画面を離れる時（OnDisappearing）に保存される
                Debug.WriteLine($"カード選択開始: {selectedCard.Id} - 保存処理は行いません");

                // 新しいカードを読み込む
                editCardId = selectedCard.Id;
                await LoadCardData(selectedCard.Id);
                
                // 削除ボタンを表示する
                UpdateDeleteButtonVisibility(true);
                
                // プレビューを強制更新
                await Task.Delay(200); // WebView初期化を待つ
                
                var cardType = CardTypePicker.SelectedItem?.ToString();
                Debug.WriteLine($"カード選択後のプレビュー更新: {cardType}");
                
                switch (cardType)
                {
                    case "基本・穴埋め":
                        Debug.WriteLine("基本カードのプレビューを更新");
                        FrontPreviewLabel.RichText = FrontTextEditor.Text ?? "";
                        FrontPreviewLabel.ShowAnswer = false;
                        FrontPreviewLabel.ImageFolderPath = tempExtractPath;
                        
                        BackPreviewLabel.RichText = BackTextEditor.Text ?? "";
                        BackPreviewLabel.ShowAnswer = false;
                        BackPreviewLabel.ImageFolderPath = tempExtractPath;
                        break;
                    case "選択肢":
                        Debug.WriteLine("選択肢カードのプレビューを更新");
                        ChoicePreviewLabel.RichText = ChoiceQuestion.Text ?? "";
                        ChoicePreviewLabel.ShowAnswer = false;
                        ChoicePreviewLabel.ImageFolderPath = tempExtractPath;
                        
                        ChoiceExplanationPreviewLabel.RichText = ChoiceQuestionExplanation.Text ?? "";
                        ChoiceExplanationPreviewLabel.ShowAnswer = false;
                        ChoiceExplanationPreviewLabel.ImageFolderPath = tempExtractPath;
                        break;
                    case "画像穴埋め":
                        Debug.WriteLine("画像穴埋めカードのプレビューを更新");
                        // 画像穴埋めカードの場合は、LoadCardDataで既に画像が読み込まれているので
                        // キャンバスの再描画のみ行う
                        CanvasView.InvalidateSurface();
                        break;
                }
                
                Debug.WriteLine($"カード選択完了: {selectedCard.Id} - cards.txtは更新されません");
            }
        }

        protected override async void OnDisappearing()
        {
            base.OnDisappearing();
            // 画面を離れる時に保存（内容が変更されている場合のみ）
            if (isDirty && !string.IsNullOrEmpty(editCardId))
            {
                Debug.WriteLine($"画面離脱時: 内容が変更されているため保存を実行 - cardId: {editCardId}");
                await SaveCurrentCard();
            }
            else
            {
                Debug.WriteLine($"画面離脱時: 内容に変更がないため保存をスキップ - cardId: {editCardId}, isDirty: {isDirty}");
            }
            
            // リソース解放
            autoSaveTimer?.Stop();
            autoSaveTimer?.Dispose();
            
            _frontPreviewTimer?.Stop();
            _frontPreviewTimer?.Dispose();
            
            _backPreviewTimer?.Stop();
            _backPreviewTimer?.Dispose();
            
            _choiceQuestionPreviewTimer?.Stop();
            _choiceQuestionPreviewTimer?.Dispose();
            
            _choiceExplanationPreviewTimer?.Stop();
            _choiceExplanationPreviewTimer?.Dispose();
        }

        private void OnEditCardClicked(object sender, EventArgs e)
        {
            if (sender is Button button && button.CommandParameter is CardInfo card)
            {
                // カード編集画面に遷移
                Navigation.PushAsync(new Add(ankplsFilePath, tempExtractPath, card.Id));
            }
        }

        private async Task LoadCardData(string cardId)
        {
            try
            {
                Debug.WriteLine($"LoadCardData開始 - cardId: {cardId}");
                
                // カード読み込み中はTextChangedイベントを一時的に無効にする
                FrontTextEditor.TextChanged -= OnTextChanged;
                BackTextEditor.TextChanged -= OnTextChanged;
                ChoiceQuestion.TextChanged -= OnChoiceQuestionTextChanged;
                ChoiceQuestionExplanation.TextChanged -= OnChoiceExplanationTextChanged;
                
                // 既存の画像とデータをクリア
                imageBitmap?.Dispose();
                imageBitmap = null;
                selectedImagePath = null;
                selectionRects.Clear();
                
                var jsonPath = Path.Combine(tempExtractPath, "cards", $"{cardId}.json");
                
                if (File.Exists(jsonPath))
                {
                    var jsonContent = File.ReadAllText(jsonPath);
                    var cardData = JsonSerializer.Deserialize<CardData>(jsonContent);

                    // カードタイプを設定
                    int typeIndex = CardTypePicker.Items.IndexOf(cardData.type);
                    if (typeIndex >= 0)
                    {
                        CardTypePicker.SelectedIndex = typeIndex;
                    }
                    
                    // レイアウトの表示制御を直接実行
                    BasicCardLayout.IsVisible = cardData.type == "基本・穴埋め";
                    MultipleChoiceLayout.IsVisible = cardData.type == "選択肢";
                    ImageFillLayout.IsVisible = cardData.type == "画像穴埋め";
                    
                    Debug.WriteLine($"レイアウト制御: {cardData.type} - Basic:{BasicCardLayout.IsVisible}, Choice:{MultipleChoiceLayout.IsVisible}, Image:{ImageFillLayout.IsVisible}");

                    // カードの内容を設定
                    if (cardData.type == "選択肢")
                    {
                        ChoiceQuestion.Text = cardData.question ?? "";
                        ChoiceQuestionExplanation.Text = cardData.explanation ?? "";
                        
                        Debug.WriteLine($"選択肢カードのプレビュー初期化: question='{cardData.question}', explanation='{cardData.explanation}'");
                        
                        // 選択肢を設定
                        if (cardData.choices != null)
                        {
                            ChoicesContainer.Children.Clear();
                            foreach (var choice in cardData.choices)
                            {
                                var stack = new StackLayout { Orientation = StackOrientation.Horizontal };
                                var checkBox = new CheckBox { IsChecked = choice.isCorrect };
                                var editor = new Editor
                                {
                                    Text = choice.text ?? "",
                                    HeightRequest = 40,
                                    AutoSize = EditorAutoSizeOption.TextChanges
                                };
                                
                                // ダークモード対応
                                editor.SetAppThemeColor(Editor.BackgroundColorProperty, Colors.White, Color.FromArgb("#2D2D30"));
                                editor.SetAppThemeColor(Editor.TextColorProperty, Colors.Black, Colors.White);

                                stack.Children.Add(checkBox);
                                stack.Children.Add(editor);
                                ChoicesContainer.Children.Add(stack);
                            }
                        }
                    }
                    else
                    {
                        FrontTextEditor.Text = cardData.front ?? "";
                        BackTextEditor.Text = cardData.back ?? "";
                        
                        Debug.WriteLine($"基本カードのプレビュー初期化: front='{cardData.front}', back='{cardData.back}'");
                    }

                    // 画像穴埋めの場合、画像と選択範囲を設定
                    if (cardData.type == "画像穴埋め")
                    {
                        Debug.WriteLine($"画像穴埋めカード読み込み: imagePath='{cardData.imagePath}'");
                        
                        // 画像を読み込む
                        string imageFileName = null;
                        
                        // 新形式: imagePathフィールドから取得
                        if (!string.IsNullOrEmpty(cardData.imagePath))
                        {
                            imageFileName = cardData.imagePath;
                            Debug.WriteLine($"新形式のimagePathから画像ファイル名を取得: {imageFileName}");
                        }
                        // 旧形式: frontフィールドから取得（後方互換性のため）
                        else if (!string.IsNullOrEmpty(cardData.front) && 
                                 (cardData.front.StartsWith("img_") || cardData.front.EndsWith(".jpg") || cardData.front.EndsWith(".png")))
                        {
                            imageFileName = cardData.front;
                            Debug.WriteLine($"旧形式のfrontフィールドから画像ファイル名を取得: {imageFileName}");
                        }
                        
                        if (!string.IsNullOrEmpty(imageFileName))
                        {
                            // 画像パスを保存
                            selectedImagePath = imageFileName;
                            Debug.WriteLine($"使用する画像ファイル名: {selectedImagePath}");
                            
                            string imageFolder = Path.Combine(tempExtractPath, "img");
                            string actualImagePath = null;
                            
                            // 複数の拡張子でファイルの存在を確認
                            var possibleExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp" };
                            var baseFileName = Path.GetFileNameWithoutExtension(imageFileName);
                            
                            foreach (var ext in possibleExtensions)
                            {
                                var testPath = Path.Combine(imageFolder, baseFileName + ext);
                                Debug.WriteLine($"拡張子 {ext} で確認: {testPath} - 存在: {File.Exists(testPath)}");
                                
                                if (File.Exists(testPath))
                                {
                                    actualImagePath = testPath;
                                    selectedImagePath = Path.GetFileName(testPath);
                                    Debug.WriteLine($"実際の画像ファイルを発見: {actualImagePath}");
                                    break;
                                }
                            }
                            
                            if (actualImagePath != null)
                            {
                                try
                                {
                                    // 新しい画像を読み込み
                                    using (var stream = File.OpenRead(actualImagePath))
                                    {
                                        var newBitmap = SKBitmap.Decode(stream);
                                        
                                        if (newBitmap != null)
                                        {
                                            // 既存の画像をクリアしてから新しい画像を設定
                                            imageBitmap?.Dispose();
                                            imageBitmap = newBitmap;
                                            Debug.WriteLine($"画像読み込み成功: {imageBitmap.Width}x{imageBitmap.Height}");
                                            
                                            // キャンバスサイズを画像のアスペクト比に合わせて調整
                                            AdjustCanvasSizeToImage();
                                        }
                                        else
                                        {
                                            Debug.WriteLine("画像のデコードに失敗しました");
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"画像読み込みエラー: {ex.Message}");
                                }
                            }
                            else
                            {
                                Debug.WriteLine($"画像ファイルが見つかりません。確認したパス:");
                                foreach (var ext in possibleExtensions)
                                {
                                    var testPath = Path.Combine(imageFolder, baseFileName + ext);
                                    Debug.WriteLine($"  - {testPath}");
                                }
                            }
                        }
                        else
                        {
                            Debug.WriteLine("画像ファイル名が見つかりません");
                        }
                        
                                                // 選択範囲を設定
                        if (cardData.selectionRects != null)
                    {
                        selectionRects.Clear();
                        foreach (var rect in cardData.selectionRects)
                        {
                                // 既存のデータが正規化座標かどうかを判定
                                // x, y, width, heightが全て0.0-1.0の範囲にある場合は正規化座標とみなす
                                bool isNormalized = rect.x >= 0.0f && rect.x <= 1.0f && 
                                                   rect.y >= 0.0f && rect.y <= 1.0f && 
                                                   rect.width >= 0.0f && rect.width <= 1.0f && 
                                                   rect.height >= 0.0f && rect.height <= 1.0f;
                                
                                SKRect selectionRect;
                                if (isNormalized)
                                {
                                    // 正規化座標の場合は画像座標に変換
                                    if (imageBitmap != null)
                                    {
                                        float imageX = rect.x * imageBitmap.Width;
                                        float imageY = rect.y * imageBitmap.Height;
                                        float imageWidth = rect.width * imageBitmap.Width;
                                        float imageHeight = rect.height * imageBitmap.Height;
                                        
                                        selectionRect = new SKRect(imageX, imageY, imageX + imageWidth, imageY + imageHeight);
                                        Debug.WriteLine($"正規化座標を画像座標に変換: 元={rect.x},{rect.y},{rect.width},{rect.height}, 画像サイズ={imageBitmap.Width}x{imageBitmap.Height} -> 画像座標={selectionRect}");
                                    }
                                    else
                                    {
                                        // 画像が読み込まれていない場合は仮のサイズで計算
                                        float imageX = rect.x * 1000.0f;
                                        float imageY = rect.y * 1000.0f;
                                        float imageWidth = rect.width * 1000.0f;
                                        float imageHeight = rect.height * 1000.0f;
                                        
                                        selectionRect = new SKRect(imageX, imageY, imageX + imageWidth, imageY + imageHeight);
                                        Debug.WriteLine($"正規化座標を画像座標に変換（仮サイズ）: 元={rect.x},{rect.y},{rect.width},{rect.height} -> 画像座標={selectionRect}");
                                    }
                                }
                                else
                                {
                                    // 既に画像座標の場合
                                    selectionRect = new SKRect(rect.x, rect.y, rect.x + rect.width, rect.y + rect.height);
                                    Debug.WriteLine($"画像座標として読み込み: {selectionRect}");
                                }
                                
                                selectionRects.Add(selectionRect);
                            }
                            Debug.WriteLine($"選択範囲設定完了: {selectionRects.Count}個");
                        }
                        
                        // キャンバスを再描画
                        CanvasView.InvalidateSurface();
                        Debug.WriteLine("画像穴埋めカードの読み込みとキャンバス更新完了");
                    }
                }
                
                // カード読み込み完了後、TextChangedイベントを再度有効にする
                FrontTextEditor.TextChanged += OnTextChanged;
                BackTextEditor.TextChanged += OnTextChanged;
                ChoiceQuestion.TextChanged += OnChoiceQuestionTextChanged;
                ChoiceQuestionExplanation.TextChanged += OnChoiceExplanationTextChanged;
                
                // 選択肢エディターのTextChangedイベントも設定
                foreach (var stack in ChoicesContainer.Children.OfType<StackLayout>())
                {
                    var editor = stack.Children.OfType<Editor>().FirstOrDefault();
                    if (editor != null)
                    {
                        editor.TextChanged += OnChoiceTextChanged;
                    }
                }
                
                // TextChangedイベント再設定後に少し待ってからisDirtyフラグをリセット
                await Task.Delay(100);
                isDirty = false;
                Debug.WriteLine("LoadCardData完了 - TextChangedイベントを再設定し、isDirtyフラグをリセットしました");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadCardDataでエラー: {ex.Message}");
                Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                throw;
            }
        }

        private class CardData
        {
            public string type { get; set; }
            public string front { get; set; }
            public string back { get; set; }
            public string question { get; set; }
            public string explanation { get; set; }
            public List<ChoiceData> choices { get; set; }
            public List<SelectionRect> selectionRects { get; set; }
            public string imagePath { get; set; }  // 画像穴埋め用の画像パス
        }

        private class ChoiceData
        {
            public bool isCorrect { get; set; }
            public string text { get; set; }
        }

        private class SelectionRect
        {
            public float x { get; set; }
            public float y { get; set; }
            public float width { get; set; }
            public float height { get; set; }
        }

        private void OnCardTypeChanged(object sender, EventArgs e)
        {
            string selectedType = CardTypePicker.SelectedItem as string;

            BasicCardLayout.IsVisible = selectedType == "基本・穴埋め";
            MultipleChoiceLayout.IsVisible = selectedType == "選択肢";
            ImageFillLayout.IsVisible = selectedType == "画像穴埋め";
        }

        private void OnCanvasViewPaintSurface(object sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            var info = e.Info;
            
            // ダークモード対応の背景色
            var backgroundColor = Application.Current?.RequestedTheme == AppTheme.Dark ? SKColors.Black : SKColors.White;
            canvas.Clear(backgroundColor);

            if (imageBitmap != null)
            {
                Debug.WriteLine($"画像描画開始 - 画像サイズ: {imageBitmap.Width}x{imageBitmap.Height}, キャンバスサイズ: {info.Width}x{info.Height}");
                
                // キャンバスサイズは既に画像のアスペクト比に合わせて調整されているので、
                // 画像をキャンバス全体に表示
                var imageRect = new SKRect(0, 0, info.Width, info.Height);
                
                // 高品質描画用のペイントを設定
                using (var paint = new SKPaint())
                {
                    paint.IsAntialias = true;
                    paint.FilterQuality = SKFilterQuality.High;
                    canvas.DrawBitmap(imageBitmap, imageRect, paint);
                }
                
                Debug.WriteLine($"画像描画完了 - キャンバス全体に表示");
            }
            else
            {
                Debug.WriteLine("描画する画像がありません");
            }

            // 選択範囲を描画
            using (var strokePaint = new SKPaint
            {
                Color = SKColors.Red,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 3
            })
            using (var fillPaint = new SKPaint
            {
                Color = new SKColor(255, 0, 0, 100), // 赤色、透明度77（約30%）
                Style = SKPaintStyle.Fill
            })
            {
                Debug.WriteLine($"選択範囲描画開始: {selectionRects.Count}個の選択範囲");
                
                // 画像座標をキャンバス座標に変換して描画
                // キャンバスの実際のサイズを使用
                float canvasWidth = info.Width;
                float canvasHeight = info.Height;
                
                for (int i = 0; i < selectionRects.Count; i++)
                {
                    var imageRect = selectionRects[i];
                    var canvasRect = ImageToCanvasRect(imageRect, canvasWidth, canvasHeight);
                    Debug.WriteLine($"選択範囲描画: 画像座標={imageRect}, キャンバス={canvasRect}, キャンバスサイズ={canvasWidth}x{canvasHeight}");
                    
                    // 透明な塗りつぶし
                    canvas.DrawRect(canvasRect, fillPaint);
                    // 赤い枠線
                    canvas.DrawRect(canvasRect, strokePaint);
                    
                    // すべての枠にハンドルを表示
                    DrawResizeHandles(canvas, canvasRect);
                }

                if (isDragging)
                {
                    var currentRect = SKRect.Create(
                        Math.Min(startPoint.X, endPoint.X),
                        Math.Min(startPoint.Y, endPoint.Y),
                        Math.Abs(endPoint.X - startPoint.X),
                        Math.Abs(endPoint.Y - startPoint.Y)
                    );
                    Debug.WriteLine($"ドラッグ中の選択範囲: {currentRect}");
                    
                    // 新しい枠の描画（透明な塗りつぶし + 赤い枠線）
                    canvas.DrawRect(currentRect, fillPaint);
                    canvas.DrawRect(currentRect, strokePaint);
                }
            }
        }

        /// <summary>
        /// リサイズハンドルを描画
        /// </summary>
        private void DrawResizeHandles(SKCanvas canvas, SKRect rect)
        {
            using (var paint = new SKPaint
            {
                Color = SKColors.Blue,
                Style = SKPaintStyle.Fill
            })
            {
                // ハンドルを枠の内側に配置（枠の端から5ピクセル内側）
                float handleOffset = 5;
                float handleSize = HANDLE_SIZE;
                
                // 左上ハンドル
                canvas.DrawRect(new SKRect(rect.Left + handleOffset, rect.Top + handleOffset, rect.Left + handleOffset + handleSize, rect.Top + handleOffset + handleSize), paint);
                
                // 右上ハンドル
                canvas.DrawRect(new SKRect(rect.Right - handleOffset - handleSize, rect.Top + handleOffset, rect.Right - handleOffset, rect.Top + handleOffset + handleSize), paint);
                
                // 左下ハンドル
                canvas.DrawRect(new SKRect(rect.Left + handleOffset, rect.Bottom - handleOffset - handleSize, rect.Left + handleOffset + handleSize, rect.Bottom - handleOffset), paint);
                
                // 右下ハンドル
                canvas.DrawRect(new SKRect(rect.Right - handleOffset - handleSize, rect.Bottom - handleOffset - handleSize, rect.Right - handleOffset, rect.Bottom - handleOffset), paint);
            }
        }

        /// <summary>
        /// 指定されたポイントにある枠のインデックスを取得
        /// </summary>
        private int FindRectAtPoint(SKPoint point, float canvasWidth, float canvasHeight)
        {
            for (int i = 0; i < selectionRects.Count; i++)
            {
                var canvasRect = ImageToCanvasRect(selectionRects[i], canvasWidth, canvasHeight);
                if (canvasRect.Contains(point))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// 指定されたポイントにあるハンドルのインデックスを取得
        /// </summary>
        private int FindHandleAtPoint(SKPoint point, float canvasWidth, float canvasHeight)
        {
            if (selectedRectIndex < 0 || selectedRectIndex >= selectionRects.Count)
                return -1;

            var canvasRect = ImageToCanvasRect(selectionRects[selectedRectIndex], canvasWidth, canvasHeight);
            
            // ハンドルを枠の内側に配置（枠の端から5ピクセル内側）
            float handleOffset = 5;
            float handleHitSize = HANDLE_HIT_SIZE;
            
            // 各ハンドルの位置をチェック（クリック判定用の大きいサイズ）
            var handles = new[]
            {
                new SKRect(canvasRect.Left + handleOffset, canvasRect.Top + handleOffset, canvasRect.Left + handleOffset + handleHitSize, canvasRect.Top + handleOffset + handleHitSize), // 左上
                new SKRect(canvasRect.Right - handleOffset - handleHitSize, canvasRect.Top + handleOffset, canvasRect.Right - handleOffset, canvasRect.Top + handleOffset + handleHitSize), // 右上
                new SKRect(canvasRect.Left + handleOffset, canvasRect.Bottom - handleOffset - handleHitSize, canvasRect.Left + handleOffset + handleHitSize, canvasRect.Bottom - handleOffset), // 左下
                new SKRect(canvasRect.Right - handleOffset - handleHitSize, canvasRect.Bottom - handleOffset - handleHitSize, canvasRect.Right - handleOffset, canvasRect.Bottom - handleOffset)  // 右下
            };

            for (int i = 0; i < handles.Length; i++)
            {
                if (handles[i].Contains(point))
                {
                    return i;
                }
            }
            return -1;
        }

        private void OnCanvasTouch(object sender, SKTouchEventArgs e)
        {
            var point = e.Location;
            var canvasSize = CanvasView.CanvasSize;

            switch (e.ActionType)
            {
                case SKTouchAction.Pressed:
                    if (e.MouseButton == SKMouseButton.Right)
                    {
                        // 右クリックで削除メニュー表示
                        var clickedRectIndex = FindRectAtPoint(point, canvasSize.Width, canvasSize.Height);
                        if (clickedRectIndex >= 0)
                        {
                            var actualRect = ImageToCanvasRect(selectionRects[clickedRectIndex], canvasSize.Width, canvasSize.Height);
                            ShowContextMenu(point, actualRect);
                        }
                    }
                    else
                    {
                        // 左クリックで既存の枠を選択または新しい枠を作成
                        var rectIndex = FindRectAtPoint(point, canvasSize.Width, canvasSize.Height);
                        
                        if (rectIndex >= 0)
                        {
                            // 既存の枠を選択
                            selectedRectIndex = rectIndex;
                            
                            // ハンドルがクリックされたかチェック
                            var handleIndex = FindHandleAtPoint(point, canvasSize.Width, canvasSize.Height);
                            if (handleIndex >= 0)
                            {
                                // リサイズモード
                                isResizing = true;
                                resizeHandle = handleIndex;
                                Debug.WriteLine($"リサイズ開始: ハンドル={handleIndex}");
                            }
                            else
                            {
                                // 移動モード
                                isMoving = true;
                                var canvasRect = ImageToCanvasRect(selectionRects[rectIndex], canvasSize.Width, canvasSize.Height);
                                dragOffset = new SKPoint(point.X - canvasRect.Left, point.Y - canvasRect.Top);
                                Debug.WriteLine($"既存の枠を選択: インデックス={rectIndex}");
                            }
                        }
                        else
                        {
                            // 新しい枠を作成
                            isDragging = true;
                            selectedRectIndex = -1;
                            startPoint = point;
                            endPoint = point;
                            Debug.WriteLine("新しい枠を作成開始");
                        }
                    }
                    break;

                case SKTouchAction.Moved:
                    if (isMoving && selectedRectIndex >= 0)
                    {
                        // 既存の枠を移動（サイズは保持）
                        var newLeft = point.X - dragOffset.X;
                        var newTop = point.Y - dragOffset.Y;
                        
                        // 現在のキャンバス座標のサイズを取得
                        var currentCanvasRect = ImageToCanvasRect(selectionRects[selectedRectIndex], canvasSize.Width, canvasSize.Height);
                        
                        // キャンバス境界内に制限（キャンバス座標のサイズを使用）
                        newLeft = Math.Max(0, Math.Min(newLeft, canvasSize.Width - currentCanvasRect.Width));
                        newTop = Math.Max(0, Math.Min(newTop, canvasSize.Height - currentCanvasRect.Height));
                        
                        var newCanvasRect = new SKRect(newLeft, newTop, newLeft + currentCanvasRect.Width, newTop + currentCanvasRect.Height);
                        var newImageRect = CanvasToImageRect(newCanvasRect, canvasSize.Width, canvasSize.Height);
                        
                        selectionRects[selectedRectIndex] = newImageRect;
                        isDirty = true;
                        Debug.WriteLine($"枠を移動: 新しい位置={newImageRect}, サイズ={newImageRect.Width}x{newImageRect.Height}");
                    }
                    else if (isResizing && selectedRectIndex >= 0)
                    {
                        // 既存の枠をリサイズ
                        var currentCanvasRect = ImageToCanvasRect(selectionRects[selectedRectIndex], canvasSize.Width, canvasSize.Height);
                        var newCanvasRect = currentCanvasRect;
                        
                        switch (resizeHandle)
                        {
                            case 0: // 左上 - 右下を固定
                                newCanvasRect.Left = Math.Max(0, Math.Min(point.X, currentCanvasRect.Right - 10));
                                newCanvasRect.Top = Math.Max(0, Math.Min(point.Y, currentCanvasRect.Bottom - 10));
                                break;
                            case 1: // 右上 - 左下を固定
                                newCanvasRect.Right = Math.Max(currentCanvasRect.Left + 10, Math.Min(point.X, canvasSize.Width));
                                newCanvasRect.Top = Math.Max(0, Math.Min(point.Y, currentCanvasRect.Bottom - 10));
                                break;
                            case 2: // 左下 - 右上を固定
                                newCanvasRect.Left = Math.Max(0, Math.Min(point.X, currentCanvasRect.Right - 10));
                                newCanvasRect.Bottom = Math.Max(currentCanvasRect.Top + 10, Math.Min(point.Y, canvasSize.Height));
                                break;
                            case 3: // 右下 - 左上を固定
                                newCanvasRect.Right = Math.Max(currentCanvasRect.Left + 10, Math.Min(point.X, canvasSize.Width));
                                newCanvasRect.Bottom = Math.Max(currentCanvasRect.Top + 10, Math.Min(point.Y, canvasSize.Height));
                                break;
                        }
                        
                        var newImageRect = CanvasToImageRect(newCanvasRect, canvasSize.Width, canvasSize.Height);
                        selectionRects[selectedRectIndex] = newImageRect;
                        isDirty = true;
                        Debug.WriteLine($"枠をリサイズ: ハンドル={resizeHandle}, 新しいサイズ={newImageRect.Width}x{newImageRect.Height}");
                    }
                    else if (isDragging)
                    {
                        endPoint = point;
                    }
                    break;

                case SKTouchAction.Released:
                    if (isDragging)
                    {
                        var canvasRect = SKRect.Create(
                            Math.Min(startPoint.X, endPoint.X),
                            Math.Min(startPoint.Y, endPoint.Y),
                            Math.Abs(endPoint.X - startPoint.X),
                            Math.Abs(endPoint.Y - startPoint.Y)
                        );

                        if (!canvasRect.IsEmpty && canvasRect.Width > 5 && canvasRect.Height > 5)
                        {
                            // 重複チェック
                            var imageRect = CanvasToImageRect(canvasRect, canvasSize.Width, canvasSize.Height);
                            bool isOverlapping = selectionRects.Any(existingRect => 
                            {
                                var existingCanvasRect = ImageToCanvasRect(existingRect, canvasSize.Width, canvasSize.Height);
                                return canvasRect.IntersectsWith(existingCanvasRect);
                            });
                            
                            if (!isOverlapping)
                            {
                                selectionRects.Add(imageRect);
                                isDirty = true;
                                Debug.WriteLine($"選択範囲追加: キャンバス座標={canvasRect}, 画像座標={imageRect}");
                            }
                            else
                            {
                                Debug.WriteLine("重複するため新しい枠を作成しませんでした");
                            }
                        }
                        isDragging = false;
                    }
                    else if (isMoving)
                    {
                        isMoving = false;
                        Debug.WriteLine("枠の移動完了");
                    }
                    else if (isResizing)
                    {
                        isResizing = false;
                        resizeHandle = -1;
                        Debug.WriteLine("枠のリサイズ完了");
                    }
                    break;
            }

            CanvasView.InvalidateSurface();
        }

        private async void ShowContextMenu(SKPoint point, SKRect rect)
        {
            var action = await DisplayActionSheet("削除しますか？", "キャンセル", "削除");

            if (action == "削除")
            {
                // ポイントから枠のインデックスを取得
                var canvasSize = CanvasView.CanvasSize;
                var rectIndex = FindRectAtPoint(point, canvasSize.Width, canvasSize.Height);
                
                if (rectIndex >= 0)
                {
                    var rectToRemove = selectionRects[rectIndex];
                    selectionRects.RemoveAt(rectIndex);
                    
                    // 選択中の枠が削除された場合は選択をクリア
                    if (rectIndex == selectedRectIndex)
                    {
                        selectedRectIndex = -1;
                    }
                    else if (rectIndex < selectedRectIndex)
                    {
                        // 選択中の枠より前の枠が削除された場合はインデックスを調整
                        selectedRectIndex--;
                    }
                    
                    isDirty = true;
                    CanvasView.InvalidateSurface();
                    Debug.WriteLine($"選択範囲削除: インデックス={rectIndex}, 枠={rectToRemove}");
                }
            }
        }

        private async Task AddImage(Editor editor)
        {
            var result = await FilePicker.PickAsync(new PickOptions
            {
                PickerTitle = "画像を選択",
                FileTypes = FilePickerFileType.Images
            });

            if (result != null)
            {
                // iOS版に合わせて8桁_6桁の数字形式でIDを生成
                Random random = new Random();
                string imageId8 = random.Next(10000000, 99999999).ToString(); // 8桁の数字
                string imageId6 = random.Next(100000, 999999).ToString(); // 6桁の数字
                string imageId = $"{imageId8}_{imageId6}";

                string imageFolder = Path.Combine(tempExtractPath, "img");
                Directory.CreateDirectory(imageFolder);

                string newFileName = $"img_{imageId}.jpg";
                string newFilePath = Path.Combine(imageFolder, newFileName);

                // 画像を読み込んで圧縮して保存
                using (var sourceStream = await result.OpenReadAsync())
                {
                    using (var bitmap = SKBitmap.Decode(sourceStream))
                    {
                        using (var image = SKImage.FromBitmap(bitmap))
                        using (var data = image.Encode(SKEncodedImageFormat.Jpeg, 80)) // 品質を80%に設定
                        using (var fileStream = File.Create(newFilePath))
                        {
                            data.SaveTo(fileStream);
                        }
                    }
                }

                // エディタに `<<img_{imageId}.jpg>>` を挿入
                int cursorPosition = editor.CursorPosition;
                string text = editor.Text ?? "";
                string newText = text.Insert(cursorPosition, $"<<img_{imageId}.jpg>>");
                editor.Text = newText;
                editor.CursorPosition = cursorPosition + $"<<img_{imageId}.jpg>>".Length;

                // プレビューを更新
                if (editor == FrontTextEditor)
                {
                    FrontOnTextChanged(editor, new TextChangedEventArgs("", editor.Text));
                }
                else if (editor == BackTextEditor)
                {
                    BackOnTextChanged(editor, new TextChangedEventArgs("", editor.Text));
                }
            }
        }

        private async void FrontOnAddImageClicked(object sender, EventArgs e)
        {
            await AddImage(FrontTextEditor);
            FrontOnTextChanged(FrontTextEditor, new TextChangedEventArgs("", FrontTextEditor.Text));
        }

        private async void BackOnAddImageClicked(object sender, EventArgs e)
        {
            await AddImage(BackTextEditor);
            BackOnTextChanged(BackTextEditor, new TextChangedEventArgs("", BackTextEditor.Text));
        }

        private async void ChoiceQuestionOnAddImageClicked(object sender, EventArgs e)
        {
            await AddImage(ChoiceQuestion);
            OnChoiceQuestionTextChanged(ChoiceQuestion, new TextChangedEventArgs("", ChoiceQuestion.Text));
        }

        private async void ChoiceExplanationOnAddImageClicked(object sender, EventArgs e)
        {
            await AddImage(ChoiceQuestionExplanation);
            OnChoiceExplanationTextChanged(ChoiceQuestionExplanation, new TextChangedEventArgs("", ChoiceQuestionExplanation.Text));
        }

        private void OnAddChoice(object sender, EventArgs e)
        {
            var stack = new StackLayout { Orientation = StackOrientation.Horizontal };
            var checkBox = new CheckBox();
            var editor = new Editor
            {
                Placeholder = "選択肢を入力（改行で区切って複数入力可能）",
                HeightRequest = 40,
                AutoSize = EditorAutoSizeOption.TextChanges
            };
            
            // ダークモード対応
            editor.SetAppThemeColor(Editor.BackgroundColorProperty, Colors.White, Color.FromArgb("#2D2D30"));
            editor.SetAppThemeColor(Editor.TextColorProperty, Colors.Black, Colors.White);
            
            editor.TextChanged += OnChoiceTextChanged;

            stack.Children.Add(checkBox);
            stack.Children.Add(editor);

            ChoicesContainer.Children.Add(stack);
            isDirty = true;
            autoSaveTimer.Stop();
            autoSaveTimer.Start();
        }

        private void OnChoiceTextChanged(object sender, TextChangedEventArgs e)
        {
            var editor = sender as Editor;
            if (editor == null) return;

            // 改行が含まれている場合
            if (e.NewTextValue.Contains("\n") || e.NewTextValue.Contains("\r"))
            {
                // 改行で分割し、空の行を除外
                var choices = e.NewTextValue
                    .Replace("\r\n", "\n")  // まず \r\n を \n に統一
                    .Replace("\r", "\n")    // 残りの \r を \n に変換
                    .Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries)  // \n で分割
                    .Select(c => c.Trim())  // 各行の前後の空白を削除
                    .Where(c => !string.IsNullOrWhiteSpace(c))  // 空の行を除外
                    .ToList();

                if (choices.Count > 0)
                {
                    // 選択肢コンテナをクリア
                    ChoicesContainer.Children.Clear();

                    // 各選択肢に対して新しいエントリを作成
                    foreach (var choice in choices)
                    {
                        var stack = new StackLayout { Orientation = StackOrientation.Horizontal };
                        var checkBox = new CheckBox();
                        var newEditor = new Editor
                        {
                            Text = choice,
                            HeightRequest = 40,
                            AutoSize = EditorAutoSizeOption.TextChanges
                        };
                        
                        // ダークモード対応
                        newEditor.SetAppThemeColor(Editor.BackgroundColorProperty, Colors.White, Color.FromArgb("#2D2D30"));
                        newEditor.SetAppThemeColor(Editor.TextColorProperty, Colors.Black, Colors.White);
                        
                        newEditor.TextChanged += OnChoiceTextChanged;

                        stack.Children.Add(checkBox);
                        stack.Children.Add(newEditor);

                        ChoicesContainer.Children.Add(stack);
                    }
                }
            }

            Debug.WriteLine($"OnChoiceTextChanged: isDirtyをtrueに設定");
            isDirty = true;
            autoSaveTimer.Stop();
            autoSaveTimer.Start();
        }

        private async void OnSelectImage(object sender, EventArgs e)
        {
            var result = await FilePicker.PickAsync(new PickOptions
            {
                PickerTitle = "画像を選択",
                FileTypes = FilePickerFileType.Images
            });

            if (result != null)
            {
                try
                {
                    Debug.WriteLine($"画像選択: {result.FullPath}");
                    
                    // 既存の画像をクリア
                    imageBitmap?.Dispose();
                    imageBitmap = null;
                    
                    // 画像を読み込む
                    using (var stream = File.OpenRead(result.FullPath))
                    {
                        imageBitmap = SKBitmap.Decode(stream);
                    }

                    if (imageBitmap != null)
                    {
                        Debug.WriteLine($"画像読み込み成功: {imageBitmap.Width}x{imageBitmap.Height}");
                        
                        // iOS版に合わせて8桁_6桁の数字形式でIDを生成
                        Random random = new Random();
                        string imageId8 = random.Next(10000000, 99999999).ToString(); // 8桁の数字
                        string imageId6 = random.Next(100000, 999999).ToString(); // 6桁の数字
                        string imageId = $"{imageId8}_{imageId6}";

                        // 画像を img フォルダに保存
                        var imgFolderPath = Path.Combine(tempExtractPath, "img");
                        if (!Directory.Exists(imgFolderPath))
                        {
                            Directory.CreateDirectory(imgFolderPath);
                        }

                        var imgFileName = $"img_{imageId}.jpg";
                        var imgFilePath = Path.Combine(imgFolderPath, imgFileName);
                        
                        // 画像をJPEG形式で保存
                        using (var image = SKImage.FromBitmap(imageBitmap))
                        using (var data = image.Encode(SKEncodedImageFormat.Jpeg, 80)) // 品質を80%に設定
                        using (var fileStream = File.Create(imgFilePath))
                        {
                            data.SaveTo(fileStream);
                        }
                        
                        // 画像パスを保存
                        selectedImagePath = imgFileName;
                        Debug.WriteLine($"保存された画像パス: {selectedImagePath}");
                        
                        // キャンバスサイズを画像のアスペクト比に合わせて調整
                        AdjustCanvasSizeToImage();
                        
                        // 選択範囲をクリア
                        selectionRects.Clear();
                        isDirty = true;
                        
                        // キャンバスを再描画
                        CanvasView.InvalidateSurface();
                        
                        Debug.WriteLine("画像選択完了");
                    }
                    else
                    {
                        Debug.WriteLine("画像のデコードに失敗しました");
                        await DisplayAlert("エラー", "画像の読み込みに失敗しました。", "OK");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"画像の読み込みでエラー: {ex.Message}");
                    Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                    await DisplayAlert("エラー", "画像の読み込みに失敗しました。", "OK");
                }
            }
        }

        private void OnChoiceQuestionTextChanged(object sender, TextChangedEventArgs e)
        {
            Debug.WriteLine($"OnChoiceQuestionTextChanged: isDirtyをtrueに設定");
            isDirty = true;
            autoSaveTimer.Stop();
            autoSaveTimer.Start();

            // JavaScript手法を使用してプレビューを更新（デバウンス付き）
            UpdateChoiceQuestionPreviewWithDebounce(e.NewTextValue ?? "");
        }

        private void OnChoiceExplanationTextChanged(object sender, TextChangedEventArgs e)
        {
            Debug.WriteLine($"OnChoiceExplanationTextChanged: isDirtyをtrueに設定");
            isDirty = true;
            autoSaveTimer.Stop();
            autoSaveTimer.Start();

            // JavaScript手法を使用してプレビューを更新（デバウンス付き）
            UpdateChoiceExplanationPreviewWithDebounce(e.NewTextValue ?? "");
        }

        /// <summary>
        /// 選択肢問題プレビューを更新（デバウンス付き）
        /// </summary>
        private System.Timers.Timer _choiceQuestionPreviewTimer;
        private void UpdateChoiceQuestionPreviewWithDebounce(string text)
        {
            // 既存のタイマーを停止
            _choiceQuestionPreviewTimer?.Stop();
            _choiceQuestionPreviewTimer?.Dispose();
            
            // 新しいタイマーを作成（300ms後に実行）
            _choiceQuestionPreviewTimer = new System.Timers.Timer(300);
            _choiceQuestionPreviewTimer.Elapsed += (s, e) =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        // デバッグ情報を出力
                        Debug.WriteLine($"=== Edit.xaml.cs UpdateChoiceQuestionPreviewWithDebounce ===");
                        Debug.WriteLine($"tempExtractPath: {tempExtractPath}");
                        Debug.WriteLine($"ChoicePreviewLabel.ImageFolderPath (設定前): {ChoicePreviewLabel.ImageFolderPath}");
                        
                        ChoicePreviewLabel.ImageFolderPath = tempExtractPath;
                        ChoicePreviewLabel.RichText = text;
                        ChoicePreviewLabel.ShowAnswer = false;
                        
                        Debug.WriteLine($"ChoicePreviewLabel.ImageFolderPath (設定後): {ChoicePreviewLabel.ImageFolderPath}");
                        Debug.WriteLine($"ChoicePreviewLabel.RichText: {ChoicePreviewLabel.RichText}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"選択肢問題プレビュー更新エラー: {ex.Message}");
                    }
                });
                _choiceQuestionPreviewTimer?.Stop();
                _choiceQuestionPreviewTimer?.Dispose();
                _choiceQuestionPreviewTimer = null;
            };
            _choiceQuestionPreviewTimer.AutoReset = false;
            _choiceQuestionPreviewTimer.Start();
        }

        /// <summary>
        /// 選択肢解説プレビューを更新（デバウンス付き）
        /// </summary>
        private System.Timers.Timer _choiceExplanationPreviewTimer;
        private void UpdateChoiceExplanationPreviewWithDebounce(string text)
        {
            // 既存のタイマーを停止
            _choiceExplanationPreviewTimer?.Stop();
            _choiceExplanationPreviewTimer?.Dispose();
            
            // 新しいタイマーを作成（300ms後に実行）
            _choiceExplanationPreviewTimer = new System.Timers.Timer(300);
            _choiceExplanationPreviewTimer.Elapsed += (s, e) =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        // デバッグ情報を出力
                        Debug.WriteLine($"=== Edit.xaml.cs UpdateChoiceExplanationPreviewWithDebounce ===");
                        Debug.WriteLine($"tempExtractPath: {tempExtractPath}");
                        Debug.WriteLine($"ChoiceExplanationPreviewLabel.ImageFolderPath (設定前): {ChoiceExplanationPreviewLabel.ImageFolderPath}");
                        
                        ChoiceExplanationPreviewLabel.ImageFolderPath = tempExtractPath;
                        ChoiceExplanationPreviewLabel.RichText = text;
                        ChoiceExplanationPreviewLabel.ShowAnswer = false;
                        
                        Debug.WriteLine($"ChoiceExplanationPreviewLabel.ImageFolderPath (設定後): {ChoiceExplanationPreviewLabel.ImageFolderPath}");
                        Debug.WriteLine($"ChoiceExplanationPreviewLabel.RichText: {ChoiceExplanationPreviewLabel.RichText}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"選択肢解説プレビュー更新エラー: {ex.Message}");
                    }
                });
                _choiceExplanationPreviewTimer?.Stop();
                _choiceExplanationPreviewTimer?.Dispose();
                _choiceExplanationPreviewTimer = null;
            };
            _choiceExplanationPreviewTimer.AutoReset = false;
            _choiceExplanationPreviewTimer.Start();
        }



        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            PerformSearch(e.NewTextValue);
        }

        private void OnClearSearchClicked(object sender, EventArgs e)
        {
            CardSearchBar.Text = "";
            PerformSearch("");
        }

        private void PerformSearch(string searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText))
            {
                // 検索テキストが空の場合、全てのカードを表示
                filteredCards = new List<CardInfo>(cards);
            }
            else
            {
                // 大文字小文字を区別しない検索
                string lowerSearchText = searchText.ToLower();
                filteredCards = cards.Where(card =>
                    card.FrontText.ToLower().Contains(lowerSearchText) ||
                    (card.ImageInfo != null && card.ImageInfo.ToLower().Contains(lowerSearchText))
                ).ToList();
            }

            CardsCollectionView.ItemsSource = filteredCards;
            UpdateSearchResult();
        }

        private void UpdateSearchResult()
        {
            if (string.IsNullOrWhiteSpace(CardSearchBar.Text))
            {
                SearchResultLabel.Text = $"全{cards.Count}件";
            }
            else
            {
                SearchResultLabel.Text = $"{filteredCards.Count}/{cards.Count}件";
            }
        }

        /// <summary>
        /// カード削除ボタンがクリックされた時の処理
        /// </summary>
        private async void OnDeleteCardClicked(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(editCardId))
            {
                await DisplayAlert("エラー", "削除するカードが選択されていません。", "OK");
                return;
            }

            // 削除確認ダイアログを表示
            bool result = await DisplayAlert(
                "確認", 
                "このカードを削除しますか？\nこの操作は取り消せません。", 
                "削除", 
                "キャンセル"
            );

            if (result)
            {
                await DeleteCard(editCardId);
            }
        }

        /// <summary>
        /// カードを削除する
        /// </summary>
        private async Task DeleteCard(string cardId)
        {
            try
            {
                Debug.WriteLine($"カード削除開始: {cardId}");

                // 1. JSONファイルを削除
                var cardsDirPath = Path.Combine(tempExtractPath, "cards");
                var jsonFilePath = Path.Combine(cardsDirPath, $"{cardId}.json");
                
                if (File.Exists(jsonFilePath))
                {
                    File.Delete(jsonFilePath);
                    Debug.WriteLine($"JSONファイル削除完了: {jsonFilePath}");
                }
                else
                {
                    Debug.WriteLine($"JSONファイルが存在しません: {jsonFilePath}");
                }

                // 2. cards.txtを更新（削除フラグを追加）
                var cardsFilePath = Path.Combine(tempExtractPath, "cards.txt");
                if (File.Exists(cardsFilePath))
                {
                    var lines = File.ReadAllLines(cardsFilePath).ToList();
                    var updatedLines = new List<string>();
                    var cardCount = 0;

                    // 1行目はカード数なのでそのまま追加
                    if (lines.Count > 0)
                    {
                        updatedLines.Add(lines[0]);
                    }

                    // 2行目以降を処理
                    for (int i = 1; i < lines.Count; i++)
                    {
                        var line = lines[i];
                        if (!string.IsNullOrEmpty(line))
                        {
                            var parts = line.Split(',');
                            if (parts.Length >= 2 && parts[0] == cardId)
                            {
                                // 削除対象のカードに削除フラグを追加
                                var updatedLine = $"{parts[0]},{parts[1]},deleted";
                                updatedLines.Add(updatedLine);
                                Debug.WriteLine($"削除フラグを追加: {updatedLine}");
                            }
                            else
                            {
                                // 他のカードはそのまま追加
                                updatedLines.Add(line);
                                cardCount++;
                            }
                        }
                    }

                    // カード数を更新（削除されたカードを除く）
                    if (updatedLines.Count > 0)
                    {
                        updatedLines[0] = cardCount.ToString();
                    }

                    // 更新された内容を保存
                    File.WriteAllLines(cardsFilePath, updatedLines);
                    Debug.WriteLine($"cards.txt更新完了: カード数={cardCount}");
                }

                // 3. カードリストから削除
                var cardToRemove = cards.FirstOrDefault(c => c.Id == cardId);
                if (cardToRemove != null)
                {
                    cards.Remove(cardToRemove);
                    filteredCards.Remove(cardToRemove);
                    CardsCollectionView.ItemsSource = null;
                    CardsCollectionView.ItemsSource = filteredCards;
                    TotalCardsLabel.Text = $"カード枚数: {cards.Count}";
                    UpdateSearchResult();
                }

                // 4. 編集画面をクリア
                ClearEditForm();
                editCardId = null;

                await DisplayAlert("完了", "カードを削除しました。", "OK");
                Debug.WriteLine("カード削除完了");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"カード削除エラー: {ex.Message}");
                Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                await DisplayAlert("エラー", $"カードの削除に失敗しました: {ex.Message}", "OK");
            }
        }

        /// <summary>
        /// 編集フォームをクリアする
        /// </summary>
        private void ClearEditForm()
        {
            // 基本・穴埋めカード
            FrontTextEditor.Text = "";
            BackTextEditor.Text = "";
            FrontPreviewLabel.RichText = "";
            BackPreviewLabel.RichText = "";

            // 選択肢カード
            ChoiceQuestion.Text = "";
            ChoiceQuestionExplanation.Text = "";
            ChoicePreviewLabel.RichText = "";
            ChoiceExplanationPreviewLabel.RichText = "";
            ChoicesContainer.Children.Clear();

            // 画像穴埋めカード
            selectionRects.Clear();
            selectedRectIndex = -1;
            isMoving = false;
            isResizing = false;
            resizeHandle = -1;
            imageBitmap?.Dispose();
            imageBitmap = null;
            selectedImagePath = null;
            CanvasView.InvalidateSurface();

            // カードタイプをリセット
            CardTypePicker.SelectedIndex = 0;

            // 削除ボタンを非表示にする
            UpdateDeleteButtonVisibility(false);
        }

        /// <summary>
        /// 削除ボタンの表示/非表示を更新する
        /// </summary>
        private void UpdateDeleteButtonVisibility(bool isVisible)
        {
            BasicCardDeleteButton.IsVisible = isVisible;
            MultipleChoiceDeleteButton.IsVisible = isVisible;
            ImageFillDeleteButton.IsVisible = isVisible;
        }

        private async void OnHelpClicked(object sender, EventArgs e)
        {
            try
            {
                await HelpOverlayControl.ShowHelp(HelpType.EditPage);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ヘルプ表示中にエラー: {ex.Message}");
                await DisplayAlert("エラー", "ヘルプの表示に失敗しました", "OK");
            }
        }
    }
} 