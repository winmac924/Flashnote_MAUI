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
        private BlankInfo CheckIfCursorInBlank(Editor editor)
        {
            var text = editor.Text ?? string.Empty; var cursor = editor.CursorPosition;
            var regex = new Regex(@"<<blank\|.*?>>", RegexOptions.Singleline);
            foreach (Match m in regex.Matches(text))
            {
                if (cursor >= m.Index && cursor <= m.Index + m.Length)
                {
                    var inner = m.Value.Substring("<<blank|".Length, m.Value.Length - "<<blank|".Length - ">>".Length);
                    return new BlankInfo { IsInBlank = true, StartIndex = m.Index, EndIndex = m.Index + m.Length, InnerText = inner };
                }
            }
            return new BlankInfo { IsInBlank = false };
        }
        private Task RemoveBlank(Editor editor, BlankInfo info)
        {
            try
            {
                var t = editor.Text ?? string.Empty;
                var newT = t.Remove(info.StartIndex, info.EndIndex - info.StartIndex).Insert(info.StartIndex, info.InnerText);
                editor.Text = newT;
                editor.CursorPosition = Math.Min(info.StartIndex + (info.InnerText?.Length ?? 0), newT.Length);
            }
            catch { }
            return Task.CompletedTask;
        }

        // 装飾検出用
        private class DecorationInfo { public bool IsInDecoration; public int StartIndex; public int EndIndex; public string InnerText; }
        private DecorationInfo CheckIfCursorInDecoration(Editor editor, string prefix, string suffix)
        {
            var text = editor.Text ?? string.Empty; int cursor = editor.CursorPosition;
            int searchStart = text.LastIndexOf(prefix, Math.Max(cursor, 0), StringComparison.Ordinal);
            if (searchStart >= 0)
            {
                int end = text.IndexOf(suffix, searchStart + prefix.Length, StringComparison.Ordinal);
                if (end > searchStart && cursor >= searchStart + prefix.Length && cursor <= end)
                {
                    var inner = text.Substring(searchStart + prefix.Length, end - (searchStart + prefix.Length));
                    return new DecorationInfo { IsInDecoration = true, StartIndex = searchStart, EndIndex = end + suffix.Length, InnerText = inner };
                }
            }
            return new DecorationInfo { IsInDecoration = false };
        }
        private Task RemoveDecoration(Editor editor, DecorationInfo info, string prefix, string suffix)
        {
            try
            {
                var t = editor.Text ?? string.Empty;
                var newT = t.Remove(info.StartIndex, info.EndIndex - info.StartIndex).Insert(info.StartIndex, info.InnerText);
                editor.Text = newT;
                editor.CursorPosition = Math.Min(info.StartIndex + (info.InnerText?.Length ?? 0), newT.Length);
            }
            catch { }
            return Task.CompletedTask;
        }
        /// <summary>
        /// 穴埋めテキストを挿入または解除（かっこがある場合は削除）
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
                    // 選択範囲内の複数のかっこを一括変換
                    var conversionResult = await ConvertMultipleParenthesesInSelection(editor);
                    
                    if (!conversionResult)
                    {
                        // 複数かっこ変換が失敗した場合は、従来の単一選択範囲処理
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
                }
                else
                {
                    // 選択されたテキストがない場合は、カーソル位置が穴埋め内かチェック
                    var blankInfo = CheckIfCursorInBlank(editor);
                    
                    if (blankInfo.IsInBlank)
                    {
                        // 穴埋め内にカーソルがある場合は穴埋めを解除
                        await RemoveBlank(editor, blankInfo);
                    }
                    else
                    {
                        // カーソル位置がかっこ内にあるかチェック
                        var parenthesesInfo = CheckIfCursorInParentheses(editor);
                        
                        if (parenthesesInfo.IsInParentheses)
                        {
                            // かっこ内にカーソルがある場合は穴埋めに変換
                            await ConvertParenthesesToBlank(editor, parenthesesInfo);
                        }
                        else
                        {
                            // 穴埋め内にない場合は穴埋めタグを挿入
                            string insertText = "<<blank|>>";
                            await InsertAtCursor(editor, insertText, 8); // "<<blank|" の後にカーソルを配置
                            
                            // 穴埋めのテキスト部分にカーソルを配置
                            Debug.WriteLine($"穴埋め挿入完了: '{insertText}', カーソル位置: {editor.CursorPosition}");
                        }
                    }
                }

                // プレビューを更新
                UpdatePreviewForEditor(editor);
                
                // フォーカスをエディターに戻す
                await RestoreFocusToEditor(editor);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"穴埋めテキスト挿入中にエラー: {ex.Message}");
                // エラーが発生した場合はシンプルな挿入に戻る
                string insertText = "<<blank|>>";
                await InsertAtCursor(editor, insertText, 8);
                UpdatePreviewForEditor(editor);
                
                // エラー時もフォーカスを戻す
                await RestoreFocusToEditor(editor);
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

        /// <summary>
        /// かっこ内にカーソルがあるかどうかを判定する情報
        /// </summary>
        private class ParenthesesInfo
        {
            public bool IsInParentheses { get; set; }
            public int StartIndex { get; set; }
            public int EndIndex { get; set; }
            public string InnerText { get; set; }
            public char OpenChar { get; set; }
            public char CloseChar { get; set; }
        }
        /// <summary>
        /// カーソル位置がかっこ内にあるかどうかをチェック（一番外側のかっこのみ）
        /// </summary>
        private ParenthesesInfo CheckIfCursorInParentheses(Editor editor)
        {
            try
            {
                var cursorPosition = editor.CursorPosition;
                var text = editor.Text ?? "";
                
                Debug.WriteLine($"=== CheckIfCursorInParentheses開始 ===");
                Debug.WriteLine($"カーソル位置: {cursorPosition}");
                Debug.WriteLine($"テキスト: '{text}'");
                
                // 対応するかっこのペア
                var parenthesesPairs = new (char open, char close)[]
                {
                    ('(', ')'),
                    ('（', '）'),
                    ('[', ']'),
                    ('［', '］'),
                    ('{', '}'),
                    ('｛', '｝')
                };
                
                // 既存の穴埋めタグの位置を記録（除外対象）
                var excludedRanges = new List<(int start, int end)>();
                var blankPattern = @"<<blank\|[^>]*>>";
                var matches = Regex.Matches(text, blankPattern);
                foreach (Match match in matches)
                {
                    excludedRanges.Add((match.Index, match.Index + match.Length));
                    Debug.WriteLine($"単一変換除外範囲: {match.Index}-{match.Index + match.Length} ('{match.Value}')");
                }
                
                // カーソル位置から前方に最も近い開きかっこを探す
                int closestOpenIndex = -1;
                char closestOpenChar = '\0';
                char closestCloseChar = '\0';
                
                for (int i = cursorPosition - 1; i >= 0; i--)
                {
                    // 除外範囲内かチェック
                    bool isInExcludedRange = excludedRanges.Any(range => 
                        i >= range.start && i < range.end);
                    
                    if (isInExcludedRange)
                    {
                        Debug.WriteLine($"単一変換: 除外範囲内のかっこをスキップ: 位置 {i}");
                        continue;
                    }
                    
                    foreach (var (open, close) in parenthesesPairs)
                    {
                        if (text[i] == open)
                        {
                            closestOpenIndex = i;
                            closestOpenChar = open;
                            closestCloseChar = close;
                            break;
                        }
                    }
                    if (closestOpenIndex != -1) break;
                }
                
                if (closestOpenIndex == -1)
                {
                    Debug.WriteLine("開きかっこが見つかりません");
                    return new ParenthesesInfo { IsInParentheses = false };
                }
                
                Debug.WriteLine($"開きかっこ位置: {closestOpenIndex}, 文字: '{closestOpenChar}'");
                
                // 開きかっこから後方に対応する閉じかっこを探す
                int closeIndex = -1;
                int nestedLevel = 0;
                
                for (int i = closestOpenIndex + 1; i < text.Length; i++)
                {
                    // 除外範囲内かチェック
                    bool isInExcludedRange = excludedRanges.Any(range => 
                        i >= range.start && i < range.end);
                    
                    if (isInExcludedRange)
                    {
                        Debug.WriteLine($"単一変換: 除外範囲内の文字をスキップ: 位置 {i}, 文字 '{text[i]}'");
                        continue;
                    }
                    
                    if (text[i] == closestOpenChar)
                    {
                        nestedLevel++;
                    }
                    else if (text[i] == closestCloseChar)
                    {
                        if (nestedLevel == 0)
                        {
                            closeIndex = i;
                            break;
                        }
                        else
                        {
                            nestedLevel--;
                        }
                    }
                }
                
                if (closeIndex == -1)
                {
                    Debug.WriteLine("対応する閉じかっこが見つかりません");
                    return new ParenthesesInfo { IsInParentheses = false };
                }
                
                Debug.WriteLine($"閉じかっこ位置: {closeIndex}, 文字: '{closestCloseChar}'");
                
                // カーソルがかっこ内にあるかチェック
                bool isInParentheses = cursorPosition > closestOpenIndex && cursorPosition < closeIndex;
                
                if (isInParentheses)
                {
                    var innerText = text.Substring(closestOpenIndex + 1, closeIndex - (closestOpenIndex + 1));
                    Debug.WriteLine($"かっこ内にカーソルがあります: '{innerText}'");
                    return new ParenthesesInfo
                    {
                        IsInParentheses = true,
                        StartIndex = closestOpenIndex,
                        EndIndex = closeIndex + 1,
                        InnerText = innerText,
                        OpenChar = closestOpenChar,
                        CloseChar = closestCloseChar
                    };
                }
                else
                {
                    Debug.WriteLine("カーソルはかっこ内にありません");
                    return new ParenthesesInfo { IsInParentheses = false };
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"かっこチェックエラー: {ex.Message}");
                return new ParenthesesInfo { IsInParentheses = false };
            }
        }
        /// <summary>
        /// かっこを穴埋めに変換する
        /// </summary>
        private async Task ConvertParenthesesToBlank(Editor editor, ParenthesesInfo parenthesesInfo)
        {
            try
            {
                Debug.WriteLine($"=== ConvertParenthesesToBlank開始 ===");
                Debug.WriteLine($"かっこ変換: '{parenthesesInfo.InnerText}'");
                Debug.WriteLine($"開始位置: {parenthesesInfo.StartIndex}, 終了位置: {parenthesesInfo.EndIndex}");
                
                // 既に穴埋めタグになっているかチェック
                if (parenthesesInfo.InnerText.Contains("<<blank|") || parenthesesInfo.InnerText.Contains(">>"))
                {
                    Debug.WriteLine($"既に穴埋めタグになっているため変換をスキップ: '{parenthesesInfo.InnerText}'");
                    return;
                }
                
                // 穴埋めタグで囲む
                string decoratedText = "<<blank|" + parenthesesInfo.InnerText + ">>";
                
                var text = editor.Text ?? "";
                var newText = text.Remove(parenthesesInfo.StartIndex, parenthesesInfo.EndIndex - parenthesesInfo.StartIndex)
                                 .Insert(parenthesesInfo.StartIndex, decoratedText);
                
                editor.Text = newText;
                
                // カーソル位置を穴埋めタグの後に配置
                var newCursorPosition = parenthesesInfo.StartIndex + decoratedText.Length;
                editor.CursorPosition = Math.Min(newCursorPosition, editor.Text.Length);
                
                Debug.WriteLine($"かっこ変換完了: '{parenthesesInfo.InnerText}' → '{decoratedText}'");
                Debug.WriteLine($"=== ConvertParenthesesToBlank終了 ===");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"かっこ変換エラー: {ex.Message}");
            }
        }
        /// <summary>
        /// 選択範囲内の複数のかっこを一括変換する
        /// </summary>
        private async Task<bool> ConvertMultipleParenthesesInSelection(Editor editor)
        {
            try
            {
                Debug.WriteLine($"=== ConvertMultipleParenthesesInSelection開始 ===");
                
                // 選択範囲を取得
                int start = GetSelectionStart(editor);
                int length = GetSelectionLength(editor);
                string text = editor.Text ?? "";
                
                if (start < 0 || length <= 0 || start + length > text.Length)
                {
                    Debug.WriteLine("選択範囲の取得に失敗しました");
                    return false;
                }
                
                string selectedText = text.Substring(start, length);
                Debug.WriteLine($"選択範囲: '{selectedText}' (位置: {start}-{start + length})");
                
                // 選択範囲内のかっこを検出
                var parenthesesList = FindAllParenthesesInText(selectedText, start);
                
                if (parenthesesList.Count == 0)
                {
                    Debug.WriteLine("選択範囲内にかっこが見つかりませんでした");
                    return false;
                }
                
                Debug.WriteLine($"検出されたかっこ数: {parenthesesList.Count}");
                foreach (var parentheses in parenthesesList)
                {
                    Debug.WriteLine($"かっこ: '{parentheses.InnerText}' (位置: {parentheses.StartIndex}-{parentheses.EndIndex})");
                }
                
                // 重複する変換を除外（同じ位置のかっこは1つだけ変換）
                var uniqueParentheses = new List<ParenthesesInfo>();
                foreach (var parentheses in parenthesesList)
                {
                    bool isDuplicate = uniqueParentheses.Any(p => 
                        p.StartIndex == parentheses.StartIndex && 
                        p.EndIndex == parentheses.EndIndex);
                    
                    // 既に穴埋めタグになっているかチェック
                    bool isAlreadyBlank = parentheses.InnerText.Contains("<<blank|") || 
                                        parentheses.InnerText.Contains(">>");
                    
                    if (!isDuplicate && !isAlreadyBlank)
                    {
                        uniqueParentheses.Add(parentheses);
                    }
                    else
                    {
                        if (isDuplicate)
                        {
                            Debug.WriteLine($"重複するかっこを除外: '{parentheses.InnerText}' (位置: {parentheses.StartIndex}-{parentheses.EndIndex})");
                        }
                        if (isAlreadyBlank)
                        {
                            Debug.WriteLine($"既に穴埋めタグになっているかっこを除外: '{parentheses.InnerText}' (位置: {parentheses.StartIndex}-{parentheses.EndIndex})");
                        }
                    }
                }
                
                // 後ろから順に変換（インデックスがずれるのを防ぐため）
                var sortedParentheses = uniqueParentheses.OrderByDescending(p => p.StartIndex).ToList();
                string newText = text;
                int totalOffset = 0;
                
                foreach (var parentheses in sortedParentheses)
                {
                    // 変換対象のテキストが既に穴埋めタグになっていないか最終チェック
                    if (parentheses.InnerText.Contains("<<blank|") || parentheses.InnerText.Contains(">>"))
                    {
                        Debug.WriteLine($"変換対象が既に穴埋めタグのためスキップ: '{parentheses.InnerText}'");
                        continue;
                    }
                    
                    // 穴埋めタグで囲む
                    string decoratedText = "<<blank|" + parentheses.InnerText + ">>";
                    
                    // オフセットを考慮した位置を計算
                    int adjustedStart = parentheses.StartIndex + totalOffset;
                    int adjustedEnd = parentheses.EndIndex + totalOffset;
                    
                    // テキストを置換
                    newText = newText.Remove(adjustedStart, adjustedEnd - adjustedStart)
                                     .Insert(adjustedStart, decoratedText);
                    
                    // オフセットを更新
                    totalOffset += decoratedText.Length - (adjustedEnd - adjustedStart);
                    
                    Debug.WriteLine($"変換: '{parentheses.InnerText}' → '{decoratedText}' (位置: {adjustedStart}-{adjustedStart + decoratedText.Length})");
                }
                
                // エディターのテキストを更新
                editor.Text = newText;
                
                // カーソル位置を選択範囲の最後に配置
                int newCursorPosition = start + totalOffset;
                editor.CursorPosition = Math.Min(newCursorPosition, editor.Text.Length);
                
                Debug.WriteLine($"複数かっこ変換完了: {parenthesesList.Count}個のかっこを変換");
                Debug.WriteLine($"=== ConvertMultipleParenthesesInSelection終了 ===");
                
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"複数かっこ変換エラー: {ex.Message}");
                return false;
            }
        }
        /// <summary>
        /// テキスト内のすべてのかっこを検出する
        /// </summary>
        private List<ParenthesesInfo> FindAllParenthesesInText(string text, int baseOffset = 0)
        {
            var result = new List<ParenthesesInfo>();
            
            // 対応するかっこのペア
            var parenthesesPairs = new (char open, char close)[]
            {
                ('(', ')'),
                ('（', '）'),
                ('[', ']'),
                ('［', '］'),
                ('{', '}'),
                ('｛', '｝')
            };
            
            // 既存の穴埋めタグの位置を記録（除外対象）
            var excludedRanges = new List<(int start, int end)>();
            
            // より厳密な穴埋めタグのパターン（<<blank|...>>の形式のみ）
            var blankPattern = @"<<blank\|[^>]*>>";
            var matches = Regex.Matches(text, blankPattern);
            foreach (Match match in matches)
            {
                excludedRanges.Add((match.Index, match.Index + match.Length));
                Debug.WriteLine($"除外範囲: {match.Index}-{match.Index + match.Length} ('{match.Value}')");
            }
            
            // 不完全な穴埋めタグも除外（<<blank|...のような不完全な形式）
            var incompleteBlankPattern = @"<<blank\|[^>]*$";
            var incompleteMatches = Regex.Matches(text, incompleteBlankPattern);
            foreach (Match match in incompleteMatches)
            {
                excludedRanges.Add((match.Index, match.Index + match.Length));
                Debug.WriteLine($"不完全な除外範囲: {match.Index}-{match.Index + match.Length} ('{match.Value}')");
            }
            
            // 各かっこのペアについて検索
            foreach (var (open, close) in parenthesesPairs)
            {
                int currentIndex = 0;
                
                while (currentIndex < text.Length)
                {
                    // 開きかっこを探す
                    int openIndex = text.IndexOf(open, currentIndex);
                    if (openIndex == -1) break;
                    
                    // 除外範囲内かチェック
                    bool isInExcludedRange = excludedRanges.Any(range => 
                        openIndex >= range.start && openIndex < range.end);
                    
                    if (isInExcludedRange)
                    {
                        Debug.WriteLine($"除外範囲内のかっこをスキップ: 位置 {openIndex}");
                        currentIndex = openIndex + 1;
                        continue;
                    }
                    
                    // 対応する閉じかっこを探す
                    int closeIndex = -1;
                    int nestedLevel = 0;
                    
                    for (int i = openIndex + 1; i < text.Length; i++)
                    {
                        if (text[i] == open)
                        {
                            nestedLevel++;
                        }
                        else if (text[i] == close)
                        {
                            if (nestedLevel == 0)
                            {
                                closeIndex = i;
                                break;
                            }
                            else
                            {
                                nestedLevel--;
                            }
                        }
                    }
                    
                    if (closeIndex != -1)
                    {
                        // 閉じかっこも除外範囲内かチェック
                        bool isCloseInExcludedRange = excludedRanges.Any(range => 
                            closeIndex >= range.start && closeIndex < range.end);
                        
                        if (isCloseInExcludedRange)
                        {
                            Debug.WriteLine($"除外範囲内の閉じかっこをスキップ: 位置 {closeIndex}");
                            currentIndex = openIndex + 1;
                            continue;
                        }
                        
                        // かっこが見つかった
                        var innerText = text.Substring(openIndex + 1, closeIndex - (openIndex + 1));
                        
                        result.Add(new ParenthesesInfo
                        {
                            IsInParentheses = true,
                            StartIndex = openIndex + baseOffset,
                            EndIndex = closeIndex + 1 + baseOffset,
                            InnerText = innerText,
                            OpenChar = open,
                            CloseChar = close
                        });
                        
                        Debug.WriteLine($"かっこ検出: '{innerText}' (位置: {openIndex + baseOffset}-{closeIndex + 1 + baseOffset})");
                        
                        // 次の検索位置を設定
                        currentIndex = closeIndex + 1;
                    }
                    else
                    {
                        // 対応する閉じかっこが見つからない場合は次の位置から検索
                        currentIndex = openIndex + 1;
                    }
                }
            }
            
            return result;
        }

                /// <summary>
        /// 装飾文字を挿入または解除するヘルパーメソッド
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
                    // 色装飾の場合は色変更機能をチェック
                    if (IsColorDecoration(prefix))
                    {
                        var colorChangeInfo = CheckIfCursorInColorDecoration(editor);
                        if (colorChangeInfo.IsInColorDecoration)
                        {
                            // 色装飾内にカーソルがある場合は色を変更
                            await ChangeColorDecoration(editor, colorChangeInfo, prefix);
                        }
                        else
                        {
                            // 色装飾内にない場合は装飾タグを挿入
                            string insertText = prefix + suffix;
                            await InsertAtCursor(editor, insertText, prefix.Length);
                            
                            // 色装飾のテキスト部分にカーソルを配置
                            Debug.WriteLine($"色装飾挿入完了: '{insertText}', カーソル位置: {editor.CursorPosition}");
                        }
                    }
                    else
                    {
                        // 通常の装飾文字の場合は従来の処理
                        var decorationInfo = CheckIfCursorInDecoration(editor, prefix, suffix);
                        
                        if (decorationInfo.IsInDecoration)
                        {
                            // 装飾文字内にカーソルがある場合は装飾を解除
                            await RemoveDecoration(editor, decorationInfo, prefix, suffix);
                        }
                        else
                        {
                            // 装飾文字内にない場合は装飾タグを挿入
                            string insertText = prefix + suffix;
                            await InsertAtCursor(editor, insertText, prefix.Length);
                            
                            // 装飾文字の間にカーソルを配置
                            Debug.WriteLine($"装飾文字挿入完了: '{insertText}', カーソル位置: {editor.CursorPosition}");
                        }
                    }
                }

                // プレビューを更新
                UpdatePreviewForEditor(editor);
                
                // フォーカスをエディターに戻す
                await RestoreFocusToEditor(editor);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"装飾テキスト挿入中にエラー: {ex.Message}");
                // エラーが発生した場合はシンプルな挿入に戻る
                string insertText = prefix + suffix;
                await InsertAtCursor(editor, insertText, prefix.Length);
                UpdatePreviewForEditor(editor);
                
                // エラー時もフォーカスを戻す
                await RestoreFocusToEditor(editor);
            }
        }
        /// <summary>
        /// 色装飾かどうかを判定
        /// </summary>
        private bool IsColorDecoration(string prefix)
        {
            return prefix.StartsWith("{{") && prefix.EndsWith("|");
        }

        /// <summary>
        /// 色装飾内にカーソルがあるかどうかを判定する情報
        /// </summary>
        private class ColorDecorationInfo
        {
            public bool IsInColorDecoration { get; set; }
            public int StartIndex { get; set; }
            public int EndIndex { get; set; }
            public string InnerText { get; set; }
            public string CurrentColor { get; set; }
        }
        /// <summary>
        /// カーソル位置が色装飾内にあるかどうかをチェック
        /// </summary>
        private ColorDecorationInfo CheckIfCursorInColorDecoration(Editor editor)
        {
            try
            {
                var cursorPosition = editor.CursorPosition;
                var text = editor.Text ?? "";
                
                Debug.WriteLine($"=== CheckIfCursorInColorDecoration開始 ===");
                Debug.WriteLine($"カーソル位置: {cursorPosition}");
                Debug.WriteLine($"テキスト: '{text}'");
                
                // カーソル位置から前方に "{{" を探す
                int colorStart = -1;
                for (int i = cursorPosition - 1; i >= 0; i--)
                {
                    if (i + 2 <= text.Length && text.Substring(i, 2) == "{{")
                    {
                        colorStart = i;
                        break;
                    }
                }
                
                if (colorStart == -1)
                {
                    Debug.WriteLine("{{が見つかりません");
                    return new ColorDecorationInfo { IsInColorDecoration = false };
                }
                
                Debug.WriteLine($"{{開始位置: {colorStart}");
                
                // "{{" 開始位置から後方に "}}" を探す
                int colorEnd = -1;
                for (int i = colorStart + 2; i <= text.Length - 2; i++)
                {
                    if (text.Substring(i, 2) == "}}")
                    {
                        colorEnd = i;
                        break;
                    }
                }
                
                if (colorEnd == -1)
                {
                    Debug.WriteLine("}}が見つかりません");
                    return new ColorDecorationInfo { IsInColorDecoration = false };
                }
                
                Debug.WriteLine($"}}開始位置: {colorEnd}");
                
                // "{{" と "}}" の間のテキストを取得
                var colorText = text.Substring(colorStart + 2, colorEnd - (colorStart + 2));
                
                // 色名とテキストを分離（例: "red|text" → "red" と "text"）
                var pipeIndex = colorText.IndexOf('|');
                if (pipeIndex == -1)
                {
                    Debug.WriteLine("|が見つかりません");
                    return new ColorDecorationInfo { IsInColorDecoration = false };
                }
                
                var currentColor = colorText.Substring(0, pipeIndex);
                var innerText = colorText.Substring(pipeIndex + 1);
                
                Debug.WriteLine($"現在の色: '{currentColor}', 内側テキスト: '{innerText}'");
                
                // カーソルが色装飾内にあるかチェック
                bool isInColorDecoration = cursorPosition >= colorStart + 2 + pipeIndex + 1 && cursorPosition <= colorEnd;
                
                if (isInColorDecoration)
                {
                    Debug.WriteLine($"色装飾内にカーソルがあります: '{innerText}'");
                    return new ColorDecorationInfo
                    {
                        IsInColorDecoration = true,
                        StartIndex = colorStart,
                        EndIndex = colorEnd + 2,
                        InnerText = innerText,
                        CurrentColor = currentColor
                    };
                }
                else
                {
                    Debug.WriteLine("カーソルは色装飾内にありません");
                    return new ColorDecorationInfo { IsInColorDecoration = false };
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"色装飾チェックエラー: {ex.Message}");
                return new ColorDecorationInfo { IsInColorDecoration = false };
            }
        }
        /// <summary>
        /// 色装飾の色を変更する
        /// </summary>
        private async Task ChangeColorDecoration(Editor editor, ColorDecorationInfo colorInfo, string newPrefix)
        {
            try
            {
                Debug.WriteLine($"=== ChangeColorDecoration開始 ===");
                Debug.WriteLine($"現在の色: '{colorInfo.CurrentColor}'");
                Debug.WriteLine($"新しい色: '{GetColorFromPrefix(newPrefix)}'");
                Debug.WriteLine($"テキスト: '{colorInfo.InnerText}'");
                Debug.WriteLine($"開始位置: {colorInfo.StartIndex}, 終了位置: {colorInfo.EndIndex}");
                
                var newColor = GetColorFromPrefix(newPrefix);
                
                // 同じ色の場合は装飾を解除
                if (colorInfo.CurrentColor.Equals(newColor, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.WriteLine("同じ色が押されたため、色装飾を解除します");
                    await RemoveColorDecoration(editor, colorInfo);
                    return;
                }
                
                // 異なる色の場合は色を変更
                var newColorText = $"{{{{{newColor}|{colorInfo.InnerText}}}}}";
                
                var text = editor.Text ?? "";
                var newText = text.Remove(colorInfo.StartIndex, colorInfo.EndIndex - colorInfo.StartIndex)
                                 .Insert(colorInfo.StartIndex, newColorText);
                
                editor.Text = newText;
                
                // カーソル位置を色変更後のテキスト内に配置
                var newCursorPosition = colorInfo.StartIndex + 2 + newColor.Length + 1 + colorInfo.InnerText.Length;
                editor.CursorPosition = Math.Min(newCursorPosition, editor.Text.Length);
                
                Debug.WriteLine($"色変更完了: '{colorInfo.InnerText}'");
                Debug.WriteLine($"=== ChangeColorDecoration終了 ===");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"色変更エラー: {ex.Message}");
            }
        }
        /// <summary>
        /// 色装飾を解除する
        /// </summary>
        private async Task RemoveColorDecoration(Editor editor, ColorDecorationInfo colorInfo)
        {
            try
            {
                Debug.WriteLine($"=== RemoveColorDecoration開始 ===");
                Debug.WriteLine($"解除する色装飾: '{colorInfo.CurrentColor}'");
                Debug.WriteLine($"テキスト: '{colorInfo.InnerText}'");
                Debug.WriteLine($"開始位置: {colorInfo.StartIndex}, 終了位置: {colorInfo.EndIndex}");
                
                var text = editor.Text ?? "";
                
                // 色装飾タグを削除して、内側のテキストのみを残す
                var newText = text.Remove(colorInfo.StartIndex, colorInfo.EndIndex - colorInfo.StartIndex)
                                 .Insert(colorInfo.StartIndex, colorInfo.InnerText);
                
                editor.Text = newText;
                
                // カーソル位置を解除後のテキスト内に配置
                var newCursorPosition = colorInfo.StartIndex + colorInfo.InnerText.Length;
                editor.CursorPosition = Math.Min(newCursorPosition, editor.Text.Length);
                
                Debug.WriteLine($"色装飾解除完了: '{colorInfo.InnerText}'");
                Debug.WriteLine($"=== RemoveColorDecoration終了 ===");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"色装飾解除エラー: {ex.Message}");
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
        /// カラー装飾のプレフィックスから色名を取得
        /// </summary>
        private string GetColorFromPrefix(string prefix)
        {
            // 例: {{red| → red
            if (string.IsNullOrEmpty(prefix)) return string.Empty;
            var trimmed = prefix.Trim('{');
            var pipeIndex = trimmed.IndexOf('|');
            if (pipeIndex >= 0)
            {
                return trimmed.Substring(0, pipeIndex);
            }
            return trimmed;
        }
    }
}
