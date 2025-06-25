using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Flashnote.Services
{
    public class LearningResultSyncService
    {
        private readonly BlobStorageService _blobStorageService;
        private DateTime _lastSyncTime = DateTime.MinValue;
        private bool _hasUnsyncedChanges = false; // 未同期の変更があるかのフラグ

        public LearningResultSyncService(BlobStorageService blobStorageService)
        {
            _blobStorageService = blobStorageService;
        }

        /// <summary>
        /// アプリ起動時に学習記録をダウンロードして同期
        /// </summary>
        public async Task SyncOnAppStartAsync(string uid, string noteName, string subFolder = null)
        {
            try
            {
                Debug.WriteLine($"アプリ起動時の学習記録同期開始 - ノート: {noteName}");
                
                var localResultPath = GetLocalResultPath(noteName, subFolder);
                var cloudResult = await _blobStorageService.DownloadLearningResultAsync(uid, noteName, subFolder);
                
                if (cloudResult != null)
                {
                    var localLastModified = File.Exists(localResultPath) 
                        ? File.GetLastWriteTime(localResultPath) 
                        : DateTime.MinValue;
                    
                    var cloudLastModified = await _blobStorageService.GetLearningResultLastModifiedAsync(uid, noteName, subFolder) 
                                          ?? DateTime.MinValue;

                    // クラウドの方が新しい場合はローカルを更新
                    if (cloudLastModified > localLastModified)
                    {
                        await File.WriteAllTextAsync(localResultPath, cloudResult);
                        Debug.WriteLine($"クラウドからローカルに学習記録を同期: {localResultPath}");
                    }
                    // ローカルの方が新しい場合はクラウドを更新
                    else if (localLastModified > cloudLastModified && File.Exists(localResultPath))
                    {
                        var localResult = await File.ReadAllTextAsync(localResultPath);
                        await _blobStorageService.UploadLearningResultAsync(uid, noteName, localResult, subFolder);
                        Debug.WriteLine($"ローカルからクラウドに学習記録を同期");
                    }
                }
                else if (File.Exists(localResultPath))
                {
                    // クラウドにファイルがない場合はローカルをアップロード
                    var localResult = await File.ReadAllTextAsync(localResultPath);
                    await _blobStorageService.UploadLearningResultAsync(uid, noteName, localResult, subFolder);
                    Debug.WriteLine($"ローカルの学習記録をクラウドに初回アップロード");
                }

                _lastSyncTime = DateTime.Now;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"アプリ起動時の学習記録同期エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 問題を解いた時に呼び出される（未同期フラグを立てる）
        /// </summary>
        public void OnQuestionAnswered()
        {
            _hasUnsyncedChanges = true;
            Debug.WriteLine("未同期の変更フラグを設定");
        }

        /// <summary>
        /// 学習セッション終了時に同期（未同期の変更がある場合のみ）
        /// </summary>
        public async Task SyncOnSessionEndAsync(string uid, string noteName, string subFolder = null)
        {
            try
            {
                if (_hasUnsyncedChanges)
                {
                    Debug.WriteLine($"学習セッション終了時の同期開始（未同期の変更あり）");
                    await PerformSyncAsync(uid, noteName, subFolder);
                    _hasUnsyncedChanges = false;
                }
                else
                {
                    Debug.WriteLine("学習セッション終了：未同期の変更なし、同期をスキップ");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"学習セッション終了時の同期エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 手動で同期を実行（必要に応じて）
        /// </summary>
        public async Task ManualSyncAsync(string uid, string noteName, string subFolder = null)
        {
            try
            {
                Debug.WriteLine("手動同期を実行");
                await PerformSyncAsync(uid, noteName, subFolder);
                _hasUnsyncedChanges = false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"手動同期エラー: {ex.Message}");
            }
        }

        private async Task PerformSyncAsync(string uid, string noteName, string subFolder = null)
        {
            try
            {
                var localResultPath = GetLocalResultPath(noteName, subFolder);
                
                if (File.Exists(localResultPath))
                {
                    var localResult = await File.ReadAllTextAsync(localResultPath);
                    await _blobStorageService.UploadLearningResultAsync(uid, noteName, localResult, subFolder);
                    _lastSyncTime = DateTime.Now;
                    Debug.WriteLine($"学習記録をクラウドに同期完了: {noteName}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"学習記録同期エラー: {ex.Message}");
            }
        }

        private string GetLocalResultPath(string noteName, string subFolder = null)
        {
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var basePath = Path.Combine(documentsPath, "Flashnote");
            
            if (!string.IsNullOrEmpty(subFolder))
            {
                basePath = Path.Combine(basePath, subFolder);
            }
            
            var tempFolderName = $"{noteName}_temp";
            var tempPath = Path.Combine(basePath, tempFolderName);
            
            return Path.Combine(tempPath, "result.txt");
        }

        /// <summary>
        /// 未同期の変更があるかチェック
        /// </summary>
        public bool HasUnsyncedChanges => _hasUnsyncedChanges;
    }
} 