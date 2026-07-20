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

                // ペースト処理中フラグを追加（クラスレベルで管理）
                var isPasting = false;

                // キーボードイベントを監視
                textBox.KeyDown += async (sender, args) =>
                {
                    Debug.WriteLine($"=== KeyDownイベント ===");
                    Debug.WriteLine($"Key: {args.Key}");
                    Debug.WriteLine($"IsMenuKeyDown: {args.KeyStatus.IsMenuKeyDown}");
                    Debug.WriteLine($"現在の_isCtrlDown: {_isCtrlDown}");
                    Debug.WriteLine($"現在の_isShiftDown: {_isShiftDown}");
                    Debug.WriteLine($"ペースト処理中: {isPasting}");

                    if (args.Key == VirtualKey.Control)
                    {
                        _isCtrlDown = true;
                        Debug.WriteLine($"Ctrlキー押下: _isCtrlDown={_isCtrlDown}");
                    }
                    if (args.Key == VirtualKey.Shift)
                    {
                        _isShiftDown = true;
                        Debug.WriteLine($"Shiftキー押下: _isShiftDown={_isShiftDown}");
                    }

                    // Ctrl+VまたはCtrl+Shift+Vが押された場合にペースト処理を実行
                    bool isCtrlVPressed = args.Key == VirtualKey.V && _isCtrlDown && !isPasting;
                    bool isCtrlShiftVPressed = args.Key == VirtualKey.V && _isCtrlDown && _isShiftDown && !isPasting;

                    if (isCtrlVPressed || isCtrlShiftVPressed)
                    {
                        Debug.WriteLine($"=== {(isCtrlShiftVPressed ? "Ctrl+Shift+V" : "Ctrl+V")}検出 ===");
                        Debug.WriteLine($"エディタ: {editor.AutomationId}");
                        Debug.WriteLine($"ペースト処理を開始します");

                        // デフォルトのペーストを確実にキャンセル
                        args.Handled = true;
                        isPasting = true;

                        // 非同期処理を同期的に待機して、デフォルトのペースト処理との競合を防ぐ
                        await HandleRichTextPasteAsync(editor);

                        // ペースト処理完了後にフラグをリセット
                        isPasting = false;
                        Debug.WriteLine("ペースト処理完了: フラグをリセット");
                    }

                    // Ctrl+Bが押された場合に太字処理を実行
                    if (args.Key == VirtualKey.B && _isCtrlDown)
                    {
                        Debug.WriteLine($"=== Ctrl+B検出 ===");
                        Debug.WriteLine($"エディタ: {editor.AutomationId}");
                        Debug.WriteLine($"太字処理を開始します");

                        // デフォルトの処理をキャンセル
                        args.Handled = true;

                        // 現在のエディタに対して太字処理を実行
                        InsertDecorationText(editor, "**", "**");
                        // フォーカスを復元
                        await Task.Delay(200);
                        await RestoreFocusToEditor(editor);
                    }

                    // Ctrl+Rが押された場合に赤色処理を実行
                    if (args.Key == VirtualKey.R && _isCtrlDown)
                    {
                        Debug.WriteLine($"=== Ctrl+R検出 ===");
                        Debug.WriteLine($"エディタ: {editor.AutomationId}");
                        Debug.WriteLine($"赤色処理を開始します");

                        // デフォルトの処理をキャンセル
                        args.Handled = true;

                        // 現在のエディタに対して赤色処理を実行
                        InsertDecorationText(editor, "{{red|", "}}");
                        // フォーカスを復元
                        await Task.Delay(200);
                        await RestoreFocusToEditor(editor);
                    }

                    // Ctrl+Lが押された場合に青色処理を実行
                    if (args.Key == VirtualKey.L && _isCtrlDown)
                    {
                        Debug.WriteLine($"=== Ctrl+L検出 ===");
                        Debug.WriteLine($"エディタ: {editor.AutomationId}");
                        Debug.WriteLine($"青色処理を開始します");

                        // デフォルトの処理をキャンセル
                        args.Handled = true;

                        // 現在のエディタに対して青色処理を実行
                        InsertDecorationText(editor, "{{blue|", "}}");
                        // フォーカスを復元
                        await Task.Delay(200);
                        await RestoreFocusToEditor(editor);
                    }

                    // Ctrl+Kが押された場合に穴埋め処理を実行（基本カードの表面エディターでのみ）
                    if (args.Key == VirtualKey.K && _isCtrlDown)
                    {
                        Debug.WriteLine($"=== Ctrl+K検出 ===");
                        Debug.WriteLine($"エディタ: {editor.AutomationId}");
                        Debug.WriteLine($"穴埋め処理を開始します");

                        // デフォルトの処理をキャンセル
                        args.Handled = true;

                        // 基本カードの表面エディターでのみ穴埋めを挿入
                        if (editor == _frontTextEditor)
                        {
                            Debug.WriteLine($"穴埋めを挿入: {editor.AutomationId}");
                            InsertBlankText(editor);
                            // フォーカスを復元
                            await Task.Delay(200);
                            await RestoreFocusToEditor(editor);
                        }
                        else
                        {
                            Debug.WriteLine("穴埋めは基本カードの表面でのみ使用できます");
                        }
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
                    if (args.Key == VirtualKey.Shift)
                    {
                        _isShiftDown = false;
                        Debug.WriteLine($"Shiftキー離上: _isShiftDown={_isShiftDown}");
                    }
                };

                // Pasteイベントも監視して、デフォルトのペースト処理を確実にキャンセル
                textBox.Paste += (sender, args) =>
                {
                    Debug.WriteLine($"=== Pasteイベント ===");
                    Debug.WriteLine($"ペースト処理中フラグ: {isPasting}");

                    // カスタムペースト処理が実行中の場合は、デフォルトのペーストをキャンセル
                    if (isPasting)
                    {
                        Debug.WriteLine("カスタムペースト処理中なので、デフォルトのペーストをキャンセルします");
                        args.Handled = true;
                    }
                    else
                    {
                        // カスタム処理が実行されていない場合でも、Ctrl+VやCtrl+Shift+Vの場合は
                        // デフォルトのペーストをキャンセルして、カスタム処理を実行
                        Debug.WriteLine("カスタムペースト処理を実行します");
                        args.Handled = true;
                        isPasting = true;
                        _ = HandleRichTextPasteAsync(editor).ContinueWith(t =>
                        {
                            isPasting = false;
                            Debug.WriteLine("カスタムペースト処理完了: フラグをリセット");
                        });
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

                // ペースト処理開始前に少し遅延を入れて、デフォルトのペースト処理との競合を防ぐ
                await Task.Delay(10);

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
                    return; // リッチテキスト処理が成功した場合はここで終了
                }
                else
                {
                    Debug.WriteLine("リッチテキストが取得できませんでした");
                    // リッチテキストが取得できない場合はフォールバック処理を実行
                    await FallbackToPlainTextPaste(editor);
                }

                Debug.WriteLine($"=== HandleRichTextPasteAsync終了 ===");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"統合ペースト処理エラー: {ex.Message}");
                Debug.WriteLine($"スタックトレース: {ex.StackTrace}");

                // エラー時は通常のペーストにフォールバック
                await FallbackToPlainTextPaste(editor);
            }
        }
        /// <summary>
        /// プレーンテキストペーストのフォールバック処理
        /// </summary>
        private async Task FallbackToPlainTextPaste(Editor editor)
        {
            try
            {
                Debug.WriteLine($"=== FallbackToPlainTextPaste開始 ===");

                // フォールバック処理開始前に少し遅延を入れて、デフォルトのペースト処理との競合を防ぐ
                await Task.Delay(10);

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

                    Debug.WriteLine("フォールバックペースト処理完了");
                }
                else
                {
                    Debug.WriteLine("クリップボードにテキストがありません");
                }

                Debug.WriteLine($"=== FallbackToPlainTextPaste終了 ===");
            }
            catch (Exception fallbackEx)
            {
                Debug.WriteLine($"フォールバックペースト処理エラー: {fallbackEx.Message}");
                Debug.WriteLine($"スタックトレース: {fallbackEx.StackTrace}");
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
    }
}
