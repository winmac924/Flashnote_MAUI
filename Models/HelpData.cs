using System.Collections.Generic;

namespace Flashnote.Models
{
    public class HelpData
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public List<HelpStep> Steps { get; set; } = new List<HelpStep>();
        public string Icon { get; set; }
        public HelpType Type { get; set; }
        public string VideoUrl { get; set; } // 動画URL
        public string ScreenshotPath { get; set; } // スクリーンショットパス
        public bool HasVideo { get; set; } = false;
        public bool HasScreenshot { get; set; } = false;
    }

    public class HelpStep
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public string Animation { get; set; } // アニメーション名またはアイコン
        public int Order { get; set; }
        public string VideoUrl { get; set; } // ステップ固有の動画URL
        public string ScreenshotPath { get; set; } // ステップ固有のスクリーンショット
        public AnimationType AnimationType { get; set; } = AnimationType.None;
        public string HighlightElement { get; set; } // ハイライトする要素のID
        public bool HasVideo { get; set; } = false;
        public bool HasScreenshot { get; set; } = false;
    }

    public enum HelpType
    {
        MainPage,
        NotePage,
        EditPage,
        QaPage,
        SettingsPage,
        AddPage,
        General
    }

    public enum AnimationType
    {
        None,
        FadeIn,
        SlideIn,
        Pulse,
        Bounce,
        Shake,
        Rotate,
        Scale,
        Custom
    }
} 