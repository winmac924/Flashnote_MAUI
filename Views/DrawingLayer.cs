using Microsoft.Maui.Controls;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Flashnote.Drawing;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Flashnote.Views
{
    /// <summary>
    /// 描画専用の透明レイヤー
    /// ペン、マーカー、消しゴムの描画機能のみを提供
    /// </summary>
    public class DrawingLayer : SKCanvasView, IDisposable
    {
        private const float SELECTION_MARGIN = 20f;
        private const float CONTEXT_MENU_WIDTH = 240;
        private const float CONTEXT_MENU_HEIGHT = 100;
        private const float CONTEXT_MENU_ITEM_HEIGHT = 40;
        private const float COLOR_BOX_SIZE = 30;
        private const float STROKE_WIDTH_BOX_SIZE = 30;
        private const float MIN_SCALE = 0.5f;
        private const float MAX_SCALE = 3.0f;

        // 描画ツールとペイント
        private SKPaint _penPaint;
        private SKPaint _markerPaint;
        private SKPaint _eraserPaint;
        private SKPaint _currentPaint;
        private SKPath _currentPath;
        private DrawingTool _currentTool = DrawingTool.Pen;

        // 描画要素の管理
        private readonly ObservableCollection<DrawingStroke> _drawingElements;
        private DrawingStroke _selectedElement;
        private bool _isDrawing;
        private SKPoint _lastPoint;
        private bool _isDisposed = false;

        // 元に戻す/やり直し機能
        private Stack<DrawingStroke> _undoStack = new Stack<DrawingStroke>();
        private Stack<DrawingStroke> _redoStack = new Stack<DrawingStroke>();

        // ツール設定
        private SKColor _penColor = SKColors.Black;
        private float _penStrokeWidth = 2.0f;
        private SKColor _markerColor = SKColors.Yellow.WithAlpha(128);
        private float _markerStrokeWidth = 10.0f;

        // 描画データ管理
        private string _noteName;
        private string _tempDirectory;
        private bool _isInitialLoad = true; // 初回読み込みフラグ
        private bool _isInitializing = false; // 初期化中フラグを追加

        // コンテキストメニュー
        private bool _isShowingContextMenu;
        private SKPoint _lastRightClickPoint;

        // 色とサイズの選択肢
        private readonly Dictionary<string, SKColor> _colors = new Dictionary<string, SKColor>
        {
            { "黒", SKColors.Black },
            { "白", SKColors.White },
            { "赤", SKColors.Red },
            { "青", SKColors.Blue },
            { "緑", SKColors.Green },
            { "黄", SKColors.Yellow },
            { "オレンジ", SKColors.Orange }
        };

        private readonly Dictionary<string, float> _strokeWidths = new Dictionary<string, float>
        {
            { "極細", 0.5f },
            { "細", 1.0f },
            { "中", 2.0f },
            { "太", 4.0f },
            { "極太", 8.0f }
        };

        private readonly Dictionary<string, float> _markerWidths = new Dictionary<string, float>
        {
            { "極細", 5.0f },
            { "細", 8.0f },
            { "中", 12.0f },
            { "太", 16.0f },
            { "極太", 20.0f }
        };

        // 拡大機能
        private float _currentScale = 1.0f;
        
        // 定期再描画用タイマー（描画の欠けを防ぐ）
        private System.Timers.Timer _refreshTimer;
        
        // BackgroundCanvasとの座標系同期用
        private float _totalHeight = 0f;
        private const float BASE_CANVAS_WIDTH = 600f;

        private bool _isSaving = false; // 保存中フラグを追加
        private bool _isLoading = false; // 読み込み中フラグを追加

        public DrawingLayer()
        {
            EnableTouchEvents = true;
            IgnorePixelScaling = true;
            BackgroundColor = Colors.Transparent; // 透明レイヤー

            _drawingElements = new ObservableCollection<DrawingStroke>();
            InitializePaints();
            
            // 定期再描画タイマーの初期化（一時的に無効化）
            // _refreshTimer = new System.Timers.Timer(1000); // 1秒間隔
            // _refreshTimer.Elapsed += (sender, e) => 
            // {
            //     Device.BeginInvokeOnMainThread(() => InvalidateSurface());
            // };
            // _refreshTimer.AutoReset = true;
            // _refreshTimer.Start();
        }

        /// <summary>
        /// 描画キャンバスを初期化し、保存されたデータがあれば自動的に読み込む
        /// </summary>
        public async Task InitializeAsync(string noteName, string tempDirectory)
        {
            // 初期化中なら処理をスキップ
            if (_isInitializing)
            {
                Debug.WriteLine("初期化処理が進行中のため、スキップします");
                return;
            }

            try
            {
                _isInitializing = true;
                _noteName = noteName;
                _tempDirectory = tempDirectory;
                
                Debug.WriteLine($"DrawingLayer初期化開始: ノート={_noteName}, ディレクトリ={_tempDirectory}");
                
                // 一時ディレクトリを確保
                if (!Directory.Exists(_tempDirectory))
                {
                    Directory.CreateDirectory(_tempDirectory);
                    Debug.WriteLine($"一時ディレクトリを作成: {_tempDirectory}");
                }
                
                // 保存された描画データを自動読み込み
                await LoadDrawingDataFromFileAsync();
            }
            finally
            {
                _isInitializing = false;
            }
        }

        /// <summary>
        /// ファイルから描画データを読み込む（内部処理）
        /// </summary>
        private async Task LoadDrawingDataFromFileAsync()
        {
            if (string.IsNullOrEmpty(_tempDirectory))
            {
                Debug.WriteLine("一時ディレクトリが未設定のため描画データを読み込めません");
                return;
            }

            // 読み込み中なら処理をスキップ
            if (_isLoading)
            {
                Debug.WriteLine("読み込み処理が進行中のため、スキップします");
                return;
            }

            var drawingDataPath = Path.Combine(_tempDirectory, "drawing_data.json");
            if (!File.Exists(drawingDataPath))
            {
                Debug.WriteLine($"描画データファイルが存在しません: {drawingDataPath}");
                return;
            }

            try
            {
                _isLoading = true;
                // バックアップファイルを作成
                var backupPath = drawingDataPath + ".backup";
                await CreateBackupAsync(drawingDataPath, backupPath);

                var json = await File.ReadAllTextAsync(drawingDataPath);
                Debug.WriteLine($"描画データファイル読み込み成功: {json.Length} 文字");
                
                if (string.IsNullOrWhiteSpace(json))
                {
                    Debug.WriteLine("描画データファイルが空です");
                    return;
                }

                var drawingData = System.Text.Json.JsonSerializer.Deserialize<List<DrawingStrokeData>>(json);
                
                if (drawingData != null && drawingData.Count > 0)
                {
                    // 初回読み込み時はクリア、それ以外は既存データを保持
                    if (_isInitialLoad)
                    {
                        LoadDrawingDataWithClear(drawingData);
                        _isInitialLoad = false; // 初回読み込み完了
                        Debug.WriteLine($"描画データの初回読み込み完了: {drawingData.Count} ストローク");
                    }
                    else
                    {
                        LoadDrawingData(drawingData);
                        Debug.WriteLine($"描画データの追加読み込み完了: {drawingData.Count} ストローク");
                    }
                }
                else
                {
                    Debug.WriteLine("描画データが空または無効です");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"描画データの自動読み込みエラー: {ex.Message}");
            }
            finally
            {
                _isLoading = false;
            }
        }

        /// <summary>
        /// バックアップファイルを作成する
        /// </summary>
        private async Task CreateBackupAsync(string originalPath, string backupPath)
        {
            try
            {
                if (File.Exists(originalPath))
                {
                    // 既存のバックアップファイルがあれば削除
                    if (File.Exists(backupPath))
                    {
                        File.Delete(backupPath);
                        Debug.WriteLine($"既存のバックアップファイルを削除: {backupPath}");
                    }

                    // 新しいバックアップを作成
                    File.Copy(originalPath, backupPath);
                    Debug.WriteLine($"バックアップファイルを作成: {backupPath}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"バックアップファイル作成エラー: {ex.Message}");
                // バックアップ作成失敗は処理を止めない
            }
        }

        private void InitializePaints()
        {
            _penPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = _penColor,
                StrokeWidth = _penStrokeWidth,
                IsAntialias = true,
                BlendMode = SKBlendMode.Src
            };

            _markerPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = _markerColor,
                StrokeWidth = _markerStrokeWidth,
                IsAntialias = true,
                BlendMode = SKBlendMode.SrcOver
            };

            _eraserPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = SKColors.Transparent,
                StrokeWidth = 20.0f,
                IsAntialias = true,
                BlendMode = SKBlendMode.Clear
            };

            _currentPaint = _penPaint;
        }

        public void SetTool(DrawingTool tool)
        {
            _currentTool = tool;
            _currentPaint = tool switch
            {
                DrawingTool.Pen => _penPaint,
                DrawingTool.Marker => _markerPaint,
                DrawingTool.Eraser => _eraserPaint,
                _ => _penPaint
            };
            Debug.WriteLine($"ツール変更: {tool}");
        }

        public void SetPenColor(SKColor color)
        {
            _penColor = color;
            _penPaint.Color = color;
            if (_currentTool == DrawingTool.Pen)
            {
                _currentPaint = _penPaint;
            }
        }

        public void SetPenStrokeWidth(float width)
        {
            _penStrokeWidth = width;
            _penPaint.StrokeWidth = width;
            if (_currentTool == DrawingTool.Pen)
            {
                _currentPaint = _penPaint;
            }
        }

        public void SetMarkerColor(SKColor color)
        {
            _markerColor = color;
            _markerPaint.Color = color;
            if (_currentTool == DrawingTool.Marker)
            {
                _currentPaint = _markerPaint;
            }
        }

        public void SetMarkerStrokeWidth(float width)
        {
            _markerStrokeWidth = width;
            _markerPaint.StrokeWidth = width;
            if (_currentTool == DrawingTool.Marker)
            {
                _currentPaint = _markerPaint;
            }
        }

        protected override void OnTouch(SKTouchEventArgs e)
        {
            if (_isDisposed)
            {
                e.Handled = false;
                return;
            }

            // 右クリックでのコンテキストメニュー表示（BackgroundCanvasと同じ方式：直接UI座標を使用）
            if (e.ActionType == SKTouchAction.Pressed && e.MouseButton == SKMouseButton.Right)
            {
                // UI座標をそのまま保存
                _lastRightClickPoint = e.Location;
                _isShowingContextMenu = true;
                Debug.WriteLine($"コンテキストメニューを表示: UI位置 ({e.Location.X}, {e.Location.Y})");
                InvalidateSurface();
                e.Handled = true;
                return;
            }

            // BackgroundCanvasと同じ方法：座標変換は不要（直接UI座標を使用）
            var adjustedLocation = e.Location;

            try
            {
                switch (e.ActionType)
                {
                    case SKTouchAction.Pressed:
                        // 右クリックは上で処理済みのため、ここは左クリックまたは他のボタン
                        if (e.MouseButton == SKMouseButton.Left)
                        {
                            Debug.WriteLine($"左クリック: UI位置 ({e.Location.X}, {e.Location.Y}) → 変換後 ({adjustedLocation.X}, {adjustedLocation.Y})");
                        }
                        HandleTouchPressed(adjustedLocation, e.MouseButton);
                        break;

                    case SKTouchAction.Moved:
                        if (e.MouseButton != SKMouseButton.Right) // 右ボタンでのドラッグは描画処理しない
                        {
                            HandleTouchMoved(adjustedLocation);
                        }
                        break;

                    case SKTouchAction.Released:
                        if (e.MouseButton == SKMouseButton.Right)
                        {
                            // 右クリックリリース時のコンテキストメニュー操作（BackgroundCanvasと同じ方式：直接UI座標を使用）
                            Debug.WriteLine($"右クリックリリース: UI位置 ({e.Location.X}, {e.Location.Y})");
                            HandleContextMenuInteraction(e.Location);
                        }
                        else
                        {
                            HandleTouchReleased(adjustedLocation);
                        }
                        break;
                }
                e.Handled = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"タッチイベントエラー: {ex.Message}");
                e.Handled = false;
            }
        }

        // SKTouchEventArgs を直接受け取る代わりに、必要な情報だけを引数で受け取るように変更
        private void HandleTouchPressed(SKPoint location, SKMouseButton mouseButton) // location はUI座標
        {
            if (_isDisposed)
            {
                return;
            }

            // タッチ時に強制再描画（欠けた部分の表示を修正）
            InvalidateSurface();

            // コンテキストメニューが表示されている場合は左クリックでメニュー操作
            if (_isShowingContextMenu && mouseButton == SKMouseButton.Left)
            {
                Debug.WriteLine($"コンテキストメニュー表示中の左クリック: 位置 ({location.X}, {location.Y})");
                HandleContextMenuClick(location);
                return;
            }

            // 右クリックの処理は OnTouch で分岐済み
            // if (mouseButton == SKMouseButton.Right)
            // {
            //     _lastRightClickPoint = location; // これはスケール1.0の座標。UI座標を使うべき。OnTouchで処理。
            //     _isShowingContextMenu = true;
            //     Debug.WriteLine($"コンテキストメニューを表示: 位置 ({location.X}, {location.Y})");
            //     InvalidateSurface();
            //     return;
            // }

            try
            {
                _isDrawing = true;
                _currentPath = new SKPath();
                // BackgroundCanvasと同じ方式：UI座標をスケール1.0座標に変換して保存
                var scaledLocation = new SKPoint(location.X / _currentScale, location.Y / _currentScale);
                _currentPath.MoveTo(scaledLocation);
                _lastPoint = scaledLocation;

                // やり直しスタックをクリア
                while (_redoStack.Count > 0)
                {
                    _redoStack.Pop().Dispose();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"タッチ開始エラー: {ex.GetType().Name} - {ex.Message}");
                _isDrawing = false;
                _currentPath?.Dispose();
                _currentPath = null;
            }
        }

        private void HandleTouchMoved(SKPoint location) // location はUI座標
        {
            // mouseButton のチェックは OnTouch で実施済み
            if (_isDisposed || !_isDrawing)
            {
                return;
            }

            try
            {
                if (IsValidPath(_currentPath))
                {
                    // BackgroundCanvasと同じ方式：UI座標をスケール1.0座標に変換して保存
                    var scaledLocation = new SKPoint(location.X / _currentScale, location.Y / _currentScale);
                    _currentPath.LineTo(scaledLocation);
                    _lastPoint = scaledLocation;
                    InvalidateSurface();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"タッチ移動エラー: {ex.GetType().Name} - {ex.Message}");
                _isDrawing = false;
                _currentPath?.Dispose();
                _currentPath = null;
            }
        }

        private void HandleTouchReleased(SKPoint location) // location はUI座標
        {
            if (_isDisposed)
            {
                return;
            }
            // 右クリックやコンテキストメニュー関連の分岐は OnTouch で実施済み

            if (_isDrawing)
            {
                try
                {
                    if (IsValidPath(_currentPath) && IsValidPaint(_currentPaint))
                    {
                        // 描画ストロークを作成して保存
                        var paint = new SKPaint
                        {
                            Color = _currentPaint.Color,
                            StrokeWidth = _currentPaint.StrokeWidth,
                            Style = _currentPaint.Style,
                            IsAntialias = _currentPaint.IsAntialias,
                            BlendMode = _currentPaint.BlendMode,
                            Typeface = _currentPaint.Typeface,
                            TextSize = _currentPaint.TextSize
                        };
                        var stroke = new DrawingStroke(_currentPath, paint);
                        
                        lock (_drawingElements)
                        {
                            _drawingElements.Add(stroke);
                        }
                        _undoStack.Push(stroke);
                        
                        // 描画完了時に自動保存（少し遅延させて実行）
                        Device.BeginInvokeOnMainThread(async () =>
                        {
                            await Task.Delay(100); // 100ミリ秒待機
                            await SaveDrawingDataAsync();
                        });
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ストローク作成エラー: {ex.GetType().Name} - {ex.Message}");
                    _currentPath?.Dispose();
                }
                finally
                {
                    _isDrawing = false;
                    _currentPath = null;
                    InvalidateSurface();
                }
            }
        }

        private void HandleContextMenuInteraction(SKPoint adjustedLocation)
        {
            if (_isShowingContextMenu)
            {
                HandleContextMenuClick(adjustedLocation); // 変換後座標を渡す
                // 右クリックでメニュー外をクリックした場合のみメニューを閉じる
                // メニュー項目をクリックした場合は閉じない
                InvalidateSurface();
            }
        }

        private void HandleContextMenuClick(SKPoint adjustedLocation) // location はUI座標
        {
            // メニューの高さを再計算して判定に使用
            var menuHeight = 5 + COLOR_BOX_SIZE + 10 + STROKE_WIDTH_BOX_SIZE + 10 + (CONTEXT_MENU_ITEM_HEIGHT * 2) + 5;
            
            // _lastRightClickPoint はUI座標（コンテキストメニューはUI要素なので変換不要）
            var menuRect = new SKRect(_lastRightClickPoint.X, _lastRightClickPoint.Y,
                _lastRightClickPoint.X + CONTEXT_MENU_WIDTH, _lastRightClickPoint.Y + menuHeight);
            
            Debug.WriteLine($"コンテキストメニュー判定開始:");
            Debug.WriteLine($"  現在のスケール: {_currentScale}");
            Debug.WriteLine($"  クリック位置: ({adjustedLocation.X}, {adjustedLocation.Y})");
            Debug.WriteLine($"  メニュー領域: ({menuRect.Left}, {menuRect.Top}) - ({menuRect.Right}, {menuRect.Bottom})");
            Debug.WriteLine($"  メニュー内クリック: {menuRect.Contains(adjustedLocation)}");
            
            // メニュー外をクリックした場合はメニューを閉じる
            if (!menuRect.Contains(adjustedLocation))
            {
                Debug.WriteLine("メニュー外をクリック - メニューを閉じます");
                _isShowingContextMenu = false;
                return;
            }

            // 色ボックス領域の判定（最上部に配置）（コンテキストメニューはUI要素なので変換不要）
            var colorBoxStartY = _lastRightClickPoint.Y + 5;
            var colorBoxEndY = colorBoxStartY + COLOR_BOX_SIZE;
            Debug.WriteLine($"  色ボックス領域: Y={colorBoxStartY}-{colorBoxEndY}, 判定={adjustedLocation.Y >= colorBoxStartY && adjustedLocation.Y <= colorBoxEndY}");
            if (adjustedLocation.Y >= colorBoxStartY && adjustedLocation.Y <= colorBoxEndY)
            {
                Debug.WriteLine($"色ボックス領域をクリック: 位置 ({adjustedLocation.X}, {adjustedLocation.Y})");
                HandleColorSelection(adjustedLocation); // UI座標を渡す
                InvalidateSurface();
                return;
            }

            // 線の太さボックス領域の判定（色ボックスの下に配置）（コンテキストメニューはUI要素なので変換不要）
            var strokeBoxStartY = _lastRightClickPoint.Y + 5 + COLOR_BOX_SIZE + 10;
            var strokeBoxEndY = strokeBoxStartY + STROKE_WIDTH_BOX_SIZE;
            Debug.WriteLine($"  線の太さボックス領域: Y={strokeBoxStartY}-{strokeBoxEndY}, 判定={adjustedLocation.Y >= strokeBoxStartY && adjustedLocation.Y <= strokeBoxEndY}");
            if (adjustedLocation.Y >= strokeBoxStartY && adjustedLocation.Y <= strokeBoxEndY)
            {
                Debug.WriteLine($"線の太さボックス領域をクリック: 位置 ({adjustedLocation.X}, {adjustedLocation.Y})");
                HandleStrokeWidthSelection(adjustedLocation); // UI座標を渡す
                InvalidateSurface();
                return;
            }

            // ボタンクリックの処理
            if (HandleButtonClick(adjustedLocation))
            {
                InvalidateSurface();
                return;
            }

            Debug.WriteLine($"メニュー内の他の領域をクリック: 位置 ({adjustedLocation.X}, {adjustedLocation.Y})");
        }

        private void HandleColorSelection(SKPoint adjustedLocation) // location は変換後座標
        {
            var colors = _colors.Values.ToArray();
            var colorNames = _colors.Keys.ToArray();
            var colorIndex = GetColorBoxIndex(adjustedLocation); // 変換後座標を渡す
            
            Debug.WriteLine($"色選択: クリック位置 ({adjustedLocation.X}, {adjustedLocation.Y}), インデックス {colorIndex}");
            
            if (colorIndex >= 0 && colorIndex < colors.Length)
            {
                var selectedColor = colors[colorIndex];
                var colorName = colorNames[colorIndex];
                
                if (_currentTool == DrawingTool.Pen)
                {
                    SetPenColor(selectedColor);
                    Debug.WriteLine($"ペンの色を{colorName}に変更しました");
                }
                else if (_currentTool == DrawingTool.Marker)
                {
                    SetMarkerColor(selectedColor.WithAlpha(128));
                    Debug.WriteLine($"マーカーの色を{colorName}に変更しました");
                }
                _isShowingContextMenu = false; // 色選択後はメニューを閉じる
            }
            else
            {
                Debug.WriteLine("有効な色が選択されませんでした");
            }
        }

        private void HandleStrokeWidthSelection(SKPoint adjustedLocation) // location は変換後座標
        {
            var widthDict = _currentTool == DrawingTool.Marker ? _markerWidths : _strokeWidths;
            var widths = widthDict.Values.ToArray();
            var widthNames = widthDict.Keys.ToArray();
            var widthIndex = GetStrokeWidthBoxIndex(adjustedLocation); // 変換後座標を渡す
            
            Debug.WriteLine($"線の太さ選択: クリック位置 ({adjustedLocation.X}, {adjustedLocation.Y}), インデックス {widthIndex}");
            
            if (widthIndex >= 0 && widthIndex < widths.Length)
            {
                var selectedWidth = widths[widthIndex];
                var widthName = widthNames[widthIndex];
                
                if (_currentTool == DrawingTool.Pen)
                {
                    SetPenStrokeWidth(selectedWidth);
                    Debug.WriteLine($"ペンの太さを{widthName}({selectedWidth})に変更しました");
                }
                else if (_currentTool == DrawingTool.Marker)
                {
                    SetMarkerStrokeWidth(selectedWidth);
                    Debug.WriteLine($"マーカーの太さを{widthName}({selectedWidth})に変更しました");
                }
                _isShowingContextMenu = false; // 太さ選択後はメニューを閉じる
            }
            else
            {
                Debug.WriteLine("有効な線の太さが選択されませんでした");
            }
        }

        private bool HandleButtonClick(SKPoint adjustedLocation)
        {
            // ボタン領域の判定（線の太さボックスの下に配置）（コンテキストメニューはUI要素なので変換不要）
            var buttonStartY = _lastRightClickPoint.Y + 5 + COLOR_BOX_SIZE + 10 + STROKE_WIDTH_BOX_SIZE + 10;
            
            // 元に戻すボタンの判定
            var undoButtonRect = new SKRect(_lastRightClickPoint.X + 5, buttonStartY,
                _lastRightClickPoint.X + CONTEXT_MENU_WIDTH - 5, buttonStartY + CONTEXT_MENU_ITEM_HEIGHT);
            
            // クリアボタンの判定
            var clearButtonRect = new SKRect(_lastRightClickPoint.X + 5, buttonStartY + CONTEXT_MENU_ITEM_HEIGHT,
                _lastRightClickPoint.X + CONTEXT_MENU_WIDTH - 5, buttonStartY + CONTEXT_MENU_ITEM_HEIGHT * 2);

            Debug.WriteLine($"  ボタン判定:");
            Debug.WriteLine($"    元に戻すボタン: ({undoButtonRect.Left}, {undoButtonRect.Top}) - ({undoButtonRect.Right}, {undoButtonRect.Bottom}), 判定={undoButtonRect.Contains(adjustedLocation)}");
            Debug.WriteLine($"    クリアボタン: ({clearButtonRect.Left}, {clearButtonRect.Top}) - ({clearButtonRect.Right}, {clearButtonRect.Bottom}), 判定={clearButtonRect.Contains(adjustedLocation)}");

            if (undoButtonRect.Contains(adjustedLocation))
            {
                Debug.WriteLine($"元に戻すボタンをクリック: 位置 ({adjustedLocation.X}, {adjustedLocation.Y})");
                Undo();
                _isShowingContextMenu = false; // ボタン押下後はメニューを非表示
                return true;
            }
            
            if (clearButtonRect.Contains(adjustedLocation))
            {
                Debug.WriteLine($"クリアボタンをクリック: 位置 ({adjustedLocation.X}, {adjustedLocation.Y})");
                Clear();
                _isShowingContextMenu = false; // ボタン押下後はメニューを非表示
                return true;
            }

            Debug.WriteLine($"  ボタン領域外");
            return false; // ボタンがクリックされていない
        }

        private int GetColorBoxIndex(SKPoint adjustedLocation) // location はUI座標
        {
            // _lastRightClickPoint はUI座標（コンテキストメニューはUI要素なので変換不要）
            var startX = _lastRightClickPoint.X + 10;
            var y = _lastRightClickPoint.Y + 5; // 最上部に配置
            
            for (int i = 0; i < _colors.Count; i++)
            {
                var boxRect = new SKRect(startX + i * (COLOR_BOX_SIZE + 5), y,
                    startX + i * (COLOR_BOX_SIZE + 5) + COLOR_BOX_SIZE, y + COLOR_BOX_SIZE);
                if (boxRect.Contains(adjustedLocation))
                    return i;
            }
            return -1;
        }

        private int GetStrokeWidthBoxIndex(SKPoint adjustedLocation) // location はUI座標
        {
            // _lastRightClickPoint はUI座標（コンテキストメニューはUI要素なので変換不要）
            var startX = _lastRightClickPoint.X + 10;
            var y = _lastRightClickPoint.Y + 5 + COLOR_BOX_SIZE + 10; // 色ボックスの下に配置
            
            var widths = _currentTool == DrawingTool.Marker ? _markerWidths : _strokeWidths;
            for (int i = 0; i < widths.Count; i++)
            {
                var boxRect = new SKRect(startX + i * (STROKE_WIDTH_BOX_SIZE + 5), y,
                    startX + i * (STROKE_WIDTH_BOX_SIZE + 5) + STROKE_WIDTH_BOX_SIZE, y + STROKE_WIDTH_BOX_SIZE);
                if (boxRect.Contains(adjustedLocation))
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// SKPaintオブジェクトが有効かどうかをチェック
        /// </summary>
        private bool IsValidPaint(SKPaint paint)
        {
            if (paint == null || _isDisposed)
            {
                return false;
            }

            try
            {
                // ハンドルチェック
                if (paint.Handle == IntPtr.Zero)
                {
                    Debug.WriteLine("SKPaintハンドルが無効");
                    return false;
                }

                // 基本プロパティアクセステスト
                var color = paint.Color;
                var strokeWidth = paint.StrokeWidth;
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SKPaint有効性チェックエラー: {ex.GetType().Name}");
                return false;
            }
        }

        /// <summary>
        /// SKPathオブジェクトが有効かどうかをチェック
        /// </summary>
        private bool IsValidPath(SKPath path)
        {
            if (path == null || _isDisposed)
            {
                return false;
            }

            try
            {
                // ハンドルチェック
                if (path.Handle == IntPtr.Zero)
                {
                    Debug.WriteLine("SKPathハンドルが無効");
                    return false;
                }

                // 基本プロパティアクセステスト
                var bounds = path.Bounds;
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SKPath有効性チェックエラー: {ex.GetType().Name}");
                return false;
            }
        }

        protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
        {
            if (_isDisposed || e?.Surface?.Canvas == null)
            {
                return;
            }

            // UIスレッドでの実行を確保
            if (!MainThread.IsMainThread)
            {
                Debug.WriteLine("OnPaintSurface: UIスレッド外で呼び出されました");
                return;
            }

            var canvas = e.Surface.Canvas;
            var info = e.Info;
            
            try
            {
                canvas.Clear(SKColors.Transparent);

                // キャンバス全体をクリッピング領域として設定（描画領域を制限しない）
                var canvasRect = new SKRect(0, 0, info.Width, info.Height);
                canvas.ClipRect(canvasRect);

                // BackgroundCanvasと完全に同じ方式：マトリックス変換を使用
                canvas.Save();
                canvas.Scale(_currentScale, _currentScale);

                // 保存された描画要素を安全に描画（元のスケール1.0で描画）
                var elementsToRender = new List<(SKPath path, SKPaint paint)>();
                
                lock (_drawingElements)
                {
                    foreach (var element in _drawingElements)
                    {
                        if (element != null && IsValidPath(element.DrawingPath) && IsValidPaint(element.DrawingPaint))
                        {
                            elementsToRender.Add((element.DrawingPath, element.DrawingPaint));
                        }
                    }
                }

                // ロック外で描画実行（元のパスとペイントをそのまま使用）
                foreach (var (path, paint) in elementsToRender)
                {
                    try
                    {
                        if (IsValidPath(path) && IsValidPaint(paint))
                        {
                            // マトリックス変換により自動的にスケールされるため、元のパスとペイントをそのまま使用
                            canvas.DrawPath(path, paint);
                        }
                    }
                    catch (AccessViolationException ave)
                    {
                        Debug.WriteLine($"描画要素でAVE: {ave.Message}");
                        RemoveInvalidElement(path);
                    }
                    catch (System.ExecutionEngineException eee)
                    {
                        Debug.WriteLine($"描画要素でEEE: {eee.Message}");
                        RemoveInvalidElement(path);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"描画要素の描画エラー: {ex.GetType().Name} - {ex.Message}");
                        RemoveInvalidElement(path);
                    }
                }

                // 現在描画中のパスを安全に描画（元のスケール1.0で描画）
                if (_isDrawing && IsValidPath(_currentPath) && IsValidPaint(_currentPaint))
                    {
                        try
                        {
                        // マトリックス変換により自動的にスケールされるため、元のパスとペイントをそのまま使用
                        canvas.DrawPath(_currentPath, _currentPaint);
                        }
                        catch (AccessViolationException ave)
                        {
                            Debug.WriteLine($"現在描画パスでAVE: {ave.Message}");
                            _isDrawing = false;
                            _currentPath?.Dispose();
                            _currentPath = null;
                        }
                        catch (System.ExecutionEngineException eee)
                        {
                            Debug.WriteLine($"現在描画パスでEEE: {eee.Message}");
                            _isDrawing = false;
                            _currentPath?.Dispose();
                            _currentPath = null;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"現在描画パスエラー: {ex.GetType().Name} - {ex.Message}");
                            _isDrawing = false;
                            _currentPath?.Dispose();
                            _currentPath = null;
                    }
                }

                canvas.Restore();

                // コンテキストメニューを描画 (UI座標系で、スケーリングの影響を受けない)
                if (_isShowingContextMenu)
                {
                    try
                    {
                        DrawContextMenu(canvas);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"コンテキストメニュー描画エラー: {ex.GetType().Name} - {ex.Message}");
                        _isShowingContextMenu = false;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OnPaintSurface全体エラー: {ex.GetType().Name} - {ex.Message}");
            }
        }

        /// <summary>
        /// 無効な描画要素を安全に削除する
        /// </summary>
        private void RemoveInvalidElement(SKPath targetPath)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                lock (_drawingElements)
                {
                    var toRemove = _drawingElements.Where(e => e.DrawingPath == targetPath).ToList();
                    foreach (var element in toRemove)
                    {
                        _drawingElements.Remove(element);
                        element.Dispose();
                    }
                }
            });
        }

        private void DrawContextMenu(SKCanvas canvas)
        {
            // メニューの高さを再計算（色ボックス + 線の太さボックス + ボタン2つ）
            var menuHeight = 5 + COLOR_BOX_SIZE + 10 + STROKE_WIDTH_BOX_SIZE + 10 + (CONTEXT_MENU_ITEM_HEIGHT * 2) + 5;
            
            // _lastRightClickPoint はUI座標（コンテキストメニューはUI要素なので変換不要）
            var menuRect = new SKRect(_lastRightClickPoint.X, _lastRightClickPoint.Y,
                _lastRightClickPoint.X + CONTEXT_MENU_WIDTH, _lastRightClickPoint.Y + menuHeight);

            // メニュー背景
            var menuPaint = new SKPaint
            {
                Color = SKColors.White,
                Style = SKPaintStyle.Fill
            };
            canvas.DrawRect(menuRect, menuPaint);

            // メニュー枠
            var borderPaint = new SKPaint
            {
                Color = SKColors.Black,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1
            };
            canvas.DrawRect(menuRect, borderPaint);

            // 色ボックス
            DrawColorBoxes(canvas);
            
            // 線の太さボックス
            DrawStrokeWidthBoxes(canvas);
            
            // ボタンテキスト
            DrawMenuButtons(canvas);
        }

        private void DrawColorBoxes(SKCanvas canvas)
        {
            // _lastRightClickPoint はUI座標（コンテキストメニューはUI要素なので変換不要）
            var startX = _lastRightClickPoint.X + 10;
            var y = _lastRightClickPoint.Y + 5; // 最上部に配置
            var colors = _colors.Values.ToArray();

            for (int i = 0; i < colors.Length; i++)
            {
                var boxRect = new SKRect(startX + i * (COLOR_BOX_SIZE + 5), y,
                    startX + i * (COLOR_BOX_SIZE + 5) + COLOR_BOX_SIZE, y + COLOR_BOX_SIZE);

                var colorPaint = new SKPaint { Color = colors[i], Style = SKPaintStyle.Fill };
                canvas.DrawRect(boxRect, colorPaint);

                var borderPaint = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
                canvas.DrawRect(boxRect, borderPaint);
            }
        }

        private void DrawStrokeWidthBoxes(SKCanvas canvas)
        {
            // _lastRightClickPoint はUI座標（コンテキストメニューはUI要素なので変換不要）
            var startX = _lastRightClickPoint.X + 10;
            var y = _lastRightClickPoint.Y + 5 + COLOR_BOX_SIZE + 10; // 色ボックスの下に配置
            var widths = (_currentTool == DrawingTool.Marker ? _markerWidths : _strokeWidths).Values.ToArray();

            for (int i = 0; i < widths.Length; i++)
            {
                var boxRect = new SKRect(startX + i * (STROKE_WIDTH_BOX_SIZE + 5), y,
                    startX + i * (STROKE_WIDTH_BOX_SIZE + 5) + STROKE_WIDTH_BOX_SIZE, y + STROKE_WIDTH_BOX_SIZE);

                var bgPaint = new SKPaint { Color = SKColors.LightGray, Style = SKPaintStyle.Fill };
                canvas.DrawRect(boxRect, bgPaint);

                var linePaint = new SKPaint 
                { 
                    Color = SKColors.Black, 
                    Style = SKPaintStyle.Stroke, 
                    StrokeWidth = widths[i],
                    IsAntialias = true
                };

                var center = boxRect.MidY;
                canvas.DrawLine(boxRect.Left + 5, center, boxRect.Right - 5, center, linePaint);

                var borderPaint = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
                canvas.DrawRect(boxRect, borderPaint);
            }
        }

        private void DrawMenuButtons(SKCanvas canvas)
        {
            var textPaint = new SKPaint
            {
                Color = SKColors.Black,
                TextSize = 14,
                IsAntialias = true,
                Typeface = SKTypeface.Default
            };

            var buttonStartY = _lastRightClickPoint.Y + 5 + COLOR_BOX_SIZE + 10 + STROKE_WIDTH_BOX_SIZE + 10;
            
            // 元に戻すボタン（コンテキストメニューはUI要素なので変換不要）
            var undoButtonRect = new SKRect(_lastRightClickPoint.X + 5, buttonStartY,
                _lastRightClickPoint.X + CONTEXT_MENU_WIDTH - 5, buttonStartY + CONTEXT_MENU_ITEM_HEIGHT);
            var undoButtonPaint = new SKPaint { Color = SKColors.LightBlue, Style = SKPaintStyle.Fill };
            canvas.DrawRect(undoButtonRect, undoButtonPaint);
            canvas.DrawText("元に戻す", _lastRightClickPoint.X + 10, buttonStartY + 20, textPaint);

            // クリアボタン（コンテキストメニューはUI要素なので変換不要）
            var clearButtonRect = new SKRect(_lastRightClickPoint.X + 5, buttonStartY + CONTEXT_MENU_ITEM_HEIGHT,
                _lastRightClickPoint.X + CONTEXT_MENU_WIDTH - 5, buttonStartY + CONTEXT_MENU_ITEM_HEIGHT * 2);
            var clearButtonPaint = new SKPaint { Color = SKColors.LightCoral, Style = SKPaintStyle.Fill };
            canvas.DrawRect(clearButtonRect, clearButtonPaint);
            canvas.DrawText("クリア", _lastRightClickPoint.X + 10, buttonStartY + CONTEXT_MENU_ITEM_HEIGHT + 20, textPaint);

            // ボタン枠線
            var buttonBorderPaint = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
            canvas.DrawRect(undoButtonRect, buttonBorderPaint);
            canvas.DrawRect(clearButtonRect, buttonBorderPaint);
        }

        public void Clear()
        {
            if (_isDisposed)
            {
                Debug.WriteLine("Clear: 既に破棄されています");
                return;
            }

            try
            {
                Debug.WriteLine($"Clear開始: 描画要素数={_drawingElements.Count}");
                lock (_drawingElements)
                {
                    foreach (var element in _drawingElements)
                    {
                        element.Dispose();
                    }
                    _drawingElements.Clear();
                    Debug.WriteLine($"描画要素をクリア: 現在の要素数={_drawingElements.Count}");
                }
                
                // スタックもクリア
                var undoCount = _undoStack.Count;
                var redoCount = _redoStack.Count;
                while (_undoStack.Count > 0)
                {
                    _undoStack.Pop().Dispose();
                }
                while (_redoStack.Count > 0)
                {
                    _redoStack.Pop().Dispose();
                }
                Debug.WriteLine($"スタックをクリア: Undo={undoCount}, Redo={redoCount}");

                InvalidateSurface();
                
                // 注意: Clearは手動操作のため自動保存しない
                Debug.WriteLine("描画をクリアしました（手動保存が必要）");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"クリアエラー: {ex.GetType().Name} - {ex.Message}");
            }
        }

        public void Undo()
        {
            if (_isDisposed)
            {
                Debug.WriteLine("Undo: 既に破棄されています");
                return;
            }

            try
            {
                Debug.WriteLine($"Undo開始: Undoスタック数={_undoStack.Count}");
                if (_undoStack.Count > 0)
                {
                    var lastStroke = _undoStack.Pop();
                    
                    lock (_drawingElements)
                    {
                        _drawingElements.Remove(lastStroke);
                        Debug.WriteLine($"描画要素を削除: 現在の要素数={_drawingElements.Count}");
                    }
                    
                    _redoStack.Push(lastStroke);
                    InvalidateSurface();
                    
                    // 注意: Undoは手動操作のため自動保存しない
                    Debug.WriteLine("元に戻し実行（手動保存が必要）");
                }
                else
                {
                    Debug.WriteLine("Undo: 元に戻す操作がありません");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Undoエラー: {ex.GetType().Name} - {ex.Message}");
            }
        }

        public void Redo()
        {
            if (_isDisposed)
            {
                return;
            }

            try
            {
                if (_redoStack.Count > 0)
                {
                    var stroke = _redoStack.Pop();
                    
                    lock (_drawingElements)
                    {
                        _drawingElements.Add(stroke);
                    }
                    
                    _undoStack.Push(stroke);
                    InvalidateSurface();
                    
                    // 注意: Redoは手動操作のため自動保存しない
                    Debug.WriteLine("やり直し実行（手動保存が必要）");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Redoエラー: {ex.GetType().Name} - {ex.Message}");
            }
        }

        /// <summary>
        /// 描画データを保存用形式で取得
        /// </summary>
        public List<DrawingStrokeData> GetDrawingData()
        {
            var data = new List<DrawingStrokeData>();
            
            Debug.WriteLine($"描画データの取得開始: {_drawingElements.Count} 要素");
            
            foreach (var element in _drawingElements)
            {
                var strokeData = new DrawingStrokeData
                {
                    Points = GetPointsFromPath(element.DrawingPath),
                    Color = element.DrawingPaint.Color,
                    StrokeWidth = element.DrawingPaint.StrokeWidth,
                    Style = element.DrawingPaint.Style,
                    Tool = GetToolFromPaint(element.DrawingPaint)
                };
                data.Add(strokeData);
                
                Debug.WriteLine($"ストローク保存: 色={strokeData.Color}, 太さ={strokeData.StrokeWidth}, ポイント数={strokeData.Points.Count}");
            }
            
            Debug.WriteLine($"描画データの取得完了: {data.Count} ストローク");
            return data;
        }

        /// <summary>
        /// 保存されたデータから描画を復元（既存データを保持）
        /// </summary>
        public void LoadDrawingData(List<DrawingStrokeData> data)
        {
            Debug.WriteLine($"描画データの復元開始: {data.Count} ストローク（既存データ保持）");
            
            foreach (var strokeData in data)
            {
                var path = new SKPath();
                if (strokeData.Points.Count > 0)
                {
                    path.MoveTo(strokeData.Points[0]);
                    for (int i = 1; i < strokeData.Points.Count; i++)
                    {
                        path.LineTo(strokeData.Points[i]);
                    }
                }

                var paint = new SKPaint
                {
                    Color = strokeData.Color,
                    StrokeWidth = strokeData.StrokeWidth,
                    Style = strokeData.Style,
                    IsAntialias = true,
                    BlendMode = strokeData.Tool == DrawingTool.Marker ? SKBlendMode.SrcOver : SKBlendMode.Src
                };

                var stroke = new DrawingStroke(path, paint);
                
                lock (_drawingElements)
                {
                    _drawingElements.Add(stroke);
                }
                // Undoスタックには追加しない（復元時は不要）
                
                Debug.WriteLine($"ストローク復元: 色={strokeData.Color}, 太さ={strokeData.StrokeWidth}, ポイント数={strokeData.Points.Count}");
            }
            
            Debug.WriteLine($"描画データの復元完了: {_drawingElements.Count} 要素");
            InvalidateSurface();
        }

        /// <summary>
        /// 保存されたデータから描画を復元（既存データをクリア）
        /// </summary>
        public void LoadDrawingDataWithClear(List<DrawingStrokeData> data)
        {
            Clear();
            
            Debug.WriteLine($"描画データの復元開始: {data.Count} ストローク（既存データクリア）");
            
            foreach (var strokeData in data)
            {
                var path = new SKPath();
                if (strokeData.Points.Count > 0)
                {
                    path.MoveTo(strokeData.Points[0]);
                    for (int i = 1; i < strokeData.Points.Count; i++)
                    {
                        path.LineTo(strokeData.Points[i]);
                    }
                }

                var paint = new SKPaint
                {
                    Color = strokeData.Color,
                    StrokeWidth = strokeData.StrokeWidth,
                    Style = strokeData.Style,
                    IsAntialias = true,
                    BlendMode = strokeData.Tool == DrawingTool.Marker ? SKBlendMode.SrcOver : SKBlendMode.Src
                };

                var stroke = new DrawingStroke(path, paint);
                
                lock (_drawingElements)
                {
                    _drawingElements.Add(stroke);
                }
                // Undoスタックには追加しない（復元時は不要）
                
                Debug.WriteLine($"ストローク復元: 色={strokeData.Color}, 太さ={strokeData.StrokeWidth}, ポイント数={strokeData.Points.Count}");
            }
            
            Debug.WriteLine($"描画データの復元完了: {_drawingElements.Count} 要素");
            InvalidateSurface();
        }

        /// <summary>
        /// 描画データを現在の一時ディレクトリに保存
        /// </summary>
        public async Task SaveDrawingDataAsync()
        {
            if (string.IsNullOrEmpty(_tempDirectory))
            {
                Debug.WriteLine("一時ディレクトリが設定されていません");
                return;
            }

            // 保存中なら処理をスキップ
            if (_isSaving)
            {
                Debug.WriteLine("保存処理が進行中のため、スキップします");
                return;
            }

            try
            {
                _isSaving = true;
                var drawingData = GetDrawingData();
                var jsonData = System.Text.Json.JsonSerializer.Serialize(drawingData);
                var saveFilePath = Path.Combine(_tempDirectory, "drawing_data.json");
                
                // ディレクトリが存在しない場合は作成
                var directory = Path.GetDirectoryName(saveFilePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                await File.WriteAllTextAsync(saveFilePath, jsonData);
                Debug.WriteLine($"描画データを保存: {saveFilePath}");
                Debug.WriteLine($"保存されたJSON: {jsonData.Substring(0, Math.Min(jsonData.Length, 200))}...");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"描画データの保存エラー: {ex.Message}");
            }
            finally
            {
                _isSaving = false;
            }
        }

        private List<SKPoint> GetPointsFromPath(SKPath path)
        {
            var points = new List<SKPoint>();
            var iterator = path.CreateIterator(false);
            var pathPoints = new SKPoint[4];
            
            while (iterator.Next(pathPoints) != SKPathVerb.Done)
            {
                points.Add(pathPoints[0]);
            }
            
            return points;
        }

        private DrawingTool GetToolFromPaint(SKPaint paint)
        {
            if (paint.BlendMode == SKBlendMode.Clear)
                return DrawingTool.Eraser;
            else if (paint.BlendMode == SKBlendMode.SrcOver)
                return DrawingTool.Marker;
            else
                return DrawingTool.Pen;
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                // 定期再描画タイマーを停止・破棄
                if (_refreshTimer != null)
                {
                    _refreshTimer.Stop();
                    _refreshTimer.Dispose();
                    _refreshTimer = null;
                    Debug.WriteLine("定期再描画タイマーを破棄");
                }
                
                Clear();
                _penPaint?.Dispose();
                _markerPaint?.Dispose();
                _eraserPaint?.Dispose();
                _isDisposed = true;
            }
        }

        /// <summary>
        /// 描画要素が存在するかどうかを確認
        /// </summary>
        public bool HasDrawings => _drawingElements.Count > 0;

        /// <summary>
        /// 現在の描画ツールを取得
        /// </summary>
        public DrawingTool CurrentTool => _currentTool;

        /// <summary>
        /// 現在のペンの色を取得
        /// </summary>
        public SKColor CurrentPenColor => _penColor;

        /// <summary>
        /// 現在のペンの太さを取得
        /// </summary>
        public float CurrentPenStrokeWidth => _penStrokeWidth;

        /// <summary>
        /// 現在のマーカーの色を取得
        /// </summary>
        public SKColor CurrentMarkerColor => _markerColor;

        /// <summary>
        /// 現在のマーカーの太さを取得
        /// </summary>
        public float CurrentMarkerStrokeWidth => _markerStrokeWidth;

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
                    UpdateCanvasSize();
                    
                    // スケール変更時に強制再描画（複数回実行して確実に描画）
                    InvalidateSurface();
                    
                    // 少し遅延させて再度再描画（描画の欠けを防ぐ）
                    Device.BeginInvokeOnMainThread(async () =>
                    {
                        await Task.Delay(10);
                        InvalidateSurface();
                    });
                    
                    Debug.WriteLine($"DrawingLayer ズーム倍率変更: {_currentScale:F2}");
                }
            }
        }

        /// <summary>
        /// 変換マトリックスを更新
        /// </summary>
        // private void UpdateTransformMatrix() { } // 削除

        /// <summary>
        /// キャンバスサイズをズーム倍率に応じて更新
        /// </summary>
        private void UpdateCanvasSize()
        {
            // 基本サイズに拡大倍率を適用
            WidthRequest = BASE_CANVAS_WIDTH * _currentScale;
            
            // BackgroundCanvasと同じ高さ計算を使用
            if (_totalHeight > 0)
            {
                // PDFが読み込まれている場合：BackgroundCanvasの総高さを使用
                HeightRequest = _totalHeight * _currentScale;
            }
            else
            {
                // デフォルト値：4:3のアスペクト比
                HeightRequest = BASE_CANVAS_WIDTH * (4.0f / 3.0f) * _currentScale;
            }
            
            Debug.WriteLine($"DrawingLayer サイズ更新: {WidthRequest}x{HeightRequest}, 総高さ={_totalHeight}, Scale={_currentScale}");
        }

        /// <summary>
        /// 描画データを手動で再読み込み（外部から呼び出し可能）
        /// </summary>
        public async Task ReloadAsync()
        {
            await LoadDrawingDataFromFileAsync();
        }

        /// <summary>
        /// 描画データを手動で再読み込み（既存データをクリア）
        /// </summary>
        public async Task ReloadWithClearAsync()
        {
            _isInitialLoad = true; // 強制的に初回読み込み扱いにする
            await LoadDrawingDataFromFileAsync();
        }

        /// <summary>
        /// 指定した描画データを読み込み（既存データを保持）
        /// </summary>
        public void AddDrawingData(List<DrawingStrokeData> data)
        {
            LoadDrawingData(data);
        }

        /// <summary>
        /// 指定した描画データを読み込み（既存データをクリア）
        /// </summary>
        public void ReplaceDrawingData(List<DrawingStrokeData> data)
        {
            LoadDrawingDataWithClear(data);
        }

        /// <summary>
        /// バックアップファイルから復元する
        /// </summary>
        public async Task RestoreFromBackupAsync()
        {
            if (string.IsNullOrEmpty(_tempDirectory))
            {
                Debug.WriteLine("一時ディレクトリが未設定のためバックアップから復元できません");
                return;
            }

            var backupPath = Path.Combine(_tempDirectory, "drawing_data.json.backup");
            if (!File.Exists(backupPath))
            {
                Debug.WriteLine($"バックアップファイルが存在しません: {backupPath}");
                return;
            }

            try
            {
                var json = await File.ReadAllTextAsync(backupPath);
                Debug.WriteLine($"バックアップファイル読み込み成功: {json.Length} 文字");

                if (string.IsNullOrWhiteSpace(json))
                {
                    Debug.WriteLine("バックアップファイルが空です");
                    return;
                }

                var drawingData = System.Text.Json.JsonSerializer.Deserialize<List<DrawingStrokeData>>(json);

                if (drawingData != null && drawingData.Count > 0)
                {
                    LoadDrawingDataWithClear(drawingData);
                    Debug.WriteLine($"バックアップから復元完了: {drawingData.Count} ストローク");
                    
                    // 復元後、現在の状態を保存
                    await SaveDrawingDataAsync();
                    Debug.WriteLine("復元したデータを正式なファイルに保存");
                }
                else
                {
                    Debug.WriteLine("バックアップファイルのデータが空または無効です");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"バックアップからの復元エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 手動で描画データを保存（外部から呼び出し可能）
        /// </summary>
        public async Task SaveAsync()
        {
            await SaveDrawingDataAsync();
        }
        
        /// <summary>
        /// BackgroundCanvasから座標系情報を同期
        /// </summary>
        public void SyncWithBackgroundCanvas(float totalHeight, float currentScale)
        {
            _totalHeight = totalHeight;
            if (_currentScale != currentScale)
            {
                _currentScale = currentScale;
                UpdateCanvasSize();
                InvalidateSurface();
            }
            Debug.WriteLine($"DrawingLayer座標系同期: 総高さ={_totalHeight}, スケール={_currentScale}");
        }
    }

    /// <summary>
    /// 描画ストロークを表すクラス
    /// </summary>
    public class DrawingStroke : IDisposable
    {
        public SKPath DrawingPath { get; private set; }
        public SKPaint DrawingPaint { get; private set; }

        public DrawingStroke(SKPath path, SKPaint paint)
        {
            DrawingPath = new SKPath(path);
            DrawingPaint = new SKPaint
            {
                Color = paint.Color,
                StrokeWidth = paint.StrokeWidth,
                Style = paint.Style,
                IsAntialias = paint.IsAntialias,
                BlendMode = paint.BlendMode,
                Typeface = paint.Typeface,
                TextSize = paint.TextSize
            };
        }

        public void Dispose()
        {
            DrawingPath?.Dispose();
            DrawingPaint?.Dispose();
        }
    }

    /// <summary>
    /// 描画データの保存用クラス
    /// </summary>
    public class DrawingStrokeData
    {
        public List<SKPoint> Points { get; set; } = new List<SKPoint>();
        public SKColor Color { get; set; }
        public float StrokeWidth { get; set; }
        public SKPaintStyle Style { get; set; }
        public DrawingTool Tool { get; set; }

        // JSONシリアライゼーション用
        public uint ColorValue
        {
            get => BitConverter.ToUInt32(new byte[] { Color.Alpha, Color.Red, Color.Green, Color.Blue }, 0);
            set
            {
                var bytes = BitConverter.GetBytes(value);
                Color = new SKColor(bytes[1], bytes[2], bytes[3], bytes[0]);
            }
        }
    }
} 