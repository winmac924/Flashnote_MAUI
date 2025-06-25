using Flashnote.Services;
using System.Diagnostics;

namespace Flashnote
{
    public partial class SharedKeyImportPage : ContentPage
    {
        private readonly BlobStorageService _blobStorageService;
        private readonly SharedKeyService _sharedKeyService;
        private readonly CardSyncService _cardSyncService;
        private string _previewedUserId;
        private string _previewedNotePath;
        private bool _previewedIsFolder;

        // インポート完了イベント
        public event EventHandler ImportCompleted;

        public SharedKeyImportPage(BlobStorageService blobStorageService, SharedKeyService sharedKeyService, CardSyncService cardSyncService)
        {
            InitializeComponent();
            _blobStorageService = blobStorageService;
            _sharedKeyService = sharedKeyService;
            _cardSyncService = cardSyncService;
        }

        private async void OnPreviewClicked(object sender, EventArgs e)
        {
            var shareKey = ShareKeyEntry.Text?.Trim();
            if (string.IsNullOrEmpty(shareKey))
            {
                StatusLabel.Text = "共有キーを入力してください。";
                return;
            }

            LoadingIndicator.IsVisible = true;
            LoadingIndicator.IsRunning = true;
            StatusLabel.Text = "共有キーを確認中...";

            try
            {
                var (userId, notePath, isFolder) = await _blobStorageService.AccessNoteWithShareKeyAsync(shareKey);
                
                _previewedUserId = userId;
                _previewedNotePath = notePath;
                _previewedIsFolder = isFolder;

                InfoFrame.IsVisible = true;
                NoteInfoLabel.Text = $"タイプ: {(isFolder ? "フォルダ" : "ノート")}\nパス: {notePath}\n共有元: {userId}";
                StatusLabel.Text = "共有キーが有効です！";
                StatusLabel.TextColor = Colors.Green;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"共有キーのプレビューに失敗: {ex.Message}");
                StatusLabel.Text = "共有キーが無効です。";
                StatusLabel.TextColor = Colors.Red;
                InfoFrame.IsVisible = false;
            }
            finally
            {
                LoadingIndicator.IsVisible = false;
                LoadingIndicator.IsRunning = false;
            }
        }

        private async void OnImportClicked(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_previewedUserId))
            {
                StatusLabel.Text = "まずプレビューを実行してください。";
                StatusLabel.TextColor = Colors.Red;
                return;
            }

            LoadingIndicator.IsVisible = true;
            LoadingIndicator.IsRunning = true;
            StatusLabel.Text = "ノートをインポート中...";

            try
            {
                var shareKey = ShareKeyEntry.Text?.Trim();
                
                if (_previewedIsFolder)
                {
                    // フォルダとして処理されているが、実際は単一ノートの可能性もある
                    var (isActuallyFolder, downloadedNotes) = await _blobStorageService.DownloadSharedFolderAsync(_previewedUserId, _previewedNotePath, shareKey);
                    
                    Debug.WriteLine($"ダウンロード結果: 実際にフォルダ={isActuallyFolder}, ノート数={downloadedNotes.Count}");
                    
                    if (isActuallyFolder)
                    {
                        // 実際にフォルダの場合：フォルダ全体として1つの共有キー情報を保存
                        var folderSharedInfo = new SharedKeyInfo
                        {
                            OriginalUserId = _previewedUserId,
                            NotePath = _previewedNotePath,
                            ShareKey = shareKey,
                            IsFolder = true
                        };
                        
                        var folderName = Path.GetFileName(_previewedNotePath);
                        _sharedKeyService.AddSharedNote(folderName, folderSharedInfo);
                        Debug.WriteLine($"共有フォルダ情報を保存: {folderName} -> {_previewedNotePath}");
                    }
                    else
                    {
                        // 実際には単一ノートの場合：単一ノートとして共有キー情報を保存
                        if (downloadedNotes.Count > 0)
                        {
                            var (noteName, subFolder, fullNotePath) = downloadedNotes[0];
                            var noteSharedInfo = new SharedKeyInfo
                            {
                                OriginalUserId = _previewedUserId,
                                NotePath = fullNotePath,
                                ShareKey = shareKey,
                                IsFolder = false
                            };
                            
                            _sharedKeyService.AddSharedNote(noteName, noteSharedInfo);
                            Debug.WriteLine($"共有ノート情報を保存（フォルダとして報告されたが実際は単一ノート）: {noteName} -> {fullNotePath}");
                        }
                    }
                }
                else
                {
                    // 単一ノートの場合
                    // ParseBlobPathメソッドを使用してサブフォルダとノート名を正しく分離
                    var (subFolder, noteName, _) = _blobStorageService.ParseBlobPath(_previewedNotePath);
                    
                    Debug.WriteLine($"共有ノートインポート - パス: {_previewedNotePath}, サブフォルダ: {subFolder}, ノート名: {noteName}");
                    
                    var sharedInfo = new SharedKeyInfo
                    {
                        OriginalUserId = _previewedUserId,
                        NotePath = _previewedNotePath,
                        ShareKey = shareKey,
                        IsFolder = false
                    };

                    _sharedKeyService.AddSharedNote(noteName, sharedInfo);
                    await _blobStorageService.DownloadSharedNoteAsync(_previewedUserId, _previewedNotePath, noteName, subFolder);
                }

                StatusLabel.Text = "インポートが完了しました！";
                StatusLabel.TextColor = Colors.Green;
                
                // インポート完了イベントを発火
                ImportCompleted?.Invoke(this, EventArgs.Empty);
                
                // 2秒後にページを閉じる
                await Task.Delay(2000);
                await Navigation.PopAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ノートのインポートに失敗: {ex.Message}");
                StatusLabel.Text = "インポートに失敗しました。";
                StatusLabel.TextColor = Colors.Red;
            }
            finally
            {
                LoadingIndicator.IsVisible = false;
                LoadingIndicator.IsRunning = false;
            }
        }

        private async void OnCancelClicked(object sender, EventArgs e)
        {
            await Navigation.PopAsync();
        }
    }
} 