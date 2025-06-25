using Microsoft.Maui.Controls;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System;

namespace Flashnote
{
    public class RichTextLabel : ContentView
    {
        public static readonly BindableProperty RichTextProperty =
            BindableProperty.Create(nameof(RichText), typeof(string), typeof(RichTextLabel), string.Empty,
                propertyChanged: OnRichTextChanged);

        public static readonly BindableProperty ShowAnswerProperty =
            BindableProperty.Create(nameof(ShowAnswer), typeof(bool), typeof(RichTextLabel), false,
                propertyChanged: OnShowAnswerChanged);

        public static readonly BindableProperty ImageFolderPathProperty =
            BindableProperty.Create(nameof(ImageFolderPath), typeof(string), typeof(RichTextLabel), string.Empty);

        public static readonly BindableProperty FontSizeProperty =
            BindableProperty.Create(nameof(FontSize), typeof(double), typeof(RichTextLabel), 18.0,
                propertyChanged: OnFontSizeChanged);

        public static readonly BindableProperty LineBreakModeProperty =
            BindableProperty.Create(nameof(LineBreakMode), typeof(LineBreakMode), typeof(RichTextLabel), LineBreakMode.WordWrap,
                propertyChanged: OnLineBreakModeChanged);

        public string RichText
        {
            get => (string)GetValue(RichTextProperty);
            set => SetValue(RichTextProperty, value);
        }

        public bool ShowAnswer
        {
            get => (bool)GetValue(ShowAnswerProperty);
            set => SetValue(ShowAnswerProperty, value);
        }

        public string ImageFolderPath
        {
            get => (string)GetValue(ImageFolderPathProperty);
            set => SetValue(ImageFolderPathProperty, value);
        }

        public double FontSize
        {
            get => (double)GetValue(FontSizeProperty);
            set => SetValue(FontSizeProperty, value);
        }

        public LineBreakMode LineBreakMode
        {
            get => (LineBreakMode)GetValue(LineBreakModeProperty);
            set => SetValue(LineBreakModeProperty, value);
        }

        private Label _label;

        public RichTextLabel()
        {
            _label = new Label
            {
                LineBreakMode = LineBreakMode.WordWrap,
                VerticalOptions = LayoutOptions.Start,
                HorizontalOptions = LayoutOptions.Fill,
            };
            Content = _label;
        }

        private static void OnRichTextChanged(BindableObject bindable, object oldValue, object newValue)
        {
            if (bindable is RichTextLabel label)
            {
                label.ProcessRichText();
            }
        }

        private static void OnShowAnswerChanged(BindableObject bindable, object oldValue, object newValue)
        {
            if (bindable is RichTextLabel label)
            {
                label.ProcessRichText();
            }
        }

        private static void OnFontSizeChanged(BindableObject bindable, object oldValue, object newValue)
        {
            if (bindable is RichTextLabel label)
            {
                label.ProcessRichText();
            }
        }

        private static void OnLineBreakModeChanged(BindableObject bindable, object oldValue, object newValue)
        {
            if (bindable is RichTextLabel label)
            {
                label._label.LineBreakMode = label.LineBreakMode;
                label.ProcessRichText();
            }
        }

        private void ProcessRichText()
        {
            if (_label == null) return;
            _label.FormattedText = null;
            _label.Text = string.Empty;

            if (string.IsNullOrWhiteSpace(RichText))
            {
                _label.Text = " ";
                return;
            }

            var text = RichText;
            text = ProcessImageTags(text);

            var formatted = new FormattedString();
            var lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                {
                    formatted.Spans.Add(new Span { Text = "\n" });
                }
                ParseDecoratedText(lines[i], formatted);
            }
            _label.FormattedText = formatted;
            _label.FontSize = FontSize;
        }

        private void ParseDecoratedText(string text, FormattedString formatted)
        {
            if (string.IsNullOrEmpty(text)) return;

            // パターンの優先順位: blank > color > bold > sup > sub
            var patterns = new (string pattern, string type)[]
            {
                ("<<blank\\|(.*?)>>", "blank"),
                ("\\{\\{(\\w+)\\|(.*?)\\}\\}", "color"),
                ("\\*\\*([^*]+?)\\*\\*", "bold"),
                ("\\^\\^(.*?)\\^\\^", "sup"),
                ("~~(.*?)~~", "sub"),
            };

            int idx = 0;
            while (idx < text.Length)
            {
                int minIndex = text.Length;
                int patIdx = -1;
                Match minMatch = null;
                string colorName = null;

                // 最も早くマッチするパターンを探す
                for (int i = 0; i < patterns.Length; i++)
                {
                    var regex = new Regex(patterns[i].pattern);
                    var match = regex.Match(text, idx);
                    if (match.Success && match.Index < minIndex)
                    {
                        minIndex = match.Index;
                        patIdx = i;
                        minMatch = match;
                        if (patterns[i].type == "color")
                        {
                            colorName = match.Groups[1].Value;
                        }
                    }
                }

                if (patIdx == -1)
                {
                    // 残りは通常テキスト
                    var normal = text.Substring(idx);
                    if (!string.IsNullOrEmpty(normal))
                        formatted.Spans.Add(new Span { Text = UnescapeText(normal) });
                    break;
                }

                // 通常テキスト部分
                if (minIndex > idx)
                {
                    var normal = text.Substring(idx, minIndex - idx);
                    if (!string.IsNullOrEmpty(normal))
                        formatted.Spans.Add(new Span { Text = UnescapeText(normal) });
                }

                // 装飾部分
                switch (patterns[patIdx].type)
                {
                    case "blank":
                        var blankText = minMatch.Groups[1].Value;
                        formatted.Spans.Add(new Span { Text = "(", FontSize = FontSize });
                        formatted.Spans.Add(new Span
                        {
                            Text = ShowAnswer ? blankText : "_____",
                            FontSize = FontSize,
                            TextColor = ShowAnswer ? Colors.Red : Colors.Gray
                        });
                        formatted.Spans.Add(new Span { Text = ")", FontSize = FontSize });
                        break;
                    case "color":
                        var colorText = minMatch.Groups[2].Value;
                        // ネストした装飾があるかチェック
                        if (HasNestedDecorations(colorText))
                        {
                            // ネストした装飾を処理
                            ProcessNestedDecorationsInSpan(colorText, $"color:{colorName}", formatted);
                        }
                        else
                        {
                            formatted.Spans.Add(new Span
                            {
                                Text = UnescapeText(colorText),
                                FontSize = FontSize,
                                TextColor = GetColorFromName(colorName)
                            });
                        }
                        break;
                    case "bold":
                        var boldText = minMatch.Groups[1].Value;
                        formatted.Spans.Add(new Span
                        {
                            Text = UnescapeText(boldText),
                            FontSize = FontSize,
                            FontAttributes = FontAttributes.Bold
                        });
                        break;
                    case "sup":
                        var supText = minMatch.Groups[1].Value;
                        formatted.Spans.Add(new Span
                        {
                            Text = UnescapeText(supText),
                            FontSize = FontSize * 0.7
                        });
                        break;
                    case "sub":
                        var subText = minMatch.Groups[1].Value;
                        formatted.Spans.Add(new Span
                        {
                            Text = UnescapeText(subText),
                            FontSize = FontSize * 0.7
                        });
                        break;
                }
                idx = minMatch.Index + minMatch.Length;
            }
        }

        /// <summary>
        /// テキストにネストした装飾があるかチェック
        /// </summary>
        private bool HasNestedDecorations(string text)
        {
            return text.Contains("**") || text.Contains("^^") || text.Contains("~~") ||
                   text.Contains("{{") || text.Contains("}}") || text.Contains("<<blank|");
        }

        /// <summary>
        /// Span内でネストした装飾を処理
        /// </summary>
        private void ProcessNestedDecorationsInSpan(string text, string parentDecorationType, FormattedString formatted)
        {
            if (string.IsNullOrEmpty(text)) return;

            // 親の色情報を取得
            string parentColor = null;
            if (parentDecorationType.StartsWith("color:"))
            {
                parentColor = parentDecorationType.Substring(6);
            }

            // パターンの優先順位: blank > color > bold > sup > sub
            var patterns = new (string pattern, string type)[]
            {
                ("<<blank\\|(.*?)>>", "blank"),
                ("\\{\\{(\\w+)\\|(.*?)\\}\\}", "color"),
                ("\\*\\*([^*]+?)\\*\\*", "bold"),
                ("\\^\\^(.*?)\\^\\^", "sup"),
                ("~~(.*?)~~", "sub"),
            };

            int idx = 0;
            while (idx < text.Length)
            {
                int minIndex = text.Length;
                int patIdx = -1;
                Match minMatch = null;
                string colorName = null;

                // 最も早くマッチするパターンを探す
                for (int i = 0; i < patterns.Length; i++)
                {
                    var regex = new Regex(patterns[i].pattern);
                    var match = regex.Match(text, idx);
                    if (match.Success && match.Index < minIndex)
                    {
                        minIndex = match.Index;
                        patIdx = i;
                        minMatch = match;
                        if (patterns[i].type == "color")
                        {
                            colorName = match.Groups[1].Value;
                        }
                    }
                }

                if (patIdx == -1)
                {
                    // 残りは通常テキスト（親の色を適用）
                    var normal = text.Substring(idx);
                    if (!string.IsNullOrEmpty(normal))
                    {
                        if (!string.IsNullOrEmpty(parentColor))
                        {
                            formatted.Spans.Add(new Span
                            {
                                Text = UnescapeText(normal),
                                FontSize = FontSize,
                                TextColor = GetColorFromName(parentColor)
                            });
                        }
                        else
                        {
                            formatted.Spans.Add(new Span { Text = UnescapeText(normal) });
                        }
                    }
                    break;
                }

                // 通常テキスト部分（親の色を適用）
                if (minIndex > idx)
                {
                    var normal = text.Substring(idx, minIndex - idx);
                    if (!string.IsNullOrEmpty(normal))
                    {
                        if (!string.IsNullOrEmpty(parentColor))
                        {
                            formatted.Spans.Add(new Span
                            {
                                Text = UnescapeText(normal),
                                FontSize = FontSize,
                                TextColor = GetColorFromName(parentColor)
                            });
                        }
                        else
                        {
                            formatted.Spans.Add(new Span { Text = UnescapeText(normal) });
                        }
                    }
                }

                // 装飾部分
                switch (patterns[patIdx].type)
                {
                    case "blank":
                        var blankText = minMatch.Groups[1].Value;
                        formatted.Spans.Add(new Span { Text = "(", FontSize = FontSize });
                        formatted.Spans.Add(new Span
                        {
                            Text = ShowAnswer ? blankText : "_____",
                            FontSize = FontSize,
                            TextColor = ShowAnswer ? Colors.Red : Colors.Gray
                        });
                        formatted.Spans.Add(new Span { Text = ")", FontSize = FontSize });
                        break;
                    case "color":
                        var colorText = minMatch.Groups[2].Value;
                        formatted.Spans.Add(new Span
                        {
                            Text = UnescapeText(colorText),
                            FontSize = FontSize,
                            TextColor = GetColorFromName(colorName)
                        });
                        break;
                    case "bold":
                        var boldText = minMatch.Groups[1].Value;
                        if (!string.IsNullOrEmpty(parentColor))
                        {
                            // 親の色を適用した太字
                            formatted.Spans.Add(new Span
                            {
                                Text = UnescapeText(boldText),
                                FontSize = FontSize,
                                FontAttributes = FontAttributes.Bold,
                                TextColor = GetColorFromName(parentColor)
                            });
                        }
                        else
                        {
                            formatted.Spans.Add(new Span
                            {
                                Text = UnescapeText(boldText),
                                FontSize = FontSize,
                                FontAttributes = FontAttributes.Bold
                            });
                        }
                        break;
                    case "sup":
                        var supText = minMatch.Groups[1].Value;
                        if (!string.IsNullOrEmpty(parentColor))
                        {
                            // 親の色を適用した上付き文字
                            formatted.Spans.Add(new Span
                            {
                                Text = UnescapeText(supText),
                                FontSize = FontSize * 0.7,
                                TextColor = GetColorFromName(parentColor)
                            });
                        }
                        else
                        {
                            formatted.Spans.Add(new Span
                            {
                                Text = UnescapeText(supText),
                                FontSize = FontSize * 0.7
                            });
                        }
                        break;
                    case "sub":
                        var subText = minMatch.Groups[1].Value;
                        if (!string.IsNullOrEmpty(parentColor))
                        {
                            // 親の色を適用した下付き文字
                            formatted.Spans.Add(new Span
                            {
                                Text = UnescapeText(subText),
                                FontSize = FontSize * 0.7,
                                TextColor = GetColorFromName(parentColor)
                            });
                        }
                        else
                        {
                            formatted.Spans.Add(new Span
                            {
                                Text = UnescapeText(subText),
                                FontSize = FontSize * 0.7
                            });
                        }
                        break;
                }
                idx = minMatch.Index + minMatch.Length;
            }
        }

        private Color GetColorFromName(string colorName)
        {
            return colorName.ToLower() switch
            {
                "red" => Colors.Red,
                "blue" => Colors.Blue,
                "green" => Colors.Green,
                "yellow" => Colors.Yellow,
                "orange" => Colors.Orange,
                "purple" => Colors.Purple,
                "pink" => Colors.Pink,
                "brown" => Colors.Brown,
                "gray" => Colors.Gray,
                "black" => Colors.Black,
                "white" => Colors.White,
                _ => Colors.Black
            };
        }

        private string ProcessImageTags(string text)
        {
            // 画像タグを処理（例: <img>filename.png</img>）
            var imagePattern = @"<img>(.*?)</img>";
            return Regex.Replace(text, imagePattern, match =>
            {
                var imagePath = match.Groups[1].Value;
                // 画像の表示処理をここに追加
                return $"[画像: {imagePath}]";
            });
        }

        private string UnescapeText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;
            return text
                .Replace("\\n", "\n")
                .Replace("\\t", "\t")
                .Replace("\\r", "\r")
                .Replace("\\\\", "\\")
                .Replace("\\*", "*")
                .Replace("\\^", "^")
                .Replace("\\_", "_")
                .Replace("\\{", "{")
                .Replace("\\}", "}")
                .Replace("\\<", "<")
                .Replace("\\>", ">");
        }
    }
} 