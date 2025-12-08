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
using Flashnote.Models;
using Flashnote.Services;
using System.Reflection;
using SkiaSharp.Views.Maui;
using SkiaSharp;
using System.IO.Compression;
using System.Web;
using SkiaSharp.Views.Maui.Controls;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Graphics;

namespace Flashnote
{
    public partial class Add : ContentPage
    {
        private CardManager _cardManager;
        private Label _toastLabel; // トースト表示用ラベル
        private string _noteBaseDir; // cards/img と同階層に materials を作るための基底ディレクトリ
        private string _tempExtractPath; // tempPath を保持しておく

        public Add(string cardsPath, string tempPath, string cardId = null)
        {
            try
            {
                Debug.WriteLine($"Add.xaml.cs コンストラクタ開始");
                Debug.WriteLine($"cardsPath: {cardsPath}");
                Debug.WriteLine($"tempPath: {tempPath}");
                Debug.WriteLine($"cardId: {cardId}");

                InitializeComponent();
                Debug.WriteLine("InitializeComponent完了");

                // StatusIndicatorを初期化
                StatusIndicator.RefreshStatus();

                // tempPath をフィールドに保持
                if (!string.IsNullOrEmpty(tempPath))
                {
                    try
                    {
                        _tempExtractPath = tempPath;
                        Debug.WriteLine($"_tempExtractPath を設定: {_tempExtractPath}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"tempPath の設定でエラー: {ex.Message}");
                    }
                }

                // ベースディレクトリを設定: まずcardsPathのディレクトリを優先してmaterialsフォルダを作成する
                _noteBaseDir = null;
                if (!string.IsNullOrEmpty(cardsPath))
                {
                    try
                    {
                        var cardsDir = Path.GetDirectoryName(cardsPath);
                        if (!string.IsNullOrEmpty(cardsDir))
                        {
                            _noteBaseDir = cardsDir;
                            Debug.WriteLine($"_noteBaseDir を cardsPath から設定: {_noteBaseDir}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"cardsPath からベースディレクトリ取得エラー: {ex.Message}");
                    }
                }

                // サブフォルダ情報を取得
                string subFolder = null;
                if (!string.IsNullOrEmpty(tempPath))
                {
                    var tempDir = Path.GetDirectoryName(tempPath);
                    if (!string.IsNullOrEmpty(tempDir))
                    {
                        // cardsPath が未設定の場合は tempPath から基底ディレクトリを保持
                        if (string.IsNullOrEmpty(_noteBaseDir))
                        {
                            _noteBaseDir = tempDir;
                            Debug.WriteLine($"_noteBaseDir を tempPath から設定: {_noteBaseDir}");
                        }

                        var tempBasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Flashnote");
                        if (tempDir.StartsWith(tempBasePath))
                        {
                            var relativePath = Path.GetRelativePath(tempBasePath, tempDir);
                            if (!relativePath.StartsWith(".") && relativePath != ".")
                            {
                                subFolder = relativePath;
                                Debug.WriteLine($"サブフォルダを検出: {subFolder}");
                            }
                        }
                    }
                }

                // CardManagerを初期化
                _cardManager = new CardManager(cardsPath, tempPath, cardId, subFolder);
                Debug.WriteLine("CardManager初期化完了");

                // CardManagerにトーストコールバックを設定
                _cardManager.SetPageSelectionCallbacks(
                    selectPageCallback: null, // Addページでは使用しない
                    loadCurrentImageCallback: null, // Addページでは使用しない
                    showToastCallback: async (message) => await ShowToast(message),
                    showAlertCallback: async (title, message) => await Application.Current.MainPage.DisplayAlert(title, message, "OK")
                );

                // CardManagerを使用してUIを初期化
                _cardManager.InitializeCardUI(CardContainer, includePageImageButtons: false);
                Debug.WriteLine("CardUI初期化完了");

                // 起動時に metadata.json から defaultMaterial を読み込み、PDFを表示する試行
                try
                {
                    MainThread.BeginInvokeOnMainThread(async () =>
                    {
                        try
                        {
                            await TryLoadDefaultMaterialFromMetadata();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"defaultMaterial 読み込みエラー: {ex.Message}");
                        }
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"defaultMaterial の起動時読み込み設定エラー: {ex.Message}");
                }

                 Debug.WriteLine("Add.xaml.cs コンストラクタ完了");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("オフライン") || ex.Message.Contains("ネットワーク"))
            {
                Debug.WriteLine($"Add.xaml.cs コンストラクタでオフラインエラー: {ex.Message}");
                // オフラインエラーの場合は再スローして呼び出し元で処理
                throw;
            }
            catch (TimeoutException ex)
            {
                Debug.WriteLine($"Add.xaml.cs コンストラクタでタイムアウトエラー: {ex.Message}");
                // タイムアウトエラーの場合は再スローして呼び出し元で処理
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Add.xaml.cs コンストラクタでエラー: {ex.Message}");
                Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                throw;
            }
        }

        /// <summary>
        /// ページが表示される前の処理
        /// </summary>
        protected override void OnAppearing()
        {
            base.OnAppearing();
            Debug.WriteLine("Addページが表示されました");
            
            // トースト表示のテスト（開発時のみ）
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Task.Delay(1000); // 1秒待ってからテスト表示
                await ShowToast("Addページのトースト表示テスト");
            });
        }

        /// <summary>
        /// ページから離れる前の処理
        /// </summary>
        protected override async void OnDisappearing()
        {
            base.OnDisappearing();
            Debug.WriteLine("Addページから離れます");
        }

        /// <summary>
        /// 戻るボタンが押された時の処理
        /// </summary>
        protected override bool OnBackButtonPressed()
        {
            try
            {
                Debug.WriteLine("Addページの戻るボタンが押されました");
                
                // 未保存の変更があるかチェック
                if (_cardManager.HasUnsavedChanges())
                {
                    Debug.WriteLine("未保存の変更があります。破棄確認ダイアログを表示します");
                    
                    // UIスレッドでダイアログを表示
                    MainThread.BeginInvokeOnMainThread(async () =>
                    {
                        try
                        {
                            var shouldDiscard = await _cardManager.ShowDiscardConfirmationDialog();
                            
                            if (shouldDiscard)
                            {
                                Debug.WriteLine("破棄が選択されました。フィールドをクリアします");
                                _cardManager.ClearFields();
                                await Navigation.PopAsync();
                            }
                            else
                            {
                                Debug.WriteLine("キャンセルが選択されました。ページを離れません");
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
                    Debug.WriteLine("未保存の変更はありません。通常通り戻ります");
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
        /// ページが破棄される時の処理
        /// </summary>
        protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
        {
            base.OnNavigatedFrom(args);
            Debug.WriteLine("Addページからナビゲートされました");
            
            // CardManagerのリソースを解放
            _cardManager?.Dispose();
        }

        /// <summary>
        /// ヘルプボタンがクリックされた時の処理
        /// </summary>
        private async void OnHelpClicked(object sender, EventArgs e)
        {
            try
            {
                await HelpOverlayControl.ShowHelp(HelpType.AddPage);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ヘルプ表示エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// トースト風の通知を表示（画面下部オーバーレイ）
        /// </summary>
        private async Task ShowToast(string message)
        {
            try
            {
                Debug.WriteLine($"=== ShowToast開始: {message} ===");
                
                // トーストラベルが存在しない場合は作成
                if (_toastLabel == null)
                {
                    Debug.WriteLine("トーストラベルを作成中...");
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
                        HorizontalTextAlignment = TextAlignment.Center,
                        ZIndex = 1000 // 最前面に表示
                    };

                    Debug.WriteLine("トーストラベルをレイアウトに追加中...");
                    
                    // 現在のContentをGridで包んで、トーストラベルを追加
                    var currentContent = Content;
                    var mainGrid = new Grid();
                    
                    // 既存のコンテンツをGridに追加
                    mainGrid.Children.Add(currentContent);
                    Grid.SetRow(currentContent, 0);
                    Grid.SetColumn(currentContent, 0);
                    
                    // トーストラベルをGridに追加（最前面）
                    mainGrid.Children.Add(_toastLabel);
                    Grid.SetRow(_toastLabel, 0);
                    Grid.SetColumn(_toastLabel, 0);
                    
                    // Contentを新しいGridに設定
                    Content = mainGrid;
                    
                    Debug.WriteLine("トーストラベルをレイアウトに追加完了");
                }
                else
                {
                    Debug.WriteLine("既存のトーストラベルを使用");
                    _toastLabel.Text = message;
                }

                Debug.WriteLine("トーストアニメーション開始");
                
                // トーストを表示
                _toastLabel.IsVisible = true;
                _toastLabel.Opacity = 0;
                _toastLabel.TranslationY = 50; // 下から上にスライドイン

                // アニメーション：フェードイン & スライドイン
                var fadeTask = _toastLabel.FadeTo(1, 300);
                var slideTask = _toastLabel.TranslateTo(0, 0, 300, Easing.CubicOut);
                await Task.WhenAll(fadeTask, slideTask);

                Debug.WriteLine("トースト表示中（2.5秒間）");
                
                // 2.5秒間表示
                await Task.Delay(2500);

                Debug.WriteLine("トーストアニメーション終了開始");
                
                // アニメーション：フェードアウト & スライドアウト
                var fadeOutTask = _toastLabel.FadeTo(0, 300);
                var slideOutTask = _toastLabel.TranslateTo(0, 50, 300, Easing.CubicIn);
                await Task.WhenAll(fadeOutTask, slideOutTask);
                
                _toastLabel.IsVisible = false;
                
                Debug.WriteLine("=== ShowToast完了 ===");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"トースト表示エラー: {ex.Message}");
                Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
            }
        }
        private async void OnSelectMaterialClicked(object sender, EventArgs e)
        {
            try
            {
                var result = await FilePicker.PickAsync(new PickOptions
                {
                    PickerTitle = "資料を選択（PDF）",
                    FileTypes = FilePickerFileType.Pdf
                });

                if (result == null) return;

                if (!result.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    await DisplayAlert("エラー", "PDFファイルを選択してください", "OK");
                    return;
                }

                // 保存先フォルダを決定: tempExtractPath/material を優先して作成
                string materialDir = null;
                try
                {
                    if (!string.IsNullOrEmpty(_tempExtractPath) && Directory.Exists(_tempExtractPath))
                    {
                        materialDir = Path.Combine(_tempExtractPath, "material"); // 要望通り 'material' フォルダ
                        Debug.WriteLine($"materialDir を tempExtractPath に設定: {materialDir}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"tempExtractPath を使った materialDir の構築エラー: {ex.Message}");
                    materialDir = null;
                }

                // fallback: 従来の場所
                if (string.IsNullOrEmpty(materialDir))
                {
                    if (!string.IsNullOrEmpty(_noteBaseDir))
                    {
                        materialDir = Path.Combine(_noteBaseDir, "materials");
                    }
                    else
                    {
                        materialDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Flashnote", "materials");
                    }
                }

                Directory.CreateDirectory(materialDir);

                var destFile = Path.Combine(materialDir, result.FileName);
                using (var src = await result.OpenReadAsync())
                using (var dst = File.Create(destFile))
                {
                    await src.CopyToAsync(dst);
                }

                Debug.WriteLine($"資料をコピーしました: {destFile}");

                // PDFビューワーを表示（埋め込み）
                try
                {
                    ShowEmbeddedPdf(destFile);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"PDF埋め込み表示エラー: {ex.Message}");
                }

                // CardManagerが存在すれば既定の素材として設定
                if (_cardManager != null)
                {
                    var material = new Flashnote.Models.DefaultMaterial { isPDF = true, fileName = Path.GetFileName(destFile) };
                    try
                    {
                        await _cardManager.SetDefaultMaterial(material);
                        await ShowToast("資料を設定しました");
                        return;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"CardManager.SetDefaultMaterial呼び出しエラー: {ex.Message}");
                    }
                }

                await DisplayAlert("完了", "資料を保存しました: " + destFile, "OK");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"資料選択エラー: {ex.Message}");
                await DisplayAlert("エラー", "資料の読み込みに失敗しました: " + ex.Message, "OK");
            }
        }

        // 新しく追加したハンドラー: 現在表示しているPDFを既定の資料として設定する
        private async void OnSetDefaultMaterialClicked(object sender, EventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_currentPdfPath))
                {
                    await ShowToast("設定する資料が選択されていません");
                    return;
                }

                if (_cardManager == null)
                {
                    await ShowToast("CardManagerが利用できません");
                    return;
                }

                var material = new Flashnote.Models.DefaultMaterial
                {
                    isPDF = true,
                    fileName = Path.GetFileName(_currentPdfPath)
                };

                try
                {
                    await _cardManager.SetDefaultMaterial(material);
                    await ShowToast("この資料をデフォルトに設定しました");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"デフォルト設定エラー: {ex.Message}");
                    await DisplayAlert("エラー", "デフォルト設定に失敗しました: " + ex.Message, "OK");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OnSetDefaultMaterialClicked error: {ex.Message}");
            }
        }

        private int _currentPdfPage = 1;
        private int _pdfPageCount = 0;
        private string _currentPdfPath = null;
        private bool _suppressNavigatedUpdate = false;

        private void ShowEmbeddedPdf(string pdfPath)
        {
            try
            {
                _currentPdfPath = pdfPath;
                PdfFileNameLabel.Text = Path.GetFileName(pdfPath);

                // attach navigated handler (ensure single attach)
                PdfEmbedWebView.Navigated -= OnPdfWebViewNavigated;
                PdfEmbedWebView.Navigated += OnPdfWebViewNavigated;

                PdfViewerContainer.IsVisible = true;
                _currentPdfPage = 1;
                PageNumberEntry.Text = _currentPdfPage.ToString();

                // Try to load bundled PDF.js viewer if present
                try
                {
                    var viewerUrl = $"ms-appx-web:///Resources/Raw/pdfjs-viewer.html?file={Uri.EscapeDataString(_currentPdfPath)}#page={_currentPdfPage}";
                    PdfEmbedWebView.Source = new UrlWebViewSource { Url = viewerUrl };
                    _pdfPageCount = 0;
                    _suppressNavigatedUpdate = true;
                    return;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"pdfjs viewer load error, falling back to direct file: {ex}");
                }

                // Fallback: load PDF directly
                string url = pdfPath;
                if (!url.Contains("://")) url = "file:///" + pdfPath.Replace('\\', '/');
                _suppressNavigatedUpdate = true;
                PdfEmbedWebView.Source = new UrlWebViewSource { Url = url + "#page=" + _currentPdfPage };

            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ShowEmbeddedPdf error: {ex}");
            }
        }

        private async void OnPdfWebViewNavigated(object sender, WebNavigatedEventArgs e)
        {
            try
            {
                var url = e?.Url ?? string.Empty;
                if (string.IsNullOrEmpty(url)) return;

                // Handle custom app://page= notification from JS listener
                if (url.StartsWith("app://", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var uri = new Uri(url);
                        var raw = uri.AbsoluteUri; // e.g. app://page=3
                        var q = raw.Substring("app://".Length);
                        if (q.StartsWith("page=") && int.TryParse(q.Substring(5), out var p))
                        {
                            if (p > 0 && p != _currentPdfPage)
                            {
                                _currentPdfPage = p;
                                PageNumberEntry.Text = _currentPdfPage.ToString();
                            }
                        }
                    }
                    catch { }

                    // prevent navigation history pollution by reverting to pdf viewer URL
                    if (!string.IsNullOrEmpty(_currentPdfPath))
                    {
                        // re-navigate to viewer URL without adding history
                        try
                        {
                            var viewerUrl = $"ms-appx-web:///Resources/Raw/pdfjs-viewer.html?file={Uri.EscapeDataString(_currentPdfPath)}#page={_currentPdfPage}";
                            _suppressNavigatedUpdate = true;
                            PdfEmbedWebView.Source = new UrlWebViewSource { Url = viewerUrl };
                        }
                        catch { }
                    }

                    return;
                }

                // If fragment contains page info (#page=3)
                try
                {
                    var uri2 = new Uri(url);
                    var frag = uri2.Fragment; // includes leading '#'
                    if (!string.IsNullOrEmpty(frag) && frag.StartsWith("#page="))
                    {
                        var s = frag.Substring(6);
                        if (int.TryParse(s, out var p) && p > 0 && p != _currentPdfPage)
                        {
                            _currentPdfPage = p;
                            PageNumberEntry.Text = _currentPdfPage.ToString();
                        }
                    }
                }
                catch { }

                // After loading the viewer, inject JS to listen for page changes (PDF.js)
                try
                {
                    var script = @"(function(){ try{ if(window.PDFViewerApplication && window.PDFViewerApplication.eventBus){ window.PDFViewerApplication.eventBus.on('pagechange', function(e){ try{ window.location.href = 'app://page=' + e.pageNumber; }catch(e){} }); } }catch(e){} })();";
                    await PdfEmbedWebView.EvaluateJavaScriptAsync(script);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Inject JS error: {ex}");
                }

            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OnPdfWebViewNavigated error: {ex}");
            }
        }

        private void OnClosePdfEmbedClicked(object sender, EventArgs e)
        {
            PdfViewerContainer.IsVisible = false;
            PdfEmbedWebView.Source = null;
            _currentPdfPath = null;
            _currentPdfPage = 1;
            PageNumberEntry.Text = "";
        }

        private async void OnPrevPageClicked(object sender, EventArgs e)
        {
            if (_currentPdfPage > 1)
            {
                _currentPdfPage--;
                PageNumberEntry.Text = _currentPdfPage.ToString();
                await SetPdfPageInWebView(_currentPdfPage);
            }
        }

        private async void OnNextPageClicked(object sender, EventArgs e)
        {
            _currentPdfPage++;
            PageNumberEntry.Text = _currentPdfPage.ToString();
            await SetPdfPageInWebView(_currentPdfPage);
        }

        private async void OnSetPageClicked(object sender, EventArgs e)
        {
            if (int.TryParse(PageNumberEntry.Text, out int page) && page > 0)
            {
                _currentPdfPage = page;
                await SetPdfPageInWebView(_currentPdfPage);
            }
        }

        private async Task SetPdfPageInWebView(int page)
        {
            try
            {
                if (string.IsNullOrEmpty(_currentPdfPath)) return;

#if WINDOWS
                // Windows専用: Windows.Data.Pdfを使ってページをレンダリング
                try
                {
                    var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(_currentPdfPath);
                    var pdfDoc = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
                    
                    if (page > 0 && page <= pdfDoc.PageCount)
                    {
                        using (var pdfPage = pdfDoc.GetPage((uint)(page - 1)))
                        {
                            var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                            var options = new Windows.Data.Pdf.PdfPageRenderOptions
                            {
                                DestinationWidth = (uint)(pdfPage.Size.Width * 2), // 高解像度
                                DestinationHeight = (uint)(pdfPage.Size.Height * 2)
                            };
                            
                            await pdfPage.RenderToStreamAsync(stream, options);
                            stream.Seek(0);

                            MainThread.BeginInvokeOnMainThread(() =>
                            {
                                try
                                {
                                    PdfEmbedWebView.IsVisible = false;
                                    var img = this.FindByName<Image>("PdfPageImage");
                                    if (img != null)
                                    {
                                        img.IsVisible = true;
                                        img.Source = ImageSource.FromStream(() => stream.AsStreamForRead());
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"SetPdfPageInWebView UI set error: {ex}");
                                }
                            });

                            _currentPdfPage = page;
                            PageNumberEntry.Text = _currentPdfPage.ToString();
                            
                            // ページ数を記録
                            if (_pdfPageCount == 0)
                            {
                                _pdfPageCount = (int)pdfDoc.PageCount;
                                Debug.WriteLine($"PDF総ページ数: {_pdfPageCount}");
                            }
                            
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Windows PDF rendering error: {ex.Message}");
                }
#endif

                // Build base URL for file or viewer
                var baseUrl = _currentPdfPath;
                if (!baseUrl.Contains("://")) baseUrl = "file:///" + _currentPdfPath.Replace('\\', '/');

                // Ensure PDF.js viewer is ready then ask it to switch page and return canvas data URL
                try
                {
                    var checkReady = "(function(){ try{ return !!(window.PDFViewerApplication && (window.PDFViewerApplication.initialized || window.PDFViewerApplication.pdfDocument)); }catch(e){ return false; } })();";
                    var ready = false;
                    for (int i = 0; i < 10; i++)
                    {
                        try
                        {
                            var r = await PdfEmbedWebView.EvaluateJavaScriptAsync(checkReady);
                            if (!string.IsNullOrEmpty(r) && (r == "true" || r == "True")) { ready = true; break; }
                        }
                        catch { }
                        await Task.Delay(200);
                    }

                    if (ready)
                    {
                        // Build JS by concatenation to avoid complex C# quoting issues
                        string js = "(function(){try{var p=" + page + ";var app=window.PDFViewerApplication; if(!app) return ''; if(app.pdfViewer) app.pdfViewer.currentPageNumber=p; if(app.setPage) app.setPage(p); if(app.pdfViewer && app.pdfViewer.scrollPageIntoView) app.pdfViewer.scrollPageIntoView({pageNumber:p}); var start=Date.now(); var canvas=null; while(true){ var pv=(app.pdfViewer && app.pdfViewer.getPageView)? app.pdfViewer.getPageView(p-1): null; if(pv) canvas = pv.canvas || (pv.div? pv.div.querySelector('canvas'): null); if(!canvas){ var sel = document.querySelector('#viewer .page[data-page-number=' + p + '] canvas'); if(sel) canvas = sel; } if(canvas) break; if(Date.now()-start>2000) break; } if(canvas) return canvas.toDataURL('image/png'); return ''; }catch(e){return '';} })();";

                        var result = await PdfEmbedWebView.EvaluateJavaScriptAsync(js);
                        if (!string.IsNullOrEmpty(result) && result.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
                        {
                            var comma = result.IndexOf(',');
                            var base64 = comma >= 0 ? result.Substring(comma + 1) : result;
                            try
                            {
                                var bytes = Convert.FromBase64String(base64);
                                MainThread.BeginInvokeOnMainThread(() =>
                                {
                                    try
                                    {
                                        PdfEmbedWebView.IsVisible = false;
                                        var img = this.FindByName<Image>("PdfPageImage");
                                        if (img != null)
                                        {
                                            img.IsVisible = true;
                                            img.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.WriteLine($"SetPdfPageInWebView UI set error: {ex}");
                                    }
                                });
                                _currentPdfPage = page;
                                PageNumberEntry.Text = _currentPdfPage.ToString();
                                return;
                            }
                            catch (FormatException) { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SetPdfPageInWebView JS evaluation error: {ex}");
                }

                // Fallback: navigate to fragment URL on the PDF file or viewer
                PdfEmbedWebView.IsVisible = true;
                var fallbackUrl = baseUrl + "#page=" + page;
                PdfEmbedWebView.Source = new UrlWebViewSource { Url = fallbackUrl };

                _currentPdfPage = page;
                PageNumberEntry.Text = _currentPdfPage.ToString();

                // Notify CardManager if it has page selection callback
                try
                {
                    var setPageMethod = _cardManager?.GetType().GetMethod("OnSelectPageForImageFill");
                    var setDefaultMethod = _cardManager?.GetType().GetMethod("SetDefaultMaterial");
                    if (setDefaultMethod != null)
                    {
                        var material = new Flashnote.Models.DefaultMaterial { isPDF = true, fileName = Path.GetFileName(_currentPdfPath) };
                        var task = (Task)setDefaultMethod.Invoke(_cardManager, new object[] { material });
#pragma warning disable CS4014
                        _ = task; // fire-and-forget
#pragma warning restore CS4014
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SetPdfPageInWebView callback error: {ex}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SetPdfPageInWebView error: {ex}");
            }
        }

        private async Task TryLoadDefaultMaterialFromMetadata()
        {
            try
            {
                if (string.IsNullOrEmpty(_tempExtractPath)) return;

                var metaPath = Path.Combine(_tempExtractPath, "metadata.json");
                if (!File.Exists(metaPath))
                {
                    Debug.WriteLine($"metadata.json が見つかりません: {metaPath}");
                    return;
                }

                string json = await File.ReadAllTextAsync(metaPath);
                if (string.IsNullOrEmpty(json))
                {
                    Debug.WriteLine("metadata.json が空です");
                    return;
                }

                try
                {
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("defaultMaterial", out var dm))
                    {
                        bool isPdf = false;
                        string fileName = null;
                        try { if (dm.TryGetProperty("isPDF", out var p)) isPdf = p.GetBoolean(); } catch { }
                        try { if (dm.TryGetProperty("isPdf", out var p2)) isPdf = p2.GetBoolean(); } catch { }
                        try { if (dm.TryGetProperty("fileName", out var f)) fileName = f.GetString(); } catch { }

                        if (isPdf && !string.IsNullOrEmpty(fileName))
                        {
                            // 検索候補のパス
                            var candidates = new List<string>();

                            // 1) tempExtractPath/material/<fileName>
                            candidates.Add(Path.Combine(_tempExtractPath, "material", fileName));
                            candidates.Add(Path.Combine(_tempExtractPath, "materials", fileName));

                            // 2) _noteBaseDir/materials/<fileName>
                            if (!string.IsNullOrEmpty(_noteBaseDir))
                                candidates.Add(Path.Combine(_noteBaseDir, "materials", fileName));

                            // 3) LocalApplicationData/Flashnote/materials/<fileName>
                            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Flashnote", "materials", fileName));

                            // 4) Direct path under tempExtractPath
                            candidates.Add(Path.Combine(_tempExtractPath, fileName));

                            string found = null;
                            foreach (var c in candidates)
                            {
                                try
                                {
                                    if (!string.IsNullOrEmpty(c) && File.Exists(c))
                                    {
                                        found = c;
                                        Debug.WriteLine($"defaultMaterial を発見: {found}");
                                        break;
                                    }
                                }
                                catch { }
                            }

                            if (!string.IsNullOrEmpty(found))
                            {
                                // 表示
                                try
                                {
                                    ShowEmbeddedPdf(found);
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"ShowEmbeddedPdf エラー: {ex.Message}");
                                }

                                // CardManagerにも既定素材を設定しておく
                                try
                                {
                                    if (_cardManager != null)
                                    {
                                        var material = new Flashnote.Models.DefaultMaterial { isPDF = true, fileName = Path.GetFileName(found) };
                                        await _cardManager.SetDefaultMaterial(material);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"CardManager.SetDefaultMaterial エラー: {ex.Message}");
                                }
                            }
                            else
                            {
                                Debug.WriteLine($"defaultMaterial が見つかりませんでした: {fileName}");
                            }
                        }
                    }
                    else
                    {
                        Debug.WriteLine("metadata.json に defaultMaterial が含まれていません");
                    }
                }
                catch (JsonException jex)
                {
                    Debug.WriteLine($"metadata.json の解析エラー: {jex.Message}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TryLoadDefaultMaterialFromMetadata エラー: {ex.Message}");
            }
        }
     }
 }