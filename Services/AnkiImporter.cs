using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Flashnote.Models;
using System.Diagnostics;
using SQLite;
using System.Linq;
using Flashnote;

namespace Flashnote.Services
{
    public class AnkiImporter
    {
        private readonly BlobStorageService _blobStorageService;

        public AnkiImporter(BlobStorageService blobStorageService = null)
        {
            _blobStorageService = blobStorageService;
        }

        public async Task<List<CardData>> ImportApkg(string apkgFilePath)
        {
            var cards = new List<CardData>();
            string tempExtractPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

            try
            {
                // APKGファイルを展開
                Directory.CreateDirectory(tempExtractPath);
                ZipFile.ExtractToDirectory(apkgFilePath, tempExtractPath);

                string collectionPath = Path.Combine(tempExtractPath, "collection.anki21");
                if (!File.Exists(collectionPath))
                {
                    throw new FileNotFoundException("collection.anki21ファイルが見つかりません");
                }

                // SQLiteデータベースからデータを読み取り
                cards = await ReadCardsFromDatabase(collectionPath);

                // メディアファイルの処理（必要に応じて）
                await ProcessMediaFiles(tempExtractPath);

                return cards;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"APKGインポート中にエラー: {ex.Message}");
                throw;
            }
            finally
            {
                // 一時ディレクトリを削除
                if (Directory.Exists(tempExtractPath))
                {
                    try
                    {
                        Directory.Delete(tempExtractPath, true);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"一時ディレクトリの削除中にエラー: {ex.Message}");
                    }
                }
            }
        }

        private async Task<List<CardData>> ReadCardsFromDatabase(string collectionPath)
        {
            var cards = new List<CardData>();

            using (var db = new SQLiteConnection(collectionPath))
            {
                // ノートテーブルからデータを取得
                var notes = db.Query<ImportAnkiNote>("SELECT * FROM notes");
                var modelQuery = db.QueryScalars<string>("SELECT models FROM col LIMIT 1");

                // モデル情報を解析
                var modelData = modelQuery.FirstOrDefault();
                var modelDict = new Dictionary<int, ImportAnkiModel>();
                
                if (!string.IsNullOrEmpty(modelData))
                {
                    try
                    {
                        // JSON文字列の妥当性をチェック
                        if (!modelData.Trim().StartsWith("{"))
                        {
                            Debug.WriteLine("モデルデータが有効なJSONオブジェクトではありません");
                        }
                        else
                        {
                            var modelJson = JsonSerializer.Deserialize<JsonElement>(modelData);
                            foreach (var modelProp in modelJson.EnumerateObject())
                            {
                                // long型でパースしてからint範囲をチェック
                                if (long.TryParse(modelProp.Name, out long modelIdLong))
                                {
                                    if (modelIdLong >= int.MinValue && modelIdLong <= int.MaxValue)
                                    {
                                        int modelId = (int)modelIdLong;
                                        try
                                        {
                                            var model = JsonSerializer.Deserialize<ImportAnkiModel>(modelProp.Value.GetRawText());
                                            if (model != null)
                                            {
                                                modelDict[modelId] = model;
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            Debug.WriteLine($"モデル{modelId}の解析中にエラー: {ex.Message}");
                                            continue;
                                        }
                                    }
                                    else
                                    {
                                        Debug.WriteLine($"モデルID範囲外: {modelIdLong}");
                                    }
                                }
                                else
                                {
                                    Debug.WriteLine($"無効なモデルID: {modelProp.Name}");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"モデル情報の解析中にエラー: {ex.Message}");
                    }
                }

                foreach (var note in notes)
                {
                    try
                    {
                        var cardData = ConvertNoteToCardData(note, modelDict);
                        if (cardData != null)
                        {
                            cards.Add(cardData);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"カードデータの変換中にエラー: {ex.Message}");
                        continue;
                    }
                }
            }

            return cards;
        }

        private CardData ConvertNoteToCardData(ImportAnkiNote note, Dictionary<int, ImportAnkiModel> models)
        {
            try
            {
                // フィールド区切り文字（\x1f）でフィールドを分割
                if (string.IsNullOrEmpty(note.flds))
                {
                    Debug.WriteLine("ノートフィールドが空です");
                    return null;
                }

                // 生のfldsデータをログ出力（デバッグ用）
                Debug.WriteLine($"=== ノートID {note.id} の生データ ===");
                Debug.WriteLine($"flds生データ: {note.flds}");
                Debug.WriteLine($"flds長さ: {note.flds.Length}文字");

                var fields = note.flds.Split('\x1f');
                Debug.WriteLine($"分割後フィールド数: {fields.Length}");
                
                for (int i = 0; i < fields.Length; i++)
                {
                    Debug.WriteLine($"フィールド[{i}]: {fields[i]}");
                }

                if (fields.Length < 1)
                {
                    Debug.WriteLine($"フィールド数が不足しています: {fields.Length}");
                    return null;
                }

                var front = fields[0] ?? "";
                var back = fields.Length > 1 ? (fields[1] ?? "") : "";

                // HTMLタグを処理してFlashnote形式に変換
                try
                {
                    Debug.WriteLine($"HTML変換前 - Front: {front}");
                    Debug.WriteLine($"HTML変換前 - Back: {back}");
                    
                    front = ProcessHtmlContent(front);
                    back = ProcessHtmlContent(back);
                    
                    Debug.WriteLine($"HTML変換後 - Front: {front}");
                    Debug.WriteLine($"HTML変換後 - Back: {back}");
                }
                catch (Exception htmlEx)
                {
                    Debug.WriteLine($"HTML変換中にエラー: {htmlEx.Message}");
                    Debug.WriteLine($"エラー詳細: {htmlEx.StackTrace}");
                    // エラーが発生した場合は元のコンテンツを使用
                }

                // 画像タグを検出してログ出力
                ExtractAndLogImages(front, "Front");
                ExtractAndLogImages(back, "Back");

                // Ankiの警告メッセージやシステムメッセージをフィルタリング
                if (IsSystemMessage(front) || IsSystemMessage(back))
                {
                    Debug.WriteLine($"システムメッセージをスキップ: {front}");
                    return null;
                }

                // 空のカードもスキップ
                if (string.IsNullOrWhiteSpace(front) && string.IsNullOrWhiteSpace(back))
                {
                    Debug.WriteLine("空のカードをスキップ");
                    return null;
                }

                // モデル情報を取得してタイプを判定
                bool isCloze = false;
                if (models.ContainsKey(note.mid))
                {
                    var model = models[note.mid];
                    isCloze = model.type == 1; // Ankiではtype=1がClozeモデル
                }

                // Cloze形式の処理
                if (isCloze || ContainsClozePattern(front) || ContainsClozePattern(back))
                {
                    // Ankiの{{c1::text}}形式を<<blank|text>>形式に変換
                    front = ConvertClozeToBlank(front);
                    back = ConvertClozeToBlank(back);
                }

                // カードタイプを判定（選択肢カードの可能性も含む）
                string cardType = DetermineCardType(isCloze, front, back);
                
                // 選択肢カードへの変換を試行
                (bool success, string question, string explanation, List<ChoiceData> choices) choiceConversionResult = (false, "", "", new List<ChoiceData>());
                if (!isCloze && !ContainsClozePattern(front) && !ContainsClozePattern(back))
                {
                    choiceConversionResult = TryConvertToChoiceCard(front, back);
                    if (choiceConversionResult.success)
                    {
                        Debug.WriteLine("=== 選択肢カードに変換されました ===");
                        cardType = "選択肢";
                        
                        Debug.WriteLine($"質問: {choiceConversionResult.question}");
                        Debug.WriteLine($"解説: {choiceConversionResult.explanation}");
                        Debug.WriteLine($"選択肢数: {choiceConversionResult.choices.Count}");
                        foreach (var choice in choiceConversionResult.choices)
                        {
                            Debug.WriteLine($"  - {choice.text} (正解: {choice.isCorrect})");
                        }
                    }
                }

                // Unix timestampを DateTime に変換
                DateTime? modifiedDate = null;
                if (note.mod > 0)
                {
                    try
                    {
                        modifiedDate = DateTimeOffset.FromUnixTimeSeconds(note.mod).DateTime;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"日時変換エラー: {ex.Message}");
                    }
                }

                // 選択肢カードの場合は適切にフィールドを設定
                var cardData = new CardData
                {
                    id = Guid.NewGuid().ToString(),
                    guid = note.guid ?? Guid.NewGuid().ToString(), // AnkiのUUIDを使用、なければ新規作成
                    createdAt = modifiedDate, // Ankiには作成日時がないため修正日時を使用
                    modifiedAt = modifiedDate,
                    type = cardType,
                    front = cardType == "選択肢" ? "" : front,                                  // 選択肢の場合はfrontは空
                    back = cardType == "選択肢" ? "" : back,                                    // 選択肢の場合はbackは空
                    question = cardType == "選択肢" ? choiceConversionResult.question : "",     // 選択肢の場合はquestionに質問を設定
                    explanation = cardType == "選択肢" ? choiceConversionResult.explanation : "", // 選択肢の場合はexplanationに解説を設定
                    choices = cardType == "選択肢" ? choiceConversionResult.choices : new List<ChoiceData>(),
                    selectionRects = new List<SelectionRect>()
                };

                Debug.WriteLine($"=== カードデータ作成完了 ===");
                Debug.WriteLine($"ID: {cardData.id}");
                Debug.WriteLine($"GUID: {cardData.guid}");
                Debug.WriteLine($"Modified: {cardData.modifiedAt}");
                Debug.WriteLine($"Type: {cardData.type}");
                Debug.WriteLine($"Front: {cardData.front}");
                Debug.WriteLine($"Back: {cardData.back}");

                return cardData;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"カードデータ変換中にエラー: {ex.Message}");
                return null;
            }
        }

        private void ExtractAndLogImages(string text, string fieldName)
        {
            if (string.IsNullOrEmpty(text)) return;

            // HTMLの<img>タグを検索
            var imgPattern = @"<img[^>]+src=""([^""]+)""[^>]*>";
            var matches = Regex.Matches(text, imgPattern, RegexOptions.IgnoreCase);

            if (matches.Count > 0)
            {
                Debug.WriteLine($"=== {fieldName}フィールドで{matches.Count}個の画像が見つかりました ===");
                foreach (Match match in matches)
                {
                    var fullTag = match.Value;
                    var src = match.Groups[1].Value;
                    Debug.WriteLine($"画像タグ: {fullTag}");
                    Debug.WriteLine($"画像ソース: {src}");
                }
            }

            // その他の参照パターンもチェック
            var soundPattern = @"\[sound:[^\]]+\]";
            var soundMatches = Regex.Matches(text, soundPattern, RegexOptions.IgnoreCase);
            if (soundMatches.Count > 0)
            {
                Debug.WriteLine($"=== {fieldName}フィールドで{soundMatches.Count}個の音声ファイルが見つかりました ===");
                foreach (Match match in soundMatches)
                {
                    Debug.WriteLine($"音声タグ: {match.Value}");
                }
            }
        }

        private bool ContainsClozePattern(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return text.Contains("{{c") && text.Contains("::") && text.Contains("}}");
        }

        private string ConvertClozeToBlank(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            
            // {{c1::answer}}形式を<<blank|answer>>形式に変換
            return Regex.Replace(text, @"{{c\d+::(.*?)}}", "<<blank|$1>>");
        }

        private string ProcessHtmlContent(string htmlContent)
        {
            if (string.IsNullOrEmpty(htmlContent)) return htmlContent;

            string result = htmlContent;

            try
            {
                Debug.WriteLine($"=== ProcessHtmlContent 開始 ===");
                Debug.WriteLine($"入力: {htmlContent}");

                // <br>タグを改行に変換
                Debug.WriteLine("ステップ1: <br>タグ処理");
                result = Regex.Replace(result, @"<br[^>]*/?>"," \n", RegexOptions.IgnoreCase);
                Debug.WriteLine($"<br>処理後: {result}");

                // <div>タグを改行に変換
                Debug.WriteLine("ステップ2: <div>タグ処理");
                result = Regex.Replace(result, @"<div[^>]*>", "\n", RegexOptions.IgnoreCase);
                result = Regex.Replace(result, @"</div>", "", RegexOptions.IgnoreCase);
                Debug.WriteLine($"<div>処理後: {result}");

                // <ol>と<ul>タグの処理
                Debug.WriteLine("ステップ3: <ol>/<ul>タグ処理");
                result = Regex.Replace(result, @"<ol[^>]*>", "\n", RegexOptions.IgnoreCase);
                result = Regex.Replace(result, @"</ol>", "\n", RegexOptions.IgnoreCase);
                result = Regex.Replace(result, @"<ul[^>]*>", "\n", RegexOptions.IgnoreCase);
                result = Regex.Replace(result, @"</ul>", "\n", RegexOptions.IgnoreCase);
                Debug.WriteLine($"<ol>/<ul>処理後: {result}");

                // <dl>, <dt>, <dd>タグの処理
                Debug.WriteLine("ステップ4: <dl>/<dt>/<dd>タグ処理");
                result = Regex.Replace(result, @"<dl[^>]*>", "\n", RegexOptions.IgnoreCase);
                result = Regex.Replace(result, @"</dl>", "\n", RegexOptions.IgnoreCase);
                result = Regex.Replace(result, @"<dt[^>]*>", "\n", RegexOptions.IgnoreCase);
                result = Regex.Replace(result, @"</dt>", "", RegexOptions.IgnoreCase);
                result = Regex.Replace(result, @"<dd[^>]*>", "\n", RegexOptions.IgnoreCase);
                result = Regex.Replace(result, @"</dd>", "\n", RegexOptions.IgnoreCase);
                Debug.WriteLine($"<dl>/<dt>/<dd>処理後: {result}");

                // <b>タグと重複<b>タグの処理
                Debug.WriteLine("ステップ5: <b>タグ処理");
                // 重複した<b><b>タグを<b>に統一
                result = Regex.Replace(result, @"<b>\s*<b>", "<b>", RegexOptions.IgnoreCase);
                result = Regex.Replace(result, @"</b>\s*</b>", "</b>", RegexOptions.IgnoreCase);
                // <b>タグを**text**形式に変換
                result = Regex.Replace(result, @"<b[^>]*>(.*?)</b>", "**$1**", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                Debug.WriteLine($"<b>処理後: {result}");

                // <li>タグを番号付きリストに変換
                Debug.WriteLine("ステップ6: <li>タグ処理");
                var liCounter = 1;
                result = Regex.Replace(result, @"<li[^>]*>(.*?)</li>", match =>
                {
                    try
                    {
                        var content = match.Groups[1].Value.Trim();
                        var replacement = $"{liCounter++}. {content}\n";
                        Debug.WriteLine($"<li>変換: {match.Value} -> {replacement.Trim()}");
                        return replacement;
                    }
                    catch (Exception liEx)
                    {
                        Debug.WriteLine($"<li>変換エラー: {liEx.Message}");
                        return match.Value; // エラー時は元の値を返す
                    }
                }, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                Debug.WriteLine($"<li>処理後: {result}");

                // <span>タグの色指定を処理
                Debug.WriteLine("ステップ7: <span>色処理");
                result = Regex.Replace(result, @"<span\s+style=""[^""]*color:\s*rgb\((\d+),\s*(\d+),\s*(\d+)\)[^""]*""[^>]*>(.*?)</span>",
                    match =>
                    {
                        try
                        {
                            int r = int.Parse(match.Groups[1].Value);
                            int g = int.Parse(match.Groups[2].Value);
                            int b = int.Parse(match.Groups[3].Value);
                            string content = match.Groups[4].Value;

                            Debug.WriteLine($"色変換: RGB({r}, {g}, {b}) - コンテンツ: {content}");

                            // 黒色の場合は色指定なし
                            if (r < 50 && g < 50 && b < 50)
                            {
                                Debug.WriteLine("黒色と判定 - 色指定なし");
                                return content;
                            }

                            // RGB値から最も近い色を判定
                            string colorName = GetClosestColorName(r, g, b);
                            var replacement = $"{{{{{colorName}|{content}}}}}";
                            Debug.WriteLine($"色変換結果: {replacement}");
                            return replacement;
                        }
                        catch (Exception spanEx)
                        {
                            Debug.WriteLine($"<span>色変換エラー: {spanEx.Message}");
                            return match.Groups[4].Value; // エラー時はコンテンツのみ返す
                        }
                    }, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                Debug.WriteLine($"<span>処理後: {result}");

                // その他のHTMLタグを除去
                Debug.WriteLine("ステップ8: その他HTMLタグ除去");
                result = Regex.Replace(result, @"<[^>]+>", "", RegexOptions.IgnoreCase);
                Debug.WriteLine($"HTMLタグ除去後: {result}");

                // 連続する改行を整理
                Debug.WriteLine("ステップ9: 改行整理");
                result = Regex.Replace(result, @"\n\s*\n+", "\n\n");
                result = result.Trim();
                Debug.WriteLine($"最終結果: {result}");

                Debug.WriteLine($"=== ProcessHtmlContent 完了 ===");
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HTML処理中に重大なエラー: {ex.Message}");
                Debug.WriteLine($"エラースタックトレース: {ex.StackTrace}");
                return htmlContent; // エラー時は元のコンテンツを返す
            }
        }

        private string GetClosestColorName(int r, int g, int b)
        {
            // Flashnoteで対応している基本色（Add.xaml.csと同じ）
            var colors = new Dictionary<string, (int r, int g, int b)>
            {
                {"red", (255, 0, 0)},
                {"blue", (0, 0, 255)},
                {"green", (0, 128, 0)},
                {"yellow", (255, 255, 0)},
                {"purple", (128, 0, 128)},
                {"orange", (255, 165, 0)}
            };

            // 赤系の特別処理：RGB(170, 0, 0)のような場合
            if (r > 100 && g < 50 && b < 50)
            {
                Debug.WriteLine($"RGB({r}, {g}, {b}) -> 赤系として判定");
                return "red";
            }

            // 青系の特別処理
            if (b > 100 && r < 50 && g < 50)
            {
                Debug.WriteLine($"RGB({r}, {g}, {b}) -> 青系として判定");
                return "blue";
            }

            // 緑系の特別処理
            if (g > 100 && r < 50 && b < 50)
            {
                Debug.WriteLine($"RGB({r}, {g}, {b}) -> 緑系として判定");
                return "green";
            }

            // その他の色はユークリッド距離で計算
            string closestColor = "red";
            double minDistance = double.MaxValue;

            foreach (var color in colors)
            {
                double distance = Math.Sqrt(
                    Math.Pow(r - color.Value.r, 2) +
                    Math.Pow(g - color.Value.g, 2) +
                    Math.Pow(b - color.Value.b, 2)
                );

                if (distance < minDistance)
                {
                    minDistance = distance;
                    closestColor = color.Key;
                }
            }

            Debug.WriteLine($"RGB({r}, {g}, {b}) -> {closestColor}として判定");
            return closestColor;
        }

        private bool IsSystemMessage(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            
            // Ankiのシステムメッセージを判定
            var systemMessages = new[]
            {
                "Ankiの最新バージョンにアップデートしてから、もう一度.colpkgファイルをインポートしてください。",
                "Please update to the latest version of Anki, then try importing this file again.",
                "Anki 2.1.50以降が必要です",
                "This file requires Anki 2.1.50+",
                "This collection is too new for this version of Anki"
            };

            return systemMessages.Any(msg => text.Contains(msg));
        }

        private string DetermineCardType(bool isCloze, string front, string back)
        {
            // Cloze（穴埋め）の場合
            if (isCloze || ContainsClozePattern(front) || ContainsClozePattern(back))
            {
                return "基本・穴埋め";
            }

            // 基本カードの場合も「基本・穴埋め」に統一
            return "基本・穴埋め";
        }

        private (bool success, string question, string explanation, List<ChoiceData> choices) TryConvertToChoiceCard(string front, string back)
        {
            try
            {
                Debug.WriteLine($"=== 選択肢カード変換試行 ===");
                Debug.WriteLine($"Front: {front}");
                Debug.WriteLine($"Back: {back}");

                // より厳密な選択肢パターン：複数の形式に対応（全角スペース対応）
                var choicePatterns = new[]
                {
                    @"(?:^|\n)\s*(\d+)[\.\．][\s　]*(.+?)(?=\n\s*\d+[\.\．]|\n\s*$|$)",      // 1. 選択肢、1．選択肢（全角スペース対応）
                    @"(?:^|\n)\s*(\d+)[\)）][\s　]*(.+?)(?=\n\s*\d+[\)）]|\n\s*$|$)",        // 1) 選択肢、1）選択肢（全角スペース対応）
                    @"(?:^|\n)\s*（(\d+)）[\s　]*(.+?)(?=\n\s*（\d+）|\n\s*$|$)",            // （1）選択肢（全角スペース対応）
                    @"(?:^|\n)\s*\((\d+)\)[\s　]*(.+?)(?=\n\s*\(\d+\)|\n\s*$|$)",          // (1) 選択肢（全角スペース対応）
                    @"(?:^|\n)\s*([０-９]+)[\.\．][\s　]*(.+?)(?=\n\s*[０-９]+[\.\．]|\n\s*$|$)", // 全角数字１．選択肢（全角スペース対応）
                    @"(?:^|\n)\s*([０-９]+)[\：:][\s　]*(.+?)(?=\n\s*[０-９]+[\：:]|\n\s*$|$)"  // 全角数字１：選択肢（全角スペース対応）
                };

                List<Match> allMatches = new List<Match>();
                string usedPattern = "";
                
                // 各パターンを試す
                foreach (var pattern in choicePatterns)
                {
                    var matches = Regex.Matches(front, pattern, RegexOptions.Multiline | RegexOptions.IgnoreCase);
                    if (matches.Count >= 2)
                    {
                        allMatches = matches.Cast<Match>().ToList();
                        usedPattern = pattern;
                        Debug.WriteLine($"使用されたパターン: {pattern}");
                        break;
                    }
                }

                if (allMatches.Count < 2)
                {
                    Debug.WriteLine("選択肢が2個未満のため、変換をスキップ");
                    return (false, front, back, new List<ChoiceData>());
                }

                Debug.WriteLine($"検出された選択肢数: {allMatches.Count}");

                // 質問部分を抽出（最初の選択肢より前の部分）
                var firstChoiceIndex = front.IndexOf(allMatches[0].Value);
                string question;
                
                if (firstChoiceIndex > 0)
                {
                    question = front.Substring(0, firstChoiceIndex).Trim();
                    
                    // 質問が短すぎる場合（10文字未満）は選択肢カードとして扱わない
                    if (question.Length < 10)
                    {
                        Debug.WriteLine($"質問が短すぎるため変換をスキップ: '{question}' (長さ: {question.Length})");
                        return (false, front, back, new List<ChoiceData>());
                    }
                }
                else
                {
                    // 最初から選択肢が始まる場合
                    // テキスト全体を見て、明らかに選択肢のみの場合は変換しない
                    var lines = front.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    bool allLinesAreChoices = true;
                    
                    foreach (var line in lines)
                    {
                        bool isChoice = false;
                        foreach (var pattern in choicePatterns)
                        {
                            if (Regex.IsMatch(line.Trim(), pattern.Replace(@"(?:^|\n)\s*", "^").Replace(@"(?=\n\s*\d+[\.\．]|\n\s*$|$)", "$"), RegexOptions.IgnoreCase))
                            {
                                isChoice = true;
                                break;
                            }
                        }
                        if (!isChoice && !string.IsNullOrWhiteSpace(line))
                        {
                            allLinesAreChoices = false;
                            break;
                        }
                    }
                    
                    if (allLinesAreChoices)
                    {
                        Debug.WriteLine("全ての行が選択肢のため、質問部分がないと判断して変換をスキップ");
                        return (false, front, back, new List<ChoiceData>());
                    }
                    
                    question = "質問"; // デフォルト質問
                    Debug.WriteLine("質問部分が見つからないため、デフォルト質問を使用");
                }

                // 空の質問の場合もデフォルト質問を使用
                if (string.IsNullOrWhiteSpace(question))
                {
                    question = "質問";
                    Debug.WriteLine("空の質問のため、デフォルト質問を使用");
                }

                // 選択肢を作成
                var choices = new List<ChoiceData>();
                foreach (Match match in allMatches)
                {
                    var choiceNumber = match.Groups[1].Value;
                    var choiceText = match.Groups[2].Value.Trim();
                    
                    // 全角数字を半角に変換
                    choiceNumber = ConvertFullWidthToHalfWidth(choiceNumber);
                    
                    choices.Add(new ChoiceData
                    {
                        text = choiceText,
                        isCorrect = false // デフォルトは不正解
                    });

                    Debug.WriteLine($"選択肢{choiceNumber}: {choiceText}");
                }

                // 選択肢が連続した番号になっているかチェック
                var numbers = allMatches.Select(m => 
                {
                    var num = ConvertFullWidthToHalfWidth(m.Groups[1].Value);
                    return int.TryParse(num, out int result) ? result : -1;
                }).Where(n => n > 0).OrderBy(n => n).ToList();

                if (numbers.Count >= 2)
                {
                    bool isConsecutive = true;
                    for (int i = 1; i < numbers.Count; i++)
                    {
                        if (numbers[i] != numbers[i - 1] + 1)
                        {
                            isConsecutive = false;
                            break;
                        }
                    }
                    
                    if (!isConsecutive)
                    {
                        Debug.WriteLine("選択肢の番号が連続していないため、変換をスキップ");
                        return (false, front, back, new List<ChoiceData>());
                    }
                }

                // backから正解を推定（A.1、正解：1、答え：1 などのパターン）
                TryDetermineCorrectAnswer(back, choices);

                Debug.WriteLine($"変換成功: 質問='{question}', 選択肢数={choices.Count}");
                return (true, question, back, choices);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"選択肢カード変換中にエラー: {ex.Message}");
                return (false, front, back, new List<ChoiceData>());
            }
        }

        private string ConvertFullWidthToHalfWidth(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            
            // 全角数字を半角に変換
            string result = input;
            result = result.Replace("０", "0");
            result = result.Replace("１", "1");
            result = result.Replace("２", "2");
            result = result.Replace("３", "3");
            result = result.Replace("４", "4");
            result = result.Replace("５", "5");
            result = result.Replace("６", "6");
            result = result.Replace("７", "7");
            result = result.Replace("８", "8");
            result = result.Replace("９", "9");
            
            return result;
        }

        private void TryDetermineCorrectAnswer(string explanation, List<ChoiceData> choices)
        {
            if (string.IsNullOrEmpty(explanation) || choices.Count == 0)
                return;

            try
            {
                var correctAnswers = new HashSet<int>();

                // 単一正解パターン（全角・半角対応）
                var singleAnswerPatterns = new[]
                {
                    @"[AaＡａ][\.\．]?\s*([0-9０-９]+)",           // A.1, A1, a.1, a1, Ａ．１, Ａ１
                    @"正解[：:\：]\s*([0-9０-９]+)",               // 正解：1, 正解:1
                    @"答え[：:\：]\s*([0-9０-９]+)",               // 答え：1, 答え:1
                    @"解答[：:\：]\s*([0-9０-９]+)",               // 解答：1, 解答:1
                    @"([0-9０-９]+)[\.\．]?\s*が?正解",            // 1が正解, 1.正解, １が正解
                    @"選択肢\s*([0-9０-９]+)",                    // 選択肢1, 選択肢１
                };

                // 複数正解パターン（全角・半角対応）
                var multipleAnswerPatterns = new[]
                {
                    @"[AaＡａ][\.\．]?\s*([0-9０-９\s、,，・]+)",                    // A.1、2、3 or A.1,2,3 or Ａ．１、２、３
                    @"正解[：:\：]\s*([0-9０-９\s、,，・]+)",                      // 正解：1、2、3, 正解:1、2、3
                    @"答え[：:\：]\s*([0-9０-９\s、,，・]+)",                      // 答え：1、2、3, 答え:1、2、3
                    @"解答[：:\：]\s*([0-9０-９\s、,，・]+)",                      // 解答：1、2、3, 解答:1、2、3
                    @"正解は\s*([0-9０-９\s、,，・]+)",                           // 正解は1、2、3, 正解は１、２、３
                    @"([0-9０-９\s、,，・]+)\s*が?正解",                          // 1、2、3が正解, １、２、３が正解
                    @"選択肢\s*([0-9０-９\s、,，・]+)",                           // 選択肢1、2、3, 選択肢１、２、３
                };

                Debug.WriteLine("=== 正解判定開始 ===");

                // まず単一正解パターンをチェック
                bool foundSingle = false;
                foreach (var pattern in singleAnswerPatterns)
                {
                    var matches = Regex.Matches(explanation, pattern, RegexOptions.IgnoreCase);
                    foreach (Match match in matches)
                    {
                        string numberText = match.Groups[1].Value;
                        // 全角数字を半角に変換
                        string convertedNumber = ConvertFullWidthToHalfWidth(numberText);
                        if (int.TryParse(convertedNumber, out int answerNumber))
                        {
                            correctAnswers.Add(answerNumber);
                            foundSingle = true;
                            Debug.WriteLine($"単一正解パターン検出: {answerNumber} (元: {numberText})");
                        }
                    }
                }

                // 単一正解が見つからない場合、複数正解パターンをチェック
                if (!foundSingle)
                {
                    foreach (var pattern in multipleAnswerPatterns)
                    {
                        var match = Regex.Match(explanation, pattern, RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            string numbersText = match.Groups[1].Value;
                            Debug.WriteLine($"複数正解パターン候補: '{numbersText}'");
                            
                            // 数字を抽出（全角・半角数字対応、様々な区切り文字に対応）
                            var numberMatches = Regex.Matches(numbersText, @"[0-9０-９]+");
                            foreach (Match numMatch in numberMatches)
                            {
                                string numberText = numMatch.Value;
                                // 全角数字を半角に変換
                                string convertedNumber = ConvertFullWidthToHalfWidth(numberText);
                                if (int.TryParse(convertedNumber, out int answerNumber))
                                {
                                    correctAnswers.Add(answerNumber);
                                    Debug.WriteLine($"複数正解パターン検出: {answerNumber} (元: {numberText})");
                                }
                            }
                            
                            if (correctAnswers.Count > 0)
                                break; // 最初にマッチしたパターンを使用
                        }
                    }
                }

                // 特殊パターン: 「1と3が正解」「1、3、5が正解」など
                if (correctAnswers.Count == 0)
                {
                    var specialPatterns = new[]
                    {
                        @"([0-9０-９]+)(?:と|、|,|，)([0-9０-９]+)(?:と|、|,|，)?(?:([0-9０-９]+))?(?:と|、|,|，)?(?:([0-9０-９]+))?(?:と|、|,|，)?(?:([0-9０-９]+))?\s*が?正解", // 1と3が正解
                        @"([0-9０-９]+)(?:\s*と\s*|\s*、\s*|\s*,\s*|\s*，\s*)([0-9０-９]+)(?:\s*と\s*|\s*、\s*|\s*,\s*|\s*，\s*)?(?:([0-9０-９]+))?\s*が?正解", // 1と3が正解
                    };

                    foreach (var pattern in specialPatterns)
                    {
                        var match = Regex.Match(explanation, pattern, RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            for (int i = 1; i < match.Groups.Count; i++)
                            {
                                if (match.Groups[i].Success)
                                {
                                    string numberText = match.Groups[i].Value;
                                    // 全角数字を半角に変換
                                    string convertedNumber = ConvertFullWidthToHalfWidth(numberText);
                                    if (int.TryParse(convertedNumber, out int answerNumber))
                                    {
                                        correctAnswers.Add(answerNumber);
                                        Debug.WriteLine($"特殊パターン検出: {answerNumber} (元: {numberText})");
                                    }
                                }
                            }
                            if (correctAnswers.Count > 0)
                                break;
                        }
                    }
                }

                // 正解を選択肢に反映
                if (correctAnswers.Count > 0)
                {
                    foreach (var answerNumber in correctAnswers)
                    {
                        int answerIndex = answerNumber - 1; // 1-based to 0-based
                        if (answerIndex >= 0 && answerIndex < choices.Count)
                        {
                            choices[answerIndex].isCorrect = true;
                            Debug.WriteLine($"正解設定: 選択肢{answerNumber} ({choices[answerIndex].text})");
                        }
                    }

                    Debug.WriteLine($"正解数: {correctAnswers.Count}個 (複数選択: {correctAnswers.Count > 1})");
                }
                else
                {
                    Debug.WriteLine("正解パターンが見つかりませんでした");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"正解判定中にエラー: {ex.Message}");
            }
        }

        private async Task ProcessMediaFiles(string extractPath)
        {
            // メディアファイルの処理（現在は基本的な実装）
            string mediaJsonPath = Path.Combine(extractPath, "media");
            if (File.Exists(mediaJsonPath))
            {
                try
                {
                    string mediaJson = await File.ReadAllTextAsync(mediaJsonPath);
                    
                    // 空のファイルや無効なJSONをチェック
                    if (string.IsNullOrWhiteSpace(mediaJson) || mediaJson.Trim().StartsWith("("))
                    {
                        Debug.WriteLine("メディアファイルは空または無効な形式です");
                        return;
                    }

                    // JSONとして解析を試行
                    if (mediaJson.Trim().StartsWith("{"))
                    {
                        var mediaDict = JsonSerializer.Deserialize<Dictionary<string, string>>(mediaJson);
                        Debug.WriteLine($"=== メディアファイル情報 ===");
                        Debug.WriteLine($"メディアファイル数: {mediaDict?.Count ?? 0}");
                        
                        if (mediaDict != null)
                        {
                            foreach (var kvp in mediaDict)
                            {
                                Debug.WriteLine($"メディアID {kvp.Key}: {kvp.Value}");
                                
                                // 実際のメディアファイルが存在するかチェック
                                string mediaFilePath = Path.Combine(extractPath, kvp.Key);
                                if (File.Exists(mediaFilePath))
                                {
                                    var fileInfo = new FileInfo(mediaFilePath);
                                    Debug.WriteLine($"  -> ファイル存在: {kvp.Value} (サイズ: {fileInfo.Length} bytes)");
                                }
                                else
                                {
                                    Debug.WriteLine($"  -> ファイル不明: {kvp.Value}");
                                }
                            }
                        }
                    }
                    else
                    {
                        Debug.WriteLine("メディアファイルがJSONオブジェクト形式ではありません");
                        Debug.WriteLine($"メディアファイル内容: {mediaJson.Substring(0, Math.Min(200, mediaJson.Length))}...");
                    }
                }
                catch (JsonException ex)
                {
                    Debug.WriteLine($"メディアファイルJSON解析中にエラー: {ex.Message}");
                    // JSON解析に失敗した場合、生の内容を表示
                    try
                    {
                        string mediaJson = await File.ReadAllTextAsync(mediaJsonPath);
                        Debug.WriteLine($"生のメディアファイル内容: {mediaJson.Substring(0, Math.Min(200, mediaJson.Length))}...");
                    }
                    catch (Exception readEx)
                    {
                        Debug.WriteLine($"メディアファイル読み取りエラー: {readEx.Message}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"メディアファイル処理中に予期しないエラー: {ex.Message}");
                }
            }
            else
            {
                Debug.WriteLine("メディアファイル(media)が見つかりません");
            }
            
            // 抽出フォルダ内の全ファイルをリスト表示（デバッグ用）
            Debug.WriteLine("=== 抽出フォルダ内のファイル一覧 ===");
            if (Directory.Exists(extractPath))
            {
                var files = Directory.GetFiles(extractPath);
                foreach (var file in files)
                {
                    var fileName = Path.GetFileName(file);
                    var fileInfo = new FileInfo(file);
                    Debug.WriteLine($"ファイル: {fileName} (サイズ: {fileInfo.Length} bytes)");
                }
            }
        }

        public async Task<string> SaveImportedCards(List<CardData> cards, string noteFolder, string noteName)
        {
            try
            {
                // ノートフォルダが存在しない場合は作成
                if (!Directory.Exists(noteFolder))
                {
                    Directory.CreateDirectory(noteFolder);
                }

                // .ankplsファイルのパスを生成
                string ankplsPath = Path.Combine(noteFolder, $"{noteName}.ankpls");
                
                // 一時展開パスをLocalAppData\Flashnote\{subfolder}\{notename}_tempに作成
                string localFlashnotePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Flashnote"
                );
                
                // noteFolderからサブフォルダ構造を取得
                string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string FlashnoteDocumentsPath = Path.Combine(documentsPath, "Flashnote");
                
                string subFolder = "";
                if (noteFolder.StartsWith(FlashnoteDocumentsPath, StringComparison.OrdinalIgnoreCase))
                {
                    // Documents\Flashnote以下のサブフォルダを取得
                    subFolder = Path.GetRelativePath(FlashnoteDocumentsPath, noteFolder);
                    if (subFolder == ".")
                    {
                        subFolder = ""; // ルートの場合は空文字
                    }
                }
                
                // tempフォルダのパスを構築
                string tempExtractPath;
                if (string.IsNullOrEmpty(subFolder))
                {
                    tempExtractPath = Path.Combine(localFlashnotePath, $"{noteName}_temp");
                }
                else
                {
                    tempExtractPath = Path.Combine(localFlashnotePath, subFolder, $"{noteName}_temp");
                }

                Debug.WriteLine($"=== SaveImportedCards 開始 ===");
                Debug.WriteLine($"noteFolder: {noteFolder}");
                Debug.WriteLine($"documentsPath: {documentsPath}");
                Debug.WriteLine($"FlashnoteDocumentsPath: {FlashnoteDocumentsPath}");
                Debug.WriteLine($"subFolder: '{subFolder}'");
                Debug.WriteLine($"localFlashnotePath: {localFlashnotePath}");
                Debug.WriteLine($"tempExtractPath: {tempExtractPath}");
                Debug.WriteLine($"カード数: {cards.Count}");

                // 一時フォルダを再作成（削除はしない）
                if (Directory.Exists(tempExtractPath))
                {
                    Debug.WriteLine("既存のtempフォルダが見つかりました - 内容を更新します");
                }
                else
                {
                    Directory.CreateDirectory(tempExtractPath);
                    Debug.WriteLine("新しいtempフォルダを作成しました");
                }

                // cardsディレクトリを作成
                string cardsDir = Path.Combine(tempExtractPath, "cards");
                if (!Directory.Exists(cardsDir))
                {
                    Directory.CreateDirectory(cardsDir);
                }
                Debug.WriteLine($"cardsディレクトリ: {cardsDir}");

                // cards.txtファイルを指定された形式で作成
                string cardsTxtPath = Path.Combine(tempExtractPath, "cards.txt");
                var cardsTxtLines = new List<string>
                {
                    cards.Count.ToString()
                };

                // 各カードのUUID（id）と更新日時を追加
                foreach (var card in cards)
                {
                    string modifiedTime = card.modifiedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    cardsTxtLines.Add($"{card.id},{modifiedTime}");
                }

                await File.WriteAllTextAsync(cardsTxtPath, string.Join("\n", cardsTxtLines));
                Debug.WriteLine($"cards.txt作成完了: {cardsTxtPath}");

                // 各カードをJSONファイルとして保存
                Debug.WriteLine($"=== JSONファイル作成開始 ===");
                int savedCount = 0;
                foreach (var card in cards)
                {
                    string cardJsonPath = Path.Combine(cardsDir, $"{card.id}.json");
                    
                    // 指定された形式のJSONオブジェクトを作成
                    var cardJson = new
                    {
                        id = card.id,
                        type = card.type,
                        front = card.front ?? "",
                        back = card.back ?? "",
                        question = card.question ?? "",
                        explanation = card.explanation ?? "",
                        choices = card.choices ?? new List<ChoiceData>(),
                        selectionRects = card.selectionRects ?? new List<SelectionRect>()
                    };
                    
                    string jsonString = JsonSerializer.Serialize(cardJson, new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    });
                    await File.WriteAllTextAsync(cardJsonPath, jsonString);
                    savedCount++;
                    Debug.WriteLine($"JSONファイル作成: {card.id}.json");
                }
                Debug.WriteLine($"JSONファイル作成完了: {savedCount}個のファイル");

                // .ankplsファイルを作成（ZIPアーカイブ）
                if (File.Exists(ankplsPath))
                {
                    File.Delete(ankplsPath);
                }

                ZipFile.CreateFromDirectory(tempExtractPath, ankplsPath);
                Debug.WriteLine($".ankplsファイル作成完了: {ankplsPath}");

                // tempフォルダの最終確認
                Debug.WriteLine($"=== tempフォルダ最終確認 ===");
                Debug.WriteLine($"tempフォルダパス: {tempExtractPath}");
                Debug.WriteLine($"tempフォルダ存在: {Directory.Exists(tempExtractPath)}");
                if (Directory.Exists(tempExtractPath))
                {
                    var tempFiles = Directory.GetFiles(tempExtractPath, "*", SearchOption.AllDirectories);
                    Debug.WriteLine($"tempフォルダ内ファイル数: {tempFiles.Length}");
                    foreach (var file in tempFiles)
                    {
                        Debug.WriteLine($"  - {Path.GetRelativePath(tempExtractPath, file)}");
                    }
                }

                // Blob Storageに即座にアップロード
                if (_blobStorageService != null)
                {
                    try
                    {
                        var uid = App.CurrentUser?.Uid;
                        if (!string.IsNullOrEmpty(uid))
                        {
                            Debug.WriteLine($"Blob Storageへのアップロード開始: {noteName}");
                            
                            // cards.txtをBlob Storageにアップロード
                            var cardsContent = await File.ReadAllTextAsync(cardsTxtPath);
                            await _blobStorageService.SaveNoteAsync(uid, noteName, cardsContent, subFolder);
                            Debug.WriteLine($"cards.txtをBlob Storageにアップロード完了: {noteName}");
                            
                            // 各カードのJSONファイルをBlob Storageにアップロード
                            foreach (var card in cards)
                            {
                                var cardJsonPath = Path.Combine(cardsDir, $"{card.id}.json");
                                if (File.Exists(cardJsonPath))
                                {
                                    var cardContent = await File.ReadAllTextAsync(cardJsonPath);
                                    var uploadPath = string.IsNullOrEmpty(subFolder) 
                                        ? $"{noteName}/cards" 
                                        : $"{subFolder}/{noteName}/cards";
                                    await _blobStorageService.SaveNoteAsync(uid, $"{card.id}.json", cardContent, uploadPath);
                                    Debug.WriteLine($"カードファイルをBlob Storageにアップロード: {card.id}.json");
                                }
                            }
                            
                            Debug.WriteLine($"Blob Storageへのアップロード完了: {noteName}");
                        }
                        else
                        {
                            Debug.WriteLine("ユーザーIDが取得できないため、Blob Storageへのアップロードをスキップ");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Blob Storageへのアップロード中にエラー: {ex.Message}");
                        // アップロードに失敗してもローカルファイルの作成は続行
                    }
                }
                else
                {
                    Debug.WriteLine("BlobStorageServiceが設定されていないため、Blob Storageへのアップロードをスキップ");
                }

                return ankplsPath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"インポートしたカードの保存中にエラー: {ex.Message}");
                throw;
            }
        }

        // SQLite用のデータクラス（インポート専用）
        private class ImportAnkiNote
        {
            public int id { get; set; }
            public string guid { get; set; }
            public int mid { get; set; }
            public long mod { get; set; }
            public int usn { get; set; }
            public string tags { get; set; }
            public string flds { get; set; }
            public int sfld { get; set; }
            public int csum { get; set; }
            public int flags { get; set; }
            public string data { get; set; }
        }

        private class ImportAnkiModel
        {
            public int id { get; set; }
            public string name { get; set; }
            public int type { get; set; }
            public string css { get; set; }
            public object[] flds { get; set; }
            public object[] tmpls { get; set; }
            public int sortf { get; set; }
            public int did { get; set; }
            public int usn { get; set; }
            public long mod { get; set; }
            public object[] vers { get; set; }
            public object[] tags { get; set; }
            public object[] req { get; set; }
        }
    }
} 