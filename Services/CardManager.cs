using Microsoft.Maui.Controls;
using SkiaSharp.Views.Maui;
using SkiaSharp;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using SkiaSharp.Views.Maui.Controls;
using Microsoft.Maui.ApplicationModel;
using System.Text.Json;
using Flashnote.Models;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using Windows.System;
using Microsoft.Extensions.Logging;
using SQLite;
using Flashnote_MAUI.Services;

namespace Flashnote.Services
{
    /// <summary>
    /// カード作成・編集の共通機能を提供するサービス
    /// </summary>
    public partial class CardManager : IDisposable
    {
        private readonly string _cardsFilePath;
        private readonly string _tempExtractPath;
        private readonly string _ankplsFilePath;
        private List<string> _cards = new List<string>();
        private List<string> _imagePaths = new List<string>();
        private int _imageCount = 0;
        
        // UI要素
        private Picker _cardTypePicker;
        private Editor _frontTextEditor, _backTextEditor;
        private RichTextLabel _frontPreviewLabel, _backPreviewLabel;
        private RichTextLabel _choiceQuestionPreviewLabel, _choiceExplanationPreviewLabel;
        private VerticalStackLayout _basicCardLayout;
        private StackLayout _multipleChoiceLayout, _imageFillLayout;
        private Editor _choiceQuestion, _choiceQuestionExplanation;
        private StackLayout _choicesContainer;
        private Editor _lastFocusedEditor = null;
        
        // プレビュー更新のデバウンス用
        private System.Timers.Timer _frontPreviewTimer;
        private System.Timers.Timer _backPreviewTimer;
        private System.Timers.Timer _choiceQuestionPreviewTimer;
        private System.Timers.Timer _choiceExplanationPreviewTimer;
        
        // 選択肢カード用
        private bool _removeNumbers = false;
        
        // 画像穴埋めカード用
        private string _selectedImagePath = "";
        private List<SkiaSharp.SKRect> _selectionRects = new List<SkiaSharp.SKRect>();
        private SkiaSharp.SKBitmap _imageBitmap;
        private SkiaSharp.SKPoint _startPoint, _endPoint;
        private bool _isDragging = false;
        private const float HANDLE_SIZE = 15;
        private SkiaSharp.Views.Maui.Controls.SKCanvasView _canvasView;
        
        // 編集対象のカードID（編集モード用）
        private string _editCardId = null;
        
        // ページ画像追加機能のコールバック（NotePage専用）
        private Func<Editor, Task> _addPageImageCallback;

        // ページ選択機能のコールバック（NotePage専用）
        private Func<int, Task> _selectPageCallback;
        private Func<Task> _loadCurrentImageCallback;
        private Func<string, Task> _showToastCallback;
        private Func<string, string, Task> _showAlertCallback;
        private Func<Editor, int, Task> _selectPageForImageCallback; // 新しく追加：ページ選択用画像追加

        // 元のAdd.xaml.csから追加するフィールド
        private bool _isDirty = false;
        private System.Timers.Timer _autoSaveTimer;
        private string _tempPath;
        private string _ankplsPath;

        // Ctrlキー押下状態を管理
        private bool _isCtrlDown = false;

        public CardManager(string cardsPath, string tempPath, string cardId = null, string subFolder = null)
        {
            _ankplsFilePath = cardsPath;
            _tempExtractPath = tempPath;
            _tempPath = tempPath;
            _ankplsPath = cardsPath;
            _cardsFilePath = Path.Combine(_tempExtractPath, "cards.txt");
            _editCardId = cardId;
            
            // サブフォルダ情報がある場合は、.ankplsファイルのパスを正しく構築
            if (!string.IsNullOrEmpty(subFolder))
            {
                var localBasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Flashnote");
                var noteName = Path.GetFileNameWithoutExtension(cardsPath);
                _ankplsFilePath = Path.Combine(localBasePath, subFolder, $"{noteName}.ankpls");
            }
            
            LoadCards();
            LoadImageCount();
            InitializeAutoSaveTimer();
        }

        /// <summary>
        /// ページ画像追加機能のコールバックを設定（NotePage専用）
        /// </summary>
        public void SetPageImageCallback(Func<Editor, Task> callback)
        {
            _addPageImageCallback = callback;
        }

        /// <summary>
        /// ページ選択機能のコールバックを設定（NotePage専用）
        /// </summary>
        public void SetPageSelectionCallbacks(
            Func<int, Task> selectPageCallback,
            Func<Task> loadCurrentImageCallback,
            Func<string, Task> showToastCallback,
            Func<string, string, Task> showAlertCallback)
        {
            _selectPageCallback = selectPageCallback;
            _loadCurrentImageCallback = loadCurrentImageCallback;
            _showToastCallback = showToastCallback;
            _showAlertCallback = showAlertCallback;
        }

        /// <summary>
        /// ページ選択用画像追加コールバックを設定
        /// </summary>
        public void SetPageSelectionImageCallback(Func<Editor, int, Task> callback)
        {
            _selectPageForImageCallback = callback;
        }

        /// <summary>
        /// 現在フォーカスされているエディターを取得
        /// </summary>
        public Editor GetCurrentEditor()
        {
            // 基本カードの表面エディター
            if (_frontTextEditor != null && _frontTextEditor.IsFocused)
            {
                _lastFocusedEditor = _frontTextEditor;
                return _frontTextEditor;
            }
            
            // 基本カードの裏面エディター
            if (_backTextEditor != null && _backTextEditor.IsFocused)
            {
                _lastFocusedEditor = _backTextEditor;
                return _backTextEditor;
            }
            
            // 選択肢カードの問題エディター
            if (_choiceQuestion != null && _choiceQuestion.IsFocused)
            {
                _lastFocusedEditor = _choiceQuestion;
                return _choiceQuestion;
            }
            
            // 選択肢カードの解説エディター
            if (_choiceQuestionExplanation != null && _choiceQuestionExplanation.IsFocused)
            {
                _lastFocusedEditor = _choiceQuestionExplanation;
                return _choiceQuestionExplanation;
            }

            // 動的に作成された選択肢エディターを確認
            if (_choicesContainer != null)
            {
                foreach (var stack in _choicesContainer.Children.OfType<StackLayout>())
                {
                    var editor = stack.Children.OfType<Editor>().FirstOrDefault();
                    if (editor != null && editor.IsFocused)
                    {
                        _lastFocusedEditor = editor;
                        return editor;
                    }
                }
            }

            // フォーカスされているエディターがない場合、最後にフォーカスされたエディターを使用
            if (_lastFocusedEditor != null)
            {
                return _lastFocusedEditor;
            }

            // デフォルトは表面
            _lastFocusedEditor = _frontTextEditor;
            return _frontTextEditor;
        }

        /// <summary>
        /// 指定されたエディターのプレビューを更新
        /// </summary>
        public void UpdatePreviewForEditor(Editor editor)
        {
            if (editor == _frontTextEditor)
            {
                UpdateFrontPreviewWithDebounce();
            }
            else if (editor == _backTextEditor)
            {
                UpdateBackPreviewWithDebounce();
            }
            else if (editor == _choiceQuestion)
            {
                UpdateChoiceQuestionPreviewWithDebounce();
            }
            else if (editor == _choiceQuestionExplanation)
            {
                UpdateChoiceExplanationPreviewWithDebounce();
            }
        }

        /// <summary>
        /// 画像番号を読み込む
        /// </summary>
        private void LoadImageCount()
        {
            if (File.Exists(_cardsFilePath))
            {
                var lines = File.ReadAllLines(_cardsFilePath).ToList();
                if (lines.Count > 0 && int.TryParse(lines[0], out int count))
                {
                    _imageCount = count;
                }
            }
        }

        /// <summary>
        /// 画像番号を保存する
        /// </summary>
        private void SaveImageCount()
        {
            var lines = new List<string> { _imageCount.ToString() };

            if (File.Exists(_cardsFilePath))
            {
                lines.AddRange(File.ReadAllLines(_cardsFilePath).Skip(1));
            }

            File.WriteAllLines(_cardsFilePath, lines);
        }

        /// <summary>
        /// 自動保存タイマーを初期化
        /// </summary>
        private void InitializeAutoSaveTimer()
        {
            _autoSaveTimer = new System.Timers.Timer(10000); // 10秒ごとに保存
            _autoSaveTimer.Elapsed += AutoSaveTimer_Elapsed;
            _autoSaveTimer.AutoReset = true;
            _autoSaveTimer.Enabled = true;
        }

        /// <summary>
        /// 自動保存タイマーイベント
        /// </summary>
        private async void AutoSaveTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (_isDirty)
            {
                try
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        OnSaveCardClicked(null, null);
                    });
                    _isDirty = false;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"自動保存中にエラー: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// ビットマップをファイルに保存
        /// </summary>
        private void SaveBitmapToFile(SkiaSharp.SKBitmap bitmap, string filePath)
        {
            using (var image = SkiaSharp.SKImage.FromBitmap(bitmap))
            using (var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 80))
            using (var stream = File.OpenWrite(filePath))
            {
                data.SaveTo(stream);
            }
        }

        /// <summary>
        /// 画像穴埋め用に画像を読み込み
        /// </summary>
        public async Task LoadImageForImageFill(string imagePath)
        {
            try
            {
                Debug.WriteLine($"=== LoadImageForImageFill開始 ===");
                Debug.WriteLine($"読み込み予定の画像パス: {imagePath}");
                Debug.WriteLine($"ファイル存在チェック: {File.Exists(imagePath)}");
                
                if (!File.Exists(imagePath))
                {
                    Debug.WriteLine($"❌ 画像ファイルが存在しません: {imagePath}");
                    throw new FileNotFoundException($"画像ファイルが見つかりません: {imagePath}");
                }
                
                // 既存の画像をクリア
                Debug.WriteLine("既存の画像をクリア中...");
                _imageBitmap?.Dispose();
                _selectionRects?.Clear();

                // 現在の画像を読み込み
                Debug.WriteLine("新しい画像を読み込み中...");
                _imageBitmap = SkiaSharp.SKBitmap.Decode(imagePath);
                _selectedImagePath = imagePath;

                Debug.WriteLine($"画像読み込み結果: {(_imageBitmap != null ? "成功" : "失敗")}");
                if (_imageBitmap != null)
                {
                    Debug.WriteLine($"画像サイズ: {_imageBitmap.Width}x{_imageBitmap.Height}");
                }
                
                Debug.WriteLine($"CanvasView状態: {(_canvasView != null ? "存在" : "null")}");
                if (_canvasView != null)
                {
                    Debug.WriteLine($"CanvasViewサイズ: {_canvasView.Width}x{_canvasView.Height}");
                    Debug.WriteLine($"CanvasView表示状態: IsVisible={_canvasView.IsVisible}");
                }

                if (_imageBitmap != null && _canvasView != null)
                {
                    Debug.WriteLine("CanvasView再描画を要求中...");
                    _canvasView.InvalidateSurface();
                    Debug.WriteLine("InvalidateSurface完了");
                }
                else
                {
                    string errorMsg = $"画像読み込み失敗 - imageBitmap: {(_imageBitmap != null ? "OK" : "null")}, canvasView: {(_canvasView != null ? "OK" : "null")}";
                    Debug.WriteLine($"❌ {errorMsg}");
                    throw new Exception(errorMsg);
                }
                
                Debug.WriteLine("=== LoadImageForImageFill完了 ===");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ 画像穴埋め用画像読み込みエラー: {ex.Message}");
                Debug.WriteLine($"❌ スタックトレース: {ex.StackTrace}");
                throw;
            }
        }

        /// <summary>
        /// カード追加UIを初期化
        /// </summary>
        public void InitializeCardUI(VerticalStackLayout container, bool includePageImageButtons = false)
        {
            try
            {
                // 既存の内容をクリア
                container.Children.Clear();
                
                // カードタイプ選択
                _cardTypePicker = new Picker
                {
                    Title = "カードタイプを選択",
                    HorizontalOptions = LayoutOptions.Fill
                };
                _cardTypePicker.Items.Add("基本・穴埋め");
                _cardTypePicker.Items.Add("選択肢");
                _cardTypePicker.Items.Add("画像穴埋め");
                _cardTypePicker.SelectedIndex = 0;
                _cardTypePicker.SelectedIndexChanged += OnCardTypeChanged;
                
                container.Children.Add(_cardTypePicker);
                
                // 装飾ボタン群（共通）
                var decorationButtonsLayout = CreateDecorationButtons();
                container.Children.Add(decorationButtonsLayout);
                
                // 基本・穴埋めカード入力
                CreateBasicCardLayout(includePageImageButtons);
                container.Children.Add(_basicCardLayout);
                
                // 選択肢カード入力
                CreateMultipleChoiceLayout(includePageImageButtons);
                container.Children.Add(_multipleChoiceLayout);
                
                // 画像穴埋めカード入力
                CreateImageFillLayout(includePageImageButtons);
                container.Children.Add(_imageFillLayout);
                
                // 保存ボタン
                var saveButton = new Button
                {
                    Text = "カードを保存",
                    BackgroundColor = Colors.Green,
                    TextColor = Colors.White,
                    Margin = new Thickness(0, 10)
                };
                saveButton.Clicked += OnSaveCardClicked;
                
                container.Children.Add(saveButton);
                
                // 編集モードの場合、カードデータを読み込み
                if (!string.IsNullOrEmpty(_editCardId))
                {
                    LoadCardData(_editCardId);
                }
                
                // リッチテキストペースト機能を有効化
                EnableRichTextPaste();
                
                Debug.WriteLine("カード追加UI初期化完了");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"カード追加UI初期化エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 基本・穴埋めカードレイアウトを作成
        /// </summary>
        private void CreateBasicCardLayout(bool includePageImageButtons)
        {
            _basicCardLayout = new VerticalStackLayout();
            
            // 表面
            var frontHeaderLayout = new HorizontalStackLayout();
            var frontLabel = new Label { Text = "表面", FontSize = 16 };
            var frontImageButton = new Button { Text = "画像追加", WidthRequest = 80 };
            frontImageButton.Clicked += OnFrontAddImageClicked;
            
            var frontPageImageButton = new Button { Text = "ページ画像", WidthRequest = 80 };
            frontPageImageButton.Clicked += async (s, e) => await OnAddPageImage(_frontTextEditor);
            
            frontHeaderLayout.Children.Add(frontLabel);
            frontHeaderLayout.Children.Add(frontImageButton);
            
            if (includePageImageButtons)
            {
                frontHeaderLayout.Children.Add(frontPageImageButton);
            }
            
            _frontTextEditor = new Editor
            {
                HeightRequest = 80,
                Placeholder = "表面の内容を入力",
                AutomationId = "FrontTextEditor" // AutomationIdを追加
            };
            _frontTextEditor.TextChanged += OnFrontTextChanged;
            _frontTextEditor.Focused += (s, e) => _lastFocusedEditor = _frontTextEditor;
            
            var frontPreviewLabel = new Label { Text = "プレビュー", FontSize = 16 };
            _frontPreviewLabel = new RichTextLabel
            { 
                ImageFolderPath = _tempExtractPath
            };
            
            // デバッグ情報を出力
            System.Diagnostics.Debug.WriteLine($"=== RichTextLabel作成 ===");
            System.Diagnostics.Debug.WriteLine($"_tempExtractPath: {_tempExtractPath}");
            System.Diagnostics.Debug.WriteLine($"FrontPreviewLabel.ImageFolderPath: {_frontPreviewLabel.ImageFolderPath}");
            
            // 裏面
            var backHeaderLayout = new HorizontalStackLayout();
            var backLabel = new Label { Text = "裏面", FontSize = 16 };
            var backImageButton = new Button { Text = "画像追加", WidthRequest = 80 };
            backImageButton.Clicked += OnBackAddImageClicked;

            var backPageImageButton = new Button { Text = "ページ画像", WidthRequest = 80 };
            backPageImageButton.Clicked += async (s, e) => await OnAddPageImage(_backTextEditor);
            
            backHeaderLayout.Children.Add(backLabel);
            backHeaderLayout.Children.Add(backImageButton);
            
            if (includePageImageButtons)
            {
                backHeaderLayout.Children.Add(backPageImageButton);
            }
            
            _backTextEditor = new Editor
            {
                HeightRequest = 80,
                Placeholder = "Markdown 記法で装飾できます",
                AutomationId = "BackTextEditor" // AutomationIdを追加
            };
            _backTextEditor.TextChanged += OnBackTextChanged;
            _backTextEditor.Focused += (s, e) => _lastFocusedEditor = _backTextEditor;
            
            var backPreviewLabel = new Label { Text = "プレビュー", FontSize = 16 };
            _backPreviewLabel = new RichTextLabel
            { 
                ImageFolderPath = _tempExtractPath
            };
            
            // デバッグ情報を出力
            System.Diagnostics.Debug.WriteLine($"BackPreviewLabel.ImageFolderPath: {_backPreviewLabel.ImageFolderPath}");
            
            _basicCardLayout.Children.Add(frontHeaderLayout);
            _basicCardLayout.Children.Add(_frontTextEditor);
            _basicCardLayout.Children.Add(frontPreviewLabel);
            _basicCardLayout.Children.Add(_frontPreviewLabel);
            _basicCardLayout.Children.Add(backHeaderLayout);
            _basicCardLayout.Children.Add(_backTextEditor);
            _basicCardLayout.Children.Add(backPreviewLabel);
            _basicCardLayout.Children.Add(_backPreviewLabel);
        }

        /// <summary>
        /// 装飾ボタンを作成
        /// </summary>
        private HorizontalStackLayout CreateDecorationButtons()
        {
            var decorationButtonsLayout = new HorizontalStackLayout
            {
                Spacing = 5,
                Margin = new Thickness(0, 5)
            };
            
            var boldButton = new Button { Text = "B", WidthRequest = 50 };
            boldButton.Clicked += OnBoldClicked;
            
            var redButton = new Button { Text = "赤", WidthRequest = 40, BackgroundColor = Colors.Red, TextColor = Colors.White };
            redButton.Clicked += OnRedColorClicked;
            
            var blueButton = new Button { Text = "青", WidthRequest = 40, BackgroundColor = Colors.Blue, TextColor = Colors.White };
            blueButton.Clicked += OnBlueColorClicked;
            
            var greenButton = new Button { Text = "緑", WidthRequest = 40, BackgroundColor = Colors.Green, TextColor = Colors.White };
            greenButton.Clicked += OnGreenColorClicked;
            
            var yellowButton = new Button { Text = "黄", WidthRequest = 40, BackgroundColor = Colors.Yellow, TextColor = Colors.Black };
            yellowButton.Clicked += OnYellowColorClicked;
            
            var purpleButton = new Button { Text = "紫", WidthRequest = 40, BackgroundColor = Colors.Purple, TextColor = Colors.White };
            purpleButton.Clicked += OnPurpleColorClicked;
            
            var orangeButton = new Button { Text = "橙", WidthRequest = 40, BackgroundColor = Colors.Orange, TextColor = Colors.White };
            orangeButton.Clicked += OnOrangeColorClicked;
            
            var supButton = new Button { Text = "x²", WidthRequest = 60 };
            supButton.Clicked += OnSuperscriptClicked;
            
            var subButton = new Button { Text = "x₂", WidthRequest = 60 };
            subButton.Clicked += OnSubscriptClicked;
            
            var blankButton = new Button { Text = "()", WidthRequest = 60 };
            blankButton.Clicked += OnBlankClicked;

            decorationButtonsLayout.Children.Add(boldButton);
            decorationButtonsLayout.Children.Add(redButton);
            decorationButtonsLayout.Children.Add(blueButton);
            decorationButtonsLayout.Children.Add(greenButton);
            decorationButtonsLayout.Children.Add(yellowButton);
            decorationButtonsLayout.Children.Add(purpleButton);
            decorationButtonsLayout.Children.Add(orangeButton);
            decorationButtonsLayout.Children.Add(supButton);
            decorationButtonsLayout.Children.Add(subButton);
            decorationButtonsLayout.Children.Add(blankButton);
            
            return decorationButtonsLayout;
        }

        /// <summary>
        /// 選択肢カードレイアウトを作成
        /// </summary>
        private void CreateMultipleChoiceLayout(bool includePageImageButtons)
        {
            _multipleChoiceLayout = new StackLayout { IsVisible = false };
            
            var choiceQuestionHeaderLayout = new HorizontalStackLayout();
            var choiceQuestionLabel = new Label { Text = "選択肢問題", FontSize = 16 };
            var choiceQuestionImageButton = new Button { Text = "画像追加", WidthRequest = 80 };
            choiceQuestionImageButton.Clicked += OnChoiceQuestionAddImageClicked;
            
            var choiceQuestionPageImageButton = new Button { Text = "ページ画像", WidthRequest = 80 };
            choiceQuestionPageImageButton.Clicked += async (s, e) => await OnAddPageImage(_choiceQuestion);
            
            choiceQuestionHeaderLayout.Children.Add(choiceQuestionLabel);
            choiceQuestionHeaderLayout.Children.Add(choiceQuestionImageButton);
            
            if (includePageImageButtons)
            {
                choiceQuestionHeaderLayout.Children.Add(choiceQuestionPageImageButton);
            }
            
            _choiceQuestion = new Editor
            {
                HeightRequest = 80,
                Placeholder = "選択肢問題を入力（改行で区切って複数入力可能）",
                AutomationId = "ChoiceQuestionEditor" // AutomationIdを追加
            };
            _choiceQuestion.TextChanged += OnChoiceQuestionTextChanged;
            _choiceQuestion.Focused += (s, e) => _lastFocusedEditor = _choiceQuestion;
            
            var choiceQuestionPreviewLabel = new Label { Text = "プレビュー", FontSize = 16 };
            _choiceQuestionPreviewLabel = new RichTextLabel
            { 
                HeightRequest = 80,
                ImageFolderPath = _tempExtractPath
            };
            
            // デバッグ情報を出力
            System.Diagnostics.Debug.WriteLine($"ChoiceQuestionPreviewLabel.ImageFolderPath: {_choiceQuestionPreviewLabel.ImageFolderPath}");
            
            // 選択肢
            var choicesLabel = new Label { Text = "選択肢", FontSize = 16 };
            
            var choicesControlLayout = new HorizontalStackLayout();
            var addChoiceButton = new Button { Text = "選択肢を追加" };
            addChoiceButton.Clicked += OnAddChoice;
            
            var removeNumbersSwitch = new Microsoft.Maui.Controls.Switch();
            var removeNumbersLabel = new Label { Text = "番号を自動削除" };
            removeNumbersSwitch.Toggled += OnRemoveNumbersToggled;
            
            choicesControlLayout.Children.Add(addChoiceButton);
            choicesControlLayout.Children.Add(removeNumbersLabel);
            choicesControlLayout.Children.Add(removeNumbersSwitch);
            
            _choicesContainer = new StackLayout();
            
            // 選択肢解説
            var choiceExplanationHeaderLayout = new HorizontalStackLayout();
            var choiceExplanationLabel = new Label { Text = "選択肢解説", FontSize = 16 };
            var choiceExplanationImageButton = new Button { Text = "画像追加", WidthRequest = 80 };
            choiceExplanationImageButton.Clicked += OnChoiceExplanationAddImageClicked;

            var choiceExplanationPageImageButton = new Button { Text = "ページ画像", WidthRequest = 80 };
            choiceExplanationPageImageButton.Clicked += async (s, e) => await OnAddPageImage(_choiceQuestionExplanation);
            
            choiceExplanationHeaderLayout.Children.Add(choiceExplanationLabel);
            choiceExplanationHeaderLayout.Children.Add(choiceExplanationImageButton);
            
            if (includePageImageButtons)
            {
                choiceExplanationHeaderLayout.Children.Add(choiceExplanationPageImageButton);
            }
            
            _choiceQuestionExplanation = new Editor
            {
                HeightRequest = 80,
                Placeholder = "選択肢の解説を入力",
                AutomationId = "ChoiceExplanationEditor" // AutomationIdを追加
            };
            _choiceQuestionExplanation.TextChanged += OnChoiceExplanationTextChanged;
            _choiceQuestionExplanation.Focused += (s, e) => _lastFocusedEditor = _choiceQuestionExplanation;
            
            var choiceExplanationPreviewLabel = new Label { Text = "プレビュー", FontSize = 16 };
            _choiceExplanationPreviewLabel = new RichTextLabel
            { 
                HeightRequest = 80,
                ImageFolderPath = _tempExtractPath
            };
            
            // デバッグ情報を出力
            System.Diagnostics.Debug.WriteLine($"ChoiceExplanationPreviewLabel.ImageFolderPath: {_choiceExplanationPreviewLabel.ImageFolderPath}");
            
            _multipleChoiceLayout.Children.Add(choiceQuestionHeaderLayout);
            _multipleChoiceLayout.Children.Add(_choiceQuestion);
            _multipleChoiceLayout.Children.Add(choiceQuestionPreviewLabel);
            _multipleChoiceLayout.Children.Add(_choiceQuestionPreviewLabel);
            _multipleChoiceLayout.Children.Add(choicesLabel);
            _multipleChoiceLayout.Children.Add(choicesControlLayout);
            _multipleChoiceLayout.Children.Add(_choicesContainer);
            _multipleChoiceLayout.Children.Add(choiceExplanationHeaderLayout);
            _multipleChoiceLayout.Children.Add(_choiceQuestionExplanation);
            _multipleChoiceLayout.Children.Add(choiceExplanationPreviewLabel);
            _multipleChoiceLayout.Children.Add(_choiceExplanationPreviewLabel);
        }

        /// <summary>
        /// 画像穴埋めカードレイアウトを作成
        /// </summary>
        private void CreateImageFillLayout(bool includePageImageButtons)
        {
            _imageFillLayout = new StackLayout { IsVisible = false };
            
            var imageFillLabel = new Label { Text = "画像穴埋めカード", FontSize = 16 };
            
            var imageSelectLayout = new HorizontalStackLayout();
            var selectImageButton = new Button { Text = "画像を選択" };
            selectImageButton.Clicked += OnSelectImage;
            
            if (includePageImageButtons)
            {
                var selectPageButton = new Button { Text = "ページを選択" };
                selectPageButton.Clicked += async (s, e) => await OnSelectPageForImageFill();
                imageSelectLayout.Children.Add(selectPageButton);
            }
            
            imageSelectLayout.Children.Add(selectImageButton);
            
            var clearImageButton = new Button { Text = "画像をクリア" };
            clearImageButton.Clicked += OnClearImage;
            imageSelectLayout.Children.Add(clearImageButton);
            
            _canvasView = new SKCanvasView
            {
                HeightRequest = 300,
                BackgroundColor = Colors.LightGray
            };
            _canvasView.PaintSurface += OnCanvasViewPaintSurface;
            _canvasView.Touch += OnCanvasTouch;
            
            _imageFillLayout.Children.Add(imageFillLabel);
            _imageFillLayout.Children.Add(imageSelectLayout);
            _imageFillLayout.Children.Add(_canvasView);
        }

        #region イベントハンドラー

        /// <summary>
        /// カードタイプ変更イベント
        /// </summary>
        private void OnCardTypeChanged(object sender, EventArgs e)
        {
            if (_cardTypePicker == null) return;
            
            var selectedType = _cardTypePicker.SelectedItem?.ToString();
            Debug.WriteLine($"=== カードタイプ変更: {selectedType} ===");
            
            // 全てのレイアウトを非表示
            if (_basicCardLayout != null) 
            {
                _basicCardLayout.IsVisible = false;
                Debug.WriteLine("基本カードレイアウト: 非表示");
            }
            if (_multipleChoiceLayout != null) 
            {
                _multipleChoiceLayout.IsVisible = false;
                Debug.WriteLine("選択肢レイアウト: 非表示");
            }
            if (_imageFillLayout != null) 
            {
                _imageFillLayout.IsVisible = false;
                Debug.WriteLine("画像穴埋めレイアウト: 非表示");
            }
            
            // 選択されたタイプに応じてレイアウトを表示
            switch (selectedType)
            {
                case "基本・穴埋め":
                    if (_basicCardLayout != null) 
                    {
                        _basicCardLayout.IsVisible = true;
                        Debug.WriteLine("基本カードレイアウト: 表示");
                    }
                    break;
                case "選択肢":
                    if (_multipleChoiceLayout != null) 
                    {
                        _multipleChoiceLayout.IsVisible = true;
                        Debug.WriteLine("選択肢レイアウト: 表示");
                    }
                    break;
                case "画像穴埋め":
                    if (_imageFillLayout != null) 
                    {
                        _imageFillLayout.IsVisible = true;
                        Debug.WriteLine("画像穴埋めレイアウト: 表示");
                        Debug.WriteLine($"CanvasView状態: {(_canvasView != null ? "存在" : "null")}");
                        if (_canvasView != null)
                        {
                            Debug.WriteLine($"CanvasView表示状態: IsVisible={_canvasView.IsVisible}");
                            Debug.WriteLine($"CanvasViewサイズ: {_canvasView.Width}x{_canvasView.Height}");
                            Debug.WriteLine($"CanvasView HeightRequest: {_canvasView.HeightRequest}");
                            
                            // 再描画を強制実行
                            Debug.WriteLine("CanvasView強制再描画を実行中...");
                            _canvasView.InvalidateSurface();
                        }
                    }
                    break;
            }
            
            Debug.WriteLine($"=== カードタイプ変更完了: {selectedType} ===");
        }

        /// <summary>
        /// 表面テキスト変更イベント
        /// </summary>
        private void OnFrontTextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateFrontPreviewWithDebounce();
        }

        /// <summary>
        /// 裏面テキスト変更イベント
        /// </summary>
        private void OnBackTextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateBackPreviewWithDebounce();
        }

        /// <summary>
        /// 選択肢問題テキスト変更イベント
        /// </summary>
        private void OnChoiceQuestionTextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateChoiceQuestionPreviewWithDebounce();
        }

        /// <summary>
        /// 選択肢解説テキスト変更イベント
        /// </summary>
        private void OnChoiceExplanationTextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateChoiceExplanationPreviewWithDebounce();
        }

        /// <summary>
        /// 太字ボタンのクリックイベント
        /// </summary>
        private void OnBoldClicked(object sender, EventArgs e)
        {
            var editor = GetCurrentEditor();
            if (editor != null)
            {
                InsertDecorationText(editor, "**", "**");
            }
        }

        /// <summary>
        /// 赤色ボタンのクリックイベント
        /// </summary>
        private void OnRedColorClicked(object sender, EventArgs e)
        {
            var editor = GetCurrentEditor();
            if (editor != null)
            {
                InsertDecorationText(editor, "{{red|", "}}");
            }
        }

        /// <summary>
        /// 青色ボタンのクリックイベント
        /// </summary>
        private void OnBlueColorClicked(object sender, EventArgs e)
        {
            var editor = GetCurrentEditor();
            if (editor != null)
            {
                InsertDecorationText(editor, "{{blue|", "}}");
            }
        }

        /// <summary>
        /// 緑色ボタンのクリックイベント
        /// </summary>
        private void OnGreenColorClicked(object sender, EventArgs e)
        {
            var editor = GetCurrentEditor();
            if (editor != null)
            {
                InsertDecorationText(editor, "{{green|", "}}");
            }
        }

        /// <summary>
        /// 黄色ボタンのクリックイベント
        /// </summary>
        private void OnYellowColorClicked(object sender, EventArgs e)
        {
            var editor = GetCurrentEditor();
            if (editor != null)
            {
                InsertDecorationText(editor, "{{yellow|", "}}");
            }
        }

        /// <summary>
        /// 紫色ボタンのクリックイベント
        /// </summary>
        private void OnPurpleColorClicked(object sender, EventArgs e)
        {
            var editor = GetCurrentEditor();
            if (editor != null)
            {
                InsertDecorationText(editor, "{{purple|", "}}");
            }
        }

        /// <summary>
        /// オレンジ色ボタンのクリックイベント
        /// </summary>
        private void OnOrangeColorClicked(object sender, EventArgs e)
        {
            var editor = GetCurrentEditor();
            if (editor != null)
            {
                InsertDecorationText(editor, "{{orange|", "}}");
            }
        }

        /// <summary>
        /// 上付き文字ボタンのクリックイベント
        /// </summary>
        private void OnSuperscriptClicked(object sender, EventArgs e)
        {
            var editor = GetCurrentEditor();
            if (editor != null)
            {
                InsertDecorationText(editor, "^^", "^^");
            }
        }

        /// <summary>
        /// 下付き文字ボタンのクリックイベント
        /// </summary>
        private void OnSubscriptClicked(object sender, EventArgs e)
        {
            var editor = GetCurrentEditor();
            if (editor != null)
            {
                InsertDecorationText(editor, "~~", "~~");
            }
        }

        /// <summary>
        /// 穴埋めボタンのクリックイベント
        /// </summary>
        private void OnBlankClicked(object sender, EventArgs e)
        {
            var editor = GetCurrentEditor();
            
            // 基本カードの表面エディターでのみ穴埋めを挿入
            if (editor != null && editor == _frontTextEditor)
            {
                Debug.WriteLine($"穴埋めを挿入: {editor.AutomationId}");
                InsertBlankText(editor);
            }
            else
            {
                Debug.WriteLine("穴埋めは基本カードの表面でのみ使用できます");
            }
        }

        /// <summary>
        /// 穴埋めテキストを挿入（かっこがある場合は削除）
        /// </summary>
        private async void InsertBlankText(Editor editor)
        {
            if (editor == null) return;

            try
            {
                // エディターから直接選択されたテキストを取得を試みる
                string selectedText = GetSelectedTextFromEditor(editor);
                
                if (!string.IsNullOrEmpty(selectedText))
                {
                    // 選択されたテキストの最初と最後のかっこを削除
                    string cleanedText = RemoveSurroundingParentheses(selectedText);
                    
                    // 穴埋めタグで囲む
                    string decoratedText = "<<blank|" + cleanedText + ">>";
                    
                    // 現在のテキストとカーソル位置を取得
                    int start = GetSelectionStart(editor);
                    int length = GetSelectionLength(editor);
                    string text = editor.Text ?? "";
                    
                    if (start >= 0 && length > 0 && start + length <= text.Length)
                    {
                        // 選択範囲を装飾されたテキストに置換
                        string newText = text.Remove(start, length).Insert(start, decoratedText);
                        editor.Text = newText;
                        editor.CursorPosition = start + decoratedText.Length;
                        Debug.WriteLine($"選択されたテキスト '{selectedText}' を穴埋めに変換しました: {decoratedText}");
                    }
                    else
                    {
                        // 選択範囲の取得に失敗した場合はカーソル位置に挿入
                        await InsertAtCursor(editor, decoratedText);
                    }
                }
                else
                {
                    // 選択されたテキストがない場合はカーソル位置に穴埋めタグを挿入
                    string insertText = "<<blank|>>";
                    await InsertAtCursor(editor, insertText, 8); // "<<blank|" の後にカーソルを配置
                }

                // プレビューを更新
                UpdatePreviewForEditor(editor);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"穴埋めテキスト挿入中にエラー: {ex.Message}");
                // エラーが発生した場合はシンプルな挿入に戻る
                string insertText = "<<blank|>>";
                await InsertAtCursor(editor, insertText, 8);
                UpdatePreviewForEditor(editor);
            }
        }

        /// <summary>
        /// 文字列の最初と最後のかっこを削除
        /// </summary>
        private string RemoveSurroundingParentheses(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            
            // 最初と最後が対応するかっこの場合は削除
            var parenthesesPairs = new (char open, char close)[]
            {
                ('(', ')'),
                ('（', '）'),
                ('[', ']'),
                ('［', '］'),
                ('{', '}'),
                ('｛', '｝')
            };
            
            foreach (var (open, close) in parenthesesPairs)
            {
                if (text.Length >= 2 && text[0] == open && text[text.Length - 1] == close)
                {
                    string result = text.Substring(1, text.Length - 2);
                    Debug.WriteLine($"かっこを削除: '{text}' → '{result}'");
                    return result;
                }
            }
            
            return text;
        }

        #endregion

        #region プレビュー更新

        /// <summary>
        /// 表面プレビューを更新（デバウンス付き）
        /// </summary>
        private void UpdateFrontPreviewWithDebounce()
        {
            if (_frontTextEditor == null || _frontPreviewLabel == null) return;
            
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
                        var markdown = _frontTextEditor.Text ?? "";
                        _frontPreviewLabel.RichText = markdown;
                        _frontPreviewLabel.ShowAnswer = false;
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
        private void UpdateBackPreviewWithDebounce()
        {
            if (_backTextEditor == null || _backPreviewLabel == null) return;
            
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
                        var markdown = _backTextEditor.Text ?? "";
                        _backPreviewLabel.RichText = markdown;
                        _backPreviewLabel.ShowAnswer = false;
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

        /// <summary>
        /// 選択肢問題プレビューを更新（デバウンス付き）
        /// </summary>
        private void UpdateChoiceQuestionPreviewWithDebounce()
        {
            if (_choiceQuestion == null || _choiceQuestionPreviewLabel == null) return;
            
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
                        var markdown = _choiceQuestion.Text ?? "";
                        _choiceQuestionPreviewLabel.RichText = markdown;
                        _choiceQuestionPreviewLabel.ShowAnswer = false;
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
        private void UpdateChoiceExplanationPreviewWithDebounce()
        {
            if (_choiceQuestionExplanation == null || _choiceExplanationPreviewLabel == null) return;
            
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
                        var markdown = _choiceQuestionExplanation.Text ?? "";
                        _choiceExplanationPreviewLabel.RichText = markdown;
                        _choiceExplanationPreviewLabel.ShowAnswer = false;
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

        #endregion

        /// <summary>
        /// 画像穴埋め用のページ選択
        /// </summary>
        public async Task OnSelectPageForImageFill()
        {
            try
            {
                Debug.WriteLine("=== OnSelectPageForImageFill開始 ===");
                
                if (_selectPageCallback == null)
                {
                    Debug.WriteLine("selectPageCallbackが設定されていません");
                    return;
                }
                
                // ページ選択モードを直接開始（アラートなし）
                Debug.WriteLine("ページ選択モードを開始");
                
                // 現在のページインデックスを取得して選択オーバーレイを表示
                // NotePage.xaml.csのShowPageSelectionOverlayが呼ばれる
                await _selectPageCallback(0); // 現在のページから開始
                
                if (_showToastCallback != null)
                {
                    await _showToastCallback("ページ選択モード開始: スクロールしてページを選択し、「選択」ボタンをクリックしてください");
                }
                
                Debug.WriteLine("=== OnSelectPageForImageFill完了 ===");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ページ選択エラー: {ex.Message}");
                Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                
                // エラー時もアラートではなくトーストメッセージのみ
                if (_showToastCallback != null)
                {
                    await _showToastCallback("ページ選択中にエラーが発生しました");
                }
            }
        }

        /// <summary>
        /// 現在の画像を画像穴埋め用に読み込み
        /// </summary>
        public async Task LoadCurrentImageAsImageFill()
        {
            try
            {
                if (_loadCurrentImageCallback != null)
                {
                    await _loadCurrentImageCallback();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"現在画像読み込みエラー: {ex.Message}");
                
                // エラー時もアラートではなくトーストメッセージのみ
                if (_showToastCallback != null)
                {
                    await _showToastCallback("画像の読み込み中にエラーが発生しました");
                }
            }
        }

        /// <summary>
        /// 画像選択と保存処理
        /// </summary>
        private async void OnSelectImage(object sender, EventArgs e)
        {
            var result = await FilePicker.PickAsync(new PickOptions
            {
                PickerTitle = "画像を選択",
                FileTypes = FilePickerFileType.Images
            });

            if (result != null)
            {
                using (var stream = await result.OpenReadAsync())
                {
                    _imageBitmap = SkiaSharp.SKBitmap.Decode(stream);
                }

                // 画像番号の読み込みと更新
                LoadImageCount();
                _imageCount++;
                SaveImageCount();

                // iOS版に合わせて8桁_6桁の数字形式でIDを生成
                Random random = new Random();
                string imageId8 = random.Next(10000000, 99999999).ToString(); // 8桁の数字
                string imageId6 = random.Next(100000, 999999).ToString(); // 6桁の数字
                string imageId = $"{imageId8}_{imageId6}";

                // 画像を img フォルダに保存
                var imgFolderPath = Path.Combine(_tempExtractPath, "img");
                if (!Directory.Exists(imgFolderPath))
                {
                    Directory.CreateDirectory(imgFolderPath);
                }

                var imgFileName = $"img_{imageId}.jpg";
                var imgFilePath = Path.Combine(imgFolderPath, imgFileName);
                SaveBitmapToFile(_imageBitmap, imgFilePath);
                _selectedImagePath = imgFileName;
                _imagePaths.Add(imgFilePath);
                _canvasView?.InvalidateSurface();
            }
        }

        /// <summary>
        /// 画像を追加
        /// </summary>
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
                
                string imageFolder = Path.Combine(_tempExtractPath, "img");
                Directory.CreateDirectory(imageFolder);

                string newFileName = $"img_{imageId}.jpg";
                string newFilePath = Path.Combine(imageFolder, newFileName);

                // 画像を読み込んで圧縮して保存
                using (var sourceStream = await result.OpenReadAsync())
                {
                    using (var bitmap = SkiaSharp.SKBitmap.Decode(sourceStream))
                    {
                        using (var image = SkiaSharp.SKImage.FromBitmap(bitmap))
                        using (var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 80)) // 品質を80%に設定
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
                UpdatePreviewForEditor(editor);
            }
        }

        /// <summary>
        /// 表面に画像を追加
        /// </summary>
        private async void OnFrontAddImageClicked(object sender, EventArgs e)
        {
            await AddImage(_frontTextEditor);
            UpdatePreviewForEditor(_frontTextEditor);
        }

        /// <summary>
        /// 裏面に画像を追加
        /// </summary>
        private async void OnBackAddImageClicked(object sender, EventArgs e)
        {
            await AddImage(_backTextEditor);
            UpdatePreviewForEditor(_backTextEditor);
        }

        /// <summary>
        /// 選択肢問題に画像を追加
        /// </summary>
        private async void OnChoiceQuestionAddImageClicked(object sender, EventArgs e)
        {
            await AddImage(_choiceQuestion);
        }

        /// <summary>
        /// 選択肢解説に画像を追加
        /// </summary>
        private async void OnChoiceExplanationAddImageClicked(object sender, EventArgs e)
        {
            await AddImage(_choiceQuestionExplanation);
        }

        /// <summary>
        /// 装飾文字を挿入するヘルパーメソッド
        /// </summary>
        private async void InsertDecorationText(Editor editor, string prefix, string suffix = "")
        {
            if (editor == null) return;

            try
            {
                // エディターから直接選択されたテキストを取得を試みる
                string selectedText = GetSelectedTextFromEditor(editor);
                
                if (!string.IsNullOrEmpty(selectedText))
                {
                    // 選択されたテキストがある場合は装飾で囲む
                    string decoratedText = prefix + selectedText + suffix;
                    
                    // 現在のテキストとカーソル位置を取得
                    int start = GetSelectionStart(editor);
                    int length = GetSelectionLength(editor);
                    string text = editor.Text ?? "";
                    
                    if (start >= 0 && length > 0 && start + length <= text.Length)
                    {
                        // 選択範囲を装飾されたテキストに置換
                        string newText = text.Remove(start, length).Insert(start, decoratedText);
                        editor.Text = newText;
                        editor.CursorPosition = start + decoratedText.Length;
                        Debug.WriteLine($"選択されたテキスト '{selectedText}' を装飾しました: {decoratedText}");
                    }
                    else
                    {
                        // 選択範囲の取得に失敗した場合はカーソル位置に挿入
                        await InsertAtCursor(editor, decoratedText);
                    }
                }
                else
                {
                    // 選択されたテキストがない場合はカーソル位置に装飾タグを挿入
                    string insertText = prefix + suffix;
                    await InsertAtCursor(editor, insertText, prefix.Length);
                }

                // プレビューを更新
                UpdatePreviewForEditor(editor);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"装飾テキスト挿入中にエラー: {ex.Message}");
                // エラーが発生した場合はシンプルな挿入に戻る
                string insertText = prefix + suffix;
                await InsertAtCursor(editor, insertText, prefix.Length);
                UpdatePreviewForEditor(editor);
            }
        }

        /// <summary>
        /// エディターから選択されたテキストを取得
        /// </summary>
        private string GetSelectedTextFromEditor(Editor editor)
        {
            try
            {
                // SelectionStart と SelectionLength プロパティを試す
                var selectionStart = GetSelectionStart(editor);
                var selectionLength = GetSelectionLength(editor);
                
                if (selectionStart >= 0 && selectionLength > 0)
                {
                    string text = editor.Text ?? "";
                    if (selectionStart + selectionLength <= text.Length)
                    {
                        string selectedText = text.Substring(selectionStart, selectionLength);
                        Debug.WriteLine($"エディターから選択テキストを取得: '{selectedText}' (start: {selectionStart}, length: {selectionLength})");
                        return selectedText;
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"選択テキストの取得に失敗: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 選択開始位置を取得（リフレクションまたはプロパティアクセス）
        /// </summary>
        private int GetSelectionStart(Editor editor)
        {
            try
            {
                // まず直接プロパティアクセスを試す
                var type = editor.GetType();
                var property = type.GetProperty("SelectionStart");
                if (property != null)
                {
                    var value = property.GetValue(editor);
                    if (value is int startPos)
                    {
                        return startPos;
                    }
                }

                // プラットフォーム固有のハンドラーを使用してみる
                return GetSelectionStartFromHandler(editor);
            }
            catch
            {
                return editor.CursorPosition;
            }
        }

        /// <summary>
        /// プラットフォーム固有のハンドラーから選択開始位置を取得
        /// </summary>
        private int GetSelectionStartFromHandler(Editor editor)
        {
            try
            {
                var handler = editor.Handler;
                if (handler != null)
                {
                    // Windowsの場合
#if WINDOWS
                    if (handler.PlatformView is Microsoft.UI.Xaml.Controls.TextBox textBox)
                    {
                        return textBox.SelectionStart;
                    }
#endif

                    // Androidの場合
#if ANDROID
                    if (handler.PlatformView is AndroidX.AppCompat.Widget.AppCompatEditText editText)
                    {
                        return editText.SelectionStart;
                    }
#endif

                    // iOSの場合
#if IOS
                    if (handler.PlatformView is UIKit.UITextView textView)
                    {
                        var selectedRange = textView.SelectedRange;
                        return (int)selectedRange.Location;
                    }
#endif
                }
                return editor.CursorPosition;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"プラットフォーム固有の選択開始位置取得に失敗: {ex.Message}");
                return editor.CursorPosition;
            }
        }

        /// <summary>
        /// 選択範囲の長さを取得（リフレクションまたはプロパティアクセス）
        /// </summary>
        private int GetSelectionLength(Editor editor)
        {
            try
            {
                // まず直接プロパティアクセスを試す
                var type = editor.GetType();
                var property = type.GetProperty("SelectionLength");
                if (property != null)
                {
                    var value = property.GetValue(editor);
                    if (value is int length)
                    {
                        return length;
                    }
                }

                // プラットフォーム固有のハンドラーを使用してみる
                return GetSelectionLengthFromHandler(editor);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// プラットフォーム固有のハンドラーから選択範囲を取得
        /// </summary>
        private int GetSelectionLengthFromHandler(Editor editor)
        {
            try
            {
                // ハンドラーからプラットフォーム固有の実装にアクセス
                var handler = editor.Handler;
                if (handler != null)
                {
                    // Windowsの場合
#if WINDOWS
                    if (handler.PlatformView is Microsoft.UI.Xaml.Controls.TextBox textBox)
                    {
                        return textBox.SelectionLength;
                    }
#endif

                    // Androidの場合
#if ANDROID
                    if (handler.PlatformView is AndroidX.AppCompat.Widget.AppCompatEditText editText)
                    {
                        return editText.SelectionEnd - editText.SelectionStart;
                    }
#endif

                    // iOSの場合
#if IOS
                    if (handler.PlatformView is UIKit.UITextView textView)
                    {
                        var selectedRange = textView.SelectedRange;
                        return (int)selectedRange.Length;
                    }
#endif
                }
                return 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"プラットフォーム固有の選択範囲取得に失敗: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// カーソル位置にテキストを挿入
        /// </summary>
        private async Task InsertAtCursor(Editor editor, string text, int cursorOffset = 0)
        {
            int start = editor.CursorPosition;
            string currentText = editor.Text ?? "";
            string newText = currentText.Insert(start, text);
            editor.Text = newText;
            editor.CursorPosition = start + cursorOffset;
        }

        /// <summary>
        /// 画像と選択範囲を消去
        /// </summary>
        private async void OnClearImage(object sender, EventArgs e)
        {
            try
            {
                // 確認アラート
                bool result = await UIThreadHelper.ShowAlertAsync("確認", "現在の画像と選択範囲を消去しますか？", "はい", "いいえ");
                if (!result) return;

                // 画像とデータをクリア
                _imageBitmap?.Dispose();
                _imageBitmap = null;
                _selectedImagePath = "";
                _selectionRects.Clear();
                _isDragging = false;

                // キャンバスを再描画
                _canvasView?.InvalidateSurface();

                await UIThreadHelper.ShowAlertAsync("完了", "画像を消去しました", "OK");
                
                Debug.WriteLine("画像穴埋めの画像と選択範囲を消去");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"画像消去エラー: {ex.Message}");
                await UIThreadHelper.ShowAlertAsync("エラー", "画像の消去中にエラーが発生しました", "OK");
            }
        }



        /// <summary>
        /// リソース解放
        /// </summary>
        public void Dispose()
        {
            try
            {
                // タイマーを停止・解放
                _frontPreviewTimer?.Stop();
                _frontPreviewTimer?.Dispose();
                
                _backPreviewTimer?.Stop();
                _backPreviewTimer?.Dispose();
                
                _choiceQuestionPreviewTimer?.Stop();
                _choiceQuestionPreviewTimer?.Dispose();
                
                _choiceExplanationPreviewTimer?.Stop();
                _choiceExplanationPreviewTimer?.Dispose();
                
                _autoSaveTimer?.Stop();
                _autoSaveTimer?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CardManagerリソース解放エラー: {ex.Message}");
            }
        }



        /// <summary>
        /// カードデータを読み込み
        /// </summary>
        private void LoadCards()
        {
            try
            {
                _cards.Clear();
                
                if (File.Exists(_cardsFilePath))
                {
                    var lines = File.ReadAllLines(_cardsFilePath, System.Text.Encoding.UTF8);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        // 最初の行はカード数なのでスキップ
                        if (i == 0) continue;
                        
                        var line = lines[i];
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            // cards.txtの行はUUID,更新日時の形式
                            // 実際のカードデータはJSONファイルから読み込む
                            var parts = line.Split(',');
                            if (parts.Length >= 1)
                            {
                                var cardId = parts[0];
                                // プレースホルダーとしてカードIDのみを保存
                                // 実際のデータはLoadJsonCardsで読み込まれる
                                _cards.Add($"{cardId}|placeholder");
                            }
                        }
                    }
                }
                
                // 新形式のJSONファイルも読み込み
                LoadJsonCards();
                
                Debug.WriteLine($"カード読み込み完了: {_cards.Count}件");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"カード読み込みエラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 新形式のJSONファイルを読み込み
        /// </summary>
        private void LoadJsonCards()
        {
            try
            {
                var cardsDir = Path.Combine(_tempExtractPath, "cards");
                if (!Directory.Exists(cardsDir)) return;
                
                var jsonFiles = Directory.GetFiles(cardsDir, "*.json");
                _cards.Clear(); // 新しいフォーマットのみなので、リストをクリア
                
                foreach (var jsonFile in jsonFiles)
                {
                    try
                    {
                        var jsonContent = File.ReadAllText(jsonFile, System.Text.Encoding.UTF8);
                        var cardData = JsonSerializer.Deserialize<JsonElement>(jsonContent);
                        
                        // JSONフォーマットから直接データを読み込み
                        var jsonDataObject = new
                        {
                            id = cardData.GetProperty("id").GetString(),
                            type = cardData.GetProperty("type").GetString(),
                            front = cardData.TryGetProperty("front", out var frontProp) ? frontProp.GetString() : "",
                            back = cardData.TryGetProperty("back", out var backProp) ? backProp.GetString() : "",
                            question = cardData.TryGetProperty("question", out var questionProp) ? questionProp.GetString() : "",
                            explanation = cardData.TryGetProperty("explanation", out var explanationProp) ? explanationProp.GetString() : "",
                            choices = cardData.TryGetProperty("choices", out var choicesProp) ? choicesProp : (JsonElement?)null,
                            imagePath = cardData.TryGetProperty("imagePath", out var imagePathProp) ? imagePathProp.GetString() : "",
                            selections = cardData.TryGetProperty("selections", out var selectionsProp) ? selectionsProp : (JsonElement?)null
                        };
                        
                        // カードタイプに応じて正しいJSONフォーマットに変換
                        object cardJsonData = null;
                        switch (jsonDataObject.type)
                        {
                            case "基本・穴埋め":
                                cardJsonData = new
                                {
                                    id = jsonDataObject.id,
                                    type = "basic",
                                    front = jsonDataObject.front,
                                    back = jsonDataObject.back
                                };
                                break;
                                
                            case "選択肢":
                                cardJsonData = new
                                {
                                    id = jsonDataObject.id,
                                    type = "choice",
                                    question = jsonDataObject.question,
                                    explanation = jsonDataObject.explanation,
                                    choices = jsonDataObject.choices?.EnumerateArray().Select(c => new
                                    {
                                        text = c.GetProperty("text").GetString(),
                                        correct = c.GetProperty("isCorrect").GetBoolean()
                                    }).ToList()
                                };
                                break;
                                
                            case "画像穴埋め":
                                cardJsonData = new
                                {
                                    id = jsonDataObject.id,
                                    type = "image_fill",
                                    imagePath = jsonDataObject.imagePath,
                                    selections = jsonDataObject.selections?.EnumerateArray().Select(s => new
                                    {
                                        x = s.GetProperty("x").GetSingle(),
                                        y = s.GetProperty("y").GetSingle(),
                                        width = s.GetProperty("width").GetSingle(),
                                        height = s.GetProperty("height").GetSingle()
                                    }).ToList()
                                };
                                break;
                        }
                        
                        if (cardJsonData != null)
                        {
                            var cardDataJson = JsonSerializer.Serialize(cardJsonData, new JsonSerializerOptions
                            {
                                WriteIndented = false,
                                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                            });
                            _cards.Add(cardDataJson);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"JSONファイル読み込みエラー {jsonFile}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"JSONカード読み込みエラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 新形式から旧形式に変換


        /// <summary>
        /// カードデータを読み込み
        /// </summary>
        private void LoadCardData(string cardId)
        {
            try
            {
                Debug.WriteLine($"カードデータ読み込み開始: {cardId}");
                
                var cardLine = _cards.FirstOrDefault(c => 
                {
                    try
                    {
                        var jsonElement = JsonSerializer.Deserialize<JsonElement>(c);
                        return jsonElement.GetProperty("id").GetString() == cardId;
                    }
                    catch
                    {
                        return false;
                    }
                });
                
                if (cardLine == null)
                {
                    Debug.WriteLine($"カードが見つかりません: {cardId}");
                    return;
                }
                
                var jsonElement = JsonSerializer.Deserialize<JsonElement>(cardLine);
                var cardType = jsonElement.GetProperty("type").GetString();
                
                switch (cardType)
                {
                    case "basic":
                        _cardTypePicker.SelectedIndex = 0;
                        LoadBasicCardDataFromJson(jsonElement);
                        break;
                    case "choice":
                        _cardTypePicker.SelectedIndex = 1;
                        LoadChoiceCardDataFromJson(jsonElement);
                        break;
                    case "image_fill":
                        _cardTypePicker.SelectedIndex = 2;
                        LoadImageFillCardDataFromJson(jsonElement);
                        break;
                }
                
                Debug.WriteLine($"カードデータ読み込み完了: {cardId}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"カードデータ読み込みエラー: {ex.Message}");
            }
        }



        /// <summary>
        /// 基本カードデータをJSONから読み込み
        /// </summary>
        private void LoadBasicCardDataFromJson(JsonElement jsonElement)
        {
            try
            {
                if (jsonElement.TryGetProperty("front", out var frontElement) && 
                    jsonElement.TryGetProperty("back", out var backElement))
                {
                    _frontTextEditor.Text = frontElement.GetString() ?? "";
                    _backTextEditor.Text = backElement.GetString() ?? "";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"基本カードJSONデータ読み込みエラー: {ex.Message}");
            }
        }



        /// <summary>
        /// 選択肢カードデータをJSONから読み込み
        /// </summary>
        private void LoadChoiceCardDataFromJson(JsonElement jsonElement)
        {
            try
            {
                if (jsonElement.TryGetProperty("question", out var questionElement))
                {
                    _choiceQuestion.Text = questionElement.GetString() ?? "";
                }

                if (jsonElement.TryGetProperty("explanation", out var explanationElement))
                {
                    _choiceQuestionExplanation.Text = explanationElement.GetString() ?? "";
                }

                if (jsonElement.TryGetProperty("choices", out var choicesElement) && choicesElement.ValueKind == JsonValueKind.Array)
                {
                    _choicesContainer.Children.Clear();
                    foreach (var choice in choicesElement.EnumerateArray())
                    {
                        if (choice.TryGetProperty("text", out var textElement) &&
                            choice.TryGetProperty("correct", out var correctElement))
                        {
                            var text = textElement.GetString() ?? "";
                            var isCorrect = correctElement.GetBoolean();
                        AddChoiceItem(text, isCorrect);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"選択肢カードJSONデータ読み込みエラー: {ex.Message}");
            }
        }



        /// <summary>
        /// 画像穴埋めカードデータをJSONから読み込み
        /// </summary>
        private void LoadImageFillCardDataFromJson(JsonElement jsonElement)
        {
            try
            {
                if (jsonElement.TryGetProperty("imagePath", out var imagePathElement))
                {
                    var imagePath = imagePathElement.GetString() ?? "";
                    LoadImageFromPath(imagePath);
                }
                    
                if (jsonElement.TryGetProperty("selections", out var selectionsElement) && selectionsElement.ValueKind == JsonValueKind.Array)
                {
                    _selectionRects.Clear();
                    foreach (var selection in selectionsElement.EnumerateArray())
                    {
                        if (selection.TryGetProperty("x", out var xElement) &&
                            selection.TryGetProperty("y", out var yElement) &&
                            selection.TryGetProperty("width", out var widthElement) &&
                            selection.TryGetProperty("height", out var heightElement))
                        {
                            var x = xElement.GetSingle();
                            var y = yElement.GetSingle();
                            var width = widthElement.GetSingle();
                            var height = heightElement.GetSingle();
                        
                        _selectionRects.Add(new SKRect(x, y, x + width, y + height));
                        }
                    }
                    
                    // キャンバスを更新
                    if (_canvasView != null)
                    {
                        _canvasView.InvalidateSurface();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"画像穴埋めカードJSONデータ読み込みエラー: {ex.Message}");
            }
        }

        /// <summary>
        /// パスから画像を読み込み
        /// </summary>
        private void LoadImageFromPath(string imagePath)
        {
            try
            {
                if (File.Exists(imagePath))
                {
                    _imageBitmap = SKBitmap.Decode(imagePath);
                    _selectedImagePath = imagePath;
                    
                    if (_canvasView != null)
                    {
                        _canvasView.InvalidateSurface();
                    }
                    
                    Debug.WriteLine($"画像読み込み完了: {imagePath}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"画像読み込みエラー: {ex.Message}");
            }
        }

        /// <summary>
        /// カード保存ボタンクリック
        /// </summary>
        private async void OnSaveCardClicked(object sender, EventArgs e)
        {
            try
            {
                Debug.WriteLine("=== カード保存処理開始 ===");
                
                var selectedType = _cardTypePicker.SelectedItem?.ToString();
                Debug.WriteLine($"選択されたカードタイプ: {selectedType}");
                
                switch (selectedType)
                {
                    case "基本・穴埋め":
                        await SaveBasicCard();
                        break;
                    case "選択肢":
                        await SaveChoiceCard();
                        break;
                    case "画像穴埋め":
                        await SaveImageFillCard();
                        break;
                    default:
                        if (_showToastCallback != null)
                        {
                            await _showToastCallback("カードタイプが選択されていません");
                        }
                        else
                        {
                            await UIThreadHelper.ShowAlertAsync("エラー", "カードタイプが選択されていません。", "OK");
                        }
                        return;
                }
                
                Debug.WriteLine("=== カード保存処理完了 ===");
                
                // フィールドをリセット
                ResetCardFields();
                
                // トーストを表示
                if (_showToastCallback != null)
                {
                    await _showToastCallback("カードが保存されました");
                }
                else
                {
                    Debug.WriteLine("トーストコールバックが設定されていません");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"カード保存エラー: {ex.Message}");
                
                // エラー時はトーストまたはアラートを表示
                if (_showToastCallback != null)
                {
                    await _showToastCallback("カードの保存に失敗しました");
                }
                else
                {
                    await UIThreadHelper.ShowAlertAsync("エラー", "カードの保存に失敗しました。", "OK");
                }
            }
        }

        /// <summary>
        /// 基本カードを保存
        /// </summary>
        private async Task SaveBasicCard()
        {
            try
            {
                var frontText = _frontTextEditor.Text?.Replace("\r\n", "\n").Replace("\r", "\n") ?? "";
                var backText = _backTextEditor.Text?.Replace("\r\n", "\n").Replace("\r", "\n") ?? "";
                
                if (string.IsNullOrWhiteSpace(frontText))
                {
                    if (_showToastCallback != null)
                    {
                        await _showToastCallback("表面を入力してください");
                    }
                    else
                    {
                        await UIThreadHelper.ShowAlertAsync("エラー", "表面を入力してください。", "OK");
                    }
                    return;
                }
                
                var cardId = string.IsNullOrEmpty(_editCardId) ? Guid.NewGuid().ToString() : _editCardId;
                
                // JSONフォーマットでカードデータを作成
                var cardData = JsonSerializer.Serialize(new
                {
                    id = cardId,
                    type = "基本・穴埋め",
                    front = frontText,
                    back = backText
                }, new JsonSerializerOptions
                {
                    WriteIndented = false,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
                
                await SaveCardData(cardData);
                
                Debug.WriteLine($"基本カード保存完了: {cardId}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"基本カード保存エラー: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 選択肢カードを保存
        /// </summary>
        private async Task SaveChoiceCard()
        {
            try
            {
                var questionText = _choiceQuestion.Text?.Replace("\r\n", "\n").Replace("\r", "\n") ?? "";
                var explanationText = _choiceQuestionExplanation.Text?.Replace("\r\n", "\n").Replace("\r", "\n") ?? "";
                
                if (string.IsNullOrWhiteSpace(questionText))
                {
                    if (_showToastCallback != null)
                    {
                        await _showToastCallback("選択肢問題を入力してください");
                    }
                    else
                    {
                        await UIThreadHelper.ShowAlertAsync("エラー", "選択肢問題を入力してください。", "OK");
                    }
                    return;
                }
                
                // 選択肢データを収集
                var choices = new List<object>();
                foreach (var child in _choicesContainer.Children)
                {
                    if (child is HorizontalStackLayout layout)
                    {
                        var editor = layout.Children.OfType<Editor>().FirstOrDefault();
                        var checkBox = layout.Children.OfType<CheckBox>().FirstOrDefault();
                        
                        if (editor != null && !string.IsNullOrWhiteSpace(editor.Text))
                        {
                            choices.Add(new
                            {
                                text = editor.Text?.Replace("\r\n", "\n").Replace("\r", "\n") ?? "",
                                correct = checkBox?.IsChecked ?? false
                            });
                        }
                    }
                }
                
                if (choices.Count < 1)
                {
                    if (_showToastCallback != null)
                    {
                        await _showToastCallback("最低1つの選択肢を入力してください");
                    }
                    else
                    {
                        await UIThreadHelper.ShowAlertAsync("エラー", "最低1つの選択肢を入力してください。", "OK");
                    }
                    return;
                }
                
                var cardId = string.IsNullOrEmpty(_editCardId) ? Guid.NewGuid().ToString() : _editCardId;
                
                // JSONフォーマットでカードデータを作成
                var cardData = JsonSerializer.Serialize(new
                {
                    id = cardId,
                    type = "選択肢",
                    question = questionText,
                    choices = choices,
                    explanation = explanationText
                }, new JsonSerializerOptions
                {
                    WriteIndented = false,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
                
                await SaveCardData(cardData);
                
                Debug.WriteLine($"選択肢カード保存完了: {cardId}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"選択肢カード保存エラー: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 画像穴埋めカードを保存
        /// </summary>
        private async Task SaveImageFillCard()
        {
            try
            {
                if (string.IsNullOrEmpty(_selectedImagePath))
                {
                    if (_showToastCallback != null)
                    {
                        await _showToastCallback("画像を選択してください");
                    }
                    else
                    {
                        await UIThreadHelper.ShowAlertAsync("エラー", "画像を選択してください。", "OK");
                    }
                    return;
                }
                
                if (_selectionRects.Count == 0)
                {
                    if (_showToastCallback != null)
                    {
                        await _showToastCallback("穴埋め範囲を選択してください");
                    }
                    else
                    {
                        await UIThreadHelper.ShowAlertAsync("エラー", "穴埋め範囲を選択してください。", "OK");
                    }
                    return;
                }
                
                // 選択範囲を正しい形式でシリアライズ
                var selections = _selectionRects.Select(rect => new
                {
                    x = rect.Left,
                    y = rect.Top,
                    width = rect.Width,
                    height = rect.Height
                }).ToList();
                
                var cardId = string.IsNullOrEmpty(_editCardId) ? Guid.NewGuid().ToString() : _editCardId;
                
                // JSONフォーマットでカードデータを作成
                var cardData = JsonSerializer.Serialize(new
                {
                    id = cardId,
                    type = "画像穴埋め",
                    imagePath = _selectedImagePath,
                    selections = selections
                }, new JsonSerializerOptions
                {
                    WriteIndented = false,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
                
                await SaveCardData(cardData);
                
                Debug.WriteLine($"画像穴埋めカード保存完了: {cardId}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"画像穴埋めカード保存エラー: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// カード入力フィールドをリセット
        /// </summary>
        private void ResetCardFields()
        {
            try
            {
                // 基本カードのフィールドをリセット
                if (_frontTextEditor != null)
                    _frontTextEditor.Text = "";
                if (_backTextEditor != null)
                    _backTextEditor.Text = "";
                
                // 選択肢カードのフィールドをリセット
                if (_choiceQuestion != null)
                    _choiceQuestion.Text = "";
                if (_choiceQuestionExplanation != null)
                    _choiceQuestionExplanation.Text = "";
                
                // 選択肢を1つにリセット
                if (_choicesContainer != null)
                {
                    _choicesContainer.Children.Clear();
                    AddChoiceItem("", false); // 空の選択肢を1つ追加
                }
                
                // 画像穴埋めカードをリセット
                _selectedImagePath = "";
                _selectionRects.Clear();
                
                // 画像ビューをクリア
                if (_canvasView != null)
                {
                    _canvasView.InvalidateSurface();
                }
                
                // 編集モードをリセット
                _editCardId = null;
                
                Debug.WriteLine("カード入力フィールドをリセットしました");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"フィールドリセットエラー: {ex.Message}");
            }
        }

        /// <summary>
        /// カードデータを保存
        /// </summary>
        private async Task SaveCardData(string cardData)
        {
            try
            {
                var jsonElement = JsonSerializer.Deserialize<JsonElement>(cardData);
                var cardId = jsonElement.GetProperty("id").GetString();
                
                // 既存のcards.txtから更新日時を読み込み
                var existingTimestamps = new Dictionary<string, string>();
                if (File.Exists(_cardsFilePath))
                {
                    var cardslines = File.ReadAllLines(_cardsFilePath, System.Text.Encoding.UTF8);
                    for (int i = 1; i < cardslines.Length; i++) // 最初の行（カード数）はスキップ
                    {
                        var line = cardslines[i];
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            var parts = line.Split(',');
                            if (parts.Length >= 2)
                            {
                                var existingCardId = parts[0];
                                var timestamp = parts[1];
                                existingTimestamps[existingCardId] = timestamp;
                            }
                        }
                    }
                }

                // 編集モードの場合は既存のデータを更新
                if (!string.IsNullOrEmpty(_editCardId))
                {
                    var existingIndex = _cards.FindIndex(c => 
                    {
                        try
                        {
                            var element = JsonSerializer.Deserialize<JsonElement>(c);
                            return element.GetProperty("id").GetString() == _editCardId;
                        }
                        catch
                        {
                            return false;
                        }
                    });
                    
                    if (existingIndex >= 0)
                    {
                        _cards[existingIndex] = cardData;
                    }
                    else
                    {
                        _cards.Add(cardData);
                    }
                }
                else
                {
                    _cards.Add(cardData);
                }
                
                // ファイルに保存（カード数をヘッダーとして追加）
                var lines = new List<string>();
                lines.Add(_cards.Count.ToString()); // カード数をヘッダーとして追加
                
                // 各カードデータからIDと更新日時を抽出
                foreach (var card in _cards)
                {
                    try
                    {
                        var element = JsonSerializer.Deserialize<JsonElement>(card);
                        var currentCardId = element.GetProperty("id").GetString();
                        string timestamp;
                        
                        // 既存のカードの場合は既存の更新日時を使用、新規の場合は現在時刻を使用
                        if (existingTimestamps.ContainsKey(currentCardId))
                        {
                            timestamp = existingTimestamps[currentCardId];
                        }
                        else
                        {
                            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        }
                        
                        lines.Add($"{currentCardId},{timestamp}");
                    }
                    catch
                    {
                        // JSONパースに失敗した場合はスキップ
                        continue;
                    }
                }
                
                var content = string.Join("\n", lines);
                await File.WriteAllTextAsync(_cardsFilePath, content, System.Text.Encoding.UTF8);
                
                // 新形式のJSONファイルも作成
                await CreateJsonFile(cardData);
                
                // ankplsファイルを更新
                await UpdateAnkplsFile();
                
                Debug.WriteLine("カードデータ保存完了");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"カードデータ保存エラー: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 新形式のJSONファイルを作成
        /// </summary>
        private async Task CreateJsonFile(string cardData)
        {
            try
            {
                // JSONフォーマットのデータをデシリアライズ
                var jsonElement = JsonSerializer.Deserialize<JsonElement>(cardData);
                
                // cardsディレクトリを作成
                var cardsDir = Path.Combine(_tempExtractPath, "cards");
                Directory.CreateDirectory(cardsDir);
                
                var cardId = jsonElement.GetProperty("id").GetString();
                var jsonPath = Path.Combine(cardsDir, $"{cardId}.json");
                var cardType = jsonElement.GetProperty("type").GetString();
                
                // 統一したJSONデータ形式を作成
                object jsonData = null;
                
                switch (cardType)
                {
                    case "基本・穴埋め":
                            jsonData = new
                            {
                                id = cardId,
                                type = "基本・穴埋め",
                            front = jsonElement.GetProperty("front").GetString() ?? "",
                            back = jsonElement.GetProperty("back").GetString() ?? "",
                                question = "",
                                explanation = "",
                                choices = new object[0],
                                selectionRects = new object[0]
                            };
                        break;
                        
                    case "選択肢":
                        var choices = jsonElement.GetProperty("choices").EnumerateArray().Select(c => new
                        {
                            text = c.GetProperty("text").GetString() ?? "",
                            isCorrect = c.TryGetProperty("correct", out var correctProp) ? 
                                correctProp.GetBoolean() : 
                                (c.TryGetProperty("isCorrect", out var isCorrectProp) ? isCorrectProp.GetBoolean() : false)
                        }).ToArray();
                            
                            jsonData = new
                            {
                                id = cardId,
                                type = "選択肢",
                                front = "",
                                back = "",
                            question = jsonElement.GetProperty("question").GetString() ?? "",
                            explanation = jsonElement.GetProperty("explanation").GetString() ?? "",
                            choices = choices,
                                selectionRects = new object[0]
                            };
                        break;
                        
                    case "画像穴埋め":
                        var selections = jsonElement.GetProperty("selections").EnumerateArray().Select(s => new
                        {
                            x = s.GetProperty("x").GetSingle(),
                            y = s.GetProperty("y").GetSingle(),
                            width = s.GetProperty("width").GetSingle(),
                            height = s.GetProperty("height").GetSingle()
                        }).ToArray();
                            
                            jsonData = new
                            {
                                id = cardId,
                                type = "画像穴埋め",
                                front = "",
                                back = "",
                                question = "",
                                explanation = "",
                                choices = new object[0],
                            selectionRects = selections
                            };
                        break;
                }
                
                if (jsonData != null)
                {
                    var jsonString = JsonSerializer.Serialize(jsonData, new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    });
                    await File.WriteAllTextAsync(jsonPath, jsonString, System.Text.Encoding.UTF8);
                    Debug.WriteLine($"JSONファイル作成完了: {jsonPath}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"JSONファイル作成エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// ankplsファイルを更新
        /// </summary>
        private async Task UpdateAnkplsFile()
        {
            try
            {
                if (File.Exists(_ankplsFilePath))
                {
                    File.Delete(_ankplsFilePath);
                }
                
                ZipFile.CreateFromDirectory(_tempExtractPath, _ankplsFilePath);
                
                Debug.WriteLine("ankplsファイル更新完了");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ankplsファイル更新エラー: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 選択肢項目を追加（正解フラグ付き）
        /// </summary>
        private void AddChoiceItem(string text, bool isCorrect)
        {
            try
            {
                var choiceLayout = new HorizontalStackLayout
                {
                    Spacing = 10,
                    Margin = new Thickness(0, 5)
                };
                
                var correctCheckBox = new CheckBox
                {
                    IsChecked = isCorrect,
                    VerticalOptions = LayoutOptions.Center
                };
                
                var correctLabel = new Label
                {
                    Text = "正解",
                    VerticalOptions = LayoutOptions.Center
                };
                
                var choiceEditor = new Editor
                {
                    Text = text,
                    Placeholder = "選択肢を入力（改行で区切って複数入力可能）",
                    VerticalOptions = LayoutOptions.Center,
                    HorizontalOptions = LayoutOptions.FillAndExpand,
                    HeightRequest = 40,
                    AutoSize = EditorAutoSizeOption.TextChanges,
                    AutomationId = $"ChoiceEditor_{Guid.NewGuid().ToString("N")[..8]}" // 一意のAutomationIdを追加
                };
                
                // ダークモード対応
                choiceEditor.SetAppThemeColor(Editor.BackgroundColorProperty, Colors.White, Color.FromArgb("#2D2D30"));
                choiceEditor.SetAppThemeColor(Editor.TextColorProperty, Colors.Black, Colors.White);
                
                choiceEditor.TextChanged += OnChoiceTextChanged;
                
                // リッチテキストペースト機能を追加
                // SetupPasteMonitoring(choiceEditor);
                
                var removeButton = new Button
                {
                    Text = "削除",
                    VerticalOptions = LayoutOptions.Center,
                    WidthRequest = 60
                };
                
                removeButton.Clicked += (s, e) =>
                {
                    _choicesContainer.Children.Remove(choiceLayout);
                };
                
                choiceLayout.Children.Add(correctLabel);
                choiceLayout.Children.Add(correctCheckBox);
                choiceLayout.Children.Add(choiceEditor);
                choiceLayout.Children.Add(removeButton);
                
                _choicesContainer.Children.Add(choiceLayout);
                
                Debug.WriteLine($"選択肢項目を追加: '{text}' (正解: {isCorrect})");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"選択肢項目追加エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 選択肢テキスト変更イベント
        /// </summary>
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
                    .Select(c => RemoveChoiceNumbers(c))  // 番号を削除
                    .ToList();

                if (choices.Count > 0)
                {
                    // 選択肢コンテナをクリア
                    _choicesContainer.Children.Clear();

                    // 各選択肢に対して新しいエントリを作成
                    foreach (var choice in choices)
                    {
                        AddChoiceItem(choice, false);
                    }
                }
            }

            Debug.WriteLine($"選択肢テキスト変更: '{e.NewTextValue}'");
        }

        /// <summary>
        /// 選択肢の番号を削除
        /// </summary>
        private string RemoveChoiceNumbers(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            // 選択肢番号のパターン（行の先頭のみ、全角スペース対応）
            var patterns = new[]
            {
                @"^(\d+)[\.\．][\s　]*",     // 1. または 1．（全角スペース対応）
                @"^(\d+)[\)）][\s　]*",       // 1) または 1）（全角スペース対応）
                @"^（(\d+)）[\s　]*",         // （1）（全角スペース対応）
                @"^\((\d+)\)[\s　]*",        // (1)（全角スペース対応）
                @"^([０-９]+)[\.\．][\s　]*", // 全角数字１．（全角スペース対応）
                @"^([０-９]+)[\：:][\s　]*"  // 全角数字１：（全角スペース対応）
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(text, pattern);
                if (match.Success)
                {
                    // 番号を削除して残りのテキストを返す
                    var result = text.Substring(match.Length);
                    Debug.WriteLine($"番号削除: '{text}' → '{result}'");
                    return result;
                }
            }

            return text;
        }

        /// <summary>
        /// 番号自動削除の切り替え
        /// </summary>
        private void OnRemoveNumbersToggled(object sender, ToggledEventArgs e)
        {
            _removeNumbers = e.Value;
            Debug.WriteLine($"番号自動削除: {_removeNumbers}");
        }

        /// <summary>
        /// 選択肢追加ボタンクリックイベント
        /// </summary>
        private void OnAddChoice(object sender, EventArgs e)
        {
            AddChoiceItem("", false);
        }



        /// <summary>
        /// キャンバス描画イベント
        /// </summary>
        private void OnCanvasViewPaintSurface(object sender, SKPaintSurfaceEventArgs e)
        {
            Debug.WriteLine("=== OnCanvasViewPaintSurface開始 ===");
            var canvas = e.Surface.Canvas;
            canvas.Clear(SKColors.White);
            
            Debug.WriteLine($"キャンバスサイズ: {e.Info.Width}x{e.Info.Height}");
            Debug.WriteLine($"_imageBitmap状態: {(_imageBitmap != null ? "存在" : "null")}");

            // 画像を表示
            if (_imageBitmap != null)
            {
                Debug.WriteLine($"画像を描画中 - サイズ: {_imageBitmap.Width}x{_imageBitmap.Height}");
                var rect = new SKRect(0, 0, e.Info.Width, e.Info.Height);
                Debug.WriteLine($"描画先矩形: {rect}");
                canvas.DrawBitmap(_imageBitmap, rect);
                Debug.WriteLine("画像描画完了");
            }
            else
            {
                Debug.WriteLine("⚠️ 描画する画像がありません");
            }

            Debug.WriteLine($"選択矩形数: {_selectionRects.Count}");

            // 四角形を描画
            using (var paint = new SKPaint
            {
                Color = SKColors.Red,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 3
            })
            {
                foreach (var rect in _selectionRects)
                {
                    canvas.DrawRect(rect, paint);
                    DrawResizeHandles(canvas, rect);
                }

                if (_isDragging)
                {
                    var currentRect = SKRect.Create(
                        Math.Min(_startPoint.X, _endPoint.X),
                        Math.Min(_startPoint.Y, _endPoint.Y),
                        Math.Abs(_endPoint.X - _startPoint.X),
                        Math.Abs(_endPoint.Y - _startPoint.Y)
                    );

                    canvas.DrawRect(currentRect, paint);
                }
            }
            
            Debug.WriteLine("=== OnCanvasViewPaintSurface完了 ===");
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
                canvas.DrawRect(new SKRect(rect.Left - HANDLE_SIZE / 2, rect.Top - HANDLE_SIZE / 2, rect.Left + HANDLE_SIZE / 2, rect.Top + HANDLE_SIZE / 2), paint);
                canvas.DrawRect(new SKRect(rect.Right - HANDLE_SIZE / 2, rect.Top - HANDLE_SIZE / 2, rect.Right + HANDLE_SIZE / 2, rect.Top + HANDLE_SIZE / 2), paint);
                canvas.DrawRect(new SKRect(rect.Left - HANDLE_SIZE / 2, rect.Bottom - HANDLE_SIZE / 2, rect.Left + HANDLE_SIZE / 2, rect.Bottom + HANDLE_SIZE / 2), paint);
                canvas.DrawRect(new SKRect(rect.Right - HANDLE_SIZE / 2, rect.Bottom - HANDLE_SIZE / 2, rect.Right + HANDLE_SIZE / 2, rect.Bottom + HANDLE_SIZE / 2), paint);
            }
        }

        /// <summary>
        /// キャンバスタッチイベント
        /// </summary>
        private void OnCanvasTouch(object sender, SKTouchEventArgs e)
        {
            var point = e.Location;

            switch (e.ActionType)
            {
                case SKTouchAction.Pressed:
                    if (e.MouseButton == SKMouseButton.Right)
                    {
                        // 右クリックで削除メニュー表示
                        var clickedRect = _selectionRects.FirstOrDefault(r => r.Contains(point));
                        if (clickedRect != SKRect.Empty)
                        {
                            ShowContextMenu(point, clickedRect);
                        }
                    }
                    else
                    {
                        // 左クリックで四角形を追加
                        _isDragging = true;
                        _startPoint = point;
                        _endPoint = point;
                    }
                    break;

                case SKTouchAction.Moved:
                    if (_isDragging)
                    {
                        _endPoint = point;
                    }
                    break;

                case SKTouchAction.Released:
                    if (_isDragging)
                    {
                        var rect = SKRect.Create(
                            Math.Min(_startPoint.X, _endPoint.X),
                            Math.Min(_startPoint.Y, _endPoint.Y),
                            Math.Abs(_endPoint.X - _startPoint.X),
                            Math.Abs(_endPoint.Y - _startPoint.Y)
                        );

                        if (!rect.IsEmpty && rect.Width > 5 && rect.Height > 5)
                        {
                            _selectionRects.Add(rect);
                        }
                    }

                    _isDragging = false;
                    break;
            }

            // 再描画
            _canvasView?.InvalidateSurface();
        }

        /// <summary>
        /// 削除コンテキストメニューの表示
        /// </summary>
        private async void ShowContextMenu(SKPoint point, SKRect rect)
        {
            var action = await Application.Current.MainPage.DisplayActionSheet("削除しますか？", "キャンセル", "削除");

            if (action == "削除")
            {
                _selectionRects.Remove(rect);
                _canvasView?.InvalidateSurface();
            }
        }



        /// <summary>
        /// リッチテキスト貼り付け機能を有効化
        /// </summary>
        private void EnableRichTextPaste()
        {
            try
            {
                // 各エディターにリッチテキスト貼り付け機能を追加
                if (_frontTextEditor != null)
                {
                    SetupPasteMonitoring(_frontTextEditor);
                }
                
                if (_backTextEditor != null)
                {
                    SetupPasteMonitoring(_backTextEditor);
                }
                
                if (_choiceQuestion != null)
                {
                    SetupPasteMonitoring(_choiceQuestion);
                }
                
                if (_choiceQuestionExplanation != null)
                {
                    SetupPasteMonitoring(_choiceQuestionExplanation);
                }
                
                Debug.WriteLine("リッチテキスト貼り付け機能を有効化しました");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"リッチテキスト貼り付け有効化エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// エディターテキスト変更イベント
        /// </summary>
        private void OnEditorTextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                var editor = sender as Editor;
                if (editor == null) return;
                
                // リッチテキスト処理は貼り付け時のみ実行
                // 通常のテキスト入力時は処理しない
                Debug.WriteLine($"エディターテキスト変更: '{e.NewTextValue}'");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"エディターテキスト変更エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// エディタにリッチテキストペースト機能を追加
        /// </summary>
        private void AddRichTextPasteToEditor(Editor editor)
        {
            try
            {
                // ペースト監視を設定
                SetupPasteMonitoring(editor);
                
                Debug.WriteLine($"エディタ {editor.AutomationId} にリッチテキストペースト機能を追加");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"リッチテキストペースト機能追加エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 手動でリッチテキストペーストを実行（ボタンやメニューから呼び出し用）
        /// </summary>
        public async Task PasteRichTextToCurrentEditor()
        {
            var editor = GetCurrentEditor();
            if (editor != null)
            {
                await HandleRichTextPasteAsync(editor);
            }
        }

        /// <summary>
        /// 貼り付け監視を設定
        /// </summary>
        private void SetupPasteMonitoring(Editor editor)
        {
            try
            {
                Debug.WriteLine($"=== SetupPasteMonitoring開始 ===");
                Debug.WriteLine($"エディタ: {editor?.AutomationId ?? "null"}");
                Debug.WriteLine($"エディタタイプ: {editor?.GetType().Name ?? "null"}");
                Debug.WriteLine($"現在のプラットフォーム: {DeviceInfo.Platform}");
                
                if (editor == null)
                {
                    Debug.WriteLine("エディタがnullのため、ペースト監視を設定できません");
                    return;
                }
                
                // Windowsプラットフォームでのみキーボードイベントを設定
#if WINDOWS
                Debug.WriteLine("Windowsプラットフォーム: HandlerChangedイベントを設定");
                
                // 現在のHandlerをチェック
                Debug.WriteLine($"現在のHandler: {editor.Handler?.GetType().Name ?? "null"}");
                Debug.WriteLine($"現在のPlatformView: {editor.Handler?.PlatformView?.GetType().Name ?? "null"}");
                
                // 既にHandlerが存在する場合は即座に設定
                if (editor.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.TextBox currentTextBox)
                {
                    Debug.WriteLine("既存のTextBoxに直接イベントを設定");
                    SetupKeyEvents(currentTextBox, editor);
                }
                
                // HandlerChangedイベントを設定（将来のHandler変更に対応）
                editor.HandlerChanged += (s, e) =>
                {
                    Debug.WriteLine($"=== HandlerChangedイベント発火 ===");
                    Debug.WriteLine($"エディタ: {editor.AutomationId}");
                    Debug.WriteLine($"新しいHandler: {editor.Handler?.GetType().Name ?? "null"}");
                    Debug.WriteLine($"新しいPlatformView: {editor.Handler?.PlatformView?.GetType().Name ?? "null"}");
                    
                    if (editor.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.TextBox textBox)
                    {
                        Debug.WriteLine($"TextBox取得成功: エディタ {editor.AutomationId}");
                        SetupKeyEvents(textBox, editor);
                    }
                    else
                    {
                        Debug.WriteLine($"TextBox取得失敗: エディタ {editor.AutomationId}");
                        Debug.WriteLine($"Handler: {editor.Handler}");
                        Debug.WriteLine($"PlatformView: {editor.Handler?.PlatformView}");
                        Debug.WriteLine($"PlatformViewタイプ: {editor.Handler?.PlatformView?.GetType().FullName}");
                    }
                };
#else
                Debug.WriteLine("Windowsプラットフォーム以外のため、ペースト監視を設定しません");
#endif
                Debug.WriteLine($"エディタ {editor.AutomationId} にペースト監視を設定完了");
                Debug.WriteLine($"=== SetupPasteMonitoring終了 ===");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ペースト監視設定エラー: {ex.Message}");
                Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// TextBoxにキーイベントを設定
        /// </summary>
        private void SetupKeyEvents(Microsoft.UI.Xaml.Controls.TextBox textBox, Editor editor)
        {
            try
            {
                Debug.WriteLine($"=== SetupKeyEvents開始 ===");
                Debug.WriteLine($"エディタ: {editor.AutomationId}");
                Debug.WriteLine($"TextBox: {textBox.GetType().Name}");
                
                // キーボードイベントを監視
                textBox.KeyDown += (sender, args) =>
                {
                    Debug.WriteLine($"=== KeyDownイベント ===");
                    Debug.WriteLine($"Key: {args.Key}");
                    Debug.WriteLine($"IsMenuKeyDown: {args.KeyStatus.IsMenuKeyDown}");
                    Debug.WriteLine($"現在の_isCtrlDown: {_isCtrlDown}");
                    
                    if (args.Key == VirtualKey.Control)
                    {
                        _isCtrlDown = true;
                        Debug.WriteLine($"Ctrlキー押下: _isCtrlDown={_isCtrlDown}");
                    }
                    // Ctrl+Vが押された場合のみペースト処理を実行
                    if (args.Key == VirtualKey.V && _isCtrlDown)
                    {
                        Debug.WriteLine($"=== Ctrl+V検出 ===");
                        Debug.WriteLine($"エディタ: {editor.AutomationId}");
                        Debug.WriteLine($"ペースト処理を開始します");
                        args.Handled = true; // デフォルトのペーストをキャンセル
                        _ = HandleRichTextPasteAsync(editor);
                    }
                };
                
                textBox.KeyUp += (sender, args) =>
                {
                    Debug.WriteLine($"=== KeyUpイベント ===");
                    Debug.WriteLine($"Key: {args.Key}");
                    if (args.Key == VirtualKey.Control)
                    {
                        _isCtrlDown = false;
                        Debug.WriteLine($"Ctrlキー離上: _isCtrlDown={_isCtrlDown}");
                    }
                };
                
                Debug.WriteLine($"キーイベント設定完了: エディタ {editor.AutomationId}");
                Debug.WriteLine($"=== SetupKeyEvents終了 ===");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"キーイベント設定エラー: {ex.Message}");
                Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// リッチテキスト貼り付け処理（非同期版）
        /// </summary>
        private async Task HandleRichTextPasteAsync(Editor editor)
        {
            try
            {
                Debug.WriteLine($"=== HandleRichTextPasteAsync開始 ===");
                Debug.WriteLine($"エディタ: {editor.AutomationId}");
                Debug.WriteLine($"エディタの種類: {editor.GetType().Name}");
                Debug.WriteLine($"統合ペースト処理開始");
                
                // リッチテキストをMarkdownに変換
                var markdownText = await RichTextParser.GetRichTextAsMarkdownAsync();
                
                Debug.WriteLine($"RichTextParser結果: {(string.IsNullOrEmpty(markdownText) ? "空" : markdownText.Substring(0, Math.Min(100, markdownText.Length)))}");
                
                if (!string.IsNullOrEmpty(markdownText))
                {
                    Debug.WriteLine($"リッチテキストを取得: {markdownText.Substring(0, Math.Min(100, markdownText.Length))}...");
                    
                    // 画像タグの処理
                    var imageMatches = Regex.Matches(markdownText, @"<<img_\d{8}_\d{6}\.jpg>>");
                    foreach (Match match in imageMatches)
                    {
                        string imgFileName = match.Value.Trim('<', '>');
                        string imgPath = Path.Combine(_tempExtractPath, "img", imgFileName);
                        
                        if (File.Exists(imgPath))
                        {
                            // RichTextLabelでは画像タグをそのまま保持
                            // markdownText = markdownText.Replace(match.Value, $"[画像: {imgFileName}]");
                        }
                    }
                    
                    // プレーンテキストに装飾を追加
                    markdownText = ProcessRichTextFormatting(markdownText);
                    
                    // 現在のテキストとカーソル位置を取得
                    var currentText = editor.Text ?? "";
                    var cursorPosition = editor.CursorPosition;
                    
                    Debug.WriteLine($"現在のテキスト: '{currentText}'");
                    Debug.WriteLine($"カーソル位置: {cursorPosition}");
                    Debug.WriteLine($"挿入するテキスト: '{markdownText}'");
                    
                    // カーソル位置にテキストを挿入
                    if (cursorPosition >= 0 && cursorPosition <= currentText.Length)
                    {
                        string newText = currentText.Insert(cursorPosition, markdownText);
                        editor.Text = newText;
                        editor.CursorPosition = cursorPosition + markdownText.Length;
                        
                        Debug.WriteLine($"テキスト挿入完了: '{editor.Text}'");
                        Debug.WriteLine($"新しいカーソル位置: {editor.CursorPosition}");
                    }
                    else
                    {
                        // カーソル位置が不正な場合は末尾に追加
                        editor.Text = currentText + markdownText;
                        editor.CursorPosition = editor.Text.Length;
                        
                        Debug.WriteLine($"テキスト末尾追加完了: '{editor.Text}'");
                    }
                    
                    // プレビューを更新
                    UpdatePreviewForEditor(editor);
                    
                    // 選択肢問題エディターの場合、ペースト完了後に自動分離を実行
                    if (editor == _choiceQuestion || editor.AutomationId == "ChoiceQuestionEditor")
                    {
                        Debug.WriteLine("選択肢問題エディターのため、自動分離を実行します");
                        Debug.WriteLine($"エディタ比較: editor == _choiceQuestion = {editor == _choiceQuestion}");
                        Debug.WriteLine($"AutomationId比較: editor.AutomationId = '{editor.AutomationId}'");
                        await Task.Delay(100); // 少し遅延させてから実行
                        TryAutoSeparateQuestionAndChoices();
                    }
                    else
                    {
                        Debug.WriteLine($"選択肢問題エディターではありません: AutomationId = '{editor.AutomationId}'");
                    }
                    
                    Debug.WriteLine($"統合ペースト処理完了: {markdownText}");
                }
                else
                {
                    Debug.WriteLine("リッチテキストが取得できませんでした");
                }
                
                Debug.WriteLine($"=== HandleRichTextPasteAsync終了 ===");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"統合ペースト処理エラー: {ex.Message}");
                Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                
                // エラー時は通常のペーストにフォールバック
                try
                {
                    if (Clipboard.HasText)
                    {
                        var plainText = await Clipboard.GetTextAsync();
                        
                        Debug.WriteLine($"フォールバック: プレーンテキスト '{plainText}'");
                        
                        // フォールバック時もカーソル位置に挿入
                        var currentText = editor.Text ?? "";
                        var cursorPosition = editor.CursorPosition;
                        
                        if (cursorPosition >= 0 && cursorPosition <= currentText.Length)
                        {
                            string newText = currentText.Insert(cursorPosition, plainText);
                            editor.Text = newText;
                            editor.CursorPosition = cursorPosition + plainText.Length;
                        }
                        else
                        {
                            // カーソル位置が不正な場合は末尾に追加
                            editor.Text = currentText + plainText;
                            editor.CursorPosition = editor.Text.Length;
                        }
                        
                        UpdatePreviewForEditor(editor);
                        
                        // 選択肢問題エディターの場合、通常のペーストでも自動分離を試行
                        if (editor == _choiceQuestion)
                        {
                            await Task.Delay(100);
                            TryAutoSeparateQuestionAndChoices();
                        }
                    }
                }
                catch (Exception fallbackEx)
                {
                    Debug.WriteLine($"フォールバックペースト処理エラー: {fallbackEx.Message}");
                }
            }
        }

        /// <summary>
        /// リッチテキストフォーマット処理
        /// </summary>
        private string ProcessRichTextFormatting(string text)
        {
            try
            {
                Debug.WriteLine($"=== ProcessRichTextFormatting開始 ===");
                Debug.WriteLine($"入力テキスト: '{text.Substring(0, Math.Min(50, text.Length))}...'");
                
                // 太字変換（**text** → **text**）
                text = Regex.Replace(text, @"\*\*(.*?)\*\*", "**$1**");
                Debug.WriteLine($"太字変換後: '{text.Substring(0, Math.Min(50, text.Length))}...'");
                
                // 色変換（{{color|text}} → {{color|text}}）
                text = Regex.Replace(text, @"\{\{red\|(.*?)\}\}", "{{red|$1}}");
                text = Regex.Replace(text, @"\{\{blue\|(.*?)\}\}", "{{blue|$1}}");
                text = Regex.Replace(text, @"\{\{green\|(.*?)\}\}", "{{green|$1}}");
                text = Regex.Replace(text, @"\{\{yellow\|(.*?)\}\}", "{{yellow|$1}}");
                text = Regex.Replace(text, @"\{\{purple\|(.*?)\}\}", "{{purple|$1}}");
                text = Regex.Replace(text, @"\{\{orange\|(.*?)\}\}", "{{orange|$1}}");
                Debug.WriteLine($"色変換後: '{text.Substring(0, Math.Min(50, text.Length))}...'");
                
                // 上付き・下付き変換（^^text^^ → ^^text^^, ~~text~~ → ~~text~~）
                text = Regex.Replace(text, @"\^\^(.*?)\^\^", "^^$1^^");
                text = Regex.Replace(text, @"~~(.*?)~~", "~~$1~~");
                Debug.WriteLine($"上付き・下付き変換後: '{text.Substring(0, Math.Min(50, text.Length))}...'");
                
                // プレーンテキストに装飾を追加（太字、色、上付き、下付き）
                // 太字の追加
                var beforeBold = text;
                text = Regex.Replace(text, @"\b(重要|注意|ポイント|キーワード)\b", "**$1**");
                if (beforeBold != text)
                {
                    Debug.WriteLine($"太字追加: '{beforeBold}' → '{text}'");
                }
                
                // 色の追加（特定のキーワードに色を付ける）
                var beforeColor = text;
                text = Regex.Replace(text, @"\b(正解|正しい|○|✓)\b", "{{green|$1}}");
                text = Regex.Replace(text, @"\b(不正解|間違い|×|✗)\b", "{{red|$1}}");
                text = Regex.Replace(text, @"\b(警告|危険|注意)\b", "{{orange|$1}}");
                if (beforeColor != text)
                {
                    Debug.WriteLine($"色追加: '{beforeColor}' → '{text}'");
                }
                
                // 上付き・下付きの追加（数字や記号）
                var beforeSubscript = text;
                text = Regex.Replace(text, @"(\d+)\^(\d+)", "$1^^$2^^");
                text = Regex.Replace(text, @"(\w+)_(\d+)", "$1~~$2~~");
                if (beforeSubscript != text)
                {
                    Debug.WriteLine($"上付き・下付き追加: '{beforeSubscript}' → '{text}'");
                }
                
                Debug.WriteLine($"最終結果: '{text.Substring(0, Math.Min(50, text.Length))}...'");
                Debug.WriteLine($"=== ProcessRichTextFormatting終了 ===");
                
                return text;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"リッチテキストフォーマット処理エラー: {ex.Message}");
                Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                return text;
            }
        }

        /// <summary>
        /// 問題と選択肢の自動分離を試行
        /// </summary>
        private void TryAutoSeparateQuestionAndChoices()
        {
            try
            {
                Debug.WriteLine($"=== TryAutoSeparateQuestionAndChoices開始 ===");
                
                var questionText = _choiceQuestion?.Text ?? "";
                if (string.IsNullOrWhiteSpace(questionText)) 
                {
                    Debug.WriteLine("問題テキストが空のため、自動分離を中止します");
                    return;
                }
                
                Debug.WriteLine($"問題テキスト: '{questionText.Substring(0, Math.Min(50, questionText.Length))}...'");
                
                // 改行で分割
                var lines = questionText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Trim())
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .ToList();
                
                Debug.WriteLine($"分割された行数: {lines.Count}");
                foreach (var line in lines)
                {
                    Debug.WriteLine($"行: '{line}'");
                }
                
                if (lines.Count < 2) 
                {
                    Debug.WriteLine("自動分離: 行数が不足（2行未満）");
                    return;
                }
                
                // 選択肢のパターンをチェック（数字+ドット、アルファベット+ドット、括弧付き数字など）
                var choicePatterns = new[]
                {
                    @"^\d+\.", // 1. 2. 3.
                    @"^[a-zA-Z]\.", // A. B. C.
                    @"^\(\d+\)", // (1) (2) (3)
                    @"^[①②③④⑤⑥⑦⑧⑨⑩]", // 丸数字
                    @"^[⑴⑵⑶⑷⑸⑹⑺⑻⑼⑽]" // 括弧付き丸数字
                };
                
                var questionLines = new List<string>();
                var choiceLines = new List<string>();
                
                foreach (var line in lines)
                {
                    bool isChoice = choicePatterns.Any(pattern => Regex.IsMatch(line, pattern));
                    Debug.WriteLine($"行 '{line}' は選択肢: {isChoice}");
                    
                    if (isChoice)
                    {
                        choiceLines.Add(line);
                    }
                    else
                    {
                        questionLines.Add(line);
                    }
                }
                
                Debug.WriteLine($"問題行数: {questionLines.Count}, 選択肢行数: {choiceLines.Count}");
                
                // 問題と選択肢を分離
                var question = string.Join("\n", questionLines);
                var choices = choiceLines;
                
                if (string.IsNullOrWhiteSpace(question) || choices.Count == 0)
                {
                    Debug.WriteLine("自動分離: 問題または選択肢が見つかりません");
                    return;
                }
                
                Debug.WriteLine($"分離された問題: '{question}'");
                Debug.WriteLine($"分離された選択肢数: {choices.Count}");
                
                // 問題テキストを更新
                if (_choiceQuestion != null)
                {
                    _choiceQuestion.Text = question;
                    Debug.WriteLine("問題テキストを更新しました");
                }
                
                // 既存の選択肢をクリア
                if (_choicesContainer != null)
                {
                    _choicesContainer.Children.Clear();
                    Debug.WriteLine("既存の選択肢をクリアしました");
                }
                
                // 選択肢を追加
                foreach (var choice in choices)
                {
                    // 選択肢の先頭の数字や記号を削除
                    var cleanChoice = RemoveChoiceNumbers(choice);
                    Debug.WriteLine($"選択肢追加: '{choice}' → '{cleanChoice}'");
                    AddChoiceItem(cleanChoice, false);
                }
                
                Debug.WriteLine($"自動分離完了: 問題='{question}', 選択肢数={choices.Count}");
                Debug.WriteLine($"=== TryAutoSeparateQuestionAndChoices終了 ===");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"問題と選択肢の自動分離エラー: {ex.Message}");
                Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// ページ画像を追加（基本・選択肢カード用）
        /// </summary>
        public async Task OnAddPageImage(Editor editor)
        {
            try
            {
                Debug.WriteLine("=== OnAddPageImage開始 ===");
                
                if (_selectPageForImageCallback != null)
                {
                    // ページ選択機能を活用してページ画像を追加
                    Debug.WriteLine("ページ選択機能を使ってページ画像を追加");
                    await _selectPageForImageCallback(editor, 0); // 現在のページから開始
                    
                    if (_showToastCallback != null)
                    {
                        await _showToastCallback("ページ選択モード開始: スクロールしてページを選択し、「選択」ボタンをクリックしてください");
                    }
                }
                else if (_addPageImageCallback != null)
                {
                    // フォールバック：直接画像追加
                    Debug.WriteLine("フォールバック: 直接ページ画像を追加");
                    await _addPageImageCallback(editor);
                }
                else
                {
                    Debug.WriteLine("ページ画像追加コールバックが設定されていません");
                }
                
                Debug.WriteLine("=== OnAddPageImage完了 ===");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ページ画像追加エラー: {ex.Message}");
                Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                
                // エラー時もアラートではなくトーストメッセージのみ
                if (_showToastCallback != null)
                {
                    await _showToastCallback("ページ画像の追加中にエラーが発生しました");
                }
            }
        }


    }
} 