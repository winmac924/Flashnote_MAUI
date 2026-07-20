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
using System.Text.Json.Nodes;

namespace Flashnote.Services
{
    public partial class CardManager
    {
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
                BackgroundColor = Colors.LightGray,
                EnableTouchEvents = true
            };
            _canvasView.PaintSurface += OnCanvasViewPaintSurface;
            _canvasView.Touch += OnCanvasTouch;
            
            _imageFillLayout.Children.Add(imageFillLabel);
            _imageFillLayout.Children.Add(imageSelectLayout);
            _imageFillLayout.Children.Add(_canvasView);
        }

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
        /// 削除コンテキストメニューの表示
        /// </summary>
        private async void ShowContextMenu(SKPoint point, SKRect rect)
        {
            var action = await Application.Current.MainPage.DisplayActionSheet("削除しますか？", "キャンセル", "削除");

            if (action == "削除")
            {
                // ポイントから枠のインデックスを取得
                var canvasSize = _canvasView.CanvasSize;
                var rectIndex = FindRectAtPoint(point, canvasSize.Width, canvasSize.Height);

                if (rectIndex >= 0)
                {
                    var rectToRemove = _selectionRects[rectIndex];
                    _selectionRects.RemoveAt(rectIndex);

                    // 選択中の枠が削除された場合は選択をクリア
                    if (rectIndex == _selectedRectIndex)
                    {
                        _selectedRectIndex = -1;
                    }
                    else if (rectIndex < _selectedRectIndex)
                    {
                        // 選択中の枠より前の枠が削除された場合はインデックスを調整
                        _selectedRectIndex--;
                    }

                    _canvasView?.InvalidateSurface();
                    Debug.WriteLine($"選択範囲削除: インデックス={rectIndex}, 枠={rectToRemove}");
                }
            }
        }
    }
}
