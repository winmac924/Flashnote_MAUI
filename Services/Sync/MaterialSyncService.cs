using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Flashnote.Services;

namespace Flashnote.Services.Sync
{
    /// <summary>
    /// ノートに添付された画像/メディアファイルの同期。iOS版 MaterialSyncService.swift に対応。
    /// </summary>
    public class MaterialSyncService
    {
        private readonly BlobStorageService _blobStorageService;

        public MaterialSyncService(BlobStorageService blobStorageService)
        {
            _blobStorageService = blobStorageService;
        }

        /// <summary>
        /// ノートを開く際の画像同期処理
        /// </summary>
        public async Task SyncImagesOnNoteOpenAsync(string uid, string noteName, string subFolder, string localCardsPath)
        {
            try
            {
                Debug.WriteLine($"=== ノート開時画像同期開始: {noteName} ===");

                // ローカルのimgフォルダのパスを取得
                var localImgDir = Path.Combine(Path.GetDirectoryName(localCardsPath), "img");
                Debug.WriteLine($"ローカルのimgフォルダのパス: {localImgDir}");

                // ローカルのimgフォルダが存在しない場合は作成
                if (!Directory.Exists(localImgDir))
                {
                    Directory.CreateDirectory(localImgDir);
                    Debug.WriteLine($"ローカルimgディレクトリを作成: {localImgDir}");
                }

                // サーバーのimgフォルダパスを構築
                string imgServerPath;
                if (!string.IsNullOrEmpty(subFolder))
                {
                    imgServerPath = $"{subFolder}/{noteName}/img";
                }
                else
                {
                    imgServerPath = $"{noteName}/img";
                }

                // サーバーの画像ファイル一覧を取得
                var serverImgFiles = await _blobStorageService.GetImageFilesAsync(uid, imgServerPath);
                Debug.WriteLine($"サーバーの画像ファイル数: {serverImgFiles.Count}");

                if (serverImgFiles.Count == 0)
                {
                    Debug.WriteLine("サーバーに画像ファイルがありません");
                    return;
                }

                // ローカルの画像ファイル一覧を取得
                var localImgFiles = Directory.GetFiles(localImgDir, "img_*.jpg")
                    .Select(Path.GetFileName)
                    .ToList();
                Debug.WriteLine($"ローカルの画像ファイル数: {localImgFiles.Count}");

                // 不足している画像ファイルを特定
                var missingImgFiles = serverImgFiles.Where(serverImg => !localImgFiles.Contains(serverImg)).ToList();
                Debug.WriteLine($"不足している画像ファイル数: {missingImgFiles.Count}");

                if (missingImgFiles.Count == 0)
                {
                    Debug.WriteLine("不足している画像ファイルはありません");
                    return;
                }

                // 不足している画像ファイルをダウンロード
                foreach (var imgFile in missingImgFiles)
                {
                    try
                    {
                        Debug.WriteLine($"画像ファイルのダウンロード開始: {imgFile}");
                        var imgBytes = await _blobStorageService.GetImageBinaryAsync(uid, imgFile, imgServerPath);

                        if (imgBytes != null)
                        {
                            var localImgPath = Path.Combine(localImgDir, imgFile);
                            await File.WriteAllBytesAsync(localImgPath, imgBytes);
                            Debug.WriteLine($"画像ファイルをダウンロード完了: {imgFile} (サイズ: {imgBytes.Length} バイト)");
                        }
                        else
                        {
                            Debug.WriteLine($"画像ファイルのコンテンツが取得できません: {imgFile}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"画像ファイルのダウンロード中にエラー: {imgFile}, エラー: {ex.Message}");
                        Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                    }
                }

                Debug.WriteLine($"=== ノート開時画像同期完了: {noteName} ===");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ノート開時画像同期エラー: {ex.Message}");
                // 画像同期エラーは致命的ではないため、例外を再スローしない
            }
        }
    }
}
