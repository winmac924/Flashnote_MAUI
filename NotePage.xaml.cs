using Flashnote.Services.Sync;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using Flashnote.Views;
using Flashnote.Drawing;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Diagnostics;
using Flashnote.Models;  // SentenceDataの名前空間を追加
using SQLite;
using Flashnote.Services;  // CardManagerの名前空間を追加
using Flashnote_MAUI.Services;

namespace Flashnote
{
    public partial class NotePage : ContentPage, IDisposable
    {
        private BackgroundCanvas _backgroundCanvas;
        private DrawingLayer _drawingLayer;
        private TextSelectionLayer _textSelectionLayer;
        private readonly string _noteName;
        private string tempExtractPath; // 一時展開パス
        private string ankplsFilePath;  // .ankplsファイルのパス
        
        // カード追加機能用
                private bool _isAddCardVisible = false;
        private CardManager _cardManager;
        private Label _toastLabel; // トースト表示用ラベル
        
        // ページ選択モード用
        private bool _isPageSelectionMode = false;
        private Frame _pageSelectionOverlay;
        private Label _pageSelectionLabel;
        private Button _pageConfirmButton;
        private Button _pageCancelButton;
        private int _selectedPageIndex = -1;
        
        // PDF.jsテキスト選択機能用
        private WebView _pdfTextSelectionWebView;
        private bool _isTextSelectionMode = false;
        private string _selectedText = "";
        private Button _textSelectionButton;
        private Grid _canvasGrid; // レイヤー管理用のGrid

        // ページ選択用画像追加機能
        private bool _isPageSelectionForImageMode = false;
        private Editor _currentEditorForPageImage = null;

        public NotePage(string noteName, string tempPath)
        {
            _noteName = Path.GetFileNameWithoutExtension(noteName);
            InitializeComponent();

            // ドキュメントパス設定
            ankplsFilePath = noteName;

            // 一時ディレクトリのパスを設定
            string relativePath = Path.GetRelativePath(SyncPathResolver.GetLocalNoteRoot(), Path.GetDirectoryName(ankplsFilePath));
            tempExtractPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Flashnote",
                relativePath,
                $"{_noteName}_temp"
            );

            Debug.WriteLine($"Temporary path: {tempExtractPath}");
            
            // レイヤーを初期化
            InitializeLayers();

        }

        private void InitializeLayers()
        {
            // バックグラウンドキャンバス（PDF/画像表示用）
            _backgroundCanvas = new BackgroundCanvas();
            _backgroundCanvas.ParentScrollView = MainScrollView;
            _backgroundCanvas.InitializeCacheDirectory(_noteName, tempExtractPath);

            // 描画レイヤーを再び有効化
            _drawingLayer = new DrawingLayer();

            // テキスト選択レイヤーを初期化
            _textSelectionLayer = new TextSelectionLayer();
            _textSelectionLayer.SetBackgroundCanvas(_backgroundCanvas);
            _textSelectionLayer.SetParentScrollView(MainScrollView);
            _textSelectionLayer.TextSelected += OnTextSelected;

            // GridでBackgroundCanvas、DrawingLayer、TextSelectionLayerを重ね合わせる
            _canvasGrid = new Grid();
            
            // BackgroundCanvasを追加（最下層）
            _backgroundCanvas.SetValue(Grid.RowProperty, 0);
            _backgroundCanvas.SetValue(Grid.ColumnProperty, 0);
            _canvasGrid.Children.Add(_backgroundCanvas);
            
            // DrawingLayerを追加（中間層）
            _drawingLayer.SetValue(Grid.RowProperty, 0);
            _drawingLayer.SetValue(Grid.ColumnProperty, 0);
            _drawingLayer.HorizontalOptions = LayoutOptions.Fill;
            _drawingLayer.VerticalOptions = LayoutOptions.Fill;
            _canvasGrid.Children.Add(_drawingLayer);

            // TextSelectionLayerを追加（最上層）
            _textSelectionLayer.SetValue(Grid.RowProperty, 0);
            _textSelectionLayer.SetValue(Grid.ColumnProperty, 0);
            _textSelectionLayer.HorizontalOptions = LayoutOptions.Fill;
            _textSelectionLayer.VerticalOptions = LayoutOptions.Fill;
            _canvasGrid.Children.Add(_textSelectionLayer);

            // 初期状態ではDrawingLayerを最前面に配置（テキスト選択モードでない）
            UpdateLayerOrder();

            // GridをPageContainerに追加
            PageContainer.Children.Clear();
            PageContainer.Children.Add(_canvasGrid);
            
            Debug.WriteLine($"BackgroundCanvasとDrawingLayerを重ね合わせて初期化");
            Debug.WriteLine($"BackgroundCanvas初期化状態: HasContent={_backgroundCanvas.HasContent}, PageCount={_backgroundCanvas.PageCount}");
            Debug.WriteLine($"DrawingLayer初期サイズ: {_drawingLayer.WidthRequest}x{_drawingLayer.HeightRequest}");
            
            // スクロールイベントハンドラーを追加
            MainScrollView.Scrolled += OnMainScrollViewScrolled;
            
            // キャッシュディレクトリの初期化と保存データの復元
            InitializeCacheDirectory();
        }

        // レイヤーの順序を更新するメソッド
        private void UpdateLayerOrder()
        {
            if (_canvasGrid == null || _drawingLayer == null || _textSelectionLayer == null)
                return;

            // 現在の子要素をクリア
            _canvasGrid.Children.Clear();

            // BackgroundCanvasを最下層に追加
            _backgroundCanvas.SetValue(Grid.RowProperty, 0);
            _backgroundCanvas.SetValue(Grid.ColumnProperty, 0);
            _canvasGrid.Children.Add(_backgroundCanvas);

            if (_isTextSelectionMode)
            {
                // テキスト選択モード: DrawingLayer → TextSelectionLayer の順
                _drawingLayer.SetValue(Grid.RowProperty, 0);
                _drawingLayer.SetValue(Grid.ColumnProperty, 0);
                _canvasGrid.Children.Add(_drawingLayer);

                _textSelectionLayer.SetValue(Grid.RowProperty, 0);
                _textSelectionLayer.SetValue(Grid.ColumnProperty, 0);
                _canvasGrid.Children.Add(_textSelectionLayer);
                
                Debug.WriteLine("レイヤー順序: BackgroundCanvas → DrawingLayer → TextSelectionLayer（最前面）");
            }
            else
            {
                // 描画モード: TextSelectionLayer → DrawingLayer の順
                _textSelectionLayer.SetValue(Grid.RowProperty, 0);
                _textSelectionLayer.SetValue(Grid.ColumnProperty, 0);
                _canvasGrid.Children.Add(_textSelectionLayer);

                _drawingLayer.SetValue(Grid.RowProperty, 0);
                _drawingLayer.SetValue(Grid.ColumnProperty, 0);
                _canvasGrid.Children.Add(_drawingLayer);
                
                Debug.WriteLine("レイヤー順序: BackgroundCanvas → TextSelectionLayer → DrawingLayer（最前面）");
            }
        }

        private async void InitializeCacheDirectory()
        {
            if (!Directory.Exists(tempExtractPath))
            {
                Directory.CreateDirectory(tempExtractPath);
                Directory.CreateDirectory(Path.Combine(tempExtractPath, "PageCache"));
                Debug.WriteLine($"一時ディレクトリを作成: {tempExtractPath}");
            }
            else
            {
                Debug.WriteLine($"既存の一時ディレクトリを使用: {tempExtractPath}");
                
                // ディレクトリ内のファイルをリスト表示
                var files = Directory.GetFiles(tempExtractPath);
                Debug.WriteLine($"一時ディレクトリ内のファイル: {string.Join(", ", files.Select(Path.GetFileName))}");
            }
            
            // 保存されたコンテンツデータを確認
            var contentDataPath = Path.Combine(tempExtractPath, "content_data.json");
            bool backgroundLoaded = false;
            
            if (File.Exists(contentDataPath))
                {
                    try
                    {
                    var json = await File.ReadAllTextAsync(contentDataPath);
                    Debug.WriteLine($"content_data.jsonの内容: {json}");
                    var contentData = System.Text.Json.JsonSerializer.Deserialize<BackgroundCanvas.ContentData>(json);
                    
                    if (contentData != null)
                    {
                        Debug.WriteLine($"保存されたコンテンツデータを発見: PDF={contentData.PdfFilePath}, Image={contentData.ImageFilePath}");
                        
                        // PDFまたは画像ファイルが存在する場合は読み込み
                        if (!string.IsNullOrEmpty(contentData.PdfFilePath) && File.Exists(contentData.PdfFilePath))
                        {
                            Debug.WriteLine($"PDFファイルを自動読み込み: {contentData.PdfFilePath}");
                            await LoadPdfAsync(contentData.PdfFilePath);
                            backgroundLoaded = true;
                            
                            // PDF自動読み込み後、テキスト選択モードを有効化
                            if (_textSelectionLayer != null)
                            {
                                Debug.WriteLine("🚀 PDF自動読み込み完了 - テキスト選択モード有効化");
                                await Task.Delay(500); // レイヤー初期化を待つ
                                _textSelectionLayer.EnableTextSelection();
                                _isTextSelectionMode = true;
                                UpdateLayerOrder(); // レイヤー順序を更新
                            }
                        }
                        else if (!string.IsNullOrEmpty(contentData.ImageFilePath) && File.Exists(contentData.ImageFilePath))
                        {
                            Debug.WriteLine($"画像ファイルを自動読み込み: {contentData.ImageFilePath}");
                            await LoadImageAsync(contentData.ImageFilePath);
                            backgroundLoaded = true;
                        }
                        else
                        {
                            Debug.WriteLine("有効なPDF/画像ファイルが見つかりません");
                        }
                    }
                    else
                    {
                        Debug.WriteLine("contentDataのデシリアライズに失敗");
                    }
                }
                catch (Exception ex)
                    {
                    Debug.WriteLine($"コンテンツデータの読み込みエラー: {ex.Message}");
                }
            }
            else
            {
                Debug.WriteLine("content_data.jsonが存在しません - DrawingLayerのみ初期化します");
            }
            
            // 背景が読み込まれていない場合は、DrawingLayerを手動で初期化
            if (!backgroundLoaded && _drawingLayer != null)
            {
                Debug.WriteLine("背景なしでDrawingLayerを初期化");
                // BackgroundCanvas の BASE_CANVAS_WIDTH と同じ値を基準にする
                const float defaultBaseWidth = 600f; 
                
                await _drawingLayer.InitializeAsync(_noteName, tempExtractPath);
                Debug.WriteLine($"DrawingLayer初期化完了: サイズ {_drawingLayer.WidthRequest}x{_drawingLayer.HeightRequest}");
            }
        }

        private void OnPenToolClicked(object sender, EventArgs e)
        {
            _drawingLayer?.SetTool(Flashnote.Drawing.DrawingTool.Pen);
        }

        private void OnMarkerToolClicked(object sender, EventArgs e)
        {
            _drawingLayer?.SetTool(Flashnote.Drawing.DrawingTool.Marker);
        }

        private void OnEraserToolClicked(object sender, EventArgs e)
        {
            _drawingLayer?.SetTool(Flashnote.Drawing.DrawingTool.Eraser);
        }

        private void OnRulerClicked(object sender, EventArgs e)
        {
            // TODO: 定規機能の実装
            Debug.WriteLine("定規ツールクリック（未実装）");
        }

        private void OnUndoClicked(object sender, EventArgs e)
        {
            _drawingLayer?.Undo();
            Debug.WriteLine("元に戻す実行");
        }

        private void OnRedoClicked(object sender, EventArgs e)
        {
            _drawingLayer?.Redo();
            Debug.WriteLine("やり直し実行");
        }

        private void OnClearClicked(object sender, EventArgs e)
        {
            _drawingLayer?.Clear();
            Debug.WriteLine("描画クリア実行");
        }

        private async void OnTextSelectionClicked(object sender, EventArgs e)
        {
            try
            {
                Debug.WriteLine("テキスト選択ボタンがクリックされました");
                
                if (_textSelectionLayer != null)
                {
                    if (_isTextSelectionMode)
                    {
                        // テキスト選択モードを無効化
                        _textSelectionLayer.DisableTextSelection();
                        _isTextSelectionMode = false;
                        UpdateLayerOrder(); // レイヤー順序を更新（DrawingLayerを最前面に）
                        await ShowToast("テキスト選択モードを無効にしました - 描画モード");
                    }
                    else
                    {
                        // テキスト選択モードを有効化
                        _textSelectionLayer.EnableTextSelection();
                        _isTextSelectionMode = true;
                        UpdateLayerOrder(); // レイヤー順序を更新（TextSelectionLayerを最前面に）
                        await ShowToast("テキスト選択モードを有効にしました");
                    }
                }
                else
                {
                    await ShowToast("テキスト選択レイヤーが初期化されていません");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"テキスト選択ボタンエラー: {ex.Message}");
                await UIThreadHelper.ShowAlertAsync("エラー", "テキスト選択機能でエラーが発生しました", "OK");
            }
        }

        private async void OnTextSelected(object sender, TextSelectedEventArgs e)
        {
            try
            {
                Debug.WriteLine($"テキストが選択されました: '{e.SelectedText}'");
                _selectedText = e.SelectedText;
                
                // 選択されたテキストをトーストで表示
                await ShowToast($"選択: {e.SelectedText.Substring(0, Math.Min(30, e.SelectedText.Length))}...");
                
                // 必要に応じて、選択されたテキストを他の機能で使用
                // 例：カード作成時に自動入力など
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"テキスト選択イベントエラー: {ex.Message}");
            }
        }


        private async void OnAddCardClicked(object sender, EventArgs e)
        {
            try
            {
                Debug.WriteLine("AddCardClicked開始");
                Debug.WriteLine($"ankplsFilePath: {ankplsFilePath}");
                Debug.WriteLine($"tempExtractPath: {tempExtractPath}");

                if (_isAddCardVisible)
                {
                    // カード追加パネルを閉じる
                    await HideAddCardPanel();
                }
                else
                {
                    // カード追加パネルを表示
                    await ShowAddCardPanel();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OnAddCardClicked エラー: {ex.Message}");
                await UIThreadHelper.ShowAlertAsync("エラー", "カード追加パネルの表示中にエラーが発生しました", "OK");
            }
        }

        private void OnZoomSliderValueChanged(object sender, ValueChangedEventArgs e)
        {
            var scale = (float)e.NewValue;
            
            // BackgroundCanvasの拡大倍率を先に設定
            if (_backgroundCanvas != null)
            {
                _backgroundCanvas.CurrentScale = scale;
            }
            
            // DrawingLayerとBackgroundCanvasの座標系を同期
            if (_drawingLayer != null && _backgroundCanvas != null)
            {
                // BackgroundCanvasから座標系情報を取得して同期
                var totalHeight = GetBackgroundCanvasTotalHeight();
                _drawingLayer.SyncWithBackgroundCanvas(totalHeight, scale);
            }
            
            Debug.WriteLine($"ズーム倍率変更: {scale:F2} ({(int)(scale * 100)}%)");
        }

        private async Task LoadPdfAsync(string filePath)
        {
            try
            {
                if (_backgroundCanvas != null)
                {
                    await _backgroundCanvas.LoadPdfAsync(filePath);

                    // 描画レイヤーとBackgroundCanvasの座標系を同期
                    if (_drawingLayer != null)
                    {
                        // BackgroundCanvasから座標系情報を取得して同期
                        var totalHeight = GetBackgroundCanvasTotalHeight();
                        _drawingLayer.SyncWithBackgroundCanvas(totalHeight, _backgroundCanvas.CurrentScale);
                        
                        // 一時ディレクトリと描画データの初期化
                        await _drawingLayer.InitializeAsync(_noteName, tempExtractPath);
                    }

                    // コンテンツデータを保存
                    await SaveContentDataAsync(filePath, null);
                    
                    Debug.WriteLine($"PDF読み込み完了: {filePath}");
                    
                    // PDF読み込み完了後、自動的にテキスト選択モードを有効化
                    if (_textSelectionLayer != null)
                    {
                        Debug.WriteLine("🚀 PDF読み込み完了 - 自動テキスト選択モード有効化");
                        _textSelectionLayer.EnableTextSelection();
                        _isTextSelectionMode = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading PDF: {ex.Message}");
                await UIThreadHelper.ShowAlertAsync("エラー", $"PDFの読み込みに失敗しました: {ex.Message}", "OK");
            }
        }

        private async Task LoadImageAsync(string filePath)
        {
            try
            {
                if (_backgroundCanvas != null)
                {
                    await _backgroundCanvas.LoadImageAsync(filePath);

                    // 描画レイヤーのサイズを背景に合わせる
                    if (_drawingLayer != null)
                    {
                        _drawingLayer.CurrentScale = _backgroundCanvas.CurrentScale; // BackgroundCanvasのスケールに合わせる

                        // サイズを同期
                        _drawingLayer.WidthRequest = _backgroundCanvas.WidthRequest;
                        _drawingLayer.HeightRequest = _backgroundCanvas.HeightRequest;
                        
                        // 一時ディレクトリと描画データの初期化
                        await _drawingLayer.InitializeAsync(_noteName, tempExtractPath);
                    }

                    // コンテンツデータを保存
                    await SaveContentDataAsync(null, filePath);
                    
                    Debug.WriteLine($"画像読み込み完了: {filePath}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading image: {ex.Message}");
                await UIThreadHelper.ShowAlertAsync("エラー", $"画像の読み込みに失敗しました: {ex.Message}", "OK");
            }
        }

        /// <summary>
        /// コンテンツデータを保存する
        /// </summary>
        private async Task SaveContentDataAsync(string pdfPath, string imagePath)
        {
            try
            {
                var contentData = new BackgroundCanvas.ContentData
                {
                    PdfFilePath = pdfPath,
                    ImageFilePath = imagePath,
                    LastScrollY = 0
                };

                var jsonData = System.Text.Json.JsonSerializer.Serialize(contentData);
                var saveFilePath = Path.Combine(tempExtractPath, "content_data.json");
                
                // ディレクトリが存在しない場合は作成
                var directory = Path.GetDirectoryName(saveFilePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                await File.WriteAllTextAsync(saveFilePath, jsonData);
                Debug.WriteLine($"コンテンツデータを保存: {saveFilePath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"コンテンツデータの保存エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// BackgroundCanvasの総高さを取得（リフレクションを使用）
        /// </summary>
        private float GetBackgroundCanvasTotalHeight()
        {
            try
            {
                // BackgroundCanvasの_totalHeightフィールドにアクセス
                var field = typeof(BackgroundCanvas).GetField("_totalHeight", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (field != null)
                {
                    var totalHeight = (float)field.GetValue(_backgroundCanvas);
                    Debug.WriteLine($"BackgroundCanvas総高さ取得: {totalHeight}");
                    return totalHeight;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"総高さ取得エラー: {ex.Message}");
            }
            
            // フォールバック：デフォルト値
            return 600f * (4.0f / 3.0f); // 4:3のアスペクト比
        }

        private async void OnImportClicked(object sender, EventArgs e)
        {
            try
            {
                var result = await FilePicker.PickAsync();
                if (result != null)
                {
                    Debug.WriteLine($"ファイル選択: {result.FileName}");
                    
                    if (result.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    {
                        await LoadPdfAsync(result.FullPath);
                    }
                    else
                    {
                        await LoadImageAsync(result.FullPath);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error importing file: {ex.Message}");
                await UIThreadHelper.ShowAlertAsync("エラー", $"ファイルのインポートに失敗しました: {ex.Message}", "OK");
            }
        }

        private async void OnHelpClicked(object sender, EventArgs e)
        {
            try
            {
                await HelpOverlayControl.ShowHelp(HelpType.NotePage);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ヘルプ表示中にエラー: {ex.Message}");
                await UIThreadHelper.ShowAlertAsync("エラー", "ヘルプの表示に失敗しました", "OK");
            }
        }

        private async void OnSaveClicked(object sender, EventArgs e)
        {
            try
            {
                // 描画データを保存
                if (_drawingLayer != null)
                {
                    await _drawingLayer.SaveAsync();
                    Debug.WriteLine("描画データを手動保存");
                }
                
                // 現在のコンテンツデータも保存（何かファイルが読み込まれている場合）
                // 注意: この時点では具体的なファイルパスが分からないため、
                // 実際のファイルが読み込まれた時にSaveContentDataAsyncが呼ばれることを想定
                
                await UIThreadHelper.ShowAlertAsync("保存完了", "描画データを保存しました", "OK");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving file: {ex.Message}");
                await UIThreadHelper.ShowAlertAsync("エラー", $"保存に失敗しました: {ex.Message}", "OK");
            }
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            Debug.WriteLine("NotePage表示開始");
            // 初期化はInitializeCacheDirectoryで実行済み
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            Debug.WriteLine("NotePage非表示開始");
            
            // 自動保存を同期的に実行
            try
            {
                if (_drawingLayer != null)
                {
                    // 同期的に保存処理を実行
                    Task.Run(async () =>
                    {
                        try
                        {
                            await _drawingLayer.SaveAsync();
                            Debug.WriteLine("描画データを自動保存完了");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"描画データ保存エラー: {ex.Message}");
                        }
                    }).Wait(TimeSpan.FromSeconds(5)); // 最大5秒待機
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"自動保存エラー: {ex.Message}");
            }

            // リソース解放を確実に実行
            try
            {
                Debug.WriteLine("リソース解放開始");
                
                if (_drawingLayer != null)
                {
                    _drawingLayer.Dispose();
                    _drawingLayer = null;
                    Debug.WriteLine("DrawingLayerを解放");
                }
                
                // ガベージコレクションを促進
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                
                Debug.WriteLine("NotePage非表示完了");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"リソース解放エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 戻るボタンが押された時の処理
        /// </summary>
        protected override bool OnBackButtonPressed()
        {
            try
            {
                Debug.WriteLine("NotePageの戻るボタンが押されました");
                
                // カード追加パネルが表示されている場合
                if (_isAddCardVisible)
                {
                    Debug.WriteLine("カード追加パネルが表示されています");
                    
                    // 未保存の変更があるかチェック
                    if (_cardManager != null && _cardManager.HasUnsavedChanges())
                    {
                        Debug.WriteLine("カード追加パネルに未保存の変更があります。破棄確認ダイアログを表示します");
                        
                        // UIスレッドでダイアログを表示
                        MainThread.BeginInvokeOnMainThread(async () =>
                        {
                            try
                            {
                                var shouldDiscard = await _cardManager.ShowDiscardConfirmationDialog();
                                
                                if (shouldDiscard)
                                {
                                    Debug.WriteLine("破棄が選択されました。カード追加パネルを閉じます");
                                    _cardManager.ClearFields();
                                    await HideAddCardPanel();
                                }
                                else
                                {
                                    Debug.WriteLine("キャンセルが選択されました。パネルを開いたままにします");
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"破棄確認ダイアログエラー: {ex.Message}");
                            }
                        });
                        
                        return true; // デフォルトの戻る動作をキャンセル
                    }
                    else
                    {
                        Debug.WriteLine("未保存の変更はありません。カード追加パネルを閉じます");
                        MainThread.BeginInvokeOnMainThread(async () =>
                        {
                            await HideAddCardPanel();
                        });
                        return true; // デフォルトの戻る動作をキャンセル
                    }
                }
                else
                {
                    Debug.WriteLine("カード追加パネルは表示されていません。通常通り戻ります");
                    return base.OnBackButtonPressed();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"戻るボタン処理エラー: {ex.Message}");
                return base.OnBackButtonPressed();
            }
        }

        /// <summary>
        /// IDisposableの実装
        /// </summary>
        public void Dispose()
        {
            try
            {
                Debug.WriteLine("NotePage Dispose開始");
                
                // リソース解放
                if (_drawingLayer != null)
                {
                    _drawingLayer.Dispose();
                    _drawingLayer = null;
                }
                
                if (_backgroundCanvas != null)
                {
                    _backgroundCanvas = null;
                }
                
                if (_textSelectionLayer != null)
                {
                    _textSelectionLayer = null;
                }
                
                if (_cardManager != null)
                {
                    _cardManager = null;
                }
                
                // ガベージコレクションを促進
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                
                Debug.WriteLine("NotePage Dispose完了");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"NotePage Disposeエラー: {ex.Message}");
            }
        }

        /// <summary>
        /// カード追加パネルを表示
        /// </summary>
        private async Task ShowAddCardPanel()
        {
            try
            {
                // CardManagerを初期化（まだ初期化されていない場合）
                if (_cardManager == null)
                {
                    Debug.WriteLine("CardManager初期化開始");
                    _cardManager = new CardManager(ankplsFilePath, tempExtractPath);
                    
                    // ページ画像追加コールバックを設定（NotePage独自機能）
                    _cardManager.SetPageImageCallback(async (editor) => await AddCurrentPageAsImage(editor));
                    
                    // ページ選択コールバックを設定（NotePage独自機能）
                    _cardManager.SetPageSelectionCallbacks(
                        selectPageCallback: async (pageIndex) => await ShowPageSelectionOverlay(pageIndex),
                        loadCurrentImageCallback: async () => await LoadCurrentImageAsImageFill(),
                        showToastCallback: async (message) => await ShowToast(message),
                        showAlertCallback: async (title, message) => await UIThreadHelper.ShowAlertAsync(title, message, "OK")
                    );
                    
                    // ページ選択用画像追加コールバックを設定（新機能）
                    _cardManager.SetPageSelectionImageCallback(async (editor, pageIndex) => await ShowPageSelectionForImage(editor, pageIndex));
                    
                    Debug.WriteLine("CardManager初期化完了");
                }
                
                // CardManagerを使用してUIを初期化
                var addCardContainer = FindByName("AddCardContainer") as VerticalStackLayout;
                if (addCardContainer != null)
                {
                    Debug.WriteLine("CardManager UIを初期化");
                    _cardManager.InitializeCardUI(addCardContainer, includePageImageButtons: true);
                    Debug.WriteLine("CardUI初期化完了");
                }
                else
                {
                    Debug.WriteLine("AddCardContainer取得失敗");
                }
                
                // アニメーション：キャンバスを左に移動、カード追加パネルを表示
                var canvasColumn = FindByName("CanvasColumn") as ColumnDefinition;
                var addCardColumn = FindByName("AddCardColumn") as ColumnDefinition;
                var addCardScrollView = FindByName("AddCardScrollView") as ScrollView;
                
                if (canvasColumn != null && addCardColumn != null && addCardScrollView != null)
                {
                    // カード追加パネルを表示
                    addCardScrollView.IsVisible = true;
                    
                    // アニメーション：キャンバスを50%、カード追加を50%に
                    var animation = new Animation();
                    animation.Add(0, 1, new Animation(v => canvasColumn.Width = new GridLength(v, GridUnitType.Star), 1, 0.5));
                    animation.Add(0, 1, new Animation(v => addCardColumn.Width = new GridLength(v, GridUnitType.Star), 0, 0.5));
                    
                    animation.Commit(this, "ShowAddCard", 16, 300, Easing.CubicOut);
                    
                    _isAddCardVisible = true;
                    Debug.WriteLine("カード追加パネルを表示");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"カード追加パネル表示エラー: {ex.Message}");
                Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                await UIThreadHelper.ShowAlertAsync("エラー", "カード追加パネルの表示中にエラーが発生しました", "OK");
            }
        }
        
        /// <summary>
        /// カード追加パネルを非表示
        /// </summary>
        private async Task HideAddCardPanel()
        {
            try
            {
                var canvasColumn = FindByName("CanvasColumn") as ColumnDefinition;
                var addCardColumn = FindByName("AddCardColumn") as ColumnDefinition;
                var addCardScrollView = FindByName("AddCardScrollView") as ScrollView;
                
                if (canvasColumn != null && addCardColumn != null && addCardScrollView != null)
                {
                    // アニメーション：キャンバスを100%、カード追加を0%に
                    var animation = new Animation();
                    animation.Add(0, 1, new Animation(v => canvasColumn.Width = new GridLength(v, GridUnitType.Star), 0.5, 1));
                    animation.Add(0, 1, new Animation(v => addCardColumn.Width = new GridLength(v, GridUnitType.Star), 0.5, 0));
                    
                    animation.Commit(this, "HideAddCard", 16, 300, Easing.CubicOut, (v, c) =>
                    {
                        // アニメーション完了後にパネルを非表示
                        addCardScrollView.IsVisible = false;
                    });
                    
                    _isAddCardVisible = false;
                    Debug.WriteLine("カード追加パネルを非表示");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"カード追加パネル非表示エラー: {ex.Message}");
            }
        }
        /// <summary>
        /// トースト風の通知を表示（画面下部オーバーレイ）
        /// </summary>
        private async Task ShowToast(string message)
        {
            try
            {
                // トーストラベルが存在しない場合は作成
                if (_toastLabel == null)
                {
                    _toastLabel = new Label
                    {
                        Text = message,
                        BackgroundColor = Color.FromRgba(0, 0, 0, 0.8f), // 半透明の黒背景
                        TextColor = Colors.White,
                        FontSize = 16,
                        Padding = new Thickness(20, 12),
                        HorizontalOptions = LayoutOptions.Center,
                        VerticalOptions = LayoutOptions.End,
                        Margin = new Thickness(20, 0, 20, 30), // 画面下部からの余白
                        IsVisible = false,
                        HorizontalTextAlignment = TextAlignment.Center
                    };

                    // メインコンテナに追加（最前面に表示）
                    if (Content is Grid mainGrid)
                    {
                        mainGrid.Children.Add(_toastLabel);
                        Grid.SetRowSpan(_toastLabel, mainGrid.RowDefinitions.Count); // 全行にスパン
                        Grid.SetColumnSpan(_toastLabel, mainGrid.ColumnDefinitions.Count); // 全列にスパン
                    }
                }
                else
                {
                    _toastLabel.Text = message;
                }

                // トーストを表示
                _toastLabel.IsVisible = true;
                _toastLabel.Opacity = 0;
                _toastLabel.TranslationY = 50; // 下から上にスライドイン

                // アニメーション：フェードイン & スライドイン
                var fadeTask = _toastLabel.FadeTo(1, 300);
                var slideTask = _toastLabel.TranslateTo(0, 0, 300, Easing.CubicOut);
                await Task.WhenAll(fadeTask, slideTask);

                // 2.5秒間表示
                await Task.Delay(2500);

                // アニメーション：フェードアウト & スライドアウト
                var fadeOutTask = _toastLabel.FadeTo(0, 300);
                var slideOutTask = _toastLabel.TranslateTo(0, 50, 300, Easing.CubicIn);
                await Task.WhenAll(fadeOutTask, slideOutTask);
                
                _toastLabel.IsVisible = false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"トースト表示エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// ページ穴埋め選択
        /// </summary>
        private async void OnSelectPageForImageFill(object sender, EventArgs e)
        {
            try
            {
                // デバッグ情報を出力
                Debug.WriteLine($"ページ選択開始 - BackgroundCanvas: {_backgroundCanvas != null}");
                if (_backgroundCanvas != null)
                {
                    Debug.WriteLine($"HasContent: {_backgroundCanvas.HasContent}");
                    Debug.WriteLine($"PageCount: {_backgroundCanvas.PageCount}");
                }

                if (!(_backgroundCanvas?.HasContent == true))
                {
                    Debug.WriteLine($"コンテンツが利用できません - BackgroundCanvas: {_backgroundCanvas != null}, HasContent: {_backgroundCanvas?.HasContent}");
                    await UIThreadHelper.ShowAlertAsync("エラー", "表示されているコンテンツがありません", "OK");
                    return;
                }

                // PDFの場合のみページ選択UI、画像の場合は直接処理
                if (_backgroundCanvas.PageCount > 0)
                {
                    // PDFページの場合：ページ選択オーバーレイを表示
                    int currentPage = GetCurrentPageIndex();
                    
                    // BackgroundCanvasでページ選択モードを有効化
                    _backgroundCanvas.EnablePageSelectionMode();
                    
                    await ShowPageSelectionOverlay(currentPage);
                }
                else
                {
                    // 単一画像の場合：直接画像として読み込み
                    await LoadCurrentImageAsImageFill();
                }


            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ページ選択エラー: {ex.Message}");
                await UIThreadHelper.ShowAlertAsync("エラー", "ページ選択中にエラーが発生しました", "OK");
            }
        }

        /// <summary>
        /// 現在表示されているページインデックスを取得
        /// </summary>
        private int GetCurrentPageIndex()
        {
            try
            {
                if (_backgroundCanvas == null || MainScrollView == null)
                    return 0;

                // BackgroundCanvasの実装と同じロジックを使用
                return _backgroundCanvas.GetCurrentPageIndex();
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// ページ選択オーバーレイを表示
        /// </summary>
        private async Task ShowPageSelectionOverlay(int pageIndex)
        {
            try
            {
                _isPageSelectionMode = true;
                _selectedPageIndex = pageIndex;

                // オーバーレイが存在しない場合は作成
                if (_pageSelectionOverlay == null)
                {
                    _pageSelectionOverlay = new Frame
                    {
                        BackgroundColor = Color.FromRgba(255, 0, 0, 0.3f), // 半透明の赤
                        BorderColor = Colors.Red,
                        CornerRadius = 8,
                        Padding = new Thickness(20),
                        HorizontalOptions = LayoutOptions.Center,
                        VerticalOptions = LayoutOptions.Center,
                        IsVisible = false
                    };

                    var overlayLayout = new VerticalStackLayout { Spacing = 15 };

                    _pageSelectionLabel = new Label
                    {
                        Text = $"ページ {pageIndex + 1} を選択しますか？",
                        TextColor = Colors.White,
                        FontSize = 18,
                        FontAttributes = FontAttributes.Bold,
                        HorizontalTextAlignment = TextAlignment.Center
                    };

                    var buttonsLayout = new HorizontalStackLayout 
                    { 
                        Spacing = 20,
                        HorizontalOptions = LayoutOptions.Center
                    };

                    _pageConfirmButton = new Button
                    {
                        Text = "選択",
                        BackgroundColor = Colors.Green,
                        TextColor = Colors.White,
                        WidthRequest = 100
                    };
                    _pageConfirmButton.Clicked += OnPageConfirmClicked;

                    _pageCancelButton = new Button
                    {
                        Text = "キャンセル",
                        BackgroundColor = Colors.Gray,
                        TextColor = Colors.White,
                        WidthRequest = 100
                    };
                    _pageCancelButton.Clicked += OnPageCancelClicked;

                    buttonsLayout.Children.Add(_pageConfirmButton);
                    buttonsLayout.Children.Add(_pageCancelButton);

                    overlayLayout.Children.Add(_pageSelectionLabel);
                    overlayLayout.Children.Add(buttonsLayout);

                    _pageSelectionOverlay.Content = overlayLayout;

                    // メインGridに追加
                    if (Content is Grid mainGrid)
                    {
                        mainGrid.Children.Add(_pageSelectionOverlay);
                        Grid.SetRowSpan(_pageSelectionOverlay, mainGrid.RowDefinitions.Count);
                        Grid.SetColumnSpan(_pageSelectionOverlay, mainGrid.ColumnDefinitions.Count);
                    }
                }
                else
                {
                    // ラベルテキストを更新
                    _pageSelectionLabel.Text = $"ページ {pageIndex + 1} を選択しますか？";
                }

                // オーバーレイを表示
                _pageSelectionOverlay.IsVisible = true;
                _pageSelectionOverlay.Opacity = 0;
                await _pageSelectionOverlay.FadeTo(1, 300);

                await ShowToast($"ページ {pageIndex + 1} が選択されています。他のページを選択するにはスクロールしてください。");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"オーバーレイ表示エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// ページ選択オーバーレイを非表示
        /// </summary>
        private async Task HidePageSelectionOverlay()
        {
            try
            {
                if (_pageSelectionOverlay != null && _pageSelectionOverlay.IsVisible)
                {
                    await _pageSelectionOverlay.FadeTo(0, 150); // 300ms → 150msに短縮
                    _pageSelectionOverlay.IsVisible = false;
                }
                _isPageSelectionMode = false;
                
                // BackgroundCanvasでページ選択モードを無効化
                _backgroundCanvas?.DisablePageSelectionMode();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"オーバーレイ非表示エラー: {ex.Message}");
            }
        }


        /// <summary>
        /// ページ選択キャンセル
        /// </summary>
        private async void OnPageCancelClicked(object sender, EventArgs e)
        {
            await HidePageSelectionOverlay();
        }

        /// <summary>
        /// ページ選択確定
        /// </summary>
        private async void OnPageConfirmClicked(object sender, EventArgs e)
        {
            try
            {
                if (_selectedPageIndex >= 0)
                {
                    // フェードアウトを即座に開始（並行実行）
                    var fadeOutTask = HidePageSelectionOverlay();
                    
                    // 選択されたページを画像穴埋め用に読み込み
                    var loadImageTask = LoadCurrentImageAsImageFill();
                    
                    // 並行実行でスピードアップ
                    await Task.WhenAll(fadeOutTask, loadImageTask);
                    
                    await ShowToast($"ページ {_selectedPageIndex + 1} が画像穴埋め用に読み込まれました");
                }
                else
                {
                    // エラー時は通常通りフェードアウト
                    await HidePageSelectionOverlay();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ページ選択確定エラー: {ex.Message}");
                await UIThreadHelper.ShowAlertAsync("エラー", "ページ選択中にエラーが発生しました", "OK");
                
                // エラー時もフェードアウトを確実に実行
                await HidePageSelectionOverlay();
            }
        }

        
        /// <summary>
        /// メインスクロールビューのスクロールイベント
        /// </summary>
        private void OnMainScrollViewScrolled(object sender, ScrolledEventArgs e)
        {
            try
            {
                // ページ選択モード中のみページ更新
                if (_isPageSelectionMode && _backgroundCanvas?.HasContent == true && _backgroundCanvas?.PageCount > 0)
                {
                    // BackgroundCanvasに直接更新させる（より高速）
                    _backgroundCanvas.UpdateSelectedPage();
                    
                    // 選択ページが変わった場合のみUI更新
                    var currentPage = _backgroundCanvas.GetCurrentPageIndex();
                    if (currentPage != _selectedPageIndex)
                    {
                        _selectedPageIndex = currentPage;
                        
                        // ラベルテキストを更新
                        if (_pageSelectionLabel != null)
                        {
                            _pageSelectionLabel.Text = $"ページ {currentPage + 1} を選択しますか？";
                        }
                        
                        Debug.WriteLine($"ページ選択更新: {currentPage + 1}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"スクロールイベントエラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 現在表示されている画像を画像穴埋め用に読み込み
        /// </summary>
        private async Task LoadCurrentImageAsImageFill()
        {
            try
            {
                Debug.WriteLine("=== LoadCurrentImageAsImageFill開始 ===");
                
                if (!(_backgroundCanvas?.HasContent == true))
                {
                    Debug.WriteLine("コンテンツが利用できません");
                    await ShowToast("表示されているコンテンツがありません");
                    return;
                }

                string imagePath = null;
                
                // PDFページの場合はページキャッシュから画像を取得
                if (_backgroundCanvas.PageCount > 0)
                {
                    int currentPageIndex = GetCurrentPageIndex();
                    Debug.WriteLine($"PDFページモード: currentPageIndex = {currentPageIndex}");
                    
                    if (currentPageIndex >= 0)
                    {
                        // PageCacheから現在のページの画像を取得
                        var cacheDir = Path.Combine(tempExtractPath, "PageCache");
                        var highDpiCacheFile = Path.Combine(cacheDir, $"page_{currentPageIndex}_{(int)150f}.jpg");
                        var mediumDpiCacheFile = Path.Combine(cacheDir, $"page_{currentPageIndex}_{(int)96f}.jpg");
                        var oldHighDpiCacheFile = Path.Combine(cacheDir, $"page_{currentPageIndex}_{(int)72f}.jpg");
                        var oldLowDpiCacheFile = Path.Combine(cacheDir, $"page_{currentPageIndex}_{(int)36f}.jpg");

                        if (File.Exists(highDpiCacheFile))
                        {
                            imagePath = highDpiCacheFile;
                            Debug.WriteLine($"150dpi画像を使用: {highDpiCacheFile}");
                        }
                        else if (File.Exists(mediumDpiCacheFile))
                        {
                            imagePath = mediumDpiCacheFile;
                            Debug.WriteLine($"96dpi画像を使用: {mediumDpiCacheFile}");
                        }
                        else if (File.Exists(oldHighDpiCacheFile))
                        {
                            imagePath = oldHighDpiCacheFile;
                            Debug.WriteLine($"72dpi画像を使用: {oldHighDpiCacheFile}");
                        }
                        else if (File.Exists(oldLowDpiCacheFile))
                        {
                            imagePath = oldLowDpiCacheFile;
                            Debug.WriteLine($"36dpi画像を使用: {oldLowDpiCacheFile}");
                        }
                    }
                }
                else
                {
                    // 単一画像の場合は元の処理
                    imagePath = _backgroundCanvas?.CurrentImagePath;
                    Debug.WriteLine($"単一画像モード: imagePath = {imagePath}");
                }

                if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                {
                    Debug.WriteLine($"画像が見つかりません: {imagePath}");
                    await ShowToast("現在の画像が見つかりません");
                    return;
                }

                Debug.WriteLine($"画像穴埋め用画像パス: {imagePath}");

                // CardManagerに画像読み込みを委譲
                if (_cardManager != null)
                {
                    await _cardManager.LoadImageForImageFill(imagePath);
                    await ShowToast($"ページ {GetCurrentPageIndex() + 1} が画像穴埋め用に読み込まれました");
                    Debug.WriteLine("CardManager.LoadImageForImageFill完了");
                }
                else
                {
                    Debug.WriteLine("CardManagerが初期化されていません");
                    await ShowToast("CardManagerが初期化されていません");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"現在画像読み込みエラー: {ex.Message}");
                Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                await ShowToast("画像の読み込み中にエラーが発生しました");
            }
        }

        /// <summary>
        /// 現在のページを画像として追加
        /// </summary>
        private async Task AddCurrentPageAsImage(Editor editor)
        {
            try
            {
                int currentPageIndex = GetCurrentPageIndex();
                if (currentPageIndex < 0)
                {
                    await UIThreadHelper.ShowAlertAsync("エラー", "現在のページを取得できませんでした", "OK");
                    return;
                }

                // 一時的に画像を保存するための変数
                SkiaSharp.SKBitmap tempBitmap = null;

                try
                {
                    // PageCacheから画像を取得
                    var cacheDir = Path.Combine(tempExtractPath, "PageCache");
                    var highDpiCacheFile = Path.Combine(cacheDir, $"page_{currentPageIndex}_{(int)150f}.jpg");
                    var mediumDpiCacheFile = Path.Combine(cacheDir, $"page_{currentPageIndex}_{(int)96f}.jpg");
                    var oldHighDpiCacheFile = Path.Combine(cacheDir, $"page_{currentPageIndex}_{(int)72f}.jpg");
                    var oldLowDpiCacheFile = Path.Combine(cacheDir, $"page_{currentPageIndex}_{(int)36f}.jpg");

                    string imageFile = null;
                    if (File.Exists(highDpiCacheFile))
                    {
                        imageFile = highDpiCacheFile;
                    }
                    else if (File.Exists(mediumDpiCacheFile))
                    {
                        imageFile = mediumDpiCacheFile;
                    }
                    else if (File.Exists(oldHighDpiCacheFile))
                    {
                        imageFile = oldHighDpiCacheFile;
                    }
                    else if (File.Exists(oldLowDpiCacheFile))
                    {
                        imageFile = oldLowDpiCacheFile;
                    }

                    if (imageFile != null)
                    {
                        // ページ画像を読み込み
                        tempBitmap = SkiaSharp.SKBitmap.Decode(imageFile);
                        
                        if (tempBitmap != null)
                        {
                            // 画像IDを生成
                            Random random = new Random();
                            string imageId8 = random.Next(10000000, 99999999).ToString();
                            string imageId6 = random.Next(100000, 999999).ToString();
                            string imageId = $"{imageId8}_{imageId6}";
                            
                            string imageFolder = Path.Combine(tempExtractPath, "img");
                            Directory.CreateDirectory(imageFolder);

                            string newFileName = $"img_{imageId}.jpg";
                            string newFilePath = Path.Combine(imageFolder, newFileName);

                            // ビットマップを保存
                            if (_cardManager != null)
                            {
                                // CardManagerのSaveBitmapToFileメソッドを使用
                                var saveMethod = typeof(CardManager).GetMethod("SaveBitmapToFile", 
                                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                saveMethod?.Invoke(_cardManager, new object[] { tempBitmap, newFilePath });
                            }

                            // エディタに画像タグを挿入
                            int cursorPosition = editor.CursorPosition;
                            string text = editor.Text ?? "";
                            string newText = text.Insert(cursorPosition, $"<<img_{imageId}.jpg>>");
                            editor.Text = newText;
                            editor.CursorPosition = cursorPosition + $"<<img_{imageId}.jpg>>".Length;

                            // プレビューを更新
                            if (_cardManager != null)
                            {
                                _cardManager.UpdatePreviewForEditor(editor);
                            }

                            await ShowToast($"ページ {currentPageIndex + 1} を画像として追加しました");
                        }
                        else
                        {
                            await UIThreadHelper.ShowAlertAsync("エラー", "ページの画像化に失敗しました", "OK");
                        }
                    }
                    else
                    {
                        await UIThreadHelper.ShowAlertAsync("エラー", "ページ画像が見つかりません", "OK");
                    }
                }
                finally
                {
                    // 一時的なビットマップを解放
                    tempBitmap?.Dispose();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ページ画像追加エラー: {ex.Message}");
                await UIThreadHelper.ShowAlertAsync("エラー", "ページの画像化中にエラーが発生しました", "OK");
            }
        }

        /// <summary>
        /// ページ選択機能を活用したページ画像追加
        /// </summary>
        private async Task ShowPageSelectionForImage(Editor editor, int pageIndex)
        {
            try
            {
                Debug.WriteLine($"ページ選択画像追加開始 - Editor: {editor != null}, PageIndex: {pageIndex}");
                
                if (!(_backgroundCanvas?.HasContent == true))
                {
                    Debug.WriteLine($"コンテンツが利用できません");
                    await ShowToast("表示されているコンテンツがありません");
                    return;
                }

                // エディタを保存
                _currentEditorForPageImage = editor;
                _isPageSelectionForImageMode = true;

                // PDFの場合のみページ選択UI、画像の場合は直接処理
                if (_backgroundCanvas.PageCount > 0)
                {
                    // PDFページの場合：ページ選択オーバーレイを表示
                    int currentPage = GetCurrentPageIndex();
                    
                    // BackgroundCanvasでページ選択モードを有効化
                    _backgroundCanvas.EnablePageSelectionMode();
                    
                    await ShowPageSelectionOverlayForImage(currentPage);
                }
                else
                {
                    // 単一画像の場合：直接画像として追加
                    await AddCurrentPageAsImage(editor);
                    _isPageSelectionForImageMode = false;
                    _currentEditorForPageImage = null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ページ選択画像追加エラー: {ex.Message}");
                await ShowToast("ページ選択中にエラーが発生しました");
                _isPageSelectionForImageMode = false;
                _currentEditorForPageImage = null;
            }
        }

        /// <summary>
        /// ページ選択オーバーレイを表示
        /// </summary>
        private async Task ShowPageSelectionOverlayForImage(int pageIndex)
        {
            try
            {
                _isPageSelectionMode = true;
                _selectedPageIndex = pageIndex;

                // オーバーレイが存在しない場合は作成
                if (_pageSelectionOverlay == null)
                {
                    _pageSelectionOverlay = new Frame
                    {
                        BackgroundColor = Color.FromRgba(255, 0, 0, 0.3f), // 半透明の赤
                        BorderColor = Colors.Red,
                        CornerRadius = 8,
                        Padding = new Thickness(20),
                        HorizontalOptions = LayoutOptions.Center,
                        VerticalOptions = LayoutOptions.Center,
                        IsVisible = false
                    };

                    var overlayLayout = new VerticalStackLayout { Spacing = 15 };

                    _pageSelectionLabel = new Label
                    {
                        Text = $"ページ {pageIndex + 1} を選択しますか？",
                        TextColor = Colors.White,
                        FontSize = 18,
                        FontAttributes = FontAttributes.Bold,
                        HorizontalTextAlignment = TextAlignment.Center
                    };

                    var buttonsLayout = new HorizontalStackLayout 
                    { 
                        Spacing = 20,
                        HorizontalOptions = LayoutOptions.Center
                    };

                    _pageConfirmButton = new Button
                    {
                        Text = "選択",
                        BackgroundColor = Colors.Green,
                        TextColor = Colors.White,
                        WidthRequest = 100
                    };
                    _pageConfirmButton.Clicked += OnPageConfirmClickedForImage;

                    _pageCancelButton = new Button
                    {
                        Text = "キャンセル",
                        BackgroundColor = Colors.Gray,
                        TextColor = Colors.White,
                        WidthRequest = 100
                    };
                    _pageCancelButton.Clicked += OnPageCancelClickedForImage;

                    buttonsLayout.Children.Add(_pageConfirmButton);
                    buttonsLayout.Children.Add(_pageCancelButton);

                    overlayLayout.Children.Add(_pageSelectionLabel);
                    overlayLayout.Children.Add(buttonsLayout);

                    _pageSelectionOverlay.Content = overlayLayout;

                    // メインGridに追加
                    if (Content is Grid mainGrid)
                    {
                        mainGrid.Children.Add(_pageSelectionOverlay);
                        Grid.SetRowSpan(_pageSelectionOverlay, mainGrid.RowDefinitions.Count);
                        Grid.SetColumnSpan(_pageSelectionOverlay, mainGrid.ColumnDefinitions.Count);
                    }
                }
                else
                {
                    // ラベルテキストを更新
                    _pageSelectionLabel.Text = $"ページ {pageIndex + 1} を選択しますか？";
                }

                // オーバーレイを表示
                _pageSelectionOverlay.IsVisible = true;
                _pageSelectionOverlay.Opacity = 0;
                await _pageSelectionOverlay.FadeTo(1, 300);

                await ShowToast($"ページ {pageIndex + 1} が選択されています。他のページを選択するにはスクロールしてください。");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"オーバーレイ表示エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// ページ選択オーバーレイを非表示
        /// </summary>
        private async Task HidePageSelectionOverlayForImage()
        {
            try
            {
                if (_pageSelectionOverlay != null && _pageSelectionOverlay.IsVisible)
                {
                    await _pageSelectionOverlay.FadeTo(0, 150); // 300ms → 150msに短縮
                    _pageSelectionOverlay.IsVisible = false;
                }
                _isPageSelectionMode = false;
                
                // BackgroundCanvasでページ選択モードを無効化
                _backgroundCanvas?.DisablePageSelectionMode();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"オーバーレイ非表示エラー: {ex.Message}");
            }
        }


        /// <summary>
        /// ページ選択キャンセル
        /// </summary>
        private async void OnPageCancelClickedForImage(object sender, EventArgs e)
        {
            await HidePageSelectionOverlayForImage();
        }

        /// <summary>
        /// ページ選択確定
        /// </summary>
        private async void OnPageConfirmClickedForImage(object sender, EventArgs e)
        {
            try
            {
                if (_selectedPageIndex >= 0 && _currentEditorForPageImage != null)
                {
                    // フェードアウトを即座に開始（並行実行）
                    var fadeOutTask = HidePageSelectionOverlayForImage();
                    
                    // 選択されたページを画像として追加
                    var addImageTask = AddSelectedPageAsImage(_currentEditorForPageImage, _selectedPageIndex);
                    
                    // 並行実行でスピードアップ
                    await Task.WhenAll(fadeOutTask, addImageTask);
                    
                    await ShowToast($"ページ {_selectedPageIndex + 1} を画像として追加しました");
                }
                else
                {
                    // エラー時は通常通りフェードアウト
                    await HidePageSelectionOverlayForImage();
                }
                
                // 状態をリセット
                _isPageSelectionForImageMode = false;
                _currentEditorForPageImage = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ページ選択確定エラー: {ex.Message}");
                await ShowToast("ページ画像追加中にエラーが発生しました");
                
                // エラー時もフェードアウトを確実に実行
                await HidePageSelectionOverlayForImage();
                _isPageSelectionForImageMode = false;
                _currentEditorForPageImage = null;
            }
        }

        /// <summary>
        /// 現在のページ番号を取得
        /// </summary>
        private int GetCurrentPageNumber()
        {
            try
            {
                if (_backgroundCanvas != null)
                {
                    return _backgroundCanvas.GetCurrentPageIndex() + 1; // 1ベースのページ番号
                }
                return 1;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"現在のページ番号取得エラー: {ex.Message}");
                return 1;
            }
        }
        
        /// <summary>
        /// 現在のページのテキストを取得
        /// </summary>
        private async Task<string> GetCurrentPageTextAsync()
        {
            try
            {
                Debug.WriteLine("=== GetCurrentPageTextAsync 開始 ===");
                
                if (_backgroundCanvas == null)
                {
                    Debug.WriteLine("❌ BackgroundCanvasが初期化されていません");
                    return "";
                }
                
                Debug.WriteLine($"✅ BackgroundCanvas初期化済み: HasContent={_backgroundCanvas.HasContent}, PageCount={_backgroundCanvas.PageCount}");
                
                var currentPageIndex = _backgroundCanvas.GetCurrentPageIndex();
                Debug.WriteLine($"📄 現在のページインデックス: {currentPageIndex}");
                
                var pdfPath = GetCurrentPdfPath();
                Debug.WriteLine($"📁 PDFパス: {pdfPath}");
                
                if (string.IsNullOrEmpty(pdfPath))
                {
                    Debug.WriteLine("❌ PDFパスが空です");
                    return "";
                }
                
                if (!File.Exists(pdfPath))
                {
                    Debug.WriteLine($"❌ PDFファイルが存在しません: {pdfPath}");
                    return "";
                }
                
                Debug.WriteLine($"✅ PDFファイル存在確認OK: {new FileInfo(pdfPath).Length} bytes");
                Debug.WriteLine($"🔍 ページ{currentPageIndex + 1}のテキスト抽出開始");
                
                // PdfiumViewerを使用してテキストを抽出
                using var document = PdfiumViewer.PdfDocument.Load(pdfPath);
                Debug.WriteLine($"📖 PDF読み込み成功: {document.PageCount}ページ");
                
                if (currentPageIndex >= 0 && currentPageIndex < document.PageCount)
                {
                    var pageText = document.GetPdfText(currentPageIndex);
                    Debug.WriteLine($"✅ ページ{currentPageIndex + 1}テキスト抽出完了: {pageText?.Length ?? 0}文字");
                    
                    if (!string.IsNullOrEmpty(pageText))
                    {
                        // 最初の100文字をログに出力
                        var preview = pageText.Length > 100 ? pageText.Substring(0, 100) + "..." : pageText;
                        Debug.WriteLine($"📝 テキストプレビュー: {preview}");
                    }
                    else
                    {
                        Debug.WriteLine("⚠️ 抽出されたテキストが空です");
                    }
                    
                    return pageText ?? "";
                }
                
                Debug.WriteLine($"❌ 無効なページインデックス: {currentPageIndex} (総ページ数: {document.PageCount})");
                return "";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ ページテキスト抽出エラー: {ex.Message}");
                Debug.WriteLine($"❌ スタックトレース: {ex.StackTrace}");
                return "";
            }
        }
        

        /// <summary>
        /// 現在のPDFファイルパスを取得
        /// </summary>
        private string GetCurrentPdfPath()
        {
            try
            {
                Debug.WriteLine("=== GetCurrentPdfPath 開始 ===");
                
                if (_backgroundCanvas == null)
                {
                    Debug.WriteLine("❌ BackgroundCanvas is null");
                    return null;
                }

                Debug.WriteLine("✅ BackgroundCanvas存在確認OK");

                // BackgroundCanvasから現在のPDFパスを取得
                // リフレクションを使って_currentPdfPathフィールドにアクセス
                var type = typeof(BackgroundCanvas);
                Debug.WriteLine($"🔍 BackgroundCanvasタイプ: {type.Name}");
                
                var field = type.GetField("_currentPdfPath", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (field != null)
                {
                    var pdfPath = field.GetValue(_backgroundCanvas) as string;
                    Debug.WriteLine($"✅ PDF path from reflection: {pdfPath}");
                    
                    // 追加の状態確認
                    var pdfDocumentField = type.GetField("_pdfDocument", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (pdfDocumentField != null)
                    {
                        var pdfDocument = pdfDocumentField.GetValue(_backgroundCanvas);
                        Debug.WriteLine($"📖 _pdfDocument状態: {(pdfDocument != null ? "存在" : "null")}");
                    }
                    
                    var hasContentProperty = type.GetProperty("HasContent", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (hasContentProperty != null)
                    {
                        var hasContent = hasContentProperty.GetValue(_backgroundCanvas);
                        Debug.WriteLine($"📄 HasContent: {hasContent}");
                    }
                    
                    var pageCountProperty = type.GetProperty("PageCount", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (pageCountProperty != null)
                    {
                        var pageCount = pageCountProperty.GetValue(_backgroundCanvas);
                        Debug.WriteLine($"📄 PageCount: {pageCount}");
                    }
                    
                    return pdfPath;
                }
                
                Debug.WriteLine("❌ _currentPdfPath field not found");
                
                // 利用可能なフィールドを列挙
                var fields = type.GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Debug.WriteLine($"🔍 利用可能なフィールド: {string.Join(", ", fields.Select(f => f.Name))}");
                
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ PDFパス取得エラー: {ex.Message}");
                Debug.WriteLine($"❌ スタックトレース: {ex.StackTrace}");
                return null;
            }
        }


        /// <summary>
        /// テキスト選択モードを無効にする
        /// </summary>
        private async Task DisableTextSelectionMode()
        {
            try
            {
                _isTextSelectionMode = false;
                Debug.WriteLine("テキスト選択モード無効化開始");

                // WebViewを非表示にして選択をクリア
                if (_pdfTextSelectionWebView != null)
                {
                    try
                    {
                        // 選択をクリア
                        await _pdfTextSelectionWebView.EvaluateJavaScriptAsync("clearSelection()");
                        await _pdfTextSelectionWebView.EvaluateJavaScriptAsync("updateStatus('テキスト選択モード終了')");
                        await Task.Delay(300);
                    }
                    catch (Exception jsEx)
                    {
                        Debug.WriteLine($"JavaScript実行エラー: {jsEx.Message}");
                    }
                    
                    // WebViewは常に表示状態を維持し、透明度のみ調整
                    _pdfTextSelectionWebView.Opacity = 0.01; // ほぼ透明に戻す
                    _pdfTextSelectionWebView.InputTransparent = false; // タッチイベントは受け取り続ける
                    Debug.WriteLine("WebView透明度調整完了");
                }

                // BackgroundCanvasの状態は変更していないので復元不要
                Debug.WriteLine("BackgroundCanvas状態維持");

                // スクロールイベントハンドラーを削除
                if (_backgroundCanvas?.ParentScrollView != null)
                {
                    _backgroundCanvas.ParentScrollView.Scrolled -= OnScrollViewScrolledForTextSelection;
                }

                // 選択されたテキストをクリア
                _selectedText = "";

                await ShowToast("テキスト選択モードを無効にしました");
                Debug.WriteLine("テキスト選択モード無効化完了");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"テキスト選択モード無効化エラー: {ex.Message}");
            }
        }


        /// <summary>
        /// テキスト選択モード用のスクロールイベントハンドラー
        /// </summary>
        private int _lastPageIndex = -1;
        private async void OnScrollViewScrolledForTextSelection(object sender, ScrolledEventArgs e)
        {
            try
            {
                if (!_isTextSelectionMode || _backgroundCanvas == null)
                    return;

                var currentPageIndex = _backgroundCanvas.GetCurrentPageIndex();
                
                // ページが変更された場合のみWebViewを更新
                if (currentPageIndex != _lastPageIndex)
                {
                    _lastPageIndex = currentPageIndex;
                    Debug.WriteLine($"ページ変更検出: {currentPageIndex + 1}ページ目");
                    
                                    // WebViewのテキストを更新
                await UpdateWebViewForCurrentPage();
                
                // 少し待ってから再度更新を試行（確実にテキストを表示するため）
                await Task.Delay(1000);
                Debug.WriteLine("🔄 追加のテキスト更新を実行");
                await UpdateWebViewForCurrentPage();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"テキスト選択用スクロールイベントエラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 現在のページに合わせてWebViewを更新
        /// </summary>
        private async Task UpdateWebViewForCurrentPage()
        {
            try
            {
                if (_pdfTextSelectionWebView == null || !_isTextSelectionMode)
                    return;

                Debug.WriteLine("WebView更新開始");
                
                // 現在のページのテキストを取得
                var currentPageText = await GetCurrentPageTextAsync();
                var currentPageNumber = GetCurrentPageNumber();
                
                if (!string.IsNullOrEmpty(currentPageText))
                {
                    // JavaScriptでページテキストを更新
                    var escapedText = System.Text.Json.JsonEncodedText.Encode(currentPageText).ToString().Trim('"');
                    var updateScript = $@"
                        try {{
                            console.log('ページテキスト更新開始: ページ{currentPageNumber}');
                            
                            // updatePageText関数を使用してテキストを更新
                            if (typeof updatePageText === 'function') {{
                                updatePageText('{escapedText}', {currentPageNumber});
                                console.log('updatePageText関数でテキスト更新完了');
                            }} else {{
                                console.log('updatePageText関数が見つかりません - 直接更新');
                                
                                // 直接テキストコンテナを更新
                                var textContainer = document.getElementById('textContainer');
                                if (!textContainer) {{
                                    console.log('textContainerが見つかりません');
                                    return;
                                }}
                                
                                // 既存のテキストをクリア
                                textContainer.innerHTML = '';
                                
                                // 実際のPDFテキストを行に分割して表示
                                var pageText = '{escapedText}';
                                var lines = pageText.split(/[\\r\\n]+/).filter(line => line.trim() !== '');
                                
                                console.log('処理する行数:', lines.length);
                                
                                // 各行を配置
                                lines.forEach(function(line, index) {{
                                    if (index >= 20) return; // 最大20行まで
                                    
                                    line = line.trim();
                                    if (line.length > 0) {{
                                        var span = document.createElement('span');
                                        span.className = 'text-line';
                                        span.textContent = line;
                                        span.style.top = (50 + index * 25) + 'px';
                                        span.style.left = '50px';
                                        
                                        textContainer.appendChild(span);
                                        console.log('行追加:', line.substring(0, 30));
                                    }}
                                }});
                                
                                // ステータス更新
                                if (typeof updateStatus === 'function') {{
                                    updateStatus('ページ {currentPageNumber} - ' + lines.length + '行のテキスト表示中');
                                }}
                                
                                // デバッグ更新
                                if (typeof updateDebug === 'function') {{
                                    updateDebug('テキスト表示完了: ' + lines.length + '行');
                                }}
                            }}
                            
                            console.log('ページ{currentPageNumber}のテキスト更新完了');
                        }} catch(e) {{
                            console.log('ページ更新エラー:', e);
                            console.error('詳細エラー:', e.message, e.stack);
                        }}
                    ";
                    
                    await _pdfTextSelectionWebView.EvaluateJavaScriptAsync(updateScript);
                    Debug.WriteLine($"✅ ページ{currentPageNumber}のテキスト更新完了");
                }
                else
                {
                    // テキストがない場合
                    var updateScript = $@"
                        try {{
                            updateStatus('ページ {currentPageNumber} - テキストなし');
                            clearPageText();
                        }} catch(e) {{
                            console.log('ページクリアエラー:', e);
                        }}
                    ";
                    
                    await _pdfTextSelectionWebView.EvaluateJavaScriptAsync(updateScript);
                    Debug.WriteLine($"ページ{currentPageNumber}テキストなし");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WebView更新エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 選択されたページを画像として追加
        /// </summary>
        private async Task AddSelectedPageAsImage(Editor editor, int pageIndex)
        {
            try
            {
                Debug.WriteLine($"選択されたページ {pageIndex + 1} を画像として追加開始");
                
                if (pageIndex < 0)
                {
                    await ShowToast("無効なページが選択されています");
                    return;
                }

                // 一時的に画像を保存するための変数
                SkiaSharp.SKBitmap tempBitmap = null;

                try
                {
                    // PageCacheから選択されたページの画像を取得
                    var cacheDir = Path.Combine(tempExtractPath, "PageCache");
                    var highDpiCacheFile = Path.Combine(cacheDir, $"page_{pageIndex}_{(int)150f}.jpg");
                    var mediumDpiCacheFile = Path.Combine(cacheDir, $"page_{pageIndex}_{(int)96f}.jpg");
                    var oldHighDpiCacheFile = Path.Combine(cacheDir, $"page_{pageIndex}_{(int)72f}.jpg");
                    var oldLowDpiCacheFile = Path.Combine(cacheDir, $"page_{pageIndex}_{(int)36f}.jpg");

                    string imageFile = null;
                    if (File.Exists(highDpiCacheFile))
                    {
                        imageFile = highDpiCacheFile;
                        Debug.WriteLine($"150dpi画像を使用: {highDpiCacheFile}");
                    }
                    else if (File.Exists(mediumDpiCacheFile))
                    {
                        imageFile = mediumDpiCacheFile;
                        Debug.WriteLine($"96dpi画像を使用: {mediumDpiCacheFile}");
                    }
                    else if (File.Exists(oldHighDpiCacheFile))
                    {
                        imageFile = oldHighDpiCacheFile;
                        Debug.WriteLine($"72dpi画像を使用: {oldHighDpiCacheFile}");
                    }
                    else if (File.Exists(oldLowDpiCacheFile))
                    {
                        imageFile = oldLowDpiCacheFile;
                        Debug.WriteLine($"36dpi画像を使用: {oldLowDpiCacheFile}");
                    }

                    if (imageFile != null)
                    {
                        // ページ画像を読み込み
                        tempBitmap = SkiaSharp.SKBitmap.Decode(imageFile);
                        
                        if (tempBitmap != null)
                        {
                            // 画像IDを生成
                            Random random = new Random();
                            string imageId8 = random.Next(10000000, 99999999).ToString();
                            string imageId6 = random.Next(100000, 999999).ToString();
                            string imageId = $"{imageId8}_{imageId6}";
                            
                            string imageFolder = Path.Combine(tempExtractPath, "img");
                            Directory.CreateDirectory(imageFolder);

                            string newFileName = $"img_{imageId}.jpg";
                            string newFilePath = Path.Combine(imageFolder, newFileName);

                            // ビットマップを保存
                            if (_cardManager != null)
                            {
                                // CardManagerのSaveBitmapToFileメソッドを使用
                                var saveMethod = typeof(CardManager).GetMethod("SaveBitmapToFile", 
                                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                saveMethod?.Invoke(_cardManager, new object[] { tempBitmap, newFilePath });
                                Debug.WriteLine($"画像保存完了: {newFilePath}");
                            }

                            // エディタに画像タグを挿入
                            int cursorPosition = editor.CursorPosition;
                            string text = editor.Text ?? "";
                            string newText = text.Insert(cursorPosition, $"<<img_{imageId}.jpg>>");
                            editor.Text = newText;
                            editor.CursorPosition = cursorPosition + $"<<img_{imageId}.jpg>>".Length;

                            // プレビューを更新
                            if (_cardManager != null)
                            {
                                _cardManager.UpdatePreviewForEditor(editor);
                            }

                            Debug.WriteLine($"ページ {pageIndex + 1} を画像として追加完了");
                        }
                        else
                        {
                            await ShowToast("ページの画像化に失敗しました");
                        }
                    }
                    else
                    {
                        await ShowToast("ページ画像が見つかりません");
                    }
                }
                finally
                {
                    // 一時的なビットマップを解放
                    tempBitmap?.Dispose();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"選択ページ画像追加エラー: {ex.Message}");
                await ShowToast("ページの画像化中にエラーが発生しました");
            }
        }
    }
}
