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
        /// 通常ノート（自分のUID配下）にカードを保存
        /// </summary>
        private async Task SaveCardToRegularNoteAsync(string uid, string noteName, string subFolder)
        {
            try
            {
                Debug.WriteLine($"=== 通常ノートへのカード保存開始: {noteName} ===");

                // 新しく保存されたカードの情報を取得
                var jsonElement = JsonSerializer.Deserialize<JsonElement>(_cards.Last());
                var cardId = jsonElement.GetProperty("id").GetString();
                var cardJsonPath = Path.Combine(_tempExtractPath, "cards", $"{cardId}.json");

                // カードのJSONコンテンツを取得
                string cardContent = null;
                if (File.Exists(cardJsonPath))
                {
                    cardContent = await File.ReadAllTextAsync(cardJsonPath);
                }

                if (string.IsNullOrEmpty(cardContent))
                {
                    Debug.WriteLine($"カードJSONファイルが見つかりません: {cardJsonPath}");
                    throw new FileNotFoundException($"カードJSONファイルが見つかりません: {cardId}");
                }

                // Append機能を使用してカードを追加
                await _blobStorageService.AppendCardToNoteAsync(uid, noteName, cardId, cardContent, subFolder);
                Debug.WriteLine($"通常ノート: カードをAppend機能で追加: {cardId}");

                // 画像ファイルをアップロード
                await UploadImagesToRegularNoteAsync(uid, noteName, subFolder);

                Debug.WriteLine($"=== 通常ノートへのカード保存完了: {noteName} ===");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"通常ノートへのカード保存エラー: {ex.Message}");
                throw;
            }
        }
        /// <summary>
        /// 共有ノート（元のUID配下）にカードを保存
        /// </summary>
        private async Task SaveCardToSharedNoteAsync(string noteName, string subFolder, SharedKeyService sharedKeyService)
        {
            try
            {
                Debug.WriteLine($"=== 共有ノートへのカード保存開始: {noteName} ===");

                // 共有ノート情報を取得
                var sharedInfo = sharedKeyService.GetSharedNoteInfo(subFolder);
                if (sharedInfo == null)
                {
                    Debug.WriteLine($"共有ノート情報が見つかりません: {subFolder}");
                    return;
                }

                Debug.WriteLine($"共有ノート情報 - 元UID: {sharedInfo.OriginalUserId}, パス: {sharedInfo.NotePath}");

                // 新しく保存されたカードの情報を取得
                var jsonElement = JsonSerializer.Deserialize<JsonElement>(_cards.Last());
                var cardId = jsonElement.GetProperty("id").GetString();
                var cardJsonPath = Path.Combine(_tempExtractPath, "cards", $"{cardId}.json");

                // カードのJSONコンテンツを取得
                string cardContent = null;
                if (File.Exists(cardJsonPath))
                {
                    cardContent = await File.ReadAllTextAsync(cardJsonPath);
                }

                if (string.IsNullOrEmpty(cardContent))
                {
                    Debug.WriteLine($"カードJSONファイルが見つかりません: {cardJsonPath}");
                    throw new FileNotFoundException($"カードJSONファイルが見つかりません: {cardId}");
                }

                // Append機能を使用してカードを追加
                var fullNotePath = $"{sharedInfo.NotePath}/{noteName}";
                await _blobStorageService.AppendCardToSharedNoteAsync(sharedInfo.OriginalUserId, fullNotePath, cardId, cardContent);
                Debug.WriteLine($"共有ノート: カードをAppend機能で追加: {cardId}");

                // 画像ファイルをアップロード
                await UploadImagesToSharedNoteAsync(noteName, subFolder, sharedKeyService);

                Debug.WriteLine($"=== 共有ノートへのカード保存完了: {noteName} ===");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"共有ノートへのカード保存エラー: {ex.Message}");
                throw;
            }
        }
        /// <summary>
        /// 通常ノートに画像ファイルをアップロード
        /// </summary>
        private async Task UploadImagesToRegularNoteAsync(string uid, string noteName, string subFolder)
        {
            try
            {
                Debug.WriteLine($"=== 通常ノートへの画像アップロード開始: {noteName} ===");

                // ローカルのimgフォルダのパスを取得
                var localImgDir = Path.Combine(_tempExtractPath, "img");
                Debug.WriteLine($"ローカルのimgフォルダのパス: {localImgDir}");
                Debug.WriteLine($"ローカルのimgフォルダの存在確認: {Directory.Exists(localImgDir)}");

                if (!Directory.Exists(localImgDir))
                {
                    Debug.WriteLine("ローカル画像ディレクトリが存在しません");
                    return;
                }

                var localImgFiles = Directory.GetFiles(localImgDir, "img_*.jpg");
                Debug.WriteLine($"ローカルの画像ファイル数: {localImgFiles.Length}");

                if (localImgFiles.Length == 0)
                {
                    Debug.WriteLine("アップロード対象の画像ファイルがありません");
                    return;
                }

                foreach (var imgFile in localImgFiles)
                {
                    try
                    {
                        var fileName = Path.GetFileName(imgFile);
                        // iOS版の形式（img_########_######.jpg）をチェック
                        if (Regex.IsMatch(fileName, @"^img_\d{8}_\d{6}\.jpg$"))
                        {
                            Debug.WriteLine($"画像ファイルのアップロード開始: {fileName}");
                            var imgBytes = await File.ReadAllBytesAsync(imgFile);
                            Debug.WriteLine($"画像ファイルのサイズ: {imgBytes.Length} バイト");

                            // Base64エンコードしてアップロード
                            var base64Content = Convert.ToBase64String(imgBytes);
                            await _blobStorageService.UploadImageToNoteAsync(uid, noteName, fileName, base64Content, subFolder);
                            Debug.WriteLine($"画像ファイルをアップロード完了: {fileName}");
                        }
                        else
                        {
                            Debug.WriteLine($"画像ファイル名の形式が正しくありません: {fileName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"画像ファイルのアップロード中にエラー: {imgFile}, エラー: {ex.Message}");
                        Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                    }
                }

                Debug.WriteLine($"=== 通常ノートへの画像アップロード完了: {noteName} ===");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"通常ノートへの画像アップロードエラー: {ex.Message}");
                throw;
            }
        }
        /// <summary>
        /// 共有ノートに画像ファイルをアップロード
        /// </summary>
        private async Task UploadImagesToSharedNoteAsync(string noteName, string subFolder, SharedKeyService sharedKeyService)
        {
            try
            {
                Debug.WriteLine($"=== 共有ノートへの画像アップロード開始: {noteName} ===");

                // 共有ノート情報を取得
                var sharedInfo = sharedKeyService.GetSharedNoteInfo(subFolder);
                if (sharedInfo == null)
                {
                    Debug.WriteLine($"共有ノート情報が見つかりません: {subFolder}");
                    return;
                }

                // ローカルのimgフォルダのパスを取得
                var localImgDir = Path.Combine(_tempExtractPath, "img");
                Debug.WriteLine($"ローカルのimgフォルダのパス: {localImgDir}");
                Debug.WriteLine($"ローカルのimgフォルダの存在確認: {Directory.Exists(localImgDir)}");

                if (!Directory.Exists(localImgDir))
                {
                    Debug.WriteLine("ローカル画像ディレクトリが存在しません");
                    return;
                }

                var localImgFiles = Directory.GetFiles(localImgDir, "img_*.jpg");
                Debug.WriteLine($"ローカルの画像ファイル数: {localImgFiles.Length}");

                if (localImgFiles.Length == 0)
                {
                    Debug.WriteLine("アップロード対象の画像ファイルがありません");
                    return;
                }

                foreach (var imgFile in localImgFiles)
                {
                    try
                    {
                        var fileName = Path.GetFileName(imgFile);
                        // iOS版の形式（img_########_######.jpg）をチェック
                        if (Regex.IsMatch(fileName, @"^img_\d{8}_\d{6}\.jpg$"))
                        {
                            Debug.WriteLine($"画像ファイルのアップロード開始: {fileName}");
                            var imgBytes = await File.ReadAllBytesAsync(imgFile);
                            Debug.WriteLine($"画像ファイルのサイズ: {imgBytes.Length} バイト");

                            // Base64エンコードしてアップロード
                            var base64Content = Convert.ToBase64String(imgBytes);
                            var fullImgPath = $"{sharedInfo.NotePath}/{noteName}";
                            await _blobStorageService.UploadSharedImageAsync(sharedInfo.OriginalUserId, fileName, base64Content, fullImgPath);
                            Debug.WriteLine($"画像ファイルをアップロード完了: {fileName}");
                        }
                        else
                        {
                            Debug.WriteLine($"画像ファイル名の形式が正しくありません: {fileName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"画像ファイルのアップロード中にエラー: {imgFile}, エラー: {ex.Message}");
                        Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                    }
                }

                Debug.WriteLine($"=== 共有ノートへの画像アップロード完了: {noteName} ===");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"共有ノートへの画像アップロードエラー: {ex.Message}");
                throw;
            }
        }
    }
}
