using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Flashnote.Models
{
    public class CardData
    {
        public string id { get; set; }
        public string guid { get; set; } // UUID
        public DateTime? createdAt { get; set; } // 作成日時
        public DateTime? modifiedAt { get; set; } // 修正日時
        public string type { get; set; }
        public string front { get; set; }
        public string back { get; set; }
        public string question { get; set; }
        public string explanation { get; set; }
        public List<ChoiceData> choices { get; set; }
        public List<SelectionRect> selectionRects { get; set; }
    }

    public class ChoiceData
    {
        public bool isCorrect { get; set; }
        public string text { get; set; }
    }

    public class SelectionRect
    {
        public float x { get; set; }
        public float y { get; set; }
        public float width { get; set; }
        public float height { get; set; }
    }
} 