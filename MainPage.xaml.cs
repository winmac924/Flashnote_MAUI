﻿using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows.Input;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using Flashnote.Services;
using Flashnote.ViewModels;
using Flashnote.Models;
using Flashnote.Views;
using System;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using System.IO.Compression;
using Firebase.Auth;
using Microsoft.Maui.Controls.PlatformConfiguration.WindowsSpecific;
using Microsoft.Maui.Controls.Xaml;

namespace Flashnote
{
    public partial class MainPage : ContentPage
    {
        private const int NoteWidth = 150; // ノート1つの幅
        private const int NoteMargin = 10;  // 各ノート間の固定間隔
        private const int PaddingSize = 10; // コレクションの左右余白
        private Note _selectedNote;
        private Stack<string> _currentPath = new Stack<string>();
        // `C:\Users\ユーザー名\Documents\Flashnote` に保存
        private static readonly string FolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Flashnote");
        private readonly CardSyncService _cardSyncService;
        private readonly UpdateNotificationService _updateService;
        private readonly BlobStorageService _blobStorageService;
        private readonly SharedKeyService _sharedKeyService;
        private readonly FileWatcherService _fileWatcherService;
        private bool _isSyncing = false;
        private MainPageViewModel _viewModel;
        private bool _isImportDropdownAnimating = false;
        private bool _isCtrlDown = false; // Ctrlキー押下状態を管理
        private bool _isShiftDown = false; // Shiftキー押下状態を管理
        private bool _keyboardEventsSetup = false; // キーボードイベント設定済みフラグ
        private bool _isProcessingKeyboardEvent = false; // キーボードイベント処理中フラグ

        public MainPage(CardSyncService cardSyncService, UpdateNotificationService updateService, BlobStorageService blobStorageService, SharedKeyService sharedKeyService, FileWatcherService fileWatcherService)
        {
            InitializeComponent();
            _viewModel = new MainPageViewModel();
            BindingContext = _viewModel;
            _cardSyncService = cardSyncService;
            _updateService = updateService;
            _blobStorageService = blobStorageService;
            _sharedKeyService = sharedKeyService;
            _fileWatcherService = fileWatcherService;
            _currentPath.Push(FolderPath);
            LoadNotes();
            
            // ファイル監視イベントを設定
            SetupFileWatcher();
            
            // アップデートチェックは初回起動時のみApp.xaml.csで実行

            // 初期レイアウトの設定
            if (NotesCollectionView?.ItemsLayout is GridItemsLayout gridLayout)
            {
                gridLayout.HorizontalItemSpacing = NoteMargin;
                gridLayout.VerticalItemSpacing = NoteMargin;
            }

            // 垂直方向も上寄せに設定
            if (NotesCollectionView != null)
            {
                NotesCollectionView.VerticalOptions = LayoutOptions.Start;
            }
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            
            // キーボードイベントを遅延実行で設定
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Task.Delay(500); // 500ms待機してから設定
                SetupKeyboardEvents();
            });
        }

        private void SetupKeyboardEvents()
        {
#if WINDOWS
            try
            {
                System.Diagnostics.Debug.WriteLine("[DEBUG] SetupKeyboardEvents開始");
                
                // 既に設定済みの場合はスキップ
                if (_keyboardEventsSetup)
                {
                    System.Diagnostics.Debug.WriteLine("[DEBUG] キーボードイベントは既に設定済みです");
                    return;
                }
                
                // 方法1: FrameworkElementから取得
                if (this.Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement frameworkElement)
                {
                    System.Diagnostics.Debug.WriteLine("[DEBUG] FrameworkElement取得成功");
                    
                    // 既存のイベントハンドラを削除（重複を防ぐ）
                    frameworkElement.KeyDown -= OnKeyDown;
                    frameworkElement.KeyUp -= OnKeyUp;
                    
                    frameworkElement.KeyDown += OnKeyDown;
                    frameworkElement.KeyUp += OnKeyUp;
                    System.Diagnostics.Debug.WriteLine("[DEBUG] FrameworkElementにキーボードイベント設定完了");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[DEBUG] FrameworkElementが見つかりませんでした");
                }
                
                // 方法2: CoreWindowから直接取得を試行
                try
                {
                    var window = Microsoft.Maui.Controls.Application.Current.Windows[0];
                    if (window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window winUIWindow)
                    {
                        System.Diagnostics.Debug.WriteLine("[DEBUG] WinUI Window取得成功");
                        var coreWindow = winUIWindow.CoreWindow;
                        if (coreWindow != null)
                        {
                            // 既存のイベントハンドラを削除（重複を防ぐ）
                            coreWindow.KeyDown -= OnCoreWindowKeyDown;
                            coreWindow.KeyUp -= OnCoreWindowKeyUp;
                            
                            coreWindow.KeyDown += OnCoreWindowKeyDown;
                            coreWindow.KeyUp += OnCoreWindowKeyUp;
                            System.Diagnostics.Debug.WriteLine("[DEBUG] CoreWindowにキーボードイベント設定完了");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("[DEBUG] CoreWindowが見つかりませんでした");
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("[DEBUG] WinUI Windowが見つかりませんでした");
                    }
                }
                catch (Exception windowEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[DEBUG] Window取得エラー: {windowEx.Message}");
                }
                
                // 方法3: 再試行（1秒後に再度試行）
                if (this.Handler?.PlatformView == null)
                {
                    System.Diagnostics.Debug.WriteLine("[DEBUG] ハンドラーがまだ準備できていないため、1秒後に再試行");
                    MainThread.BeginInvokeOnMainThread(async () =>
                    {
                        await Task.Delay(1000);
                        SetupKeyboardEvents();
                    });
                    return;
                }
                
                _keyboardEventsSetup = true;
                System.Diagnostics.Debug.WriteLine("[DEBUG] SetupKeyboardEvents完了");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DEBUG] キーボードイベント設定エラー: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[DEBUG] スタックトレース: {ex.StackTrace}");
            }
#endif
        }

#if WINDOWS
        // FrameworkElement用のイベントハンドラ
        private void OnKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[DEBUG] FrameworkElement KeyDown: {e.Key}");
            HandleKeyDown(e);
        }

        private void OnKeyUp(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[DEBUG] FrameworkElement KeyUp: {e.Key}");
            HandleKeyUp(e);
        }
        
        // CoreWindow用のイベントハンドラ
        private void OnCoreWindowKeyDown(Windows.UI.Core.CoreWindow sender, Windows.UI.Core.KeyEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[DEBUG] CoreWindow KeyDown: {e.VirtualKey}");
            HandleCoreWindowKeyDown(e);
        }

        private void OnCoreWindowKeyUp(Windows.UI.Core.CoreWindow sender, Windows.UI.Core.KeyEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[DEBUG] CoreWindow KeyUp: {e.VirtualKey}");
            HandleCoreWindowKeyUp(e);
        }
        
        // CoreWindow用の共通キー処理
        private void HandleCoreWindowKeyDown(Windows.UI.Core.KeyEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[DEBUG] HandleCoreWindowKeyDown: {e.VirtualKey}, Ctrl: {_isCtrlDown}, Shift: {_isShiftDown}, Processing: {_isProcessingKeyboardEvent}");
            
            if (_isProcessingKeyboardEvent)
            {
                System.Diagnostics.Debug.WriteLine("[DEBUG] キーボードイベント処理中、スキップ");
                return;
            }
            
            if (e.VirtualKey == Windows.System.VirtualKey.Control)
            {
                _isCtrlDown = true;
                System.Diagnostics.Debug.WriteLine("[DEBUG] Ctrlキー押下");
            }
            
            if (e.VirtualKey == Windows.System.VirtualKey.Shift)
            {
                _isShiftDown = true;
                System.Diagnostics.Debug.WriteLine("[DEBUG] Shiftキー押下");
            }
            
            // Ctrl+Nが押された場合（Shiftなし）
            if (e.VirtualKey == Windows.System.VirtualKey.N && _isCtrlDown && !_isShiftDown)
            {
                System.Diagnostics.Debug.WriteLine("[DEBUG] Ctrl+N が検出されました");
                e.Handled = true; // デフォルトの処理をキャンセル
                
                _isProcessingKeyboardEvent = true;
                MainThread.BeginInvokeOnMainThread(() => 
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine("[DEBUG] ノート作成ダイアログを表示");
                        OnCreateNoteClicked(null, null);
                    }
                    finally
                    {
                        _isProcessingKeyboardEvent = false;
                        System.Diagnostics.Debug.WriteLine("[DEBUG] キーボードイベント処理完了");
                    }
                });
            }
            
            // Ctrl+Shift+Nが押された場合
            if (e.VirtualKey == Windows.System.VirtualKey.N && _isCtrlDown && _isShiftDown)
            {
                System.Diagnostics.Debug.WriteLine("[DEBUG] Ctrl+Shift+N が検出されました");
                e.Handled = true; // デフォルトの処理をキャンセル
                
                _isProcessingKeyboardEvent = true;
                MainThread.BeginInvokeOnMainThread(() => 
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine("[DEBUG] フォルダ作成ダイアログを表示");
                        OnCreateFolderClicked(null, null);
                    }
                    finally
                    {
                        _isProcessingKeyboardEvent = false;
                        System.Diagnostics.Debug.WriteLine("[DEBUG] キーボードイベント処理完了");
                    }
                });
            }
        }

        private void HandleCoreWindowKeyUp(Windows.UI.Core.KeyEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[DEBUG] HandleCoreWindowKeyUp: {e.VirtualKey}");
            
            if (e.VirtualKey == Windows.System.VirtualKey.Control)
            {
                _isCtrlDown = false;
                System.Diagnostics.Debug.WriteLine("[DEBUG] Ctrlキー離上");
            }
            
            if (e.VirtualKey == Windows.System.VirtualKey.Shift)
            {
                _isShiftDown = false;
                System.Diagnostics.Debug.WriteLine("[DEBUG] Shiftキー離上");
            }
        }
        
        // FrameworkElement用の共通キー処理
        private void HandleKeyDown(Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[DEBUG] HandleKeyDown: {e.Key}, Ctrl: {_isCtrlDown}, Shift: {_isShiftDown}, Processing: {_isProcessingKeyboardEvent}");
            
            if (_isProcessingKeyboardEvent)
            {
                System.Diagnostics.Debug.WriteLine("[DEBUG] キーボードイベント処理中、スキップ");
                return;
            }
            
            if (e.Key == Windows.System.VirtualKey.Control)
            {
                _isCtrlDown = true;
                System.Diagnostics.Debug.WriteLine("[DEBUG] Ctrlキー押下");
            }
            
            if (e.Key == Windows.System.VirtualKey.Shift)
            {
                _isShiftDown = true;
                System.Diagnostics.Debug.WriteLine("[DEBUG] Shiftキー押下");
            }
            
            // Ctrl+Nが押された場合（Shiftなし）
            if (e.Key == Windows.System.VirtualKey.N && _isCtrlDown && !_isShiftDown)
            {
                System.Diagnostics.Debug.WriteLine("[DEBUG] Ctrl+N が検出されました");
                e.Handled = true; // デフォルトの処理をキャンセル
                
                _isProcessingKeyboardEvent = true;
                MainThread.BeginInvokeOnMainThread(() => 
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine("[DEBUG] ノート作成ダイアログを表示");
                        OnCreateNoteClicked(null, null);
                    }
                    finally
                    {
                        _isProcessingKeyboardEvent = false;
                        System.Diagnostics.Debug.WriteLine("[DEBUG] キーボードイベント処理完了");
                    }
                });
            }
            
            // Ctrl+Shift+Nが押された場合
            if (e.Key == Windows.System.VirtualKey.N && _isCtrlDown && _isShiftDown)
            {
                System.Diagnostics.Debug.WriteLine("[DEBUG] Ctrl+Shift+N が検出されました");
                e.Handled = true; // デフォルトの処理をキャンセル
                
                _isProcessingKeyboardEvent = true;
                MainThread.BeginInvokeOnMainThread(() => 
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine("[DEBUG] フォルダ作成ダイアログを表示");
                        OnCreateFolderClicked(null, null);
                    }
                    finally
                    {
                        _isProcessingKeyboardEvent = false;
                        System.Diagnostics.Debug.WriteLine("[DEBUG] キーボードイベント処理完了");
                    }
                });
            }
        }

        private void HandleKeyUp(Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[DEBUG] HandleKeyUp: {e.Key}");
            
            if (e.Key == Windows.System.VirtualKey.Control)
            {
                _isCtrlDown = false;
                System.Diagnostics.Debug.WriteLine("[DEBUG] Ctrlキー離上");
            }
            
            if (e.Key == Windows.System.VirtualKey.Shift)
            {
                _isShiftDown = false;
                System.Diagnostics.Debug.WriteLine("[DEBUG] Shiftキー離上");
            }
        }
#endif

        private void SetupFileWatcher()
        {
            // ファイル作成時のイベント
            _fileWatcherService.FileCreated += OnFileCreated;
            _fileWatcherService.FileDeleted += OnFileDeleted;
            _fileWatcherService.FileChanged += OnFileChanged;
            _fileWatcherService.FileRenamed += OnFileRenamed;
            _fileWatcherService.DirectoryCreated += OnDirectoryCreated;
            _fileWatcherService.DirectoryDeleted += OnDirectoryDeleted;
            
            // 監視を開始
            _fileWatcherService.StartWatching();
        }

        private void OnFileCreated(object sender, FileSystemEventArgs e)
        {
            // 現在のフォルダ内のファイルかチェック
            var currentFolder = _currentPath.Peek();
            if (Path.GetDirectoryName(e.FullPath) == currentFolder)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    LoadNotes();
                });
            }
        }

        private void OnFileDeleted(object sender, FileSystemEventArgs e)
        {
            // 現在のフォルダ内のファイルかチェック
            var currentFolder = _currentPath.Peek();
            if (Path.GetDirectoryName(e.FullPath) == currentFolder)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    LoadNotes();
                });
            }
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            // 現在のフォルダ内のファイルかチェック
            var currentFolder = _currentPath.Peek();
            if (Path.GetDirectoryName(e.FullPath) == currentFolder)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    LoadNotes();
                });
            }
        }

        private void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            // 現在のフォルダ内のファイルかチェック
            var currentFolder = _currentPath.Peek();
            if (Path.GetDirectoryName(e.FullPath) == currentFolder || Path.GetDirectoryName(e.OldFullPath) == currentFolder)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    LoadNotes();
                });
            }
        }

        private void OnDirectoryCreated(object sender, FileSystemEventArgs e)
        {
            // 現在のフォルダ内のディレクトリかチェック
            var currentFolder = _currentPath.Peek();
            var directoryName = Path.GetDirectoryName(e.FullPath);
            
            Debug.WriteLine($"Directory created: {e.FullPath}");
            Debug.WriteLine($"Current folder: {currentFolder}");
            Debug.WriteLine($"Directory parent: {directoryName}");
            
            // 現在のフォルダ内のディレクトリ、または現在のフォルダのサブディレクトリの場合
            if (directoryName == currentFolder || e.FullPath.StartsWith(currentFolder))
            {
                Debug.WriteLine("Directory is in current folder or subfolder, updating notes list");
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    LoadNotes();
                });
            }
            else
            {
                Debug.WriteLine("Directory is not in current folder, ignoring");
            }
        }

        private void OnDirectoryDeleted(object sender, FileSystemEventArgs e)
        {
            // 現在のフォルダ内のディレクトリかチェック
            var currentFolder = _currentPath.Peek();
            if (Path.GetDirectoryName(e.FullPath) == currentFolder)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    LoadNotes();
                });
            }
        }

        // ノートを読み込む
        private void LoadNotes()
        {
            var currentFolder = _currentPath.Peek();
            if (!Directory.Exists(currentFolder))
            {
                Directory.CreateDirectory(currentFolder);
            }

            _viewModel.Notes.Clear();

            // 親フォルダへ戻るボタンを追加（ルートフォルダでない場合）
            if (_currentPath.Count > 1)
            {
                var parentDir = Path.GetDirectoryName(currentFolder);
                _viewModel.Notes.Add(new Note
                {
                    Name = "..",
                    Icon = "folder.png",
                    IsFolder = true,
                    FullPath = parentDir,
                    LastModified = Directory.GetLastWriteTime(parentDir)
                });
            }

            // フォルダとファイルを一緒に取得して最終更新日でソート
            var items = new List<Note>();

            // フォルダを追加
            var directories = Directory.GetDirectories(currentFolder);
            foreach (var dir in directories)
            {
                var dirName = Path.GetFileName(dir);
                items.Add(new Note
                {
                    Name = dirName,
                    Icon = "folder.png",
                    IsFolder = true,
                    FullPath = dir,
                    LastModified = Directory.GetLastWriteTime(dir)
                });
            }

            // ノートファイルを追加
            var files = Directory.GetFiles(currentFolder, "*.ankpls");
            foreach (var file in files)
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                items.Add(new Note
                {
                    Name = fileName,
                    Icon = "note1.png",
                    IsFolder = false,
                    FullPath = file,
                    LastModified = File.GetLastWriteTime(file)
                });
            }

            // 最終更新日の新しい順にソート
            var sortedItems = items.OrderByDescending(item => item.LastModified);
            foreach (var item in sortedItems)
            {
                _viewModel.Notes.Add(item);
            }
            
            // ウェルカムメッセージの表示制御
            UpdateWelcomeMessage();
        }

        private void UpdateWelcomeMessage()
        {
            // ノートが1個以下（親フォルダの".."ボタンのみまたは空）の場合にウェルカムメッセージを表示
            bool showWelcome = _viewModel.Notes.Count <= 1 && 
                              (_viewModel.Notes.Count == 0 || _viewModel.Notes[0].Name == "..");
            WelcomeFrame.IsVisible = showWelcome;
        }

        // タップ時の処理（スタイラス or 指/マウス）
        private async void OnTapped(object sender, TappedEventArgs e)
        {
            var frame = sender as Frame;
            if (frame != null)
            {
                // タップ時のアニメーション
                await frame.ScaleTo(0.95, 50);
                await frame.ScaleTo(1, 50);

                var note = frame.BindingContext as Note;
                if (note == null) return;

                if (note.IsFolder)
                {
                    if (note.Name == "..")
                    {
                        if (_currentPath.Count > 1)
                        {
                            _currentPath.Pop();
                            LoadNotes();
                        }
                    }
                    else
                    {
                        _currentPath.Push(note.FullPath);
                        LoadNotes();
                    }
                }
                else
                {
                    if (e.GetType().ToString().Contains("Pen"))
                    {
                        // スタイラスペンの場合は直接NotePageへ
                    }
                    else
                    {
                        // 指/マウスの場合は確認画面へ
                        await Navigation.PushAsync(new Confirmation(note.FullPath));
                        Debug.WriteLine($"Selected Note: {note.FullPath}");
                    }
                }
            }
        }
        // 新規ノート作成ボタンのクリックイベント
        private async Task OnCreateNewNoteClicked(object sender, EventArgs e)
        {
            // ポップアップメニューを表示
            if (!CreatePopupFrame.IsVisible)
            {
                CreatePopupFrame.Opacity = 0;
                CreatePopupFrame.Scale = 0.8;
                CreatePopupFrame.IsVisible = true;

                // アニメーション
                await Task.WhenAll(
                    CreatePopupFrame.FadeTo(1, 200),
                    CreatePopupFrame.ScaleTo(1, 200)
                );

                // 3秒後に自動で非表示
                _ = Task.Delay(3000).ContinueWith(_ =>
                {
                    MainThread.BeginInvokeOnMainThread(async () =>
                    {
                        if (CreatePopupFrame.IsVisible)
                        {
                            await CreatePopupFrame.FadeTo(0, 150);
                            CreatePopupFrame.IsVisible = false;
                        }
                    });
                });
            }
            else
            {
                // 非表示にする場合
                await CreatePopupFrame.FadeTo(0, 150);
                CreatePopupFrame.IsVisible = false;
            }
        }

        // ノート作成ボタンのクリックイベント
        private async void OnCreateNoteClicked(object sender, EventArgs e)
        {
            // ポップアップを閉じる
            CreatePopupFrame.IsVisible = false;
            
            string newNoteName = await DisplayPromptAsync("新規ノート作成", "ノートの名前を入力してください");
            if (!string.IsNullOrWhiteSpace(newNoteName))
            {
                await SaveNewNoteAsync(newNoteName);
            }
        }

        // フォルダ作成ボタンのクリックイベント
        private async void OnCreateFolderClicked(object sender, EventArgs e)
        {
            // ポップアップを閉じる
            CreatePopupFrame.IsVisible = false;
            
            string newFolderName = await DisplayPromptAsync("新規フォルダ作成", "フォルダの名前を入力してください");
            if (!string.IsNullOrWhiteSpace(newFolderName))
            {
                CreateNewFolder(newFolderName);
            }
        }

        // フローティング・アクション・ボタン（FAB）のクリックイベント
        private async void OnFabCreateClicked(object sender, EventArgs e)
        {
            // FABボタンのアニメーション
            await FabCreateButton.ScaleTo(1.2, 100);
            await FabCreateButton.ScaleTo(1.0, 100);
            
            // 既存の新規作成処理を呼び出し
            await OnCreateNewNoteClicked(sender, e);
        }

        // インポートドロップダウンのクリックイベント
        private async void OnImportDropdownClicked(object sender, EventArgs e)
        {
            if (_isImportDropdownAnimating) return;
            _isImportDropdownAnimating = true;

            try
            {
                if (!ImportDropdownFrame.IsVisible)
                {
                    // 表示する場合
                    ImportDropdownFrame.Opacity = 0;
                    ImportDropdownFrame.Scale = 0.8;
                    ImportDropdownFrame.IsVisible = true;

                    // ボタンのアニメーション
                    await ImportDropdownButton.ScaleTo(0.95, 50);
                    await ImportDropdownButton.ScaleTo(1.0, 50);

                    // ドロップダウンのアニメーション
                    await Task.WhenAll(
                        ImportDropdownFrame.FadeTo(1, 200),
                        ImportDropdownFrame.ScaleTo(1, 200)
                    );

                    // 3秒後に自動で非表示
                    _ = Task.Delay(3000).ContinueWith(_ =>
                    {
                        MainThread.BeginInvokeOnMainThread(async () =>
                        {
                            if (ImportDropdownFrame.IsVisible)
                            {
                                await ImportDropdownFrame.FadeTo(0, 150);
                                ImportDropdownFrame.IsVisible = false;
                            }
                        });
                    });
                }
                else
                {
                    // 非表示にする場合
                    await ImportDropdownFrame.FadeTo(0, 150);
                    ImportDropdownFrame.IsVisible = false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"インポートドロップダウンの表示中にエラー: {ex.Message}");
            }
            finally
            {
                _isImportDropdownAnimating = false;
            }
        }

        // Ankiインポートボタンのクリックイベント
        private async void OnAnkiImportClicked(object sender, EventArgs e)
        {
            // ドロップダウンメニューを閉じる
            ImportDropdownFrame.IsVisible = false;
            
            try
            {
                // APKGファイル選択
                var customFileType = new FilePickerFileType(
                    new Dictionary<DevicePlatform, IEnumerable<string>>
                    {
                        { DevicePlatform.WinUI, new[] { ".apkg" } },
                        { DevicePlatform.macOS, new[] { "apkg" } },
                        { DevicePlatform.Android, new[] { "application/octet-stream" } },
                        { DevicePlatform.iOS, new[] { "public.data" } }
                    });

                var result = await FilePicker.PickAsync(new PickOptions
                {
                    PickerTitle = "インポートするAPKGファイルを選択",
                    FileTypes = customFileType
                });

                if (result == null)
                    return;

                // インポート処理
                var importer = new AnkiImporter(_blobStorageService);
                var cards = await importer.ImportApkg(result.FullPath);

                if (cards == null || cards.Count == 0)
                {
                    await DisplayAlert("エラー", "インポートできるカードが見つかりませんでした", "OK");
                    return;
                }

                // ノート名を入力
                string noteName = await DisplayPromptAsync("ノート名", $"インポートするノートの名前を入力してください\n（{cards.Count}枚のカードをインポート）", 
                    initialValue: Path.GetFileNameWithoutExtension(result.FileName));

                if (string.IsNullOrWhiteSpace(noteName))
                    return;

                // 現在のフォルダにノートを保存
                string currentFolder = _currentPath.Peek();
                string savedPath = await importer.SaveImportedCards(cards, currentFolder, noteName);

                // ノートリストを更新
                LoadNotes();

                await DisplayAlert("成功", $"APKGファイルを正常にインポートしました\n{cards.Count}枚のカードが「{noteName}」として保存されました", "OK");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ankiインポート中にエラー: {ex.Message}");
                Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                await DisplayAlert("エラー", $"Ankiファイルのインポートに失敗しました: {ex.Message}", "OK");
            }
        }
        // ノートを保存する
        private async Task SaveNewNoteAsync(string noteName)
        {
            try
            {
                Debug.WriteLine($"新規ノート作成開始: {noteName}");
                var currentFolder = _currentPath.Peek();
                Debug.WriteLine($"現在のフォルダ: {currentFolder}");

                var filePath = Path.Combine(currentFolder, $"{noteName}.ankpls");
                Debug.WriteLine($"作成するファイルのパス: {filePath}");

                // 一時フォルダを作成
                var tempFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Temp",
                    "Flashnote",
                    noteName + "_temp");

                if (!Directory.Exists(tempFolder))
                {
                    Directory.CreateDirectory(tempFolder);
                }
                Debug.WriteLine($"一時フォルダを作成: {tempFolder}");

                // cards.txtを作成
                var cardsFilePath = Path.Combine(tempFolder, "cards.txt");
                File.WriteAllText(cardsFilePath, "0\n");
                Debug.WriteLine($"cards.txtを作成: {cardsFilePath}");

                // ZIPファイルを作成
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                ZipFile.CreateFromDirectory(tempFolder, filePath);
                Debug.WriteLine($"ZIPファイルを作成: {filePath}");

                // ノートを追加
                var newNote = new Note 
                { 
                    Name = noteName, 
                    Icon = "note1.png", 
                    IsFolder = false, 
                    FullPath = filePath,
                    LastModified = File.GetLastWriteTime(filePath)
                };
                _viewModel.Notes.Insert(0, newNote);
                Debug.WriteLine($"ノートを追加しました: {noteName}");

                // Blob Storageに即座にアップロード
                try
                {
                    var uid = App.CurrentUser?.Uid;
                    if (!string.IsNullOrEmpty(uid))
                    {
                        Debug.WriteLine($"Blob Storageへのアップロード開始: {noteName}");
                        
                        // サブフォルダを取得
                        string subFolder = null;
                        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                        var flashnotePath = Path.Combine(documentsPath, "Flashnote");
                        
                        if (currentFolder.StartsWith(flashnotePath, StringComparison.OrdinalIgnoreCase))
                        {
                            var relativePath = Path.GetRelativePath(flashnotePath, currentFolder);
                            if (relativePath != ".")
                            {
                                subFolder = relativePath;
                            }
                        }
                        
                        Debug.WriteLine($"サブフォルダ: {subFolder ?? "ルート"}");
                        
                        // cards.txtをBlob Storageにアップロード
                        var cardsContent = "0\n";
                        await _blobStorageService.SaveNoteAsync(uid, noteName, cardsContent, subFolder);
                        Debug.WriteLine($"cards.txtをBlob Storageにアップロード完了: {noteName}");
                        
                        // 一時フォルダを正しい場所にコピー（同期処理で使用されるため）
                        var correctTempFolder = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "Flashnote",
                            subFolder ?? "",
                            noteName + "_temp");
                        
                        if (Directory.Exists(correctTempFolder))
                        {
                            Directory.Delete(correctTempFolder, true);
                        }
                        
                        // ディレクトリを作成
                        Directory.CreateDirectory(Path.GetDirectoryName(correctTempFolder));
                        
                        // 一時フォルダの内容をコピー
                        foreach (var file in Directory.GetFiles(tempFolder, "*", SearchOption.AllDirectories))
                        {
                            var relativePath = Path.GetRelativePath(tempFolder, file);
                            var targetPath = Path.Combine(correctTempFolder, relativePath);
                            var targetDir = Path.GetDirectoryName(targetPath);
                            
                            if (!Directory.Exists(targetDir))
                            {
                                Directory.CreateDirectory(targetDir);
                            }
                            
                            File.Copy(file, targetPath, true);
                        }
                        
                        Debug.WriteLine($"一時フォルダを正しい場所にコピー完了: {correctTempFolder}");
                    }
                    else
                    {
                        Debug.WriteLine("ユーザーIDが取得できないため、Blob Storageへのアップロードをスキップ");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Blob Storageへのアップロード中にエラー: {ex.Message}");
                    // アップロードに失敗してもローカルノートの作成は続行
                }

                // メインページを更新
                LoadNotes();
                Debug.WriteLine($"メインページを更新しました");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"新規ノート作成中にエラー: {ex.Message}");
                throw;
            }
        }

        // ノートを保存する（同期版 - 後方互換性のため）
        private void SaveNewNote(string noteName)
        {
            _ = SaveNewNoteAsync(noteName);
        }

        private void CreateNewFolder(string folderName)
        {
            var currentFolder = _currentPath.Peek();
            var newFolderPath = Path.Combine(currentFolder, folderName);
            Directory.CreateDirectory(newFolderPath);
            _viewModel.Notes.Insert(0, new Note { Name = folderName, Icon = "folder.png", IsFolder = true, FullPath = newFolderPath });
        }

        private void CollectCardsFromFolder(string folderPath, List<string> cards)
        {
            Debug.WriteLine($"Searching in folder: {folderPath}");

            // .ankplsファイルを探す
            var ankplsFiles = Directory.GetFiles(folderPath, "*.ankpls");
            Debug.WriteLine($"Found {ankplsFiles.Length} .ankpls files");

            foreach (var ankplsFile in ankplsFiles)
            {
                Debug.WriteLine($"Processing .ankpls file: {ankplsFile}");
                try
                {
                    // 一時フォルダのパスを作成
                    var tempFolder = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Temp",
                        "Flashnote",
                        Path.GetFileNameWithoutExtension(ankplsFile) + "_temp");

                    // 一時フォルダが存在しない場合は作成
                    if (!Directory.Exists(tempFolder))
                    {
                        Directory.CreateDirectory(tempFolder);
                    }

                    using (var archive = ZipFile.OpenRead(ankplsFile))
                    {
                        // アーカイブ内のファイル一覧を表示
                        Debug.WriteLine($"Files in {ankplsFile}:");
                        foreach (var entry in archive.Entries)
                        {
                            Debug.WriteLine($"  - {entry.FullName}");
                        }

                        // cards.txtを探す
                        var cardsEntry = archive.Entries.FirstOrDefault(e => e.Name == "cards.txt");
                        if (cardsEntry != null)
                        {
                            using (var stream = cardsEntry.Open())
                            using (var reader = new StreamReader(stream))
                            {
                                var content = reader.ReadToEnd();
                                Debug.WriteLine($"Found cards.txt with {content.Length} characters in {ankplsFile}");
                                cards.Add(content);

                                // cards.txtを一時フォルダに保存
                                File.WriteAllText(Path.Combine(tempFolder, "cards.txt"), content);
                            }
                        }
                        else
                        {
                            Debug.WriteLine($"No cards.txt found in {ankplsFile}");
                        }

                        // results.txtを探す
                        var resultsEntry = archive.Entries.FirstOrDefault(e => e.Name == "results.txt");
                        if (resultsEntry != null)
                        {
                            using (var stream = resultsEntry.Open())
                            using (var reader = new StreamReader(stream))
                            {
                                var content = reader.ReadToEnd();
                                Debug.WriteLine($"Found results.txt with {content.Length} characters in {ankplsFile}");
                                
                                // results.txtを一時フォルダに保存
                                File.WriteAllText(Path.Combine(tempFolder, "results.txt"), content);
                            }
                        }

                        // imgフォルダを探す
                        var imgEntries = archive.Entries.Where(e => e.FullName.StartsWith("img/")).ToList();
                        if (imgEntries.Any())
                        {
                            // 一時フォルダ内にimgフォルダを作成
                            var tempImgFolder = Path.Combine(tempFolder, "img");
                            if (!Directory.Exists(tempImgFolder))
                            {
                                Directory.CreateDirectory(tempImgFolder);
                            }

                            // 画像ファイルを展開
                            foreach (var entry in imgEntries)
                            {
                                var fileName = Path.GetFileName(entry.FullName);
                                var targetPath = Path.Combine(tempImgFolder, fileName);
                                
                                using (var stream = entry.Open())
                                using (var fileStream = File.Create(targetPath))
                                {
                                    stream.CopyTo(fileStream);
                                }
                                Debug.WriteLine($"Extracted image: {fileName}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error processing {ankplsFile}: {ex.Message}");
                    // 破損したファイルの場合は、一時フォルダから読み込みを試みる
                    try
                    {
                        var tempFolder = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "Temp",
                            "Flashnote",
                            Path.GetFileNameWithoutExtension(ankplsFile) + "_temp");

                        Debug.WriteLine($"Checking temp folder: {tempFolder}");
                        if (Directory.Exists(tempFolder))
                        {
                            var files = Directory.GetFiles(tempFolder);
                            Debug.WriteLine($"Files in temp folder:");
                            foreach (var file in files)
                            {
                                Debug.WriteLine($"  - {Path.GetFileName(file)}");
                            }

                            var tempCardsFile = Path.Combine(tempFolder, "cards.txt");
                            if (File.Exists(tempCardsFile))
                            {
                                var content = File.ReadAllText(tempCardsFile);
                                Debug.WriteLine($"Found cards.txt in temp folder with {content.Length} characters");
                                cards.Add(content);
                            }
                        }
                    }
                    catch (Exception tempEx)
                    {
                        Debug.WriteLine($"Error processing temp folder: {tempEx.Message}");
                    }
                }
            }

            // サブフォルダを再帰的に探索
            foreach (var dir in Directory.GetDirectories(folderPath))
            {
                Debug.WriteLine($"Recursively searching in: {dir}");
                CollectCardsFromFolder(dir, cards);
            }
        }

        private async void OnAnkiModeClicked(object sender, EventArgs e)
        {
            var currentFolder = _currentPath.Peek();
            Debug.WriteLine($"Starting card collection from: {currentFolder}");
            var allCards = new List<string>();

            // 現在のフォルダ内のすべてのcards.txtを収集
            CollectCardsFromFolder(currentFolder, allCards);
            Debug.WriteLine($"Total cards collected: {allCards.Count}");

            if (allCards.Count == 0)
            {
                await DisplayAlert("確認", "カードが見つかりませんでした。", "OK");
                return;
            }

            // Qaページに遷移し、カードを渡す
            await Navigation.PushAsync(new Qa(allCards));
        }

        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);

            // 最小幅を設定（1つのアイテムが表示できる最小幅）
            if (width < NoteWidth + (PaddingSize * 2))
            {
                width = NoteWidth + (PaddingSize * 2);
            }

            UpdateSpan(width);
        }

        private void UpdateSpan(double width)
        {
            if (NotesCollectionView?.ItemsLayout is GridItemsLayout gridLayout)
            {
                // パディングとマージンを考慮した実効幅を計算
                double effectiveWidth = width - (PaddingSize * 2);

                // 1列に表示できるアイテム数を計算（マージンを考慮）
                int newSpan = Math.Max(1, (int)((effectiveWidth + NoteMargin) / (NoteWidth + NoteMargin)));

                // 現在のアイテム数で必要な列数を計算
                int itemCount = _viewModel.Notes?.Count ?? 0;
                int minRequiredSpan = Math.Max(1, Math.Min(newSpan, itemCount));

                // 左上から整列するため、パディングは固定値を使用
                double leftPadding = PaddingSize;
                double rightPadding = PaddingSize;
                double topPadding = PaddingSize;  // 上部のパディングも固定値
                double bottomPadding = PaddingSize;

                // CollectionViewのマージンを更新（左上から整列）
                NotesCollectionView.Margin = new Thickness(leftPadding, topPadding, rightPadding, bottomPadding);

                // Spanを更新
                if (gridLayout.Span != newSpan)
                {
                    gridLayout.Span = newSpan;
                }
            }
        }

        private async void OnHelpClicked(object sender, EventArgs e)
        {
            try
            {
                await HelpOverlayControl.ShowHelp(HelpType.MainPage);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ヘルプ表示中にエラー: {ex.Message}");
                await DisplayAlert("エラー", "ヘルプの表示に失敗しました。", "OK");
            }
        }



        private async void OnSettingsClicked(object sender, EventArgs e)
        {
            try
            {
                await Shell.Current.GoToAsync("///SettingsPage");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"設定画面への移動中にエラー: {ex.Message}");
                await DisplayAlert("エラー", "設定画面の表示に失敗しました。", "OK");
            }
        }

        private async void OnSyncClicked(object sender, EventArgs e)
        {
            if (_isSyncing)
            {
                await DisplayAlert("同期中", "現在同期処理を実行中です。完了までお待ちください。", "OK");
                return;
            }

            // ログイン状態をチェック
            if (App.CurrentUser == null)
            {
                bool result = await DisplayAlert("ログインが必要", "同期機能を使用するにはログインが必要です。設定画面でログインしますか？", "はい", "いいえ");
                if (result)
                {
                    await Shell.Current.GoToAsync("///SettingsPage");
                }
                return;
            }

            try
            {
                _isSyncing = true;
                var syncButton = (Button)sender;
                syncButton.IsEnabled = false;
                syncButton.Text = "同期中...";

                var uid = App.CurrentUser.Uid;
                
                // 1. 通常のノート同期
                await _cardSyncService.SyncAllNotesAsync(uid);
                
                // 2. 共有キーの同期
                await _sharedKeyService.SyncSharedKeysAsync(uid);

                await DisplayAlert("同期完了", "すべてのノートと共有キーの同期が完了しました。", "OK");
                
                // 同期完了後にノートリストを更新
                Debug.WriteLine("同期完了、ノートリストを更新します");
                LoadNotes();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"同期中にエラー: {ex.Message}");
                await DisplayAlert("同期エラー", $"同期中にエラーが発生しました: {ex.Message}", "OK");
            }
            finally
            {
                _isSyncing = false;
                var syncButton = (Button)sender;
                syncButton.IsEnabled = true;
                syncButton.Text = "同期";
            }
        }

        private async void OnSharedKeyImportClicked(object sender, EventArgs e)
        {
            // ドロップダウンメニューを閉じる
            ImportDropdownFrame.IsVisible = false;
            
            try
            {
                var sharedKeyImportPage = new SharedKeyImportPage(_blobStorageService, _sharedKeyService, _cardSyncService);
                
                // インポート完了イベントをハンドリング
                sharedKeyImportPage.ImportCompleted += (s, args) =>
                {
                    Debug.WriteLine("共有キーインポート完了、ノートリストを更新します");
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        LoadNotes();
                    });
                };
                
                await Navigation.PushAsync(sharedKeyImportPage);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"共有キーインポートページの表示に失敗: {ex.Message}");
                await DisplayAlert("エラー", "共有キーインポートページの表示に失敗しました。", "OK");
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            
#if WINDOWS
            // キーボードイベントをクリーンアップ
            try
            {
                System.Diagnostics.Debug.WriteLine("[DEBUG] キーボードイベントクリーンアップ開始");
                
                // FrameworkElementのクリーンアップ
                if (this.Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement frameworkElement)
                {
                    frameworkElement.KeyDown -= OnKeyDown;
                    frameworkElement.KeyUp -= OnKeyUp;
                    System.Diagnostics.Debug.WriteLine("[DEBUG] FrameworkElementキーボードイベントクリーンアップ完了");
                }
                
                // CoreWindowのクリーンアップ
                try
                {
                    var window = Microsoft.Maui.Controls.Application.Current.Windows[0];
                    if (window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window winUIWindow)
                    {
                        var coreWindow = winUIWindow.CoreWindow;
                        if (coreWindow != null)
                        {
                            coreWindow.KeyDown -= OnCoreWindowKeyDown;
                            coreWindow.KeyUp -= OnCoreWindowKeyUp;
                            System.Diagnostics.Debug.WriteLine("[DEBUG] CoreWindowキーボードイベントクリーンアップ完了");
                        }
                    }
                }
                catch (Exception windowEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[DEBUG] Windowクリーンアップエラー: {windowEx.Message}");
                }
                
                // フラグをリセット（再設定可能にする）
                _keyboardEventsSetup = false;
                _isProcessingKeyboardEvent = false;
                
                System.Diagnostics.Debug.WriteLine("[DEBUG] キーボードイベントクリーンアップ完了");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DEBUG] キーボードイベントクリーンアップエラー: {ex.Message}");
            }
#endif
            
            // ファイル監視のイベントハンドラーを解除
            if (_fileWatcherService != null)
            {
                _fileWatcherService.FileCreated -= OnFileCreated;
                _fileWatcherService.FileDeleted -= OnFileDeleted;
                _fileWatcherService.FileChanged -= OnFileChanged;
                _fileWatcherService.FileRenamed -= OnFileRenamed;
                _fileWatcherService.DirectoryCreated -= OnDirectoryCreated;
                _fileWatcherService.DirectoryDeleted -= OnDirectoryDeleted;
            }
        }
    }
}
