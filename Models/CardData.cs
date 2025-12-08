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
        public string imageFileName { get; set; } // 画像穴埋めカード用の画像ファイル名
        // PDF連携用
        public PdfReference PdfReference { get; set; } // PDF情報

        // material 情報を保持（cards JSON に含まれる { "material": { "fileName": "...", "page": 1 } } を読み取る）
        public Material material { get; set; }
    }

    public class PdfReference
    {
        public string PdfId { get; set; }      // PDFファイル名やID
        public int PageNumber { get; set; }    // ページ番号（1始まり）
    }

    // material JSON に対応するクラス
    public class Material
    {
        public string fileName { get; set; }
        public int page { get; set; }
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

    // metadata.json の defaultMaterial に対応
    public class DefaultMaterial
    {
        public bool isPDF { get; set; }
        public string fileName { get; set; }
    }
} 