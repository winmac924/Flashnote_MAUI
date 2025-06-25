using Microsoft.Maui.Controls;
using System.Diagnostics;
using PdfiumViewer;
using System.Reflection;
using SkiaSharp;
using SkiaSharp.Views.Maui;

namespace Flashnote.Views
{
    public class TextSelectionLayer : ContentView
    {
        private WebView _textWebView;
        private BackgroundCanvas _backgroundCanvas;
        private ScrollView _parentScrollView;
        private int _currentPageIndex = -1;
        private bool _isEnabled = false;

        public event EventHandler<TextSelectedEventArgs> TextSelected;

        public TextSelectionLayer()
        {
            InitializeLayer();
        }

        private void InitializeLayer()
        {
            _textWebView = new WebView
            {
                BackgroundColor = Colors.Transparent,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill
            };

            _textWebView.Navigated += OnWebViewNavigated;
            Content = _textWebView;
            this.IsVisible = false;
            
            Debug.WriteLine("📝 TextSelectionLayer初期化完了 - WebViewベース");
        }

        public void SetBackgroundCanvas(BackgroundCanvas backgroundCanvas)
        {
            _backgroundCanvas = backgroundCanvas;
            Debug.WriteLine("🔗 BackgroundCanvas設定完了");
        }

        public void SetParentScrollView(ScrollView scrollView)
        {
            _parentScrollView = scrollView;
            if (_parentScrollView != null)
            {
                _parentScrollView.Scrolled += OnScrollViewScrolled;
                Debug.WriteLine("📜 ScrollView設定完了");
            }
        }

        public void EnableTextSelection()
        {
            _isEnabled = true;
            this.IsVisible = true;
            LoadCurrentPageText();
            Debug.WriteLine("✅ テキスト選択モード有効化 - WebViewベース");
        }

        public void DisableTextSelection()
        {
            _isEnabled = false;
            this.IsVisible = false;
            Debug.WriteLine("❌ テキスト選択モード無効化");
        }

        private async void OnScrollViewScrolled(object? sender, ScrolledEventArgs e)
        {
            if (!_isEnabled) return;
            var currentPageIndex = GetCurrentPageIndex();
            if (currentPageIndex != _currentPageIndex)
            {
                LoadCurrentPageText();
            }
        }

        private void LoadCurrentPageText()
        {
            if (!_isEnabled || _backgroundCanvas == null) return;

            try
            {
                var currentPageIndex = GetCurrentPageIndex();
                if (currentPageIndex < 0) return;

                var pdfPath = GetCurrentPdfPath();
                if (string.IsNullOrEmpty(pdfPath) || !File.Exists(pdfPath))
                {
                    Debug.WriteLine("❌ PDFファイルが見つかりません");
                    return;
                }

                using var document = PdfiumViewer.PdfDocument.Load(pdfPath);
                if (currentPageIndex >= 0 && currentPageIndex < document.PageCount)
                {
                    var pageText = document.GetPdfText(currentPageIndex);
                    DisplayTextAsHtml(pageText);
                    _currentPageIndex = currentPageIndex;
                    Debug.WriteLine($"✅ WebViewにテキストを表示完了");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ テキスト読み込みエラー: {ex.Message}");
            }
        }

        private void DisplayTextAsHtml(string pageText)
        {
            try
            {
                var htmlContent = CreateSelectableHtml(pageText);
                _textWebView.Source = new HtmlWebViewSource { Html = htmlContent };
                Debug.WriteLine("📄 HTMLコンテンツをWebViewに設定完了");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ HTML表示エラー: {ex.Message}");
            }
        }

        private string CreateSelectableHtml(string pageText)
        {
            var lines = pageText.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var htmlBuilder = new System.Text.StringBuilder();
            
            htmlBuilder.AppendLine(@"<!DOCTYPE html><html><head><meta charset='utf-8'><style>
body { font-family: Arial; font-size: 14px; margin: 10px; background: transparent; }
.selectable-text { display: inline-block; margin: 2px; padding: 4px 8px; 
border: 2px solid rgba(255,0,0,0.3); background: rgba(255,0,0,0.1); 
border-radius: 4px; cursor: pointer; }
.selectable-text:hover { background: rgba(255,0,0,0.2); }
.line { margin-bottom: 8px; }
</style></head><body>");

            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var line = lines[lineIndex].Trim();
                if (string.IsNullOrEmpty(line)) continue;
                
                htmlBuilder.AppendLine("<div class='line'>");
                var segments = CreateTextSegments(line);
                
                foreach (var segment in segments)
                {
                    var escapedText = System.Web.HttpUtility.HtmlEncode(segment);
                    htmlBuilder.AppendLine($"<span class='selectable-text' onclick='selectText(\"{escapedText}\")'>{escapedText}</span>");
                }
                htmlBuilder.AppendLine("</div>");
            }

            htmlBuilder.AppendLine(@"<script>
function selectText(text) {
    window.location.href = 'textselected://' + encodeURIComponent(text);
}
</script></body></html>");

            return htmlBuilder.ToString();
        }

        private List<string> CreateTextSegments(string line)
        {
            var segments = new List<string>();
            
            if (line.Length <= 15)
            {
                segments.Add(line);
            }
            else if (line.Contains("。"))
            {
                var sentences = line.Split(new char[] { '。', '！', '？' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var sentence in sentences)
                {
                    if (sentence.Trim().Length > 3)
                    {
                        segments.Add(sentence.Trim() + "。");
                    }
                }
            }
            else
            {
                for (int i = 0; i < line.Length; i += 15)
                {
                    var segment = line.Substring(i, Math.Min(15, line.Length - i));
                    if (segment.Trim().Length > 3)
                    {
                        segments.Add(segment.Trim());
                    }
                }
            }
            
            return segments.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        }

        private void OnWebViewNavigated(object sender, WebNavigatedEventArgs e)
        {
            try
            {
                if (e.Url.StartsWith("textselected://"))
                {
                    var selectedText = Uri.UnescapeDataString(e.Url.Substring("textselected://".Length));
                    OnTextSelected(selectedText);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ WebView navigation処理エラー: {ex.Message}");
            }
        }

        private void OnTextSelected(string selectedText)
        {
            try
            {
                Debug.WriteLine($"🎯 テキスト選択: '{selectedText}'");
                TextSelected?.Invoke(this, new TextSelectedEventArgs(selectedText));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ テキスト選択処理エラー: {ex.Message}");
            }
        }

        private int GetCurrentPageIndex()
        {
            try
            {
                if (_backgroundCanvas == null) return 0;
                
                var method = typeof(BackgroundCanvas).GetMethod("GetCurrentPageIndex", 
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                
                if (method != null)
                {
                    var result = method.Invoke(_backgroundCanvas, null);
                    return result is int pageIndex ? pageIndex : 0;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ ページインデックス取得エラー: {ex.Message}");
            }
            
            return 0;
        }

        private string GetCurrentPdfPath()
        {
            try
            {
                if (_backgroundCanvas == null) return null;

                var type = typeof(BackgroundCanvas);
                var field = type.GetField("_currentPdfPath", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (field != null)
                {
                    var result = field.GetValue(_backgroundCanvas);
                    return result as string;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ PDFパス取得エラー: {ex.Message}");
            }
            
            return null;
        }

        public void SyncWithBackgroundCanvas()
        {
            Debug.WriteLine("📐 WebViewベース - 座標同期不要");
        }
    }

    public class TextSelectedEventArgs : EventArgs
    {
        public string SelectedText { get; }

        public TextSelectedEventArgs(string selectedText)
        {
            SelectedText = selectedText;
        }
    }
} 