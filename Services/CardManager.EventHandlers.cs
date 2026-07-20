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
        /// 太字ボタンのクリックイベント
        /// </summary>
        private async void OnBoldClicked(object sender, EventArgs e)
        {
            var editor = GetCurrentEditor();
            if (editor != null)
            {
                InsertDecorationText(editor, "**", "**");
                // 追加のフォーカス復元
                await Task.Delay(200);
                await RestoreFocusToEditor(editor);
            }
        }
        /// <summary>
        /// 赤色ボタンのクリックイベント
        /// </summary>
        private async void OnRedColorClicked(object sender, EventArgs e)
        {
            var editor = GetCurrentEditor();
            if (editor != null)
            {
                InsertDecorationText(editor, "{{red|", "}}");
                // 追加のフォーカス復元
                await Task.Delay(200);
                await RestoreFocusToEditor(editor);
            }
        }
        /// <summary>
        /// 青色ボタンのクリックイベント
        /// </summary>
        private async void OnBlueColorClicked(object sender, EventArgs e)
        {
            var editor = GetCurrentEditor();
            if (editor != null)
            {
                InsertDecorationText(editor, "{{blue|", "}}");
                // 追加のフォーカス復元
                await Task.Delay(200);
                await RestoreFocusToEditor(editor);
            }
        }
        /// <summary>
        /// 緑色ボタンのクリックイベント
        /// </summary>
        private async void OnGreenColorClicked(object sender, EventArgs e)
        {
            var editor = GetCurrentEditor();
            if (editor != null)
            {
                InsertDecorationText(editor, "{{green|", "}}");
                // 追加のフォーカス復元
                await Task.Delay(200);
                await RestoreFocusToEditor(editor);
            }
        }
        /// <summary>
        /// 黄色ボタンのクリックイベント
        /// </summary>
        private async void OnYellowColorClicked(object sender, EventArgs e)
        {
            var editor = GetCurrentEditor();
            if (editor != null)
            {
                InsertDecorationText(editor, "{{yellow|", "}}");
                // 追加のフォーカス復元
                await Task.Delay(200);
                await RestoreFocusToEditor(editor);
            }
        }
        /// <summary>
        /// 紫色ボタンのクリックイベント
        /// </summary>
        private async void OnPurpleColorClicked(object sender, EventArgs e)
        {
            var editor = GetCurrentEditor();
            if (editor != null)
            {
                InsertDecorationText(editor, "{{purple|", "}}");
                // 追加のフォーカス復元
                await Task.Delay(200);
                await RestoreFocusToEditor(editor);
            }
        }
        /// <summary>
        /// オレンジ色ボタンのクリックイベント
        /// </summary>
        private async void OnOrangeColorClicked(object sender, EventArgs e)
        {
            var editor = GetCurrentEditor();
            if (editor != null)
            {
                InsertDecorationText(editor, "{{orange|", "}}");
                // 追加のフォーカス復元
                await Task.Delay(200);
                await RestoreFocusToEditor(editor);
            }
        }
        /// <summary>
        /// 上付き文字ボタンのクリックイベント
        /// </summary>
        private async void OnSuperscriptClicked(object sender, EventArgs e)
        {
            var editor = GetCurrentEditor();
            if (editor != null)
            {
                InsertDecorationText(editor, "^^", "^^");
                // 追加のフォーカス復元
                await Task.Delay(200);
                await RestoreFocusToEditor(editor);
            }
        }
        /// <summary>
        /// 下付き文字ボタンのクリックイベント
        /// </summary>
        private async void OnSubscriptClicked(object sender, EventArgs e)
        {
            var editor = GetCurrentEditor();
            if (editor != null)
            {
                InsertDecorationText(editor, "~~", "~~");
                // 追加のフォーカス復元
                await Task.Delay(200);
                await RestoreFocusToEditor(editor);
            }
        }
        /// <summary>
        /// 穴埋めボタンのクリックイベント
        /// </summary>
        private async void OnBlankClicked(object sender, EventArgs e)
        {
            var editor = GetCurrentEditor();
            
            // 基本カードの表面エディターでのみ穴埋めを挿入
            if (editor != null && editor == _frontTextEditor)
            {
                Debug.WriteLine($"穴埋めを挿入: {editor.AutomationId}");
                InsertBlankText(editor);
                // 追加のフォーカス復元
                await Task.Delay(200);
                await RestoreFocusToEditor(editor);
            }
            else
            {
                Debug.WriteLine("穴埋めは基本カードの表面でのみ使用できます");
            }
        }
        // Blank 検出用
        private class BlankInfo { public bool IsInBlank; public int StartIndex; public int EndIndex; public string InnerText; }
        /// <summary>
        /// カード保存ボタンクリック
        /// </summary>
        private async void OnSaveCardClicked(object sender, EventArgs e)
        {
            try
            {
                Debug.WriteLine("=== カード保存処理開始 ===");
                
                var selectedType = _cardTypePicker.SelectedItem?.ToString();
                Debug.WriteLine($"選択されたカードタイプ: {selectedType}");
                
                // ネットワーク状態を事前確認
                var networkStateService = MauiProgram.Services.GetService<NetworkStateService>();
                bool isNetworkAvailable = networkStateService?.IsNetworkAvailable ?? false;
                Debug.WriteLine($"ネットワーク状態確認: {(isNetworkAvailable ? "オンライン" : "オフライン")}");
                
                // ログイン状態を事前確認
                var isLoggedIn = await App.ValidateLoginStatusAsync();
                Debug.WriteLine($"ログイン状態確認: {(isLoggedIn ? "ログイン済み" : "未ログイン")}");
                
                // カードをローカルに保存
                switch (selectedType)
                {
                    case "基本・穴埋め":
                        await SaveBasicCard();
                        break;
                    case "選択肢":
                        await SaveChoiceCard();
                        break;
                    case "画像穴埋め":
                        await SaveImageFillCard();
                        break;
                    default:
                        if (_showToastCallback != null)
                        {
                            await _showToastCallback("カードタイプが選択されていません");
                        }
                        else
                        {
                            await UIThreadHelper.ShowAlertAsync("エラー", "カードタイプが選択されていません。", "OK");
                        }
                        return;
                }
                
                // ネットワークとログイン状態に基づいてBlob Storageアップロードを決定
                if (isNetworkAvailable && isLoggedIn)
                {
                    // オンラインかつログイン済みの場合のみBlob Storageにアップロード
                    try
                    {
                        var uid = App.CurrentUser?.Uid;
                        if (!string.IsNullOrEmpty(uid))
                        {
                            var noteName = Path.GetFileNameWithoutExtension(_ankplsFilePath);
                            string subFolder = null;
                            
                            // サブフォルダ情報を取得
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
                            
                            Debug.WriteLine($"Blob Storageアップロード開始: {noteName}");
                            
                            // 共有ノートかどうかをチェック
                            var sharedKeyService = MauiProgram.Services.GetService<SharedKeyService>();
                            bool isSharedNote = !string.IsNullOrEmpty(subFolder) && sharedKeyService.IsInSharedFolder(noteName, subFolder);
                            
                            if (isSharedNote)
                            {
                                // 共有ノートの場合は元のUID配下に保存
                                await SaveCardToSharedNoteAsync(noteName, subFolder, sharedKeyService);
                            }
                            else
                            {
                                // 通常ノートの場合は自分のUID配下に保存
                                await SaveCardToRegularNoteAsync(uid, noteName, subFolder);
                            }
                            
                            Debug.WriteLine($"Blob Storageアップロード完了: {noteName}");
                        }
                    }
                    catch (Exception uploadEx)
                    {
                        Debug.WriteLine($"Blob Storageアップロードエラー: {uploadEx.Message}");
                        
                        // アップロード失敗時は未同期ノートとして記録
                        await RecordUnsynchronizedNote("upload_failed");
                        
                        // アップロードエラーをユーザーに通知
                        string errorMessage = "サーバーへの同期に失敗しました。カードはローカルに保存されています。ログイン復帰時に自動同期されます。";
                        if (_showToastCallback != null)
                        {
                            await _showToastCallback(errorMessage);
                        }
                        else
                        {
                            await UIThreadHelper.ShowAlertAsync("同期エラー", errorMessage, "OK");
                        }
                    }
                }
                else
                {
                    // オフラインまたは未ログインの場合は未同期ノートとして記録
                    string reason = isNetworkAvailable ? "not_logged_in" : "offline";
                    await RecordUnsynchronizedNote(reason);
                    
                    // オフライン時の通知（デバッグログのみ）
                    if (!isNetworkAvailable)
                    {
                        string offlineMessage = "オフラインのため、サーバーへの同期をスキップしました。カードはローカルに保存されています。ログイン復帰時に自動同期されます。";
                        Debug.WriteLine($"オフライン通知: {offlineMessage}");
                        // トースト表示は削除（App.xaml.csでオフライン検知時に一度だけ表示）
                    }
                    else if (!isLoggedIn)
                    {
                        string notLoggedInMessage = "ログインしていないため、サーバーへの同期をスキップしました。カードはローカルに保存されます。ログイン後に自動同期されます。";
                        if (_showToastCallback != null)
                        {
                            await _showToastCallback(notLoggedInMessage);
                        }
                        else
                        {
                            Debug.WriteLine($"未ログイン通知: {notLoggedInMessage}");
                        }
                    }
                }
                
                Debug.WriteLine("=== カード保存処理完了 ===");
                
                // フィールドをリセット
                ResetCardFields();
                
                // 成功通知を表示
                if (_showToastCallback != null)
                {
                    await _showToastCallback("カードが保存されました");
                }
                else
                {
                    Debug.WriteLine("トーストコールバックが設定されていません");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"カード保存エラー: {ex.Message}");
                
                // エラー時はトーストまたはアラートを表示
                if (_showToastCallback != null)
                {
                    await _showToastCallback("カードの保存に失敗しました");
                }
                else
                {
                    await UIThreadHelper.ShowAlertAsync("エラー", "カードの保存に失敗しました。", "OK");
                }
            }
        }
        /// <summary>
        /// 基本カードを保存
        /// </summary>
        private async Task SaveBasicCard()
        {
            try
            {
                var frontText = _frontTextEditor.Text?.Replace("\r\n", "\n").Replace("\r", "\n") ?? "";
                var backText = _backTextEditor.Text?.Replace("\r\n", "\n").Replace("\r", "\n") ?? "";
                
                if (string.IsNullOrWhiteSpace(frontText))
                {
                    if (_showToastCallback != null)
                    {
                        await _showToastCallback("表面を入力してください");
                    }
                    else
                    {
                        await UIThreadHelper.ShowAlertAsync("エラー", "表面を入力してください。", "OK");
                    }
                    return;
                }
                
                var cardId = string.IsNullOrEmpty(_editCardId) ? Guid.NewGuid().ToString() : _editCardId;
                
                // JSONフォーマットでカードデータを作成
                var cardData = JsonSerializer.Serialize(new
                {
                    id = cardId,
                    type = "基本・穴埋め",
                    front = frontText,
                    back = backText
                }, new JsonSerializerOptions
                {
                    WriteIndented = false,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
                
                await SaveCardData(cardData);
                
                Debug.WriteLine($"基本カード保存完了: {cardId}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"基本カード保存エラー: {ex.Message}");
                throw;
            }
        }
        /// <summary>
        /// 選択肢カードを保存
        /// </summary>
        private async Task SaveChoiceCard()
        {
            try
            {
                var questionText = _choiceQuestion.Text?.Replace("\r\n", "\n").Replace("\r", "\n") ?? "";
                var explanationText = _choiceQuestionExplanation.Text?.Replace("\r\n", "\n").Replace("\r", "\n") ?? "";
                
                if (string.IsNullOrWhiteSpace(questionText))
                {
                    if (_showToastCallback != null)
                    {
                        await _showToastCallback("選択肢問題を入力してください");
                    }
                    else
                    {
                        await UIThreadHelper.ShowAlertAsync("エラー", "選択肢問題を入力してください。", "OK");
                    }
                    return;
                }
                
                // 選択肢データを収集
                var choices = new List<object>();
                foreach (var child in _choicesContainer.Children)
                {
                    if (child is HorizontalStackLayout layout)
                    {
                        var editor = layout.Children.OfType<Editor>().FirstOrDefault();
                        var checkBox = layout.Children.OfType<CheckBox>().FirstOrDefault();
                        
                        if (editor != null && !string.IsNullOrWhiteSpace(editor.Text))
                        {
                            choices.Add(new
                            {
                                text = editor.Text?.Replace("\r\n", "\n").Replace("\r", "\n") ?? "",
                                correct = checkBox?.IsChecked ?? false
                            });
                        }
                    }
                }
                
                if (choices.Count < 1)
                {
                    if (_showToastCallback != null)
                    {
                        await _showToastCallback("最低1つの選択肢を入力してください");
                    }
                    else
                    {
                        await UIThreadHelper.ShowAlertAsync("エラー", "最低1つの選択肢を入力してください。", "OK");
                    }
                    return;
                }
                
                var cardId = string.IsNullOrEmpty(_editCardId) ? Guid.NewGuid().ToString() : _editCardId;
                
                // JSONフォーマットでカードデータを作成
                var cardData = JsonSerializer.Serialize(new
                {
                    id = cardId,
                    type = "選択肢",
                    question = questionText,
                    explanation = explanationText,
                    choices = choices
                }, new JsonSerializerOptions
                {
                    WriteIndented = false,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
                
                await SaveCardData(cardData);
                
                Debug.WriteLine($"選択肢カード保存完了: {cardId}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"選択肢カード保存エラー: {ex.Message}");
                throw;
            }
        }
        /// <summary>
        /// 画像穴埋めカードを保存
        /// </summary>
        private async Task SaveImageFillCard()
        {
            try
            {
                if (string.IsNullOrEmpty(_selectedImagePath))
                {
                    if (_showToastCallback != null)
                    {
                        await _showToastCallback("画像を選択してください");
                    }
                    else
                    {
                        await UIThreadHelper.ShowAlertAsync("エラー", "画像を選択してください。", "OK");
                    }
                    return;
                }
                
                if (_selectionRects.Count == 0)
                {
                    if (_showToastCallback != null)
                    {
                        await _showToastCallback("穴埋め範囲を選択してください");
                    }
                    else
                    {
                        await UIThreadHelper.ShowAlertAsync("エラー", "穴埋め範囲を選択してください。", "OK");
                    }
                    return;
                }
                
                // 選択範囲を正しい形式でシリアライズ（画像座標）
                var selections = _selectionRects.Select(rect => new
                {
                    x = rect.Left,  // 画像座標（ピクセル単位）
                    y = rect.Top,   // 画像座標（ピクセル単位）
                    width = rect.Width,  // 画像座標（ピクセル単位）
                    height = rect.Height // 画像座標（ピクセル単位）
                }).ToList();
                
                var cardId = string.IsNullOrEmpty(_editCardId) ? Guid.NewGuid().ToString() : _editCardId;
                
                // JSONフォーマットでカードデータを作成
                var cardData = JsonSerializer.Serialize(new
                {
                    id = cardId,
                    type = "画像穴埋め",
                    front = _selectedImagePath,  // iOS版との互換性のためfrontにも画像ファイル名を保存
                    imagePath = _selectedImagePath,
                    selections = selections
                }, new JsonSerializerOptions
                {
                    WriteIndented = false,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
                
                await SaveCardData(cardData);
                
                Debug.WriteLine($"画像穴埋めカード保存完了: {cardId}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"画像穴埋めカード保存エラー: {ex.Message}");
                throw;
            }
        }
        /// <summary>
        /// カード入力フィールドをリセット
        /// </summary>
        private void ResetCardFields()
        {
            try
            {
                // 基本カードのフィールドをリセット
                if (_frontTextEditor != null)
                    _frontTextEditor.Text = "";
                if (_backTextEditor != null)
                    _backTextEditor.Text = "";
                
                // 選択肢カードのフィールドをリセット
                if (_choiceQuestion != null)
                    _choiceQuestion.Text = "";
                if (_choiceQuestionExplanation != null)
                    _choiceQuestionExplanation.Text = "";
                
                // 選択肢コンテナをチェック
                if (_choicesContainer != null)
                {
                    _choicesContainer.Children.Clear();
                    Debug.WriteLine("既存の選択肢をクリアしました");
                }
                
                // 画像穴埋めカードをリセット
                _selectedImagePath = "";
                _selectionRects.Clear();
                _selectedRectIndex = -1;
                _isMoving = false;
                _isResizing = false;
                _resizeHandle = -1;
                
                // 編集モードをリセット
                _editCardId = null;
                
                Debug.WriteLine("カード入力フィールドをリセットしました");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"フィールドリセットエラー: {ex.Message}");
            }
        }
        /// <summary>
        /// フィールドに何か入力されているかチェック
        /// </summary>
        public bool HasUnsavedChanges()
        {
            try
            {
                // 基本カードのフィールドをチェック
                if (!string.IsNullOrWhiteSpace(_frontTextEditor?.Text) || 
                    !string.IsNullOrWhiteSpace(_backTextEditor?.Text))
                {
                    Debug.WriteLine("基本カードフィールドに未保存の変更があります");
                    return true;
                }
                
                // 選択肢カードのフィールドをチェック
                if (!string.IsNullOrWhiteSpace(_choiceQuestion?.Text) || 
                    !string.IsNullOrWhiteSpace(_choiceQuestionExplanation?.Text))
                {
                    Debug.WriteLine("選択肢カードフィールドに未保存の変更があります");
                    return true;
                }
                
                // 選択肢コンテナをチェック
                if (_choicesContainer != null)
                {
                    foreach (var child in _choicesContainer.Children)
                    {
                        if (child is HorizontalStackLayout layout)
                        {
                            var editor = layout.Children.OfType<Editor>().FirstOrDefault();
                            if (editor != null && !string.IsNullOrWhiteSpace(editor.Text))
                            {
                                Debug.WriteLine("選択肢フィールドに未保存の変更があります");
                                return true;
                            }
                        }
                    }
                }
                
                // 画像穴埋めカードをチェック
                if (!string.IsNullOrEmpty(_selectedImagePath) || _selectionRects.Count > 0)
                {
                    Debug.WriteLine("画像穴埋めカードに未保存の変更があります");
                    return true;
                }
                
                Debug.WriteLine("未保存の変更はありません");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"未保存変更チェックエラー: {ex.Message}");
                return false;
            }
        }
        /// <summary>
        /// 破棄確認ダイアログを表示
        /// </summary>
        public async Task<bool> ShowDiscardConfirmationDialog()
        {
            try
            {
                if (_showAlertCallback != null)
                {
                    // NotePage用のコールバックを使用
                    // NotePageでは、コールバック内でダイアログを表示し、結果を返す
                    // この場合は、NotePageの戻るボタン処理で直接ダイアログを表示するため、
                    // ここでは標準ダイアログを使用
                    var result = await Application.Current.MainPage.DisplayAlert(
                        "確認", 
                        "入力内容が破棄されます。よろしいですか？", 
                        "破棄", 
                        "キャンセル");
                    
                    Debug.WriteLine($"破棄確認ダイアログ結果: {result}");
                    return result;
                }
                else
                {
                    // Add.xaml.cs用の標準ダイアログ
                    var result = await Application.Current.MainPage.DisplayAlert(
                        "確認", 
                        "入力内容が破棄されます。よろしいですか？", 
                        "破棄", 
                        "キャンセル");
                    
                    Debug.WriteLine($"破棄確認ダイアログ結果: {result}");
                    return result;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"破棄確認ダイアログエラー: {ex.Message}");
                return false;
            }
        }
        /// <summary>
        /// フィールドをクリア
        /// </summary>
        public void ClearFields()
        {
            try
            {
                ResetCardFields();
                _isDirty = false;
                Debug.WriteLine("フィールドをクリアしました");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"フィールドクリアエラー: {ex.Message}");
            }
        }
    }
}
