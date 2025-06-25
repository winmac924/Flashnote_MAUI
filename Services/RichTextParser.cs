using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;

namespace Flashnote.Services
{
    /// <summary>
    /// リッチテキスト（HTML/RTF）をMarkdown形式に変換するサービス
    /// </summary>
    public static class RichTextParser
    {
        /// <summary>
        /// クリップボードからリッチテキストを取得してMarkdownに変換
        /// </summary>
        public static async Task<string> GetRichTextAsMarkdownAsync()
        {
            try
            {
                // プラットフォーム固有のリッチテキスト取得を試行
                var richText = await GetPlatformRichTextAsync();
                
                if (!string.IsNullOrEmpty(richText))
                {
                    // HTMLまたはRTFをMarkdownに変換
                    if (richText.TrimStart().StartsWith("<") || richText.Contains("<html"))
                    {
                        return ConvertHtmlToMarkdown(richText);
                    }
                    else if (richText.StartsWith("{\\rtf"))
                    {
                        return ConvertRtfToMarkdown(richText);
                    }
                }
                
                // リッチテキストが取得できない場合は通常のテキストを取得
                if (Clipboard.HasText)
                {
                    var plainText = await Clipboard.GetTextAsync();
                    // 通常のテキストでも改行は保持する
                    return plainText ?? "";
                }
                
                return "";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"リッチテキスト取得エラー: {ex.Message}");
                
                // エラー時は通常のテキストにフォールバック
                try
                {
                    if (Clipboard.HasText)
                    {
                        var plainText = await Clipboard.GetTextAsync();
                        // フォールバック時でも改行を保持
                        return plainText ?? "";
                    }
                }
                catch
                {
                    // 無視
                }
                
                return "";
            }
        }
        
        /// <summary>
        /// プラットフォーム固有のリッチテキスト取得
        /// </summary>
        private static async Task<string> GetPlatformRichTextAsync()
        {
#if WINDOWS
            return await GetWindowsRichTextAsync();
#elif ANDROID
            return await GetAndroidRichTextAsync();
#elif IOS
            return await GetIosRichTextAsync();
#else
            return "";
#endif
        }

#if WINDOWS
        /// <summary>
        /// Windows固有のリッチテキスト取得
        /// </summary>
        private static async Task<string> GetWindowsRichTextAsync()
        {
            try
            {
                await Task.Delay(1); // 非同期メソッドにするため
                
                // Windows.ApplicationModel.DataTransfer.Clipboardを使用してHTMLを取得
                var clipboard = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
                
                // HTMLフォーマットを優先的に取得
                if (clipboard.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Html))
                {
                    var htmlContent = await clipboard.GetHtmlFormatAsync();
                    // Windows HTMLフォーマットからHTMLコンテンツを抽出
                    return ExtractHtmlFromWindowsFormat(htmlContent);
                }
                
                // RTFフォーマットを試行
                if (clipboard.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Rtf))
                {
                    return await clipboard.GetRtfAsync();
                }
                
                return "";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Windows リッチテキスト取得エラー: {ex.Message}");
                return "";
            }
        }
        
        /// <summary>
        /// Windows HTMLフォーマットからHTMLコンテンツを抽出
        /// </summary>
        private static string ExtractHtmlFromWindowsFormat(string windowsHtml)
        {
            if (string.IsNullOrEmpty(windowsHtml))
                return "";
                
            // Windows HTMLフォーマットの構造：
            // Version:1.0\r\nStartHTML:0000000000\r\nEndHTML:0000000000\r\n...
            // 実際のHTMLコンテンツはStartHTMLとEndHTMLの間にある
            
            var startHtmlMatch = Regex.Match(windowsHtml, @"StartHTML:(\d+)");
            var endHtmlMatch = Regex.Match(windowsHtml, @"EndHTML:(\d+)");
            
            if (startHtmlMatch.Success && endHtmlMatch.Success)
            {
                var startIndex = int.Parse(startHtmlMatch.Groups[1].Value);
                var endIndex = int.Parse(endHtmlMatch.Groups[1].Value);
                
                if (startIndex < windowsHtml.Length && endIndex <= windowsHtml.Length && startIndex < endIndex)
                {
                    return windowsHtml.Substring(startIndex, endIndex - startIndex);
                }
            }
            
            // フォーマット解析に失敗した場合は、HTMLタグを含む部分を探す
            var htmlStartIndex = windowsHtml.IndexOf("<html", StringComparison.OrdinalIgnoreCase);
            if (htmlStartIndex >= 0)
            {
                return windowsHtml.Substring(htmlStartIndex);
            }
            
            // body部分だけでも探す
            var bodyStartIndex = windowsHtml.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
            if (bodyStartIndex >= 0)
            {
                return windowsHtml.Substring(bodyStartIndex);
            }
            
            return windowsHtml;
        }
#endif

#if ANDROID
        /// <summary>
        /// Android固有のリッチテキスト取得
        /// </summary>
        private static async Task<string> GetAndroidRichTextAsync()
        {
            try
            {
                await Task.Delay(1); // 非同期メソッドにするため
                
                var context = Platform.CurrentActivity ?? Android.App.Application.Context;
                var clipboardManager = context.GetSystemService(Android.Content.Context.ClipboardService) as Android.Content.ClipboardManager;
                
                if (clipboardManager?.HasPrimaryClip == true)
                {
                    var clipData = clipboardManager.PrimaryClip;
                    
                    for (int i = 0; i < clipData.ItemCount; i++)
                    {
                        var item = clipData.GetItemAt(i);
                        
                        // HTMLを優先的に取得
                        var htmlText = item.HtmlText;
                        if (!string.IsNullOrEmpty(htmlText))
                        {
                            return htmlText;
                        }
                    }
                }
                
                return "";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Android リッチテキスト取得エラー: {ex.Message}");
                return "";
            }
        }
#endif

#if IOS
        /// <summary>
        /// iOS固有のリッチテキスト取得
        /// </summary>
        private static async Task<string> GetIosRichTextAsync()
        {
            try
            {
                await Task.Delay(1); // 非同期メソッドにするため
                
                var pasteboard = UIKit.UIPasteboard.General;
                
                // HTMLを優先的に取得
                if (pasteboard.Contains("public.html"))
                {
                    var htmlData = pasteboard.DataForPasteboardType("public.html");
                    if (htmlData != null)
                    {
                        return Foundation.NSString.FromData(htmlData, Foundation.NSStringEncoding.UTF8);
                    }
                }
                
                // RTFを試行
                if (pasteboard.Contains("public.rtf"))
                {
                    var rtfData = pasteboard.DataForPasteboardType("public.rtf");
                    if (rtfData != null)
                    {
                        return Foundation.NSString.FromData(rtfData, Foundation.NSStringEncoding.UTF8);
                    }
                }
                
                return "";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"iOS リッチテキスト取得エラー: {ex.Message}");
                return "";
            }
        }
#endif

        /// <summary>
        /// HTMLをMarkdownに変換
        /// </summary>
        private static string ConvertHtmlToMarkdown(string html)
        {
            if (string.IsNullOrEmpty(html))
                return "";

            var markdown = html;

            // ネストしたタグを正規化（例：<b><b>text</b></b> → <b>text</b>）
            markdown = NormalizeNestedTags(markdown);

            // HTMLタグをMarkdownに変換
            // 太字: <b>、<strong> → **text**
            markdown = Regex.Replace(markdown, @"<(?:b|strong)(?:\s[^>]*)?>([^<]*)</(?:b|strong)>", "**$1**", RegexOptions.IgnoreCase);
            
            // イタリック: <i>、<em> → *text*
            markdown = Regex.Replace(markdown, @"<(?:i|em)(?:\s[^>]*)?>([^<]*)</(?:i|em)>", "*$1*", RegexOptions.IgnoreCase);
            
            // 下線: <u> → __text__
            markdown = Regex.Replace(markdown, @"<u(?:\s[^>]*)?>([^<]*)</u>", "__$1__", RegexOptions.IgnoreCase);
            
            // 取り消し線: <s>、<strike>、<del> → ~~text~~
            markdown = Regex.Replace(markdown, @"<(?:s|strike|del)(?:\s[^>]*)?>([^<]*)</(?:s|strike|del)>", "~~$1~~", RegexOptions.IgnoreCase);
            
            // 色付きテキスト: <span style="color:red"> → {{red|text}}
            markdown = ConvertColoredText(markdown);
            
            // 上付き文字: <sup> → ^text^
            markdown = Regex.Replace(markdown, @"<sup(?:\s[^>]*)?>([^<]*)</sup>", "^$1^", RegexOptions.IgnoreCase);
            
            // 下付き文字: <sub> → ~text~
            markdown = Regex.Replace(markdown, @"<sub(?:\s[^>]*)?>([^<]*)</sub>", "~$1~", RegexOptions.IgnoreCase);
            
            // 見出し: <h1>〜<h6> → # 〜 ######
            for (int i = 1; i <= 6; i++)
            {
                var hashes = new string('#', i);
                markdown = Regex.Replace(markdown, $@"<h{i}(?:\s[^>]*)?>([^<]*)</h{i}>", $"{hashes} $1", RegexOptions.IgnoreCase);
            }
            
            // 定義リスト項目: <dt> → 改行、<dd> → 改行
            markdown = Regex.Replace(markdown, @"<dt(?:\s[^>]*)?>([^<]*)</dt>", "$1\n", RegexOptions.IgnoreCase);
            markdown = Regex.Replace(markdown, @"<dd(?:\s[^>]*)?>([^<]*)</dd>", "$1\n", RegexOptions.IgnoreCase);
            
            // 段落: <p> → 改行（段落終了後に改行を追加）
            markdown = Regex.Replace(markdown, @"<p(?:\s[^>]*)?>([^<]*)</p>", "$1\n", RegexOptions.IgnoreCase);
            
            // 改行: <br>、<br/> → 改行
            markdown = Regex.Replace(markdown, @"<br\s*/??>", "\n", RegexOptions.IgnoreCase);
            
            // リスト項目の処理（div、liタグなど）
            markdown = Regex.Replace(markdown, @"<(?:div|li)(?:\s[^>]*)?>([^<]*)</(?:div|li)>", "$1\n", RegexOptions.IgnoreCase);
            
            // 表のセル（td、th）の処理
            markdown = Regex.Replace(markdown, @"<(?:td|th)(?:\s[^>]*)?>([^<]*)</(?:td|th)>", "$1\t", RegexOptions.IgnoreCase);
            markdown = Regex.Replace(markdown, @"<tr(?:\s[^>]*)?>([^<]*)</tr>", "$1\n", RegexOptions.IgnoreCase);
            
            // 残りのHTMLタグを除去
            markdown = Regex.Replace(markdown, @"<[^>]+>", "", RegexOptions.IgnoreCase);
            
            // HTML エンティティをデコード
            markdown = DecodeHtmlEntities(markdown);
            
            // 改行の正規化（連続する改行を適切に処理）
            // 3つ以上の連続する改行を2つに制限
            markdown = Regex.Replace(markdown, @"\n{3,}", "\n\n");
            
            // 行頭・行末の余分な空白を除去しつつ、改行は保持
            var lines = markdown.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = lines[i].Trim();
            }
            markdown = string.Join("\n", lines);
            
            // 先頭と末尾の余分な改行を除去
            markdown = markdown.Trim();

            return markdown;
        }
        
        /// <summary>
        /// ネストしたタグを正規化
        /// </summary>
        private static string NormalizeNestedTags(string html)
        {
            if (string.IsNullOrEmpty(html))
                return html;

            // 同じタグの重複を除去（例：<b><b>text</b></b> → <b>text</b>）
            var tagPatterns = new[] { "b", "strong", "i", "em", "u", "s", "strike", "del", "sup", "sub" };
            
            foreach (var tag in tagPatterns)
            {
                // 重複した開始タグを除去
                html = Regex.Replace(html, $@"<{tag}(?:\s[^>]*)?>\s*<{tag}(?:\s[^>]*)?>", $"<{tag}>", RegexOptions.IgnoreCase);
                // 重複した終了タグを除去
                html = Regex.Replace(html, $@"</{tag}>\s*</{tag}>", $"</{tag}>", RegexOptions.IgnoreCase);
            }
            
            return html;
        }
        
        /// <summary>
        /// 色付きテキストをMarkdown形式に変換
        /// </summary>
        private static string ConvertColoredText(string html)
        {
            // color:red、color:#ff0000、color:rgb(255,0,0) などの形式に対応
            var colorPattern = @"<span[^>]*style=['""][^'""]*color:\s*([^;'""]+)[^'""]*['""][^>]*>([^<]*)</span>";
            
            return Regex.Replace(html, colorPattern, match =>
            {
                var colorValue = match.Groups[1].Value.Trim();
                var text = match.Groups[2].Value;
                
                // 色名を標準化
                var color = NormalizeColorName(colorValue);
                
                if (!string.IsNullOrEmpty(color))
                {
                    return $"{{{{{color}|{text}}}}}";
                }
                
                return text; // 色が認識できない場合はテキストのみ
            }, RegexOptions.IgnoreCase);
        }
        
        /// <summary>
        /// 色名を標準化
        /// </summary>
        private static string NormalizeColorName(string colorValue)
        {
            colorValue = colorValue.ToLower().Trim();
            
            // 色名の対応表
            var colorMap = new Dictionary<string, string>
            {
                ["red"] = "red",
                ["#ff0000"] = "red",
                ["#f00"] = "red",
                ["rgb(255,0,0)"] = "red",
                ["rgb(255, 0, 0)"] = "red",
                
                ["blue"] = "blue",
                ["#0000ff"] = "blue",
                ["#00f"] = "blue",
                ["rgb(0,0,255)"] = "blue",
                ["rgb(0, 0, 255)"] = "blue",
                
                ["green"] = "green",
                ["#008000"] = "green",
                ["#0f0"] = "green",
                ["rgb(0,128,0)"] = "green",
                ["rgb(0, 128, 0)"] = "green",
                
                ["yellow"] = "yellow",
                ["#ffff00"] = "yellow",
                ["#ff0"] = "yellow",
                ["rgb(255,255,0)"] = "yellow",
                ["rgb(255, 255, 0)"] = "yellow",
                
                ["purple"] = "purple",
                ["#800080"] = "purple",
                ["rgb(128,0,128)"] = "purple",
                ["rgb(128, 0, 128)"] = "purple",
                
                ["orange"] = "orange",
                ["#ffa500"] = "orange",
                ["rgb(255,165,0)"] = "orange",
                ["rgb(255, 165, 0)"] = "orange"
            };
            
            // スペースを除去して正規化
            colorValue = Regex.Replace(colorValue, @"\s+", "");
            
            return colorMap.TryGetValue(colorValue, out var normalizedColor) ? normalizedColor : "";
        }
        
        /// <summary>
        /// RTFをMarkdownに変換（基本的な変換のみ）
        /// </summary>
        private static string ConvertRtfToMarkdown(string rtf)
        {
            if (string.IsNullOrEmpty(rtf))
                return "";

            var markdown = rtf;

            // RTF制御文字を除去してプレーンテキストを抽出
            // 基本的なRTF制御文字パターン
            markdown = Regex.Replace(markdown, @"\\[a-z]+\d*", " ");
            markdown = Regex.Replace(markdown, @"[{}]", "");
            markdown = Regex.Replace(markdown, @"\\\*", "");
            
            // 余分な空白を整理
            markdown = Regex.Replace(markdown, @"\s+", " ");
            markdown = markdown.Trim();

            return markdown;
        }
        
        /// <summary>
        /// HTMLエンティティをデコード
        /// </summary>
        private static string DecodeHtmlEntities(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            var entities = new Dictionary<string, string>
            {
                ["&amp;"] = "&",
                ["&lt;"] = "<",
                ["&gt;"] = ">",
                ["&quot;"] = "\"",
                ["&apos;"] = "'",
                ["&nbsp;"] = " ",
                ["&copy;"] = "©",
                ["&reg;"] = "®",
                ["&trade;"] = "™"
            };

            foreach (var entity in entities)
            {
                text = text.Replace(entity.Key, entity.Value);
            }

            // 数値文字参照をデコード（&#123; や &#x7B; 形式）
            text = Regex.Replace(text, @"&#(\d+);", match =>
            {
                if (int.TryParse(match.Groups[1].Value, out var code))
                {
                    return char.ConvertFromUtf32(code);
                }
                return match.Value;
            });

            text = Regex.Replace(text, @"&#x([0-9a-fA-F]+);", match =>
            {
                if (int.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out var code))
                {
                    return char.ConvertFromUtf32(code);
                }
                return match.Value;
            });

            return text;
        }
    }
} 