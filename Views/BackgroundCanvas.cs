using Microsoft.Maui.Controls;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;
using PdfiumViewer;
using System.Drawing;
using SizeF = System.Drawing.SizeF;
using Size = Microsoft.Maui.Graphics.Size;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Linq;
using System.Text.Json;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Flashnote.Views
{
    /// <summary>
    /// PDF/画像表示専用のバックグラウンドキャンバス
    /// 描画機能は持たず、コンテンツ表示のみを担当
    /// </summary>
    public class BackgroundCanvas : SKCanvasView, IDisposable
    {
        private const float BASE_CANVAS_WIDTH = 600f;
        private const float MIN_SCALE = 0.5f;
        private const float MAX_SCALE = 3.0f;
        private const float RENDER_DPI = 150f;  // 高画質レンダリング（150dpi）のみ使用
        private const int VISIBLE_PAGE_BUFFER = 1;
        private const int MAX_CACHED_PAGES = 1;
        private const int CACHE_CLEANUP_THRESHOLD = 2;
        private const float SCALE_THRESHOLD = 1.5f;

        // PDF関連
        private PdfDocument _pdfDocument;
        private Stream _pdfStream;
        private List<SKBitmap> _pdfPages;
        private SizeF _pageSize;
        private float _totalHeight;
        private int _currentPage;
        private string _currentPdfPath;
        private string _filename;

        // 画像関連
        private SKBitmap _backgroundImage;

        // 表示制御
        private float _currentScale = 1.0f;
        private SKMatrix _transformMatrix = SKMatrix.CreateIdentity();
        private ScrollView _parentScrollView;
        private List<PageCanvas> _pageCanvases;
        private HashSet<int> _loadingPages;
        private int _currentVisiblePage;

        // キャッシュ管理
        private string _tempDirectory;
        private const string PAGE_CACHE_DIR = "PageCache";
        
        // 並列処理制御
        private readonly SemaphoreSlim _renderSemaphore = new SemaphoreSlim(1, 1); // 最大1つの並列処理
        private readonly object _fileAccessLock = new object(); // ファイルアクセス排他制御
        
        // 破棄状態管理
        private bool _isDisposed = false;
        
        // スクロール停止検出用タイマー
        private System.Timers.Timer _scrollStopTimer;
        private const int SCROLL_STOP_DELAY = 300; // 300ms後にスクロール停止と判定
        
        // ページ選択モード関連
        private bool _isPageSelectionMode = false;
        private int _selectedPageIndex = -1;

        /// <summary>
        /// PDFのページ数を取得
        /// </summary>
        public int PageCount { get; private set; }

        /// <summary>
        /// コンテンツが表示されているかを取得
        /// </summary>
        public bool HasContent => _pageCanvases.Count > 0 || _backgroundImage != null;

        /// <summary>
        /// 現在読み込まれている画像のパスを取得
        /// </summary>
        public string CurrentImagePath { get; private set; }

        /// <summary>
        /// 現在のスクロール位置Y
        /// </summary>
        public double ScrollY => _parentScrollView?.ScrollY ?? 0;

        /// <summary>
        /// キャンバスの高さ
        /// </summary>
        public double CanvasHeight => Height;

        /// <summary>
        /// ページ選択モードを有効にする
        /// </summary>
        public void EnablePageSelectionMode()
        {
            _isPageSelectionMode = true;
            _selectedPageIndex = GetCurrentPageIndex();
            InvalidateSurface();
            Debug.WriteLine($"ページ選択モード有効化 - 選択ページ: {_selectedPageIndex}");
        }

        /// <summary>
        /// ページ選択モードを無効にする
        /// </summary>
        public void DisablePageSelectionMode()
        {
            _isPageSelectionMode = false;
            _selectedPageIndex = -1;
            InvalidateSurface();
            Debug.WriteLine("ページ選択モード無効化");
        }

        /// <summary>
        /// 現在のページインデックスを取得
        /// </summary>
        public int GetCurrentPageIndex()
        {
            if (_pageCanvases.Count == 0) return 0;

            var scrollY = _parentScrollView?.ScrollY ?? 0;
            var scaledScrollY = scrollY / _currentScale;
            var viewportHeight = (_parentScrollView?.Height ?? Height) / _currentScale;
            var viewportTop = scaledScrollY;
            var viewportBottom = scaledScrollY + viewportHeight;
            var viewportCenter = scaledScrollY + viewportHeight / 2;

            // 最初のページ：ビューポートの上端がページの上半分に入ったら選択
            var firstPage = _pageCanvases[0];
            if (firstPage != null && viewportTop <= firstPage.Y + firstPage.Height / 2)
            {
                // Debug.WriteLine($"最初のページ選択: viewportTop={viewportTop:F1}, pageMiddle={firstPage.Y + firstPage.Height / 2:F1}");
                return 0;
            }

            // 最後のページ：ビューポートの下端がページの下半分に入ったら選択
            var lastPageIndex = _pageCanvases.Count - 1;
            var lastPage = _pageCanvases[lastPageIndex];
            if (lastPage != null && viewportBottom >= lastPage.Y + lastPage.Height / 2)
            {
                // Debug.WriteLine($"最後のページ選択: viewportBottom={viewportBottom:F1}, pageMiddle={lastPage.Y + lastPage.Height / 2:F1}");
                return lastPageIndex;
            }

            // 中間のページ：ビューポート中央がページ内にあるかをチェック
            for (int i = 1; i < _pageCanvases.Count - 1; i++)
            {
                var pageCanvas = _pageCanvases[i];
                if (pageCanvas != null)
                {
                    var pageTop = pageCanvas.Y;
                    var pageBottom = pageCanvas.Y + pageCanvas.Height;
                    
                    if (viewportCenter >= pageTop && viewportCenter <= pageBottom)
                    {
                        // Debug.WriteLine($"中間ページ{i + 1}選択: viewportCenter={viewportCenter:F1}, pageTop={pageTop:F1}, pageBottom={pageBottom:F1}");
                        return i;
                    }
                }
            }

            // フォールバック：最も近いページを返す
            var closestPage = 0;
            var minDistance = float.MaxValue;
            
            for (int i = 0; i < _pageCanvases.Count; i++)
            {
                var pageCanvas = _pageCanvases[i];
                if (pageCanvas != null)
                {
                    var pageCenter = pageCanvas.Y + pageCanvas.Height / 2;
                    var distance = Math.Abs(viewportCenter - pageCenter);
                    
                    if (distance < minDistance)
                    {
                        minDistance = (float)distance;
                        closestPage = i;
                    }
                }
            }

            return closestPage;
        }

        /// <summary>
        /// スクロール位置に応じて選択ページを更新
        /// </summary>
        public void UpdateSelectedPage()
        {
            if (_isPageSelectionMode)
            {
                var newSelectedPage = GetCurrentPageIndex();
                if (newSelectedPage != _selectedPageIndex)
                {
                    _selectedPageIndex = newSelectedPage;
                    InvalidateSurface();
                    Debug.WriteLine($"選択ページ更新: {_selectedPageIndex}");
                }
            }
        }

        /// <summary>
        /// 現在のズーム倍率を取得または設定
        /// </summary>
        public float CurrentScale 
        { 
            get => _currentScale; 
            set 
            {
                if (value >= MIN_SCALE && value <= MAX_SCALE && _currentScale != value)
                {
                    _currentScale = value;
                    UpdateTransformMatrix();
                    UpdateCanvasSize();
                    InvalidateSurface();
                    Debug.WriteLine($"ズーム倍率変更: {_currentScale:F2}");
                }
            }
        }

        /// <summary>
        /// 変換マトリックスを更新
        /// </summary>
        private void UpdateTransformMatrix()
        {
            _transformMatrix = SKMatrix.CreateScale(_currentScale, _currentScale);
        }

        /// <summary>
        /// キャンバスサイズをズーム倍率に応じて更新
        /// </summary>
        private void UpdateCanvasSize()
        {
            if (_backgroundImage != null)
            {
                var aspectRatio = (float)_backgroundImage.Height / _backgroundImage.Width;
                var baseWidth = BASE_CANVAS_WIDTH;
                var baseHeight = baseWidth * aspectRatio;
                
                // 基本サイズに拡大倍率を適用
                WidthRequest = baseWidth * _currentScale;
                HeightRequest = baseHeight * _currentScale;
                
                Debug.WriteLine($"画像キャンバスサイズ更新: {WidthRequest}x{HeightRequest} (スケール: {_currentScale})");
            }
            else if (_pageCanvases.Count > 0)
            {
                // PDFの場合：基本サイズに拡大倍率を適用
                WidthRequest = BASE_CANVAS_WIDTH * _currentScale;
                HeightRequest = _totalHeight * _currentScale;
                
                Debug.WriteLine($"PDFキャンバスサイズ更新: {WidthRequest}x{HeightRequest} (総高さ: {_totalHeight}, スケール: {_currentScale})");
            }
            else
            {
                // デフォルト値
                WidthRequest = BASE_CANVAS_WIDTH * _currentScale;
                HeightRequest = BASE_CANVAS_WIDTH * (4.0f / 3.0f) * _currentScale;
                
                Debug.WriteLine($"デフォルトキャンバスサイズ更新: {WidthRequest}x{HeightRequest} (スケール: {_currentScale})");
            }
            
            UpdateScrollViewHeight();
        }

        /// <summary>
        /// SKBitmapが有効かどうかをチェック
        /// </summary>
        private bool IsValidBitmap(SKBitmap bitmap)
        {
            if (bitmap == null || _isDisposed) 
            {
                return false;
            }
            
            try
            {
                // まず最初にHandle経由の低レベルチェック
                if (bitmap.Handle == IntPtr.Zero)
                {
                    Debug.WriteLine("ビットマップハンドルが無効");
                    return false;
                }

                // IsEmptyを最初にチェック（より軽い操作）
                if (bitmap.IsEmpty) 
                {
                    return false;
                }
                
                // Width と Height のプロパティアクセスを個別に保護
                int width = 0, height = 0;
                bool hasValidSize = false;
                
                try
                {
                    width = bitmap.Width;
                    height = bitmap.Height;
                    hasValidSize = true;
                }
                catch (AccessViolationException)
                {
                    Debug.WriteLine("ビットマップサイズアクセスでAVE発生");
                    return false;
                }
                catch (ObjectDisposedException)
                {
                    Debug.WriteLine("破棄されたビットマップを検出");
                    return false;
                }
                catch (System.ExecutionEngineException)
                {
                    Debug.WriteLine("ビットマップサイズアクセスでEE発生");
                    return false;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ビットマップサイズアクセスエラー: {ex.GetType().Name}");
                    return false;
                }
                
                if (!hasValidSize || width <= 0 || height <= 0)
                {
                    return false;
                }

                // 最大サイズチェック（メモリ保護）
                if (width > 8192 || height > 8192)
                {
                    Debug.WriteLine($"ビットマップサイズが大きすぎます: {width}x{height}");
                    return false;
                }
                
                // ColorTypeのチェック（オプション）
                try
                {
                    var colorType = bitmap.ColorType;
                    if (colorType == SKColorType.Unknown)
                    {
                        Debug.WriteLine("不明なColorType");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ColorTypeアクセスエラー: {ex.GetType().Name}");
                    // ColorTypeが取得できない場合は危険と判定
                    return false;
                }

                // 追加の安全性チェック - Info構造体アクセス
                try
                {
                    var info = bitmap.Info;
                    if (info.Width != width || info.Height != height)
                    {
                        Debug.WriteLine("ビットマップ情報の不整合");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ビットマップInfo取得エラー: {ex.GetType().Name}");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"IsValidBitmap全体エラー: {ex.GetType().Name} - {ex.Message}");
                return false;
            }
        }

        // ページキャンバス管理用クラス
        public class PageCanvas
        {
            public int PageIndex { get; set; }
            public float Width { get; set; }
            public float Height { get; set; }
            public float Y { get; set; }
            public bool IsHighQuality { get; set; }
            public bool NeedsUpdate { get; set; }

            public void Dispose()
            {
                // メモリビットマップは保持しないため、破棄不要
                // ディスクキャッシュファイルは自動管理される
            }
        }

        // コンテンツ表示用のデータ構造
        public class ContentData
        {
            public string PdfFilePath { get; set; }
            public string ImageFilePath { get; set; }
            public double LastScrollY { get; set; }
        }

        public BackgroundCanvas()
        {
            EnableTouchEvents = false; // 背景は入力を受け付けない
            IgnorePixelScaling = true;
            _pageCanvases = new List<PageCanvas>();
            _pdfPages = new List<SKBitmap>();
            _loadingPages = new HashSet<int>();
            
            // スクロール停止検出タイマーの初期化
            _scrollStopTimer = new System.Timers.Timer(SCROLL_STOP_DELAY);
            _scrollStopTimer.Elapsed += OnScrollStopped;
            _scrollStopTimer.AutoReset = false; // 一回だけ実行
            
            // 一時ディレクトリの初期化を遅延実行
            _ = Task.Run(() => InitializeTempDirectoryAsync());
        }

        private async Task InitializeTempDirectoryAsync()
        {
            try
            {
                var tempPath = Path.GetTempPath();
                _tempDirectory = Path.Combine(tempPath, "Flashnote", "Temp");
                
                if (!Directory.Exists(_tempDirectory))
                {
                    Directory.CreateDirectory(_tempDirectory);
                }
                
                var cacheDir = Path.Combine(_tempDirectory, PAGE_CACHE_DIR);
                if (!Directory.Exists(cacheDir))
                {
                    Directory.CreateDirectory(cacheDir);
                }
                
                Debug.WriteLine($"一時ディレクトリ初期化完了: {_tempDirectory}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"一時ディレクトリ初期化エラー: {ex.Message}");
            }
        }

        public ScrollView ParentScrollView
        {
            get => _parentScrollView;
            set
            {
                if (_parentScrollView != null)
                    _parentScrollView.Scrolled -= OnScrollViewScrolled;

                _parentScrollView = value;

                if (_parentScrollView != null)
                    _parentScrollView.Scrolled += OnScrollViewScrolled;
            }
        }

        private async void OnScrollViewScrolled(object sender, ScrolledEventArgs e)
        {
            if (_pdfDocument != null)
            {
                await UpdateVisiblePagesAsync(e.ScrollY);
                
                // スクロール停止検出タイマーをリセット
                _scrollStopTimer?.Stop();
                _scrollStopTimer?.Start();
            }
        }
        
        /// <summary>
        /// スクロール停止時の処理
        /// </summary>
        private async void OnScrollStopped(object sender, System.Timers.ElapsedEventArgs e)
        {
            try
            {
                Debug.WriteLine("スクロール停止を検出 - ページの再描画を実行");
                
                // UIスレッドで実行
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    if (_pdfDocument != null && _parentScrollView != null)
                    {
                        // 現在のスクロール位置で再度ページ更新
                        await UpdateVisiblePagesAsync(_parentScrollView.ScrollY);
                        
                        // 画面を再描画
                        InvalidateSurface();
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"スクロール停止処理エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// PDFファイルを読み込んで表示
        /// </summary>
        public async Task LoadPdfAsync(string filePath)
        {
            try
            {
                ClearContent();
                _currentPdfPath = filePath;

                Debug.WriteLine($"PDF読み込み開始: {filePath}");

                using (var fileStream = File.OpenRead(filePath))
                {
                    _pdfStream = new MemoryStream();
                    await fileStream.CopyToAsync(_pdfStream);
                    _pdfStream.Position = 0;
                }

                _pdfDocument = PdfDocument.Load(_pdfStream);
                _pageSize = _pdfDocument.PageSizes[0];
                
                // PageCountを設定
                PageCount = _pdfDocument.PageCount;
                Debug.WriteLine($"PDFページ数設定: {PageCount}");

                await InitializePdfPagesAsync();
                UpdateScrollViewHeight();
                InvalidateSurface();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PDF読み込みエラー: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 画像ファイルを読み込んで表示
        /// </summary>
        public async Task LoadImageAsync(string filePath)
        {
            try
            {
                ClearContent();
                
                // 高品質画像読み込み
                using (var stream = File.OpenRead(filePath))
                {
                    // デコード時に高品質設定を適用
                    var codec = SKCodec.Create(stream);
                    if (codec != null)
                    {
                        var info = codec.Info;
                        // 元の画像サイズを保持して高品質でデコード
                        _backgroundImage = SKBitmap.Decode(codec, info);
                        codec.Dispose();
                        
                        Debug.WriteLine($"画像読み込み完了: {info.Width}x{info.Height}, カラータイプ: {info.ColorType}");
                    }
                    else
                    {
                        // フォールバック：従来の方法
                        stream.Position = 0;
                    _backgroundImage = SKBitmap.Decode(stream);
                    }
                }
                
                if (_backgroundImage != null)
                {
                    CurrentImagePath = filePath; // 画像パスを保存
                    Debug.WriteLine($"画像設定完了: {_backgroundImage.Width}x{_backgroundImage.Height}");
                    UpdateCanvasSize();
                    InvalidateSurface();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"画像読み込みエラー: {ex.Message}");
                throw;
            }
        }

        private async Task InitializePdfPagesAsync()
        {
            if (_pdfDocument == null) return;

            _totalHeight = 0;
            _pageCanvases.Clear();

            for (int i = 0; i < _pdfDocument.PageCount; i++)
            {
                var pageCanvas = new PageCanvas
                {
                    Width = BASE_CANVAS_WIDTH,
                    Height = BASE_CANVAS_WIDTH * (_pageSize.Height / _pageSize.Width),
                    Y = _totalHeight,
                    PageIndex = i,
                    IsHighQuality = false,
                    NeedsUpdate = true
                };

                _pageCanvases.Add(pageCanvas);
                _totalHeight += pageCanvas.Height;

                // 最初の数ページのみを先読み
                if (i < VISIBLE_PAGE_BUFFER)
                {
                    await LoadPdfPageAsync(i, RENDER_DPI);
                }
            }

            UpdateCanvasSize();
            
            Debug.WriteLine($"PDF初期化完了: ページ数 {_pageCanvases.Count}, 総高さ {_totalHeight}, 一時ディレクトリ {_tempDirectory}");
        }

        private async Task LoadPdfPageAsync(int pageIndex, float dpi)
        {
            if (_pdfDocument == null || pageIndex >= _pdfDocument.PageCount || _loadingPages.Contains(pageIndex))
                return;

            _loadingPages.Add(pageIndex);

            // セマフォで並列処理を制御
            await _renderSemaphore.WaitAsync();

            try
            {
                // キャッシュファイルのパスを生成
                var cacheFileName = $"page_{pageIndex}_{(int)dpi}.png";
                var cacheFilePath = Path.Combine(_tempDirectory, PAGE_CACHE_DIR, cacheFileName);
                
                // キャッシュファイルが存在するかチェック
                if (File.Exists(cacheFilePath))
                {
                    Debug.WriteLine($"ページ {pageIndex}: 既存キャッシュから読み込み {cacheFilePath}");
                    // キャッシュファイルから読み込み
                    await LoadPageFromCacheAsync(pageIndex, cacheFilePath, dpi);
                    return;
                }

                // キャッシュが存在しない場合は新規作成（UIスレッドで実行）
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    try
                    {
                        // PDFレンダリング（UIスレッドで実行）
                        var page = _pdfDocument.Render(pageIndex, (int)(_pageSize.Width * dpi / 72), (int)(_pageSize.Height * dpi / 72), true);
                        if (page != null)
                        {
                            // System.Drawing.Bitmapの作成と保存（同期的に実行）
                            using (var bitmap = new System.Drawing.Bitmap(page))
                            {
                                // ディスクキャッシュに保存（UIスレッドで実行）
                                await SavePageToCacheAsync(bitmap, cacheFilePath);
                                
                                // キャッシュから読み込み直す
                                await LoadPageFromCacheAsync(pageIndex, cacheFilePath, dpi);
                            }
                            page.Dispose();
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"PDFページレンダリングエラー (Page {pageIndex}): {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PDFページ読み込みエラー (Page {pageIndex}): {ex.Message}");
            }
            finally
            {
                _renderSemaphore.Release();
                _loadingPages.Remove(pageIndex);
            }
        }

        /// <summary>
        /// 画像をディスクキャッシュに保存（UIスレッドで実行）
        /// </summary>
        private async Task SavePageToCacheAsync(System.Drawing.Bitmap bitmap, string cacheFilePath)
        {
            try
            {
                // キャッシュディレクトリが存在しない場合は作成
                var cacheDir = Path.GetDirectoryName(cacheFilePath);
                lock (_fileAccessLock)
                {
                    if (!Directory.Exists(cacheDir))
                    {
                        Directory.CreateDirectory(cacheDir);
                    }
                }

                // 一時ファイルに保存してからリネーム（原子的操作）
                var tempFilePath = cacheFilePath + ".tmp";
                
                await Task.Run(() =>
                {
                    // Bitmapのクローンを作成してスレッドセーフにする
                    using (var clonedBitmap = new System.Drawing.Bitmap(bitmap))
                    {
                        // 高品質PNG保存設定
                        var encoderParams = new System.Drawing.Imaging.EncoderParameters(1);
                        var qualityParam = new System.Drawing.Imaging.EncoderParameter(
                            System.Drawing.Imaging.Encoder.Quality, 100L); // 最高品質
                        encoderParams.Param[0] = qualityParam;
                        
                        // PNG用のImageCodecInfoを取得
                        var pngCodec = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders()
                            .FirstOrDefault(codec => codec.FormatID == System.Drawing.Imaging.ImageFormat.Png.Guid);
                        
                        if (pngCodec != null)
                        {
                            clonedBitmap.Save(tempFilePath, pngCodec, encoderParams);
                        }
                        else
                    {
                        clonedBitmap.Save(tempFilePath, System.Drawing.Imaging.ImageFormat.Png);
                        }
                        
                        encoderParams.Dispose();
                    }
                });

                // 原子的にファイルを移動（排他制御）
                lock (_fileAccessLock)
                {
                    if (File.Exists(tempFilePath))
                    {
                        if (File.Exists(cacheFilePath))
                        {
                            File.Delete(cacheFilePath);
                        }
                        File.Move(tempFilePath, cacheFilePath);
                        Debug.WriteLine($"ページキャッシュを保存: {cacheFilePath}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"キャッシュ保存エラー: {ex.Message}");
                // 一時ファイルをクリーンアップ
                try
                {
                    var tempFilePath = cacheFilePath + ".tmp";
                    lock (_fileAccessLock)
                    {
                        if (File.Exists(tempFilePath))
                        {
                            File.Delete(tempFilePath);
                        }
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// キャッシュファイルから画像を読み込み
        /// </summary>
        private async Task LoadPageFromCacheAsync(int pageIndex, string cacheFilePath, float dpi)
        {
            try
            {
                // ファイル存在チェック（排他制御）
                bool fileExists;
                lock (_fileAccessLock)
                {
                    fileExists = File.Exists(cacheFilePath);
                }

                if (!fileExists)
                {
                    Debug.WriteLine($"キャッシュファイルが存在しません: {cacheFilePath}");
                    return;
                }

                // ファイルから直接SKBitmapを作成（メモリ効率的）
                var skBitmap = await Task.Run(() =>
                {
                    try
                    {
                        // ファイルアクセスを排他制御
                        lock (_fileAccessLock)
                        {
                            return SKBitmap.Decode(cacheFilePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"キャッシュファイル読み込みエラー: {ex.Message}");
                        return null;
                    }
                });

                if (skBitmap != null && IsValidBitmap(skBitmap))
                {
                    SetPageBitmap(pageIndex, skBitmap, dpi);
                    Debug.WriteLine($"キャッシュから読み込み完了: {cacheFilePath}");
                }
                else
                {
                    Debug.WriteLine($"キャッシュファイルの読み込みに失敗: {cacheFilePath}");
                    // キャッシュファイルが破損している可能性があるため削除
                    try
                    {
                        lock (_fileAccessLock)
                        {
                            if (File.Exists(cacheFilePath))
                            {
                                File.Delete(cacheFilePath);
                            }
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"キャッシュ読み込みエラー: {ex.Message}");
            }
        }

        /// <summary>
        /// ページビットマップ設定（ディスクキャッシュのみ使用）
        /// </summary>
        private void SetPageBitmap(int pageIndex, SKBitmap bitmap, float dpi)
        {
            try
            {
                if (pageIndex < _pageCanvases.Count && bitmap != null)
                {
                    lock (_pageCanvases)
                    {
                        var pageCanvas = _pageCanvases[pageIndex];
                        
                        // メモリには保存せず、ディスクキャッシュのみ使用
                        pageCanvas.IsHighQuality = true; // 150dpiのみなので常に高画質
                        pageCanvas.NeedsUpdate = false;
                        
                        Debug.WriteLine($"ページ {pageIndex} ディスクキャッシュのみ使用（150dpi）");
                    }

                    // ビットマップは即座に破棄してメモリを解放
                    bitmap.Dispose();

                    // UIスレッドで描画を更新
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        InvalidateSurface();
                    });
                }
                else
                {
                    Debug.WriteLine($"無効なページビットマップ: pageIndex={pageIndex}, bitmap={bitmap}");
                    bitmap?.Dispose(); // 無効なビットマップは破棄
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ページビットマップ設定エラー: {ex.Message}");
                bitmap?.Dispose();
            }
        }

        private async Task UpdateVisiblePagesAsync(double scrollY)
        {
            if (_pageCanvases.Count == 0) return;

            var visibleHeight = ParentScrollView?.Height ?? 0;
            var visibleStart = (float)scrollY;
            var visibleEnd = visibleStart + (float)visibleHeight;
            
            // 先読みバッファを追加（画面の高さの半分だけ下側に拡張）
            var preloadBuffer = (float)visibleHeight * 0.5f;
            var preloadStart = visibleStart - preloadBuffer;
            var preloadEnd = visibleEnd + preloadBuffer;

            // 可視領域+先読み領域内のページを読み込み
            for (int i = 0; i < _pageCanvases.Count; i++)
            {
                var pageCanvas = _pageCanvases[i];
                // 先読み領域の判定
                if (pageCanvas.Y <= preloadEnd && pageCanvas.Y + pageCanvas.Height >= preloadStart)
                {
                    // 高画質キャッシュが存在しない場合のみ読み込み
                    var cacheFileName = $"page_{i}_{(int)RENDER_DPI}.png";
                    var cacheFilePath = Path.Combine(_tempDirectory, PAGE_CACHE_DIR, cacheFileName);
                    
                    if (!File.Exists(cacheFilePath) && !_loadingPages.Contains(i))
                    {
                        await LoadPdfPageAsync(i, RENDER_DPI);
                    }
                        }
                    }
                }



        protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
        {
            if (_isDisposed) return;
            
            // UIスレッドでの実行を確保
            if (!MainThread.IsMainThread)
            {
                Debug.WriteLine("OnPaintSurface: UIスレッド外で呼び出されました");
                return;
            }

            var canvas = e.Surface.Canvas;
            
            try
            {
                canvas.Clear(SKColors.White);

                if (_backgroundImage != null && IsValidBitmap(_backgroundImage))
                {
                    // 単一画像の高品質描画（拡大されたサイズで描画）
                    try
                    {
                        var aspectRatio = (float)_backgroundImage.Height / _backgroundImage.Width;
                        var baseWidth = BASE_CANVAS_WIDTH;
                        var baseHeight = baseWidth * aspectRatio;
                        var destRect = new SKRect(0, 0, baseWidth * _currentScale, baseHeight * _currentScale);
                        
                        // 高品質描画用のペイントを設定
                        using (var paint = new SKPaint())
                        {
                            paint.IsAntialias = true;
                            paint.FilterQuality = SKFilterQuality.High; // 高品質フィルタリング
                            canvas.DrawBitmap(_backgroundImage, destRect, paint);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"背景画像描画エラー: {ex.GetType().Name} - {ex.Message}");
                        _backgroundImage?.Dispose();
                        _backgroundImage = null;
                    }
                }
                else if (_pageCanvases.Count > 0)
                {
                    // 可視領域の計算（バッファ領域を含む）
                    var scrollY = _parentScrollView?.ScrollY ?? 0;
                    var viewportHeight = _parentScrollView?.Height ?? Height;
                    var scaledScrollY = scrollY / _currentScale;
                    var scaledViewportHeight = viewportHeight / _currentScale;
                    
                    // バッファ領域を追加（上下に1画面分の余裕）
                    var bufferSize = scaledViewportHeight;
                    var visibleStart = Math.Max(0, scaledScrollY - bufferSize);
                    var visibleEnd = scaledScrollY + scaledViewportHeight + bufferSize;
                    
                    // 可視領域+バッファ領域のページのみを描画（メモリ効率的）
                    List<PageCanvas> canvasesToDraw;
                    
                    // lockの中で最小限の処理のみ実行
                    lock (_pageCanvases)
                    {
                        canvasesToDraw = _pageCanvases.Where(pc => pc != null && 
                            // ページが可視領域+バッファと重なるかチェック
                            pc.Y + pc.Height >= visibleStart && 
                            pc.Y <= visibleEnd).ToList();
                    }

                    // 描画は lock の外で実行 - 可視領域のページのみを描画
                    Debug.WriteLine($"可視領域描画: スクロール位置={scrollY:F1}, 表示高さ={viewportHeight:F1}, 描画対象ページ数: {canvasesToDraw.Count}/{_pageCanvases.Count}");
                    
                    int drawnPages = 0;
                    foreach (var pageCanvas in canvasesToDraw)
                    {
                        try
                        {
                            // 150dpi高画質ファイルを優先、後方互換性のため旧ファイルもチェック
                            var currentDpiFile = Path.Combine(_tempDirectory, PAGE_CACHE_DIR, $"page_{pageCanvas.PageIndex}_{(int)RENDER_DPI}.png");
                            var oldHighDpiFile = Path.Combine(_tempDirectory, PAGE_CACHE_DIR, $"page_{pageCanvas.PageIndex}_72.png");
                            var oldLowDpiFile = Path.Combine(_tempDirectory, PAGE_CACHE_DIR, $"page_{pageCanvas.PageIndex}_36.png");
                            
                            string cacheFileToUse = null;
                            string qualityType = "";
                            
                            // 150dpi (最高画質)
                            if (File.Exists(currentDpiFile))
                            {
                                cacheFileToUse = currentDpiFile;
                                qualityType = "150dpi";
                            }
                            // 72dpi (旧互換性)
                            else if (File.Exists(oldHighDpiFile))
                            {
                                cacheFileToUse = oldHighDpiFile;
                                qualityType = "72dpi(旧)";
                            }
                            // 36dpi (旧互換性)
                            else if (File.Exists(oldLowDpiFile))
                            {
                                cacheFileToUse = oldLowDpiFile;
                                qualityType = "36dpi(旧)";
                            }
                            
                            // キャッシュファイルが存在する場合のみ描画
                            if (cacheFileToUse != null)
                            {
                                // 一時的にビットマップを作成（すぐに破棄）
                                using (var tempBitmap = SKBitmap.Decode(cacheFileToUse))
                                {
                                    if (tempBitmap != null && IsValidBitmap(tempBitmap))
                                    {
                                        // 拡大されたサイズで高品質描画
                                        var baseWidth = BASE_CANVAS_WIDTH * _currentScale;
                                        var baseHeight = pageCanvas.Height * _currentScale;
                                        var destRect = new SKRect(0, pageCanvas.Y * _currentScale, baseWidth, (pageCanvas.Y * _currentScale) + baseHeight);
                                        
                                        // 高品質描画用のペイントを設定
                                        using (var paint = new SKPaint())
                                        {
                                            paint.IsAntialias = true;
                                            paint.FilterQuality = SKFilterQuality.High; // 高品質フィルタリング
                                            canvas.DrawBitmap(tempBitmap, destRect, paint);
                                        }
                                        
                                        drawnPages++;
                                        // Debug.WriteLine($"ページ {pageCanvas.PageIndex} を描画 ({qualityType})");
                                    }
                                    else
                                    {
                                        Debug.WriteLine($"ページ {pageCanvas.PageIndex}: ビットマップが無効");
                                    }
                                }
                            }
                            else
                            {
                                Debug.WriteLine($"ページ {pageCanvas.PageIndex}: キャッシュファイルが存在しません");
                                Debug.WriteLine($"  72dpi(旧): {oldHighDpiFile} - 存在? {File.Exists(oldHighDpiFile)}");
                                Debug.WriteLine($"  36dpi(旧): {oldLowDpiFile} - 存在? {File.Exists(oldLowDpiFile)}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"ページ {pageCanvas.PageIndex} の描画エラー: {ex.GetType().Name} - {ex.Message}");
                        }
                    }
                    
                    if (drawnPages > 0)
                    {
                    Debug.WriteLine($"実際に描画されたページ数: {drawnPages}");
                    }
                    
                    // ページ選択モード時に選択されているページに枠を描画
                    if (_isPageSelectionMode && _selectedPageIndex >= 0 && _selectedPageIndex < _pageCanvases.Count)
                    {
                        DrawPageSelectionFrame(canvas, _selectedPageIndex);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OnPaintSurface全体エラー: {ex.GetType().Name} - {ex.Message}");
                // 全体エラーの場合は白い背景のみ表示（何もしない）
            }
        }

        /// <summary>
        /// ページ選択時の枠を描画
        /// </summary>
        private void DrawPageSelectionFrame(SKCanvas canvas, int pageIndex)
        {
            try
            {
                var pageCanvas = _pageCanvases[pageIndex];
                if (pageCanvas == null) return;

                // 枠のペイントを作成
                using (var framePaint = new SKPaint())
                {
                    framePaint.Color = SKColors.Red;
                    framePaint.Style = SKPaintStyle.Stroke;
                    framePaint.StrokeWidth = 4.0f * _currentScale; // スケールに応じて線の太さを調整
                    framePaint.IsAntialias = true;

                    // ページの境界を計算（スケールを適用）
                    var left = 0;
                    var top = pageCanvas.Y * _currentScale;
                    var right = BASE_CANVAS_WIDTH * _currentScale;
                    var bottom = (pageCanvas.Y + pageCanvas.Height) * _currentScale;

                    // 枠を描画
                    var frameRect = new SKRect(left, top, right, bottom);
                    canvas.DrawRect(frameRect, framePaint);

                    // ページ番号のテキストを描画
                    using (var textPaint = new SKPaint())
                    {
                        textPaint.Color = SKColors.Red;
                        textPaint.TextSize = 24.0f * _currentScale;
                        textPaint.IsAntialias = true;
                        textPaint.FakeBoldText = true;
                        textPaint.Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold); // フォントを明示的に指定

                        // 背景の半透明な矩形
                        using (var bgPaint = new SKPaint())
                        {
                            bgPaint.Color = new SKColor(255, 255, 255, 200); // 半透明白
                            
                            var pageText = $"Page {pageIndex + 1}"; // 英語表記に変更
                            var textBounds = new SKRect();
                            textPaint.MeasureText(pageText, ref textBounds);
                            
                            var textX = left + 10 * _currentScale;
                            var textY = top + 30 * _currentScale;
                            
                            // 背景矩形
                            var bgRect = new SKRect(
                                textX - 5 * _currentScale, 
                                textY - textBounds.Height - 5 * _currentScale,
                                textX + textBounds.Width + 10 * _currentScale, 
                                textY + 5 * _currentScale
                            );
                            canvas.DrawRect(bgRect, bgPaint);
                            
                            // テキスト描画
                            canvas.DrawText(pageText, textX, textY, textPaint);
                        }
                    }
                }

                Debug.WriteLine($"ページ選択枠描画: ページ{pageIndex + 1} (Y: {pageCanvas.Y * _currentScale:F1})");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ページ選択枠描画エラー: {ex.Message}");
            }
        }

        private void UpdateScrollViewHeight()
        {
            if (ParentScrollView != null)
            {
                double contentHeight;
                
                if (_backgroundImage != null)
                {
                    // 画像の場合：既にスケールが適用されたHeightRequestを使用
                    contentHeight = HeightRequest;
                }
                else if (_pageCanvases.Count > 0)
                {
                    // PDFの場合：_totalHeightにスケールを適用
                    contentHeight = _totalHeight * _currentScale;
                }
                else
                {
                    // デフォルト値
                    contentHeight = BASE_CANVAS_WIDTH * (4.0 / 3.0) * _currentScale; // 4:3のアスペクト比
                }
                
                // ContentSizeは読み取り専用のため、直接設定せずにHeightRequestで制御
                this.HeightRequest = contentHeight;
                this.WidthRequest = WidthRequest;
                
                Debug.WriteLine($"ScrollView高さ更新: {contentHeight} (スケール: {_currentScale})");
            }
        }

        private void ClearContent()
        {
            Debug.WriteLine("ClearContent開始");
            
            // PDF関連のクリーンアップ
            if (_pdfDocument != null)
            {
                _pdfDocument.Dispose();
                _pdfDocument = null;
                Debug.WriteLine("PDFドキュメントを解放");
            }
            
            if (_pdfStream != null)
            {
                _pdfStream.Dispose();
                _pdfStream = null;
                Debug.WriteLine("PDFストリームを解放");
            }
            
            // PageCountをリセット
            PageCount = 0;

            // PageCanvasのクリーンアップ
            lock (_pageCanvases)
            {
                int pageCanvasCount = _pageCanvases.Count;
                foreach (var pageCanvas in _pageCanvases)
                {
                    pageCanvas?.Dispose();
                }
                _pageCanvases.Clear();
                Debug.WriteLine($"PageCanvasを解放: {pageCanvasCount}個");


            }

            // ローディング状態をクリア
            _loadingPages.Clear();

            // 画像関連のクリーンアップ
            if (_backgroundImage != null)
            {
                _backgroundImage.Dispose();
                _backgroundImage = null;
                Debug.WriteLine("背景画像を解放");
            }

            // その他のプロパティをリセット
            _totalHeight = 0;
            _currentVisiblePage = 0;
            _currentPdfPath = null;
            _filename = null;
            CurrentImagePath = null;
            
            // ガベージコレクションを促進
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            Debug.WriteLine("ClearContent完了");
        }

        public void InitializeCacheDirectory(string noteName, string tempDir)
        {
            // 既に初期化済みの場合はスキップ
            if (!string.IsNullOrEmpty(_tempDirectory) && Directory.Exists(_tempDirectory))
            {
                Debug.WriteLine($"キャッシュディレクトリは既に初期化済み: {_tempDirectory}");
                return;
            }
            
            _tempDirectory = tempDir;
            var cacheDir = Path.Combine(_tempDirectory, PAGE_CACHE_DIR);
            if (!Directory.Exists(cacheDir))
            {
                Directory.CreateDirectory(cacheDir);
            }
            
            Debug.WriteLine($"キャッシュディレクトリ初期化完了: {cacheDir}");
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            
            Debug.WriteLine("BackgroundCanvas Dispose開始");
            
            // スクロール停止タイマーを停止・破棄
            if (_scrollStopTimer != null)
            {
                _scrollStopTimer.Stop();
                _scrollStopTimer.Elapsed -= OnScrollStopped;
                _scrollStopTimer.Dispose();
                _scrollStopTimer = null;
                Debug.WriteLine("スクロール停止タイマーを破棄");
            }
            
            // イベントハンドラーを解除
            if (_parentScrollView != null)
            {
                _parentScrollView.Scrolled -= OnScrollViewScrolled;
                _parentScrollView = null;
                Debug.WriteLine("ScrollViewイベントハンドラーを解除");
            }
            
            // コンテンツをクリア
            ClearContent();
            
            // セマフォを解放
            _renderSemaphore?.Dispose();
            
            Debug.WriteLine("BackgroundCanvas Dispose完了");
        }

        public async Task InitializePdf(byte[] pdfData, string filename)
        {
            try
            {
                _filename = filename;
                
                // キャッシュディレクトリを初期化
                InitializeCacheDirectory();
                
                Debug.WriteLine($"PDFデータサイズ: {pdfData.Length} bytes");
                _pdfDocument?.Dispose();
                _pdfDocument = PdfiumViewer.PdfDocument.Load(new MemoryStream(pdfData));
                
                if (_pdfDocument != null)
                {
                    PageCount = _pdfDocument.PageCount;
                    Debug.WriteLine($"InitializePdf - PDFページ数設定: {PageCount}");
                    
                    // 最初のページのサイズを取得
                    var firstPageSize = _pdfDocument.PageSizes[0];
                    _pageSize = new SizeF((float)firstPageSize.Width, (float)firstPageSize.Height);
                    
                    // 全ページ情報のリストを初期化
                    _pageCanvases.Clear();
                    for (int i = 0; i < PageCount; i++)
                    {
                        _pageCanvases.Add(null);
                    }
                    
                    // キャッシュファイルをクリーンアップ（古いファイルを削除）
                    await CleanupOldCacheFilesAsync();
                    
                    Debug.WriteLine($"PDF初期化完了: {filename}, ページ数: {PageCount}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PDF初期化エラー: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// キャッシュディレクトリを初期化
        /// </summary>
        private void InitializeCacheDirectory()
        {
            try
            {
                var cacheDir = Path.Combine(_tempDirectory, PAGE_CACHE_DIR);
                if (!Directory.Exists(cacheDir))
                {
                    Directory.CreateDirectory(cacheDir);
                    Debug.WriteLine($"キャッシュディレクトリを作成: {cacheDir}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"キャッシュディレクトリ初期化エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 古いキャッシュファイルをクリーンアップ
        /// </summary>
        private async Task CleanupOldCacheFilesAsync()
        {
            try
            {
                var cacheDir = Path.Combine(_tempDirectory, PAGE_CACHE_DIR);
                
                bool dirExists;
                lock (_fileAccessLock)
                {
                    dirExists = Directory.Exists(cacheDir);
                }
                
                if (!dirExists)
                    return;

                await Task.Run(() =>
                {
                    try
                    {
                        string[] files;
                        lock (_fileAccessLock)
                        {
                            files = Directory.GetFiles(cacheDir, "*.png");
                        }
                        
                        var cutoffTime = DateTime.Now.AddDays(-7); // 7日以上古いファイルを削除
                        int deletedCount = 0;
                        int upgradedCount = 0;

                        foreach (var file in files)
                        {
                            try
                            {
                                lock (_fileAccessLock)
                                {
                                    if (File.Exists(file))
                                    {
                                        var fileName = Path.GetFileName(file);
                                        var fileInfo = new FileInfo(file);
                                        
                                        // 古い低DPI（36dpi、72dpi、96dpi）ファイルを削除して150dpiでレンダリングを促進
                                        if (fileName.Contains("_36.png") || fileName.Contains("_72.png") || fileName.Contains("_96.png"))
                                        {
                                            // 対応する150dpiファイルが存在する場合は古いファイルを削除
                                            var pageIndexMatch = System.Text.RegularExpressions.Regex.Match(fileName, @"page_(\d+)_");
                                            if (pageIndexMatch.Success)
                                            {
                                                var pageIndex = pageIndexMatch.Groups[1].Value;
                                                var newHighDpiFile = Path.Combine(cacheDir, $"page_{pageIndex}_150.png");
                                                
                                                if (File.Exists(newHighDpiFile))
                                                {
                                                    File.Delete(file);
                                                    Debug.WriteLine($"古いDPIキャッシュファイルを削除（150dpiファイル存在）: {fileName}");
                                                    upgradedCount++;
                                                    continue;
                                                }
                                            }
                                        }
                                        
                                        // 通常の古いファイル削除（7日以上）
                                        if (fileInfo.LastWriteTime < cutoffTime)
                                        {
                                            File.Delete(file);
                                            Debug.WriteLine($"古いキャッシュファイルを削除（7日経過）: {fileName}");
                                            deletedCount++;
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"ファイル削除エラー: {ex.Message}");
                            }
                        }
                        
                        if (deletedCount > 0 || upgradedCount > 0)
                        {
                            Debug.WriteLine($"キャッシュクリーンアップ完了: 期限切れ削除={deletedCount}, DPI更新削除={upgradedCount}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"キャッシュクリーンアップエラー: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"キャッシュクリーンアップエラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 指定ページの状態をリセット（ディスクキャッシュは残す）
        /// </summary>
        private void UnloadPage(int pageIndex)
        {
            try
            {
                // PageCanvasの状態をリセット
                if (pageIndex >= 0 && pageIndex < _pageCanvases.Count)
                {
                    var pageCanvas = _pageCanvases[pageIndex];
                    pageCanvas.IsHighQuality = false;
                    pageCanvas.NeedsUpdate = true;
                    Debug.WriteLine($"ページ {pageIndex} の状態をリセット");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ページ {pageIndex} リセットエラー: {ex.Message}");
            }
        }

        private async Task UpdateVisiblePages()
        {
            if (_pdfDocument == null) return;

            try
            {
                var viewportTop = ScrollY;
                var viewportBottom = ScrollY + CanvasHeight;
                var firstVisiblePage = Math.Max(0, (int)(viewportTop / _pageSize.Height) - VISIBLE_PAGE_BUFFER);
                var lastVisiblePage = Math.Min(PageCount - 1, (int)(viewportBottom / _pageSize.Height) + VISIBLE_PAGE_BUFFER);

                // 現在表示中のページ範囲外のページをメモリから解放
                for (int i = 0; i < PageCount; i++)
                {
                    if (i < firstVisiblePage || i > lastVisiblePage)
                    {
                        // 表示範囲外のページをメモリから解放（キャッシュは残す）
                        UnloadPage(i);
                    }
                }

                // 表示範囲内のページを読み込み
                var loadTasks = new List<Task>();
                for (int i = firstVisiblePage; i <= lastVisiblePage; i++)
                {
                    if (i >= 0 && i < PageCount)
                    {
                        // メモリに読み込まれていない場合のみ読み込み（常に高画質）
                        if (_pageCanvases[i] == null)
                        {
                            loadTasks.Add(LoadPdfPageAsync(i, RENDER_DPI));
                        }
                    }
                }

                if (loadTasks.Any())
                {
                    await Task.WhenAll(loadTasks);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"表示ページ更新エラー: {ex.Message}");
            }
        }


    }
} 