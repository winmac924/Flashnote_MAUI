using Microsoft.Maui.Controls;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Flashnote.Models;
using Flashnote.Services;
using Flashnote_MAUI.Services;

namespace Flashnote
{
    public partial class SharedKeysSheet : ContentPage
    {
        private readonly SharedKeyService _sharedKeyService;
        private ObservableCollection<ImportedSharedKeyItem> _sharedKeys;
        private TaskCompletionSource<bool> _completionSource;

        public SharedKeysSheet()
        {
            InitializeComponent();
            _sharedKeyService = new SharedKeyService();
            _sharedKeys = new ObservableCollection<ImportedSharedKeyItem>();
            _completionSource = new TaskCompletionSource<bool>();
            
            SharedKeysCollection.ItemsSource = _sharedKeys;
            LoadSharedKeys();
            
            // ページが表示された後にアニメーションを開始
            this.Appearing += OnPageAppearing;
        }

        public Task<bool> ShowAsync()
        {
            return _completionSource.Task;
        }

        private void LoadSharedKeys()
        {
            try
            {
                _sharedKeys.Clear();
                var sharedNotes = _sharedKeyService.GetSharedNotes();
                
                foreach (var (noteName, sharedInfo) in sharedNotes)
                {
                    var item = new ImportedSharedKeyItem
                    {
                        NoteName = noteName,
                        Info = $"タイプ: {(sharedInfo.IsFolder ? "フォルダ" : "ノート")} | パス: {sharedInfo.NotePath} | 共有元: {sharedInfo.OriginalUserId}"
                    };
                    _sharedKeys.Add(item);
                }
                
                // ステータスラベルの更新
                if (_sharedKeys.Count > 0)
                {
                    StatusLabel.Text = $"共有キー: {_sharedKeys.Count}件";
                    StatusLabel.TextColor = Colors.Green;
                }
                else
                {
                    StatusLabel.Text = "共有キーがありません";
                    StatusLabel.TextColor = Colors.Gray;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"共有キーの読み込み中にエラー: {ex.Message}");
                StatusLabel.Text = "共有キーの読み込みに失敗しました";
                StatusLabel.TextColor = Colors.Red;
            }
        }

        private async void OnRemoveSharedKeyClicked(object sender, EventArgs e)
        {
            try
            {
                if (sender is Button button && button.CommandParameter is string noteName)
                {
                    bool result = await UIThreadHelper.ShowAlertAsync("確認", $"共有キー「{noteName}」を削除しますか？", "はい", "いいえ");
                    if (result)
                    {
                        _sharedKeyService.RemoveSharedNote(noteName);
                        LoadSharedKeys(); // リストを再読み込み
                        await UIThreadHelper.ShowAlertAsync("完了", "共有キーを削除しました。", "OK");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"共有キーの削除中にエラー: {ex.Message}");
                await UIThreadHelper.ShowAlertAsync("エラー", "共有キーの削除に失敗しました。", "OK");
            }
        }

        private void OnCloseClicked(object sender, EventArgs e)
        {
            CloseSheet();
        }

        private void OnBackgroundTapped(object sender, EventArgs e)
        {
            CloseSheet();
        }

        private async void OnPageAppearing(object sender, EventArgs e)
        {
            // シートを下から上にスライドイン
            await SheetFrame.TranslateTo(0, 0, 300, Easing.CubicOut);
        }

        private async void CloseSheet()
        {
            // シートを下にスライドアウト
            await SheetFrame.TranslateTo(0, 400, 300, Easing.CubicIn);
            _completionSource.TrySetResult(true);
        }

        protected override bool OnBackButtonPressed()
        {
            CloseSheet();
            return true;
        }
    }
} 