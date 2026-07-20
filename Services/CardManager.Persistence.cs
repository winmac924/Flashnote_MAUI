using Flashnote.Services.Sync;
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
        /// <summary>
        /// 画像番号を読み込む
        /// </summary>
        private void LoadImageCount()
        {
            if (File.Exists(_cardsFilePath))
            {
                var lines = File.ReadAllLines(_cardsFilePath).ToList();
                if (lines.Count > 0 && int.TryParse(lines[0], out int count))
                {
                    _imageCount = count;
                }
            }
        }
        /// <summary>
        /// 画像番号を保存する
        /// </summary>
        private void SaveImageCount()
        {
            var lines = new List<string> { _imageCount.ToString() };

            if (File.Exists(_cardsFilePath))
            {
                lines.AddRange(File.ReadAllLines(_cardsFilePath).Skip(1));
            }

            File.WriteAllLines(_cardsFilePath, lines);
        }
        /// <summary>
        /// カードデータを読み込み
        /// </summary>
        private void LoadCards()
        {
            try
            {
                _cards.Clear();
                
                if (File.Exists(_cardsFilePath))
                {
                    var lines = File.ReadAllLines(_cardsFilePath, System.Text.Encoding.UTF8);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        // 最初の行はカード数なのでスキップ
                        if (i == 0) continue;
                        
                        var line = lines[i];
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            // cards.txtの行はUUID,更新日時の形式
                            // 実際のカードデータはJSONファイルから読み込む
                            var parts = line.Split(',');
                            if (parts.Length >= 1)
                            {
                                var cardId = parts[0];
                                // プレースホルダーとしてカードIDのみを保存
                                // 実際のデータはLoadJsonCardsで読み込まれる
                                _cards.Add($"{cardId}|placeholder");
                            }
                        }
                    }
                }
                
                // 新形式のJSONファイルも読み込み
                LoadJsonCards();
                
                Debug.WriteLine($"カード読み込み完了: {_cards.Count}件");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"カード読み込みエラー: {ex.Message}");
            }
        }
        /// <summary>
        /// 新形式のJSONファイルを読み込み
        /// </summary>
        private void LoadJsonCards()
        {
            try
            {
                var cardsDir = Path.Combine(_tempExtractPath, "cards");
                if (!Directory.Exists(cardsDir)) return;
                
                var jsonFiles = Directory.GetFiles(cardsDir, "*.json");
                _cards.Clear(); // 新しいフォーマットのみなので、リストをクリア
                
                foreach (var jsonFile in jsonFiles)
                {
                    try
                    {
                        var jsonContent = File.ReadAllText(jsonFile, System.Text.Encoding.UTF8);
                        var cardData = JsonSerializer.Deserialize<JsonElement>(jsonContent);

                        // Extract material if present
                        object materialObj = null;
                        if (cardData.TryGetProperty("material", out var materialProp) && materialProp.ValueKind == JsonValueKind.Object)
                        {
                            string matFileName = materialProp.TryGetProperty("fileName", out var fnProp) ? fnProp.GetString() : null;
                            int matPage = materialProp.TryGetProperty("page", out var pProp) && pProp.ValueKind == JsonValueKind.Number ? pProp.GetInt32() : 0;
                            materialObj = new { fileName = matFileName, page = matPage };
                        }

                        // JSONフォーマットから直接データを読み込み
                        var jsonDataObject = new
                        {
                            id = cardData.GetProperty("id").GetString(),
                            type = cardData.GetProperty("type").GetString(),
                            front = cardData.TryGetProperty("front", out var frontProp) ? frontProp.GetString() : "",
                            back = cardData.TryGetProperty("back", out var backProp) ? backProp.GetString() : "",
                            question = cardData.TryGetProperty("question", out var questionProp) ? questionProp.GetString() : "",
                            explanation = cardData.TryGetProperty("explanation", out var explanationProp) ? explanationProp.GetString() : "",
                            choices = cardData.TryGetProperty("choices", out var choicesProp) ? choicesProp : (JsonElement?)null,
                            imagePath = cardData.TryGetProperty("imagePath", out var imagePathProp) ? imagePathProp.GetString() : "",
                            selections = cardData.TryGetProperty("selections", out var selectionsProp) ? selectionsProp : (JsonElement?)null,
                            material = materialObj
                        };

                        // カードタイプに応じて正しいJSONフォーマットに変換
                        object cardJsonData = null;
                        switch (jsonDataObject.type)
                        {
                            case "基本・穴埋め":
                                cardJsonData = new
                                {
                                    id = jsonDataObject.id,
                                    type = "basic",
                                    front = jsonDataObject.front,
                                    back = jsonDataObject.back,
                                    material = jsonDataObject.material
                                };
                                break;

                            case "選択肢":
                                cardJsonData = new
                                {
                                    id = jsonDataObject.id,
                                    type = "choice",
                                    question = jsonDataObject.question,
                                    explanation = jsonDataObject.explanation,
                                    choices = jsonDataObject.choices?.EnumerateArray().Select(c => new
                                    {
                                        text = c.GetProperty("text").GetString(),
                                        correct = c.GetProperty("isCorrect").GetBoolean()
                                    }).ToList(),
                                    material = jsonDataObject.material
                                };
                                break;

                            case "画像穴埋め":
                                cardJsonData = new
                                {
                                    id = jsonDataObject.id,
                                    type = "image_fill",
                                    imagePath = jsonDataObject.imagePath,
                                    selections = jsonDataObject.selections?.EnumerateArray().Select(s => new
                                    {
                                        x = s.GetProperty("x").GetSingle(),
                                        y = s.GetProperty("y").GetSingle(),
                                        width = s.GetProperty("width").GetSingle(),
                                        height = s.GetProperty("height").GetSingle()
                                    }).ToList(),
                                    material = jsonDataObject.material
                                };
                                break;
                        }

                        if (cardJsonData != null)
                        {
                            var cardDataJson = JsonSerializer.Serialize(cardJsonData, new JsonSerializerOptions
                            {
                                WriteIndented = false,
                                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                            });
                            _cards.Add(cardDataJson);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"JSONファイル読み込みエラー {jsonFile}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"JSONカード読み込みエラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 新形式から旧形式に変換
        /// <summary>
        /// カードデータを読み込み
        /// </summary>
        private void LoadCardData(string cardId)
        {
            try
            {
                Debug.WriteLine($"カードデータ読み込み開始: {cardId}");
                
                var cardLine = _cards.FirstOrDefault(c => 
                {
                    try
                    {
                        var jsonElement = JsonSerializer.Deserialize<JsonElement>(c);
                        return jsonElement.GetProperty("id").GetString() == cardId;
                    }
                    catch
                    {
                        return false;
                    }
                });
                
                if (cardLine == null)
                {
                    Debug.WriteLine($"カードが見つかりません: {cardId}");
                    return;
                }
                
                var jsonElement = JsonSerializer.Deserialize<JsonElement>(cardLine);
                var cardType = jsonElement.GetProperty("type").GetString();
                
                switch (cardType)
                {
                    case "basic":
                        _cardTypePicker.SelectedIndex = 0;
                        LoadBasicCardDataFromJson(jsonElement);
                        break;
                    case "choice":
                        _cardTypePicker.SelectedIndex = 1;
                        LoadChoiceCardDataFromJson(jsonElement);
                        break;
                    case "image_fill":
                        _cardTypePicker.SelectedIndex = 2;
                        LoadImageFillCardDataFromJson(jsonElement);
                        break;
                }
                
                Debug.WriteLine($"カードデータ読み込み完了: {cardId}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"カードデータ読み込みエラー: {ex.Message}");
            }
        }
        /// <summary>
        /// 基本カードデータをJSONから読み込み
        /// </summary>
        private void LoadBasicCardDataFromJson(JsonElement jsonElement)
        {
            try
            {
                if (jsonElement.TryGetProperty("front", out var frontElement) && 
                    jsonElement.TryGetProperty("back", out var backElement))
                {
                    _frontTextEditor.Text = frontElement.GetString() ?? "";
                    _backTextEditor.Text = backElement.GetString() ?? "";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"基本カードJSONデータ読み込みエラー: {ex.Message}");
            }
        }
        /// <summary>
        /// 選択肢カードデータをJSONから読み込み
        /// </summary>
        private void LoadChoiceCardDataFromJson(JsonElement jsonElement)
        {
            try
            {
                if (jsonElement.TryGetProperty("question", out var questionElement))
                {
                    _choiceQuestion.Text = questionElement.GetString() ?? "";
                }

                if (jsonElement.TryGetProperty("explanation", out var explanationElement))
                {
                    _choiceQuestionExplanation.Text = explanationElement.GetString() ?? "";
                }

                if (jsonElement.TryGetProperty("choices", out var choicesElement) && choicesElement.ValueKind == JsonValueKind.Array)
                {
                    _choicesContainer.Children.Clear();
                    foreach (var choice in choicesElement.EnumerateArray())
                    {
                        if (choice.TryGetProperty("text", out var textElement) &&
                            choice.TryGetProperty("correct", out var correctElement))
                        {
                            var text = textElement.GetString() ?? "";
                            var isCorrect = correctElement.GetBoolean();
                        AddChoiceItem(text, isCorrect);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"選択肢カードJSONデータ読み込みエラー: {ex.Message}");
            }
        }
        /// <summary>
        /// 画像穴埋めカードデータをJSONから読み込み
        /// </summary>
        private void LoadImageFillCardDataFromJson(JsonElement jsonElement)
        {
            try
            {
                if (jsonElement.TryGetProperty("imagePath", out var imagePathElement))
                {
                    var imagePath = imagePathElement.GetString() ?? "";
                    LoadImageFromPath(imagePath);
                }
                    
                if (jsonElement.TryGetProperty("selections", out var selectionsElement) && selectionsElement.ValueKind == JsonValueKind.Array)
                {
                    _selectionRects.Clear();
                    foreach (var selection in selectionsElement.EnumerateArray())
                    {
                        if (selection.TryGetProperty("x", out var xElement) &&
                            selection.TryGetProperty("y", out var yElement) &&
                            selection.TryGetProperty("width", out var widthElement) &&
                            selection.TryGetProperty("height", out var heightElement))
                        {
                            var x = xElement.GetSingle();
                            var y = yElement.GetSingle();
                            var width = widthElement.GetSingle();
                            var height = heightElement.GetSingle();
                        
                            // 既存のデータが正規化座標かどうかを判定
                            // x, y, width, heightが全て0.0-1.0の範囲にある場合は正規化座標とみなす
                            bool isNormalized = x >= 0.0f && x <= 1.0f && 
                                               y >= 0.0f && y <= 1.0f && 
                                               width >= 0.0f && width <= 1.0f && 
                                               height >= 0.0f && height <= 1.0f;
                            
                            SKRect selectionRect;
                            if (isNormalized)
                            {
                                // 正規化座標の場合は画像座標に変換
                                if (_imageBitmap != null)
                                {
                                    float imageX = x * _imageBitmap.Width;
                                    float imageY = y * _imageBitmap.Height;
                                    float imageWidth = width * _imageBitmap.Width;
                                    float imageHeight = height * _imageBitmap.Height;
                                    
                                    selectionRect = new SKRect(imageX, imageY, imageX + imageWidth, imageY + imageHeight);
                                    Debug.WriteLine($"正規化座標を画像座標に変換: 元={x},{y},{width},{height}, 画像サイズ={_imageBitmap.Width}x{_imageBitmap.Height} -> 画像座標={selectionRect}");
                                }
                                else
                                {
                                    // 画像が読み込まれていない場合は仮のサイズで計算
                                    float imageX = x * 1000.0f;
                                    float imageY = y * 1000.0f;
                                    float imageWidth = width * 1000.0f;
                                    float imageHeight = height * 1000.0f;
                                    
                                    selectionRect = new SKRect(imageX, imageY, imageX + imageWidth, imageY + imageHeight);
                                    Debug.WriteLine($"正規化座標を画像座標に変換（仮サイズ）: 元={x},{y},{width},{height} -> 画像座標={selectionRect}");
                                }
                            }
                            else
                            {
                                // 既に画像座標の場合
                                selectionRect = new SKRect(x, y, x + width, y + height);
                                Debug.WriteLine($"画像座標として読み込み: {selectionRect}");
                            }
                            
                            _selectionRects.Add(selectionRect);
                        }
                    }
                    
                    // キャンバスを更新
                    if (_canvasView != null)
                    {
                        // キャンバスサイズを画像のアスペクト比に合わせて調整
                        AdjustCanvasSizeToImage();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"画像穴埋めカードJSONデータ読み込みエラー: {ex.Message}");
            }
        }
        /// <summary>
        /// パスから画像を読み込み
        /// </summary>
        private void LoadImageFromPath(string imagePath)
        {
            try
            {
                if (File.Exists(imagePath))
                {
                    _imageBitmap = SKBitmap.Decode(imagePath);
                    _selectedImagePath = imagePath;
                    
                    if (_canvasView != null)
                    {
                        // キャンバスサイズを画像のアスペクト比に合わせて調整
                        AdjustCanvasSizeToImage();
                    }
                    
                    Debug.WriteLine($"画像読み込み完了: {imagePath}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"画像読み込みエラー: {ex.Message}");
            }
        }
        /// <summary>
        /// 未同期ノートとして記録
        /// </summary>
        private async Task RecordUnsynchronizedNote(string reason)
        {
            try
            {
                var unsyncService = MauiProgram.Services.GetService<UnsynchronizedNotesService>();
                var noteName = Path.GetFileNameWithoutExtension(_ankplsFilePath);
                
                // サブフォルダ情報を取得
                string subFolder = null;
                var flashnotePath = SyncPathResolver.GetLocalNoteRoot();
                var noteDirectory = Path.GetDirectoryName(_ankplsFilePath);
                if (noteDirectory.StartsWith(flashnotePath, StringComparison.OrdinalIgnoreCase))
                {
                    var relativePath = Path.GetRelativePath(flashnotePath, noteDirectory);
                    if (relativePath != "." && !relativePath.StartsWith("."))
                    {
                        subFolder = relativePath;
                    }
                }
                
                unsyncService?.AddUnsynchronizedNote(noteName, subFolder, reason);
                Debug.WriteLine($"未同期ノートとして記録: {noteName} (理由: {reason})");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"未同期ノート記録エラー: {ex.Message}");
            }
        }
        /// <summary>
        /// カードデータを保存
        /// </summary>
        private async Task SaveCardData(string cardData)
        {
            try
            {
                var jsonElement = JsonSerializer.Deserialize<JsonElement>(cardData);
                var cardId = jsonElement.GetProperty("id").GetString();
                
                // 既存のcards.txtから更新日時を読み込み
                var existingTimestamps = new Dictionary<string, string>();
                if (File.Exists(_cardsFilePath))
                {
                    var cardslines = File.ReadAllLines(_cardsFilePath, System.Text.Encoding.UTF8);
                    for (int i = 1; i < cardslines.Length; i++) // 最初の行（カード数）はスキップ
                    {
                        var line = cardslines[i];
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            var parts = line.Split(',');
                            if (parts.Length >= 2)
                            {
                                var existingCardId = parts[0];
                                var timestamp = parts[1];
                                existingTimestamps[existingCardId] = timestamp;
                            }
                        }
                    }
                }

                // 編集モードの場合は既存のデータを更新
                if (!string.IsNullOrEmpty(_editCardId))
                {
                    var existingIndex = _cards.FindIndex(c => 
                    {
                        try
                        {
                            var element = JsonSerializer.Deserialize<JsonElement>(c);
                            return element.GetProperty("id").GetString() == _editCardId;
                        }
                        catch
                        {
                            return false;
                        }
                    });
                    
                    if (existingIndex >= 0)
                    {
                        _cards[existingIndex] = cardData;
                    }
                    else
                    {
                        _cards.Add(cardData);
                    }
                }
                else
                {
                    _cards.Add(cardData);
                }
                
                // ファイルに保存（カード数をヘッダーとして追加）
                var lines = new List<string>();
                lines.Add(_cards.Count.ToString()); // カード数をヘッダーとして追加
                

                // 各カードデータからIDと更新日時を抽出
                foreach (var card in _cards)
                {
                    try
                    {
                        var element = JsonSerializer.Deserialize<JsonElement>(card);
                        var currentCardId = element.GetProperty("id").GetString();
                        string timestamp;
                        
                        // 既存のカードの場合は既存の更新日時を使用、新規の場合は現在時刻を使用
                        if (existingTimestamps.ContainsKey(currentCardId))
                        {
                            timestamp = existingTimestamps[currentCardId];
                        }
                        else
                        {
                            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        }
                        
                        lines.Add($"{currentCardId},{timestamp}");
                    }
                    catch
                    {
                        // JSONパースに失敗した場合はスキップ
                        continue;
                    }
                }
                
                var content = string.Join("\n", lines);
                await File.WriteAllTextAsync(_cardsFilePath, content, System.Text.Encoding.UTF8);
                
                // 新形式のJSONファイルも作成
                await CreateJsonFile(cardData);
                
                // ankplsファイルを更新
                await UpdateAnkplsFile();
                
                Debug.WriteLine("カードデータ保存完了");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"カードデータ保存エラー: {ex.Message}");
                throw;
            }
        }
        /// <summary>
        /// 新形式のJSONファイルを作成
        /// </summary>
        private async Task CreateJsonFile(string cardData)
        {
            try
            {
                // JSONフォーマットのデータをデシリアライズ
                var jsonElement = JsonSerializer.Deserialize<JsonElement>(cardData);
                
                // cardsディレクトリを作成
                var cardsDir = Path.Combine(_tempExtractPath, "cards");
                Directory.CreateDirectory(cardsDir);
                
                var cardId = jsonElement.GetProperty("id").GetString();
                var jsonPath = Path.Combine(cardsDir, $"{cardId}.json");
                var cardType = jsonElement.GetProperty("type").GetString();
                
                // 統一したJSONデータ形式を作成
                object jsonData = null;
                
                switch (cardType)
                {
                    case "基本・穴埋め":
                            jsonData = new
                            {
                                id = cardId,
                                type = "基本・穴埋め",
                            front = jsonElement.GetProperty("front").GetString() ?? "",
                            back = jsonElement.GetProperty("back").GetString() ?? "",
                                question = "",
                                explanation = "",
                                choices = new object[0],
                                selectionRects = new object[0]
                            };
                        break;
                        
                    case "選択肢":
                        var choices = jsonElement.GetProperty("choices").EnumerateArray().Select(c => new
                        {
                            text = c.GetProperty("text").GetString() ?? "",
                            isCorrect = c.TryGetProperty("correct", out var correctProp) ? correctProp.GetBoolean() : 
                                (c.TryGetProperty("isCorrect", out var isCorrectProp) ? isCorrectProp.GetBoolean() : false)
                        }).ToArray();
                            

                        jsonData = new
                        {
                            id = cardId,
                            type = "選択肢",
                            front = "",
                            back = "",
                            question = jsonElement.GetProperty("question").GetString() ?? "",
                            explanation = jsonElement.GetProperty("explanation").GetString() ?? "",
                            choices = choices,
                            selectionRects = new object[0]
                        };
                        break;
                        
                    case "画像穴埋め":
                        var selections = jsonElement.GetProperty("selections").EnumerateArray().Select(s => new
                        {
                            x = s.GetProperty("x").GetSingle(),
                            y = s.GetProperty("y").GetSingle(),
                            width = s.GetProperty("width").GetSingle(),
                            height = s.GetProperty("height").GetSingle()
                        }).ToArray();
                        
                        // imagePathフィールドから画像ファイル名を取得（iOS版との互換性のため）
                        string imageFileName = "";
                        if (jsonElement.TryGetProperty("imagePath", out var imagePathElement))
                        {
                            imageFileName = imagePathElement.GetString() ?? "";
                        }
                        else if (jsonElement.TryGetProperty("front", out var frontElement))
                        {
                            // 旧形式の場合、frontフィールドから取得
                            imageFileName = frontElement.GetString() ?? "";
                        }
                            
                        jsonData = new
                        {
                            id = cardId,
                            type = "画像穴埋め",
                            front = imageFileName,  // iOS版との互換性のため画像ファイル名を設定
                            back = "",
                            question = "",
                            explanation = "",
                            choices = new object[0],
                            selectionRects = selections
                        };
                        break;
                }
                
                if (jsonData != null)
                {
                    var jsonString = JsonSerializer.Serialize(jsonData, new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    });
                    await File.WriteAllTextAsync(jsonPath, jsonString, System.Text.Encoding.UTF8);
                    Debug.WriteLine($"JSONファイル作成完了: {jsonPath}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"JSONファイル作成エラー: {ex.Message}");
            }
        }
        /// <summary>
        /// ankplsファイルを更新
        /// </summary>
        private async Task UpdateAnkplsFile()
        {
            try
            {
                await SaveMetadataToFile();
                if (File.Exists(_ankplsFilePath)) File.Delete(_ankplsFilePath);
                ZipFile.CreateFromDirectory(_tempExtractPath, _ankplsFilePath);
                Debug.WriteLine("ankplsファイル更新完了");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ankplsファイル更新エラー: {ex.Message}");
                throw;
            }
        }
        /// <summary>
        /// cards.txtファイルをパースする
        /// </summary>
        private async Task<List<CardInfo>> ParseCardsFile(string content)
        {
            try
            {
                Debug.WriteLine($"ParseCardsFile開始 - コンテンツ長: {content.Length}");
                var cards = new List<CardInfo>();
                var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                Debug.WriteLine($"行数: {lines.Length}");

                // 1行目が数字のみの場合はカード数なのでスキップ
                int startIndex = 0;
                if (lines.Length > 0 && int.TryParse(lines[0], out _))
                {
                    startIndex = 1;
                }

                for (int i = startIndex; i < lines.Length; i++)
                {
                    try
                    {
                        // 行の末尾の改行文字を削除
                        var line = lines[i].TrimEnd('\r', '\n');
                        Debug.WriteLine($"行 {i} をパース: {line}");
                        var parts = line.Split(',');
                        if (parts.Length >= 2)
                        {
                            var card = new CardInfo
                            {
                                Uuid = parts[0],
                                LastModified = DateTime.ParseExact(parts[1].Trim(), "yyyy-MM-dd HH:mm:ss", null),
                                IsDeleted = parts.Length >= 3 && parts[2].Trim() == "deleted"
                            };
                            cards.Add(card);
                            Debug.WriteLine($"カード情報をパース: UUID={card.Uuid}, 最終更新={card.LastModified}, 削除フラグ={card.IsDeleted}");
                        }
                        else
                        {
                            Debug.WriteLine($"行 {i} のパースに失敗: カンマ区切りの値が不足しています");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"カードのパースに失敗: {lines[i]}, エラー: {ex.Message}");
                    }
                }

                Debug.WriteLine($"ParseCardsFile完了 - パースしたカード数: {cards.Count}");
                return cards;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"カードファイルのパース中にエラー: {ex.Message}");
                throw;
            }
        }
        /// <summary>
        /// カード情報を表すクラス
        /// </summary>
        private class CardInfo
        {
            public string Uuid { get; set; }
            public DateTime LastModified { get; set; }
            public string Content { get; set; }
            public bool IsDeleted { get; set; }
        }
    }
}
