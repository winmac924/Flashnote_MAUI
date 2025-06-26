using System.Text.Json;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.IO.Compression;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using System.Web;
using System.Reflection;

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
        private List<SKRect> selectionRects = new List<SKRect>();  // 画像穴埋め用の選択範囲
        private SKBitmap imageBitmap;         // 画像を表示するためのビットマップ
        private SKPoint startPoint, endPoint;
        private bool isDragging = false;
        private const float HANDLE_SIZE = 15;



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
            if (isDirty && !string.IsNullOrEmpty(editCardId))
            {
                await SaveCurrentCard();
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
                    front = frontText,
                    back = backText,
                    question = choiceQuestion,
                    explanation = choiceExplanation,
                    choices = choices,
                    selectionRects = selectionRects.Select(r => new
                    {
                        x = r.Left,
                        y = r.Top,
                        width = r.Width,
                        height = r.Height
                    }).ToList()
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
                    }
                }

                // .ankpls を更新
                if (File.Exists(ankplsFilePath))
                {
                    File.Delete(ankplsFilePath);
                }
                ZipFile.CreateFromDirectory(tempExtractPath, ankplsFilePath);

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
                // 現在のカードを保存
                if (isDirty && !string.IsNullOrEmpty(editCardId))
                {
                    await SaveCurrentCard();
                }

                // 新しいカードを読み込む
                editCardId = selectedCard.Id;
                LoadCardData(selectedCard.Id);
                
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
                }
            }
        }

        protected override async void OnDisappearing()
        {
            base.OnDisappearing();
            // 画面を離れる時に保存
            if (isDirty && !string.IsNullOrEmpty(editCardId))
            {
                await SaveCurrentCard();
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

        private void LoadCardData(string cardId)
        {
            try
            {
                Debug.WriteLine($"LoadCardData開始 - cardId: {cardId}");
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
                        
                        // TextChangedイベントハンドラーを設定
                        ChoiceQuestion.TextChanged -= OnChoiceQuestionTextChanged; // 重複を防ぐため一度削除
                        ChoiceQuestion.TextChanged += OnChoiceQuestionTextChanged;
                        ChoiceQuestionExplanation.TextChanged -= OnChoiceExplanationTextChanged;
                        ChoiceQuestionExplanation.TextChanged += OnChoiceExplanationTextChanged;
                        

                        
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
                                
                                // TextChangedイベントを追加
                                editor.TextChanged += OnChoiceTextChanged;

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

                    // 画像穴埋めの場合、選択範囲を設定
                    if (cardData.type == "画像穴埋め" && cardData.selectionRects != null)
                    {
                        selectionRects.Clear();
                        foreach (var rect in cardData.selectionRects)
                        {
                            selectionRects.Add(new SKRect(rect.x, rect.y, rect.x + rect.width, rect.y + rect.height));
                        }
                    }
                }
                Debug.WriteLine("LoadCardData完了");
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
            canvas.Clear(SKColors.White);

            if (imageBitmap != null)
            {
                var rect = new SKRect(0, 0, e.Info.Width, e.Info.Height);
                canvas.DrawBitmap(imageBitmap, rect);
            }

            using (var paint = new SKPaint
            {
                Color = SKColors.Red,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 3
            })
            {
                foreach (var rect in selectionRects)
                {
                    canvas.DrawRect(rect, paint);
                }

                if (isDragging)
                {
                    var currentRect = SKRect.Create(
                        Math.Min(startPoint.X, endPoint.X),
                        Math.Min(startPoint.Y, endPoint.Y),
                        Math.Abs(endPoint.X - startPoint.X),
                        Math.Abs(endPoint.Y - startPoint.Y)
                    );
                    canvas.DrawRect(currentRect, paint);
                }
            }
        }

        private void OnCanvasTouch(object sender, SKTouchEventArgs e)
        {
            var point = e.Location;

            switch (e.ActionType)
            {
                case SKTouchAction.Pressed:
                    if (e.MouseButton == SKMouseButton.Right)
                    {
                        var clickedRect = selectionRects.FirstOrDefault(r => r.Contains(point));
                        if (clickedRect != SKRect.Empty)
                        {
                            ShowContextMenu(point, clickedRect);
                        }
                    }
                    else
                    {
                        isDragging = true;
                        startPoint = point;
                        endPoint = point;
                    }
                    break;

                case SKTouchAction.Moved:
                    if (isDragging)
                    {
                        endPoint = point;
                    }
                    break;

                case SKTouchAction.Released:
                    if (isDragging)
                    {
                        var rect = SKRect.Create(
                            Math.Min(startPoint.X, endPoint.X),
                            Math.Min(startPoint.Y, endPoint.Y),
                            Math.Abs(endPoint.X - startPoint.X),
                            Math.Abs(endPoint.Y - startPoint.Y)
                        );

                        if (!rect.IsEmpty && rect.Width > 5 && rect.Height > 5)
                        {
                            selectionRects.Add(rect);
                            isDirty = true;
                        }
                    }
                    isDragging = false;
                    break;
            }

            CanvasView.InvalidateSurface();
        }

        private async void ShowContextMenu(SKPoint point, SKRect rect)
        {
            var action = await DisplayActionSheet("削除しますか？", "キャンセル", "削除");

            if (action == "削除")
            {
                selectionRects.Remove(rect);
                isDirty = true;
                CanvasView.InvalidateSurface();
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
                    // 画像を読み込む
                    using (var stream = File.OpenRead(result.FullPath))
                    {
                        imageBitmap = SKBitmap.Decode(stream);
                    }

                    // 画像のサイズを調整
                    if (imageBitmap != null)
                    {
                        // キャンバスのサイズに合わせて画像をリサイズ
                        var info = CanvasView.CanvasSize;
                        var scale = Math.Min(info.Width / imageBitmap.Width, info.Height / imageBitmap.Height);
                        var scaledWidth = imageBitmap.Width * scale;
                        var scaledHeight = imageBitmap.Height * scale;

                        var resizedBitmap = imageBitmap.Resize(
                            new SKImageInfo((int)scaledWidth, (int)scaledHeight),
                            SKFilterQuality.High
                        );

                        imageBitmap.Dispose();
                        imageBitmap = resizedBitmap;

                        // 選択範囲をクリア
                        selectionRects.Clear();
                        isDirty = true;
                        CanvasView.InvalidateSurface();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"画像の読み込みでエラー: {ex.Message}");
                    await DisplayAlert("エラー", "画像の読み込みに失敗しました。", "OK");
                }
            }
        }

        private void OnChoiceQuestionTextChanged(object sender, TextChangedEventArgs e)
        {
            isDirty = true;
            autoSaveTimer.Stop();
            autoSaveTimer.Start();

            // JavaScript手法を使用してプレビューを更新（デバウンス付き）
            UpdateChoiceQuestionPreviewWithDebounce(e.NewTextValue ?? "");
        }

        private void OnChoiceExplanationTextChanged(object sender, TextChangedEventArgs e)
        {
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
    }
} 