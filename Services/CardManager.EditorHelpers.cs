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

        /// <summary>
        /// エディターにフォーカスを戻す
        /// </summary>
        private async Task RestoreFocusToEditor(Editor editor)
        {
            try
            {
                Debug.WriteLine($"=== RestoreFocusToEditor開始 ===");
                Debug.WriteLine($"エディター: {editor?.AutomationId ?? "null"}");
                
                if (editor == null)
                {
                    Debug.WriteLine("エディターがnullのため、フォーカス復元をスキップします");
                    return;
                }
                
                // 少し遅延させてからフォーカスを設定
                await Task.Delay(150);
                
                // メインスレッドでフォーカスを設定
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    try
                    {
                        // 複数回フォーカスを試行
                        for (int i = 0; i < 3; i++)
                        {
                            editor.Focus();
                            Debug.WriteLine($"フォーカス試行 {i + 1}: {editor.AutomationId}");
                            
                            // プラットフォーム固有のフォーカス設定
#if WINDOWS
                            if (editor.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.TextBox textBox)
                            {
                                textBox.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
                                Debug.WriteLine($"Windows TextBoxにフォーカスを設定しました (試行 {i + 1})");
                            }
#endif
                            
                            // 少し待ってから次の試行
                            if (i < 2) await Task.Delay(100);
                        }
                        
                        Debug.WriteLine($"フォーカス設定完了: {editor.AutomationId}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"フォーカス設定エラー: {ex.Message}");
                    }
                });
                
                Debug.WriteLine($"=== RestoreFocusToEditor終了 ===");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"フォーカス復元エラー: {ex.Message}");
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
    }
}
