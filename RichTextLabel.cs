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
            BindableProperty.Create(nameof(ImageFolderPath), typeof(string), typeof(RichTextLabel), string.Empty,
                propertyChanged: OnImageFolderPathChanged);

        public static readonly BindableProperty FontSizeProperty =
            BindableProperty.Create(nameof(FontSize), typeof(double), typeof(RichTextLabel), 18.0,
                propertyChanged: OnFontSizeChanged);

        public static readonly BindableProperty LineBreakModeProperty =
            BindableProperty.Create(nameof(LineBreakMode), typeof(LineBreakMode), typeof(RichTextLabel), LineBreakMode.WordWrap,
                propertyChanged: OnLineBreakModeChanged);

        public static readonly BindableProperty TextInputModeProperty =
            BindableProperty.Create(nameof(TextInputMode), typeof(bool), typeof(RichTextLabel), false,
                propertyChanged: OnTextInputModeChanged);

        public static readonly BindableProperty BlankAnswersProperty =
            BindableProperty.Create(nameof(BlankAnswers), typeof(List<string>), typeof(RichTextLabel), null,
                propertyChanged: OnBlankAnswersChanged);

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

        public bool TextInputMode
        {
            get => (bool)GetValue(TextInputModeProperty);
            set => SetValue(TextInputModeProperty, value);
        }

        public List<string> BlankAnswers
        {
            get => (List<string>)GetValue(BlankAnswersProperty);
            set => SetValue(BlankAnswersProperty, value);
        }

        private StackLayout _container;
        private Label _label;

        public RichTextLabel()
        {
            _container = new StackLayout
            {
                Orientation = StackOrientation.Vertical,
                Spacing = 5
            };
            
            _label = new Label
            {
                LineBreakMode = LineBreakMode.WordWrap,
                VerticalOptions = LayoutOptions.Start,
                HorizontalOptions = LayoutOptions.Fill,
            };
            
            _container.Children.Add(_label);
            Content = _container;
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

        private static void OnImageFolderPathChanged(BindableObject bindable, object oldValue, object newValue)
        {
            if (bindable is RichTextLabel label)
            {
                System.Diagnostics.Debug.WriteLine($"=== RichTextLabel OnImageFolderPathChanged ===");
                System.Diagnostics.Debug.WriteLine($"oldValue: '{oldValue}'");
                System.Diagnostics.Debug.WriteLine($"newValue: '{newValue}'");
                label.ProcessRichText();
            }
        }

        private static void OnTextInputModeChanged(BindableObject bindable, object oldValue, object newValue)
        {
            if (bindable is RichTextLabel label)
            {
                label.ProcessRichText();
            }
        }

        private static void OnBlankAnswersChanged(BindableObject bindable, object oldValue, object newValue)
        {
            if (bindable is RichTextLabel label)
            {
                label.ProcessRichText();
            }
        }

        private void ProcessRichText()
        {
            if (_container == null || _label == null) return;
            
            // コンテナをクリア
            _container.Children.Clear();
            _label.FormattedText = null;
            _label.Text = string.Empty;

            if (string.IsNullOrWhiteSpace(RichText))
            {
                _label.Text = " ";
                _container.Children.Add(_label);
                return;
            }

            var text = RichText;
            
            // デバッグ情報を出力
            System.Diagnostics.Debug.WriteLine($"=== RichTextLabel ProcessRichText ===");
            System.Diagnostics.Debug.WriteLine($"ImageFolderPath: '{ImageFolderPath}'");
            System.Diagnostics.Debug.WriteLine($"RichText: '{text}'");
            
            // ImageFolderPathが設定されていない場合は画像処理をスキップ
            if (string.IsNullOrWhiteSpace(ImageFolderPath))
            {
                System.Diagnostics.Debug.WriteLine("ImageFolderPathが空のため、画像処理をスキップします");
                // 画像タグをプレースホルダーに置換
                text = System.Text.RegularExpressions.Regex.Replace(text, @"<<img_\d{8}_\d{6}\.jpg>>", match =>
                {
                    var imageFileName = match.Value.Trim('<', '>');
                    return $"[画像: {imageFileName}]";
                });
                
                // 通常のテキスト処理のみ実行
                var textLabel = new Label
                {
                    LineBreakMode = LineBreakMode,
                    VerticalOptions = LayoutOptions.Start,
                    HorizontalOptions = LayoutOptions.Fill,
                    FontSize = FontSize
                };
                
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
                textLabel.FormattedText = formatted;
                _container.Children.Add(textLabel);
                return;
            }
            
            // 画像タグを処理して画像とテキストを分離
            var processedContent = ProcessImageTagsAndText(text);
            
            // 処理されたコンテンツを表示
            foreach (var item in processedContent)
            {
                if (item is Image image)
                {
                    _container.Children.Add(image);
                }
                else if (item is string textContent && !string.IsNullOrEmpty(textContent))
                {
                    var textLabel = new Label
                    {
                        LineBreakMode = LineBreakMode,
                        VerticalOptions = LayoutOptions.Start,
                        HorizontalOptions = LayoutOptions.Fill,
                        FontSize = FontSize
                    };
                    
                    var formatted = new FormattedString();
                    var lines = textContent.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (i > 0)
                        {
                            formatted.Spans.Add(new Span { Text = "\n" });
                        }
                        ParseDecoratedText(lines[i], formatted);
                    }
                    textLabel.FormattedText = formatted;
                    _container.Children.Add(textLabel);
                }
            }
            
            // 何も追加されていない場合は空のラベルを追加
            if (_container.Children.Count == 0)
            {
                _container.Children.Add(_label);
            }
        }

        private List<object> ProcessImageTagsAndText(string text)
        {
            var result = new List<object>();
            var imagePattern = @"<<img_\d{8}_\d{6}\.jpg>>";
            var matches = Regex.Matches(text, imagePattern);
            
            if (matches.Count == 0)
            {
                // 画像タグがない場合はテキストのみ
                result.Add(text);
                return result;
            }
            
            int lastIndex = 0;
            foreach (Match match in matches)
            {
                // 画像タグ前のテキストを追加
                if (match.Index > lastIndex)
                {
                    var textBefore = text.Substring(lastIndex, match.Index - lastIndex);
                    if (!string.IsNullOrWhiteSpace(textBefore))
                    {
                        result.Add(textBefore);
                    }
                }
                
                // 画像を追加
                var imageFileName = match.Value.Trim('<', '>');
                var imagePath = System.IO.Path.Combine(ImageFolderPath, "img", imageFileName);
                
                // デバッグ情報を出力
                System.Diagnostics.Debug.WriteLine($"=== 画像パス処理 ===");
                System.Diagnostics.Debug.WriteLine($"ImageFolderPath: {ImageFolderPath}");
                System.Diagnostics.Debug.WriteLine($"imageFileName: {imageFileName}");
                System.Diagnostics.Debug.WriteLine($"構築されたimagePath: {imagePath}");
                System.Diagnostics.Debug.WriteLine($"ファイル存在: {System.IO.File.Exists(imagePath)}");
                
                if (System.IO.File.Exists(imagePath))
                {
                    var image = new Image
                    {
                        Source = imagePath,
                        Aspect = Aspect.AspectFit,
                        HeightRequest = 200,
                        HorizontalOptions = LayoutOptions.Center,
                        Margin = new Thickness(0, 5, 0, 5)
                    };
                    
                    // 画像にタップイベントを追加
                    var tapGestureRecognizer = new TapGestureRecognizer();
                    tapGestureRecognizer.Tapped += async (s, e) =>
                    {
                        try
                        {
                            var popupPage = new ImagePopupPage(imagePath, imageFileName);
                            var navigationPage = new NavigationPage(popupPage);
                            await Application.Current.MainPage.Navigation.PushModalAsync(navigationPage);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"画像拡大表示エラー: {ex.Message}");
                        }
                    };
                    image.GestureRecognizers.Add(tapGestureRecognizer);
                    
                    result.Add(image);
                    System.Diagnostics.Debug.WriteLine($"画像を追加しました: {imagePath}");
                }
                else
                {
                    // 画像ファイルが存在しない場合はプレースホルダー
                    result.Add($"[画像: {imageFileName}]");
                    System.Diagnostics.Debug.WriteLine($"画像ファイルが見つかりません: {imagePath}");
                }
                
                lastIndex = match.Index + match.Length;
            }
            
            // 最後の画像タグ後のテキストを追加
            if (lastIndex < text.Length)
            {
                var textAfter = text.Substring(lastIndex);
                if (!string.IsNullOrWhiteSpace(textAfter))
                {
                    result.Add(textAfter);
                }
            }
            
            return result;
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
                        if (TextInputMode)
                        {
                            // テキスト入力モード: 数字と正答を表示
                            int blankNumber = GetBlankNumber(formatted);
                            if (ShowAnswer)
                            {
                                // 解答表示時は正答も含める（正答部分を赤色で）
                                formatted.Spans.Add(new Span { Text = $"({blankNumber}:", FontSize = FontSize });
                                formatted.Spans.Add(new Span { Text = blankText, FontSize = FontSize, TextColor = Colors.Red });
                                formatted.Spans.Add(new Span { Text = ")", FontSize = FontSize });
                            }
                            else
                            {
                                // 解答非表示時は数字のみ
                                formatted.Spans.Add(new Span { Text = $"({blankNumber})", FontSize = FontSize });
                            }
                        }
                        else
                        {
                            // 通常モード: 括弧と下線または正解を表示
                            formatted.Spans.Add(new Span { Text = "(", FontSize = FontSize });
                            formatted.Spans.Add(new Span
                            {
                                Text = ShowAnswer ? blankText : "_____",
                                FontSize = FontSize,
                                TextColor = ShowAnswer ? Colors.Red : Colors.Gray
                            });
                            formatted.Spans.Add(new Span { Text = ")", FontSize = FontSize });
                        }
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

        // blankの連番を取得するメソッド
        private int GetBlankNumber(FormattedString formatted)
        {
            int count = 1;
            foreach (var span in formatted.Spans)
            {
                if (span.Text != null && System.Text.RegularExpressions.Regex.IsMatch(span.Text, @"\(\d+\)"))
                {
                    count++;
                }
            }
            return count;
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