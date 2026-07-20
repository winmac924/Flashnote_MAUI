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
        /// 画像のアスペクト比に合わせてキャンバスサイズを調整する
        /// </summary>
        private void AdjustCanvasSizeToImage()
        {
            if (_imageBitmap == null || _canvasView == null)
            {
                // 画像またはキャンバスがない場合はデフォルトサイズ
                if (_canvasView != null)
                {
                    _canvasView.WidthRequest = 400;
                    _canvasView.HeightRequest = 300;
                }
                return;
            }

            float imageAspect = (float)_imageBitmap.Width / _imageBitmap.Height;
            float canvasWidth, canvasHeight;

            if (imageAspect > 1.0f)
            {
                // 横長の画像
                canvasWidth = Math.Min(MAX_CANVAS_WIDTH, _imageBitmap.Width);
                canvasHeight = canvasWidth / imageAspect;
            }
            else
            {
                // 縦長の画像
                canvasHeight = Math.Min(MAX_CANVAS_HEIGHT, _imageBitmap.Height);
                canvasWidth = canvasHeight * imageAspect;
            }

            // 最小サイズを確保
            canvasWidth = Math.Max(canvasWidth, 200);
            canvasHeight = Math.Max(canvasHeight, 150);

            Debug.WriteLine($"キャンバスサイズ調整: 画像={_imageBitmap.Width}x{_imageBitmap.Height}, アスペクト比={imageAspect:F2}, キャンバス={canvasWidth:F0}x{canvasHeight:F0}");

            _canvasView.WidthRequest = canvasWidth;
            _canvasView.HeightRequest = canvasHeight;
            
            // キャンバスを再描画してサイズ変更を反映
            _canvasView.InvalidateSurface();
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
                    // キャンバスサイズを画像のアスペクト比に合わせて調整
                    AdjustCanvasSizeToImage();
                    Debug.WriteLine("キャンバスサイズ調整完了");
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
                
                // キャンバスサイズを画像のアスペクト比に合わせて調整
                if (_canvasView != null)
                {
                    AdjustCanvasSizeToImage();
                }
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
        /// 画像座標をキャンバス座標に変換する（iOS版に合わせて絶対座標を使用）
        /// </summary>
        private SKRect ImageToCanvasRect(SKRect imageRect, float canvasWidth, float canvasHeight)
        {
            // 画像の実際のサイズとキャンバスサイズの比率を計算
            float scaleX = 1.0f;
            float scaleY = 1.0f;

            if (_imageBitmap != null)
            {
                scaleX = canvasWidth / _imageBitmap.Width;
                scaleY = canvasHeight / _imageBitmap.Height;
            }

            var result = new SKRect(
                imageRect.Left * scaleX,
                imageRect.Top * scaleY,
                imageRect.Right * scaleX,
                imageRect.Bottom * scaleY
            );

            Debug.WriteLine($"画像座標変換: 入力={imageRect}, 画像サイズ={_imageBitmap?.Width}x{_imageBitmap?.Height}, キャンバスサイズ={canvasWidth}x{canvasHeight}, 拡大率=({scaleX:F3},{scaleY:F3}), 出力={result}");
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

            if (_imageBitmap != null)
            {
                scaleX = canvasWidth / _imageBitmap.Width;
                scaleY = canvasHeight / _imageBitmap.Height;
            }

            return new SKRect(
                canvasRect.Left / scaleX,
                canvasRect.Top / scaleY,
                canvasRect.Right / scaleX,
                canvasRect.Bottom / scaleY
            );
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
            using (var strokePaint = new SKPaint
            {
                Color = SKColors.Red,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 3
            })
            using (var fillPaint = new SKPaint
            {
                Color = new SKColor(255, 0, 0, 100), // 赤色
                Style = SKPaintStyle.Fill
            })
            {
                // 画像座標をキャンバス座標に変換して描画
                for (int i = 0; i < _selectionRects.Count; i++)
                {
                    var imageRect = _selectionRects[i];
                    var canvasRect = ImageToCanvasRect(imageRect, e.Info.Width, e.Info.Height);
                    Debug.WriteLine($"選択範囲描画: 画像座標={imageRect}, キャンバス={canvasRect}, キャンバスサイズ={e.Info.Width}x{e.Info.Height}");

                    // 透明な塗りつぶし
                    canvas.DrawRect(canvasRect, fillPaint);
                    // 赤い枠線
                    canvas.DrawRect(canvasRect, strokePaint);

                    // すべての枠にハンドルを表示
                    DrawResizeHandles(canvas, canvasRect);
                }

                if (_isDragging)
                {
                    var currentRect = SKRect.Create(
                        Math.Min(_startPoint.X, _endPoint.X),
                        Math.Min(_startPoint.Y, _endPoint.Y),
                        Math.Abs(_endPoint.X - _startPoint.X),
                        Math.Abs(_endPoint.Y - _startPoint.Y)
                    );

                    // 新しい枠の描画（透明な塗りつぶし + 赤い枠線）
                    canvas.DrawRect(currentRect, fillPaint);
                    canvas.DrawRect(currentRect, strokePaint);
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
        /// キャンバスタッチイベント
        /// </summary>
        private void OnCanvasTouch(object sender, SKTouchEventArgs e)
        {
            var point = e.Location;
            var canvasSize = _canvasView.CanvasSize;

            switch (e.ActionType)
            {
                case SKTouchAction.Pressed:
                    if (e.MouseButton == SKMouseButton.Right)
                    {
                        // 右クリックで削除メニュー表示
                        var clickedRectIndex = FindRectAtPoint(point, canvasSize.Width, canvasSize.Height);
                        if (clickedRectIndex >= 0)
                        {
                            var actualRect = ImageToCanvasRect(_selectionRects[clickedRectIndex], canvasSize.Width, canvasSize.Height);
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
                            _selectedRectIndex = rectIndex;

                            // ハンドルがクリックされたかチェック
                            var handleIndex = FindHandleAtPoint(point, canvasSize.Width, canvasSize.Height);
                            if (handleIndex >= 0)
                            {
                                // リサイズモード
                                _isResizing = true;
                                _resizeHandle = handleIndex;
                                Debug.WriteLine($"リサイズ開始: ハンドル={handleIndex}");
                            }
                            else
                            {
                                // 移動モード
                                _isMoving = true;
                                var canvasRect = ImageToCanvasRect(_selectionRects[rectIndex], canvasSize.Width, canvasSize.Height);
                                _dragOffset = new SKPoint(point.X - canvasRect.Left, point.Y - canvasRect.Top);
                                Debug.WriteLine($"既存の枠を選択: インデックス={rectIndex}");
                            }
                        }
                        else
                        {
                            // 新しい枠を作成
                            _isDragging = true;
                            _selectedRectIndex = -1;
                            _startPoint = point;
                            _endPoint = point;
                            Debug.WriteLine("新しい枠を作成開始");
                        }
                    }
                    break;

                case SKTouchAction.Moved:
                    if (_isMoving && _selectedRectIndex >= 0)
                    {
                        // 既存の枠を移動（サイズは保持）
                        var newLeft = point.X - _dragOffset.X;
                        var newTop = point.Y - _dragOffset.Y;

                        // 現在のキャンバス座標のサイズを取得
                        var currentCanvasRect = ImageToCanvasRect(_selectionRects[_selectedRectIndex], canvasSize.Width, canvasSize.Height);

                        // キャンバス境界内に制限（キャンバス座標のサイズを使用）
                        newLeft = Math.Max(0, Math.Min(newLeft, canvasSize.Width - currentCanvasRect.Width));
                        newTop = Math.Max(0, Math.Min(newTop, canvasSize.Height - currentCanvasRect.Height));

                        var newCanvasRect = new SKRect(newLeft, newTop, newLeft + currentCanvasRect.Width, newTop + currentCanvasRect.Height);
                        var newImageRect = CanvasToImageRect(newCanvasRect, canvasSize.Width, canvasSize.Height);

                        _selectionRects[_selectedRectIndex] = newImageRect;
                        Debug.WriteLine($"枠を移動: 新しい位置={newImageRect}, サイズ={newImageRect.Width}x{newImageRect.Height}");
                    }
                    else if (_isResizing && _selectedRectIndex >= 0)
                    {
                        // 既存の枠をリサイズ
                        var currentCanvasRect = ImageToCanvasRect(_selectionRects[_selectedRectIndex], canvasSize.Width, canvasSize.Height);
                        var newCanvasRect = currentCanvasRect;

                        switch (_resizeHandle)
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
                        _selectionRects[_selectedRectIndex] = newImageRect;
                        Debug.WriteLine($"枠をリサイズ: ハンドル={_resizeHandle}, 新しいサイズ={newImageRect.Width}x{newImageRect.Height}");
                    }
                    else if (_isDragging)
                    {
                        _endPoint = point;
                    }
                    break;

                case SKTouchAction.Released:
                    if (_isDragging)
                    {
                        var canvasRect = SKRect.Create(
                            Math.Min(_startPoint.X, _endPoint.X),
                            Math.Min(_startPoint.Y, _endPoint.Y),
                            Math.Abs(_endPoint.X - _startPoint.X),
                            Math.Abs(_endPoint.Y - _startPoint.Y)
                        );

                        if (!canvasRect.IsEmpty && canvasRect.Width > 5 && canvasRect.Height > 5)
                        {
                            // 重複チェック
                            var imageRect = CanvasToImageRect(canvasRect, canvasSize.Width, canvasSize.Height);
                            bool isOverlapping = _selectionRects.Any(existingRect =>
                            {
                                var existingCanvasRect = ImageToCanvasRect(existingRect, canvasSize.Width, canvasSize.Height);
                                return canvasRect.IntersectsWith(existingCanvasRect);
                            });

                            if (!isOverlapping)
                            {
                                _selectionRects.Add(imageRect);
                                Debug.WriteLine($"選択範囲追加: キャンバス座標={canvasRect}, 画像座標={imageRect}");
                            }
                            else
                            {
                                Debug.WriteLine("重複するため新しい枠を作成しませんでした");
                            }
                        }
                        _isDragging = false;
                    }
                    else if (_isMoving)
                    {
                        _isMoving = false;
                        Debug.WriteLine("枠の移動完了");
                    }
                    else if (_isResizing)
                    {
                        _isResizing = false;
                        _resizeHandle = -1;
                        Debug.WriteLine("枠のリサイズ完了");
                    }
                    break;
            }

            // 再描画
            _canvasView?.InvalidateSurface();
        }
        /// <summary>
        /// 指定されたポイントにある枠のインデックスを取得
        /// </summary>
        private int FindRectAtPoint(SKPoint point, float canvasWidth, float canvasHeight)
        {
            for (int i = 0; i < _selectionRects.Count; i++)
            {
                var canvasRect = ImageToCanvasRect(_selectionRects[i], canvasWidth, canvasHeight);
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
            if (_selectedRectIndex < 0 || _selectedRectIndex >= _selectionRects.Count)
                return -1;

            var canvasRect = ImageToCanvasRect(_selectionRects[_selectedRectIndex], canvasWidth, canvasHeight);

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
