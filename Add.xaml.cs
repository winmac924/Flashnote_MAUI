using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Diagnostics;
using Flashnote.Models;
using Flashnote.Services;
using System.Reflection;
using SkiaSharp.Views.Maui;
using SkiaSharp;
using System.IO.Compression;
using System.Web;
using SkiaSharp.Views.Maui.Controls;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Graphics;

namespace Flashnote
{
    public partial class Add : ContentPage
    {
        private CardManager _cardManager;
        private Label _toastLabel; // トースト表示用ラベル

        public Add(string cardsPath, string tempPath, string cardId = null)
        {
            try
            {
                Debug.WriteLine($"Add.xaml.cs コンストラクタ開始");
                Debug.WriteLine($"cardsPath: {cardsPath}");
                Debug.WriteLine($"tempPath: {tempPath}");
                Debug.WriteLine($"cardId: {cardId}");

                InitializeComponent();
                Debug.WriteLine("InitializeComponent完了");

                // サブフォルダ情報を取得
                string subFolder = null;
                if (!string.IsNullOrEmpty(tempPath))
                {
                    var tempDir = Path.GetDirectoryName(tempPath);
                    if (!string.IsNullOrEmpty(tempDir))
                    {
                        var tempBasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Flashnote");
                        if (tempDir.StartsWith(tempBasePath))
                        {
                            var relativePath = Path.GetRelativePath(tempBasePath, tempDir);
                            if (!relativePath.StartsWith(".") && relativePath != ".")
                            {
                                subFolder = relativePath;
                                Debug.WriteLine($"サブフォルダを検出: {subFolder}");
                            }
                        }
                    }
                }

                // CardManagerを初期化
                _cardManager = new CardManager(cardsPath, tempPath, cardId, subFolder);
                Debug.WriteLine("CardManager初期化完了");

                // CardManagerにトーストコールバックを設定
                _cardManager.SetPageSelectionCallbacks(
                    selectPageCallback: null, // Addページでは使用しない
                    loadCurrentImageCallback: null, // Addページでは使用しない
                    showToastCallback: async (message) => await ShowToast(message),
                    showAlertCallback: async (title, message) => await Application.Current.MainPage.DisplayAlert(title, message, "OK")
                );

                // CardManagerを使用してUIを初期化
                _cardManager.InitializeCardUI(CardContainer, includePageImageButtons: false);
                Debug.WriteLine("CardUI初期化完了");

                Debug.WriteLine("Add.xaml.cs コンストラクタ完了");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Add.xaml.cs コンストラクタでエラー: {ex.Message}");
                Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                throw;
            }
        }

        /// <summary>
        /// ページが表示される前の処理
        /// </summary>
        protected override void OnAppearing()
        {
            base.OnAppearing();
            Debug.WriteLine("Addページが表示されました");
            
            // トースト表示のテスト（開発時のみ）
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Task.Delay(1000); // 1秒待ってからテスト表示
                await ShowToast("Addページのトースト表示テスト");
            });
        }

        /// <summary>
        /// ページから離れる前の処理
        /// </summary>
        protected override async void OnDisappearing()
        {
            base.OnDisappearing();
            Debug.WriteLine("Addページから離れます");
        }

        /// <summary>
        /// 戻るボタンが押された時の処理
        /// </summary>
        protected override bool OnBackButtonPressed()
        {
            try
            {
                Debug.WriteLine("Addページの戻るボタンが押されました");
                
                // 未保存の変更があるかチェック
                if (_cardManager.HasUnsavedChanges())
                {
                    Debug.WriteLine("未保存の変更があります。破棄確認ダイアログを表示します");
                    
                    // UIスレッドでダイアログを表示
                    MainThread.BeginInvokeOnMainThread(async () =>
                    {
                        try
                        {
                            var shouldDiscard = await _cardManager.ShowDiscardConfirmationDialog();
                            
                            if (shouldDiscard)
                            {
                                Debug.WriteLine("破棄が選択されました。フィールドをクリアします");
                                _cardManager.ClearFields();
                                await Navigation.PopAsync();
                            }
                            else
                            {
                                Debug.WriteLine("キャンセルが選択されました。ページを離れません");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"破棄確認ダイアログエラー: {ex.Message}");
                        }
                    });
                    
                    return true; // デフォルトの戻る動作をキャンセル
                }
                else
                {
                    Debug.WriteLine("未保存の変更はありません。通常通り戻ります");
                    return base.OnBackButtonPressed();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"戻るボタン処理エラー: {ex.Message}");
                return base.OnBackButtonPressed();
            }
        }

        /// <summary>
        /// ページが破棄される時の処理
        /// </summary>
        protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
        {
            base.OnNavigatedFrom(args);
            Debug.WriteLine("Addページからナビゲートされました");
            
            // CardManagerのリソースを解放
            _cardManager?.Dispose();
        }

        /// <summary>
        /// トースト風の通知を表示（画面下部オーバーレイ）
        /// </summary>
        private async Task ShowToast(string message)
        {
            try
            {
                Debug.WriteLine($"=== ShowToast開始: {message} ===");
                
                // トーストラベルが存在しない場合は作成
                if (_toastLabel == null)
                {
                    Debug.WriteLine("トーストラベルを作成中...");
                    _toastLabel = new Label
                    {
                        Text = message,
                        BackgroundColor = Color.FromRgba(0, 0, 0, 0.8f), // 半透明の黒背景
                        TextColor = Colors.White,
                        FontSize = 16,
                        Padding = new Thickness(20, 12),
                        HorizontalOptions = LayoutOptions.Center,
                        VerticalOptions = LayoutOptions.End,
                        Margin = new Thickness(20, 0, 20, 30), // 画面下部からの余白
                        IsVisible = false,
                        HorizontalTextAlignment = TextAlignment.Center,
                        ZIndex = 1000 // 最前面に表示
                    };

                    Debug.WriteLine("トーストラベルをレイアウトに追加中...");
                    
                    // 現在のContentをGridで包んで、トーストラベルを追加
                    var currentContent = Content;
                    var mainGrid = new Grid();
                    
                    // 既存のコンテンツをGridに追加
                    mainGrid.Children.Add(currentContent);
                    Grid.SetRow(currentContent, 0);
                    Grid.SetColumn(currentContent, 0);
                    
                    // トーストラベルをGridに追加（最前面）
                    mainGrid.Children.Add(_toastLabel);
                    Grid.SetRow(_toastLabel, 0);
                    Grid.SetColumn(_toastLabel, 0);
                    
                    // Contentを新しいGridに設定
                    Content = mainGrid;
                    
                    Debug.WriteLine("トーストラベルをレイアウトに追加完了");
                }
                else
                {
                    Debug.WriteLine("既存のトーストラベルを使用");
                    _toastLabel.Text = message;
                }

                Debug.WriteLine("トーストアニメーション開始");
                
                // トーストを表示
                _toastLabel.IsVisible = true;
                _toastLabel.Opacity = 0;
                _toastLabel.TranslationY = 50; // 下から上にスライドイン

                // アニメーション：フェードイン & スライドイン
                var fadeTask = _toastLabel.FadeTo(1, 300);
                var slideTask = _toastLabel.TranslateTo(0, 0, 300, Easing.CubicOut);
                await Task.WhenAll(fadeTask, slideTask);

                Debug.WriteLine("トースト表示中（2.5秒間）");
                
                // 2.5秒間表示
                await Task.Delay(2500);

                Debug.WriteLine("トーストアニメーション終了開始");
                
                // アニメーション：フェードアウト & スライドアウト
                var fadeOutTask = _toastLabel.FadeTo(0, 300);
                var slideOutTask = _toastLabel.TranslateTo(0, 50, 300, Easing.CubicIn);
                await Task.WhenAll(fadeOutTask, slideOutTask);
                
                _toastLabel.IsVisible = false;
                
                Debug.WriteLine("=== ShowToast完了 ===");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"トースト表示エラー: {ex.Message}");
                Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
            }
        }
    }
}