using Flashnote.Models;
using System.Collections.Generic;
using System.Linq;

namespace Flashnote.Services
{
    public class HelpService
    {
        private static readonly Dictionary<HelpType, HelpData> _helpData = new Dictionary<HelpType, HelpData>
        {
            {
                HelpType.MainPage,
                new HelpData
                {
                    Title = "メインページの使い方",
                    Description = "ノートの管理と作成について説明します",
                    Icon = "📚",
                    Type = HelpType.MainPage,
                    HasVideo = true,
                    VideoUrl = "help_videos/main_page_tutorial.mp4",
                    Steps = new List<HelpStep>
                    {
                        new HelpStep
                        {
                            Title = "ノートの作成",
                            Description = "右下の ➕ ボタンをタップして新しいノートを作成できます。Ctrl+Nでも作成可能です。",
                            Animation = "➕",
                            Order = 1,
                            AnimationType = AnimationType.Bounce,
                            HasVideo = true,
                            VideoUrl = "help_videos/create_note.mp4"
                        },
                        new HelpStep
                        {
                            Title = "フォルダの作成",
                            Description = "➕ ボタンから「📁 フォルダ」を選択してフォルダを作成できます。フォルダでノートを整理できます。",
                            Animation = "📁",
                            Order = 2,
                            AnimationType = AnimationType.Pulse,
                            HasScreenshot = true,
                            ScreenshotPath = "help_screenshots/create_folder.png"
                        },
                        new HelpStep
                        {
                            Title = "Anki学習モード",
                            Description = "「学習」ボタンでAnki風の学習モードに切り替えられます。カード形式で学習できます。",
                            Animation = "🎓",
                            Order = 3,
                            AnimationType = AnimationType.Scale
                        },
                        new HelpStep
                        {
                            Title = "インポート機能",
                            Description = "インポートボタンからAnkiデッキ(.apkg)や共有キーをインポートできます。",
                            Animation = "📥",
                            Order = 4,
                            AnimationType = AnimationType.SlideIn
                        },
                        new HelpStep
                        {
                            Title = "同期機能",
                            Description = "同期ボタンでクラウドとの同期を行います。複数デバイス間でデータを共有できます。",
                            Animation = "🔄",
                            Order = 5,
                            AnimationType = AnimationType.Rotate
                        },
                        new HelpStep
                        {
                            Title = "キーボードショートカット",
                            Description = "Ctrl+N: 新規ノート作成、Ctrl+Shift+N: 新規フォルダ作成、Ctrl+F: 検索",
                            Animation = "⌨️",
                            Order = 6,
                            AnimationType = AnimationType.FadeIn
                        }
                    }
                }
            },
            {
                HelpType.NotePage,
                new HelpData
                {
                    Title = "ノートページの使い方",
                    Description = "ノートの閲覧と編集について説明します",
                    Icon = "📝",
                    Type = HelpType.NotePage,
                    HasVideo = true,
                    VideoUrl = "help_videos/note_page_tutorial.mp4",
                    Steps = new List<HelpStep>
                    {
                        new HelpStep
                        {
                            Title = "PDF・画像の表示",
                            Description = "PDFファイルや画像ファイルを表示できます。ズームスライダーで拡大・縮小が可能です。",
                            Animation = "📄",
                            Order = 1,
                            AnimationType = AnimationType.FadeIn,
                            HasScreenshot = true,
                            ScreenshotPath = "help_screenshots/view_note.png"
                        },
                        new HelpStep
                        {
                            Title = "描画ツール",
                            Description = "ペン、マーカー、消しゴムツールでPDF上に手書きメモを追加できます。",
                            Animation = "✏️",
                            Order = 2,
                            AnimationType = AnimationType.Pulse,
                            HasVideo = true,
                            VideoUrl = "help_videos/drawing_tools.mp4"
                        },
                        new HelpStep
                        {
                            Title = "テキスト選択",
                            Description = "テキスト選択ボタンでPDFのテキストを選択できます。選択したテキストはカードに追加できます。",
                            Animation = "🔍",
                            Order = 3,
                            AnimationType = AnimationType.Scale
                        },
                        new HelpStep
                        {
                            Title = "カードの追加",
                            Description = "カードの追加ボタンで現在のページからカードを作成できます。",
                            Animation = "➕",
                            Order = 4,
                            AnimationType = AnimationType.Bounce
                        },
                        new HelpStep
                        {
                            Title = "元に戻す・やり直し",
                            Description = "描画操作を元に戻したり、やり直したりできます。",
                            Animation = "↩️",
                            Order = 5,
                            AnimationType = AnimationType.Shake
                        },
                        new HelpStep
                        {
                            Title = "保存・インポート",
                            Description = "変更を保存したり、新しいPDFをインポートしたりできます。",
                            Animation = "💾",
                            Order = 6,
                            AnimationType = AnimationType.FadeIn
                        }
                    }
                }
            },
            {
                HelpType.EditPage,
                new HelpData
                {
                    Title = "編集ページの使い方",
                    Description = "カードの編集機能について説明します",
                    Icon = "✏️",
                    Type = HelpType.EditPage,
                    HasVideo = true,
                    VideoUrl = "help_videos/edit_page_tutorial.mp4",
                    Steps = new List<HelpStep>
                    {
                        new HelpStep
                        {
                            Title = "カードタイプ選択",
                            Description = "基本・穴埋め、選択肢、画像穴埋めの3種類のカードタイプから選択できます。",
                            Animation = "📝",
                            Order = 1,
                            AnimationType = AnimationType.FadeIn,
                            HasScreenshot = true,
                            ScreenshotPath = "help_screenshots/card_types.png"
                        },
                        new HelpStep
                        {
                            Title = "テキスト編集",
                            Description = "テキストエリアでカードの内容を編集できます。リアルタイムプレビューで確認できます。",
                            Animation = "✏️",
                            Order = 2,
                            AnimationType = AnimationType.Pulse,
                            HasVideo = true,
                            VideoUrl = "help_videos/text_edit.mp4"
                        },
                        new HelpStep
                        {
                            Title = "画像の追加",
                            Description = "画像ボタンでカードに画像を追加できます。カメラやギャラリーから選択可能です。",
                            Animation = "🖼️",
                            Order = 3,
                            AnimationType = AnimationType.Bounce
                        },
                        new HelpStep
                        {
                            Title = "画像穴埋め作成",
                            Description = "画像穴埋めカードでは、画像上で範囲を選択して穴埋め問題を作成できます。",
                            Animation = "🎯",
                            Order = 4,
                            AnimationType = AnimationType.Scale
                        },
                        new HelpStep
                        {
                            Title = "選択肢の管理",
                            Description = "選択肢カードでは、複数の選択肢を追加・削除できます。正解を設定できます。",
                            Animation = "☑️",
                            Order = 5,
                            AnimationType = AnimationType.SlideIn
                        },
                        new HelpStep
                        {
                            Title = "検索機能",
                            Description = "検索バーでカードを検索できます。クリアボタンで検索をリセットできます。",
                            Animation = "🔍",
                            Order = 6,
                            AnimationType = AnimationType.FadeIn
                        },
                        new HelpStep
                        {
                            Title = "自動保存",
                            Description = "編集内容は自動的に保存されます。手動で保存することも可能です。",
                            Animation = "💾",
                            Order = 7,
                            AnimationType = AnimationType.Pulse
                        }
                    }
                }
            },
            {
                HelpType.QaPage,
                new HelpData
                {
                    Title = "Q&A学習ページの使い方",
                    Description = "カードを使った学習機能について説明します",
                    Icon = "❓",
                    Type = HelpType.QaPage,
                    HasVideo = true,
                    VideoUrl = "help_videos/qa_page_tutorial.mp4",
                    Steps = new List<HelpStep>
                    {
                        new HelpStep
                        {
                            Title = "学習開始",
                            Description = "カードが順番に表示されます。問題を読んでから「解答を表示」をタップしてください。",
                            Animation = "🎓",
                            Order = 1,
                            AnimationType = AnimationType.FadeIn,
                            HasScreenshot = true,
                            ScreenshotPath = "help_screenshots/learning_start.png"
                        },
                        new HelpStep
                        {
                            Title = "解答表示",
                            Description = "「解答を表示」ボタンで正解と解説を確認できます。",
                            Animation = "👁️",
                            Order = 2,
                            AnimationType = AnimationType.Pulse,
                            HasVideo = true,
                            VideoUrl = "help_videos/show_answer.mp4"
                        },
                        new HelpStep
                        {
                            Title = "正解・不正解判定",
                            Description = "「正解」または「不正解」ボタンで学習結果を記録します。",
                            Animation = "✅",
                            Order = 3,
                            AnimationType = AnimationType.Scale
                        },
                        new HelpStep
                        {
                            Title = "選択肢問題",
                            Description = "選択肢問題では、正しい選択肢を選んでから判定してください。",
                            Animation = "☑️",
                            Order = 4,
                            AnimationType = AnimationType.Bounce
                        },
                        new HelpStep
                        {
                            Title = "画像穴埋め問題",
                            Description = "画像穴埋め問題では、正しい位置をタップして回答してください。",
                            Animation = "🎯",
                            Order = 5,
                            AnimationType = AnimationType.Shake
                        },
                        new HelpStep
                        {
                            Title = "学習記録",
                            Description = "学習結果は自動的に記録され、復習が必要なカードが優先表示されます。",
                            Animation = "📊",
                            Order = 6,
                            AnimationType = AnimationType.FadeIn
                        },
                        new HelpStep
                        {
                            Title = "進捗管理",
                            Description = "正解数、不正解数、学習時間が表示されます。",
                            Animation = "⏱️",
                            Order = 7,
                            AnimationType = AnimationType.Rotate
                        }
                    }
                }
            },
            {
                HelpType.SettingsPage,
                new HelpData
                {
                    Title = "設定ページの使い方",
                    Description = "アプリの設定について説明します",
                    Icon = "⚙️",
                    Type = HelpType.SettingsPage,
                    HasScreenshot = true,
                    ScreenshotPath = "help_screenshots/settings_page.png",
                    Steps = new List<HelpStep>
                    {
                        new HelpStep
                        {
                            Title = "ログイン設定",
                            Description = "メールアドレスとパスワードを入力してFirebaseアカウントにログインできます。",
                            Animation = "🔐",
                            Order = 1,
                            AnimationType = AnimationType.FadeIn,
                            HasVideo = true,
                            VideoUrl = "help_videos/login_setting.mp4"
                        },
                        new HelpStep
                        {
                            Title = "ログイン情報の保存",
                            Description = "「保存」ボタンでログイン情報を安全に保存できます。",
                            Animation = "💾",
                            Order = 2,
                            AnimationType = AnimationType.Pulse
                        },
                        new HelpStep
                        {
                            Title = "ログイン情報のクリア",
                            Description = "「クリア」ボタンで保存されたログイン情報を削除できます。",
                            Animation = "🗑️",
                            Order = 3,
                            AnimationType = AnimationType.Scale
                        },
                        new HelpStep
                        {
                            Title = "アプリバージョン",
                            Description = "現在のアプリバージョンを確認できます。",
                            Animation = "ℹ️",
                            Order = 4,
                            AnimationType = AnimationType.FadeIn
                        },
                        new HelpStep
                        {
                            Title = "ログイン状態",
                            Description = "現在のログイン状態を確認できます。",
                            Animation = "👤",
                            Order = 5,
                            AnimationType = AnimationType.Bounce
                        }
                    }
                }
            },
            {
                HelpType.AddPage,
                new HelpData
                {
                    Title = "カード追加ページの使い方",
                    Description = "新しいカードの作成について説明します",
                    Icon = "➕",
                    Type = HelpType.AddPage,
                    HasVideo = true,
                    VideoUrl = "help_videos/add_page_tutorial.mp4",
                    Steps = new List<HelpStep>
                    {
                        new HelpStep
                        {
                            Title = "カードタイプ選択",
                            Description = "基本・穴埋め、選択肢、画像穴埋めの3種類のカードタイプから選択できます。",
                            Animation = "📝",
                            Order = 1,
                            AnimationType = AnimationType.FadeIn,
                            HasScreenshot = true,
                            ScreenshotPath = "help_screenshots/add_card_types.png"
                        },
                        new HelpStep
                        {
                            Title = "テキスト入力",
                            Description = "問題文や解答を入力できます。リアルタイムプレビューで確認できます。",
                            Animation = "✏️",
                            Order = 2,
                            AnimationType = AnimationType.Pulse,
                            HasVideo = true,
                            VideoUrl = "help_videos/text_input.mp4"
                        },
                        new HelpStep
                        {
                            Title = "リッチテキスト装飾",
                            Description = "太字、色付け、上付き・下付き文字などの装飾ができます。",
                            Animation = "🎨",
                            Order = 3,
                            AnimationType = AnimationType.Bounce
                        },
                        new HelpStep
                        {
                            Title = "装飾文字の使用方法",
                            Description = "テキストを選択して装飾ボタンをクリックすると、選択範囲が装飾されます。カーソルを装飾文字内に置いて同じボタンをクリックすると装飾が解除されます。",
                            Animation = "✨",
                            Order = 4,
                            AnimationType = AnimationType.Pulse
                        },
                        new HelpStep
                        {
                            Title = "装飾文字の種類",
                            Description = "太字（**）、色付き（{{色|}}）、上付き（^^）、下付き（~~）、穴埋め（<<blank|>>）が使用できます。",
                            Animation = "🎭",
                            Order = 5,
                            AnimationType = AnimationType.Rotate
                        },
                        new HelpStep
                        {
                            Title = "画像の追加",
                            Description = "画像ボタンでカードに画像を追加できます。カメラやギャラリーから選択可能です。",
                            Animation = "🖼️",
                            Order = 6,
                            AnimationType = AnimationType.Scale
                        },
                        new HelpStep
                        {
                            Title = "選択肢の管理",
                            Description = "選択肢カードでは、複数の選択肢を追加・削除できます。正解を設定できます。",
                            Animation = "☑️",
                            Order = 7,
                            AnimationType = AnimationType.SlideIn
                        },
                        new HelpStep
                        {
                            Title = "自動分離機能",
                            Description = "問題文に「1.」「A.」「(1)」などの選択肢記号が含まれている場合、自動的に問題と選択肢に分離されます。",
                            Animation = "🔀",
                            Order = 8,
                            AnimationType = AnimationType.Shake
                        },
                        new HelpStep
                        {
                            Title = "画像穴埋め作成",
                            Description = "画像穴埋めカードでは、画像上で範囲を選択して穴埋め問題を作成できます。",
                            Animation = "🎯",
                            Order = 9,
                            AnimationType = AnimationType.Shake
                        },
                        new HelpStep
                        {
                            Title = "カードの保存",
                            Description = "「カードを保存」ボタンでカードを保存します。未保存の変更がある場合は警告が表示されます。",
                            Animation = "💾",
                            Order = 10,
                            AnimationType = AnimationType.FadeIn
                        },
                        new HelpStep
                        {
                            Title = "自動保存",
                            Description = "入力内容は自動的に保存されます。ページを離れる際に未保存の変更がある場合は確認ダイアログが表示されます。",
                            Animation = "⏰",
                            Order = 11,
                            AnimationType = AnimationType.Pulse
                        }
                    }
                }
            }
        };

        public static HelpData GetHelpData(HelpType type)
        {
            return _helpData.ContainsKey(type) ? _helpData[type] : null;
        }

        public static List<HelpData> GetAllHelpData()
        {
            return _helpData.Values.ToList();
        }
    }
} 