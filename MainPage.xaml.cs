using System.Collections.ObjectModel;
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
            
            // サブフォルダ同期ボタンの初期状態を確認
            Debug.WriteLine($"MainPageコンストラクタ - SubFolderSyncButton存在確認: {SubFolderSyncButton != null}");
            if (SubFolderSyncButton != null)
            {
                Debug.WriteLine($"MainPageコンストラクタ - SubFolderSyncButton初期状態: {SubFolderSyncButton.IsVisible}");
            }
            
            LoadNotes();
            
            // 初期タイトルを設定
            UpdateAppTitle();
            
            // テスト用: サブフォルダ同期ボタンを強制的に表示
            if (SubFolderSyncButton != null)
            {
                SubFolderSyncButton.IsVisible = true;
                Debug.WriteLine("MainPageコンストラクタ - テスト用にサブフォルダ同期ボタンを強制表示");
            }
            
            // ファイル監視イベントを設定
            SetupFileWatcher();
            
            // 自動同期完了イベントを設定
            if (_blobStorageService != null)
            {
                _blobStorageService.AutoSyncCompleted += OnAutoSyncCompleted;
            }
            
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

            // タイトルを更新
            UpdateAppTitle();

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

            // Build local metadata index (search LocalApplicationData/Flashnote and MyDocuments/Flashnote for metadata.json)
            var metadataIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // id -> json
            try
            {
                var localMetaRoots = new[] {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Flashnote"),
                    FolderPath
                };
                foreach (var root in localMetaRoots)
                {
                    if (!Directory.Exists(root)) continue;
                    var metaFiles = Directory.GetFiles(root, "metadata.json", SearchOption.AllDirectories);
                    foreach (var f in metaFiles)
                    {
                        try
                        {
                            var j = File.ReadAllText(f);
                            using var doc = System.Text.Json.JsonDocument.Parse(j);
                            if (doc.RootElement.TryGetProperty("id", out var pId))
                            {
                                var id = pId.GetString();
                                if (!string.IsNullOrEmpty(id) && !metadataIndex.ContainsKey(id))
                                {
                                    metadataIndex[id] = j;
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"metadata index build error: {ex.Message}");
            }

            // フォルダを追加
            var items = new List<Note>();
            var directories = Directory.GetDirectories(currentFolder);
            foreach (var dir in directories)
            {
                var dirName = Path.GetFileName(dir);
                // フォルダ内に metadata.json があれば originalName を表示名に使う
                try
                {
                    var metadataPath = Path.Combine(dir, "metadata.json");

                    if (File.Exists(metadataPath))
                    {
                        // Read existing metadata in the directory
                        var json = File.ReadAllText(metadataPath);
                        using var doc = System.Text.Json.JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("originalName", out var p))
                        {
                            var orig = p.GetString();
                            if (!string.IsNullOrEmpty(orig)) dirName = orig;
                        }
                    }
                    else
                    {
                        // Only attempt fallback lookup when directory name looks like a UUID
                        var dirId = Path.GetFileName(dir);
                        if (Guid.TryParse(dirId, out _))
                        {
                            if (metadataIndex.TryGetValue(dirId, out var metaJson))
                            {
                                try
                                {
                                    // Ensure directory exists before saving metadata
                                    Directory.CreateDirectory(dir);

                                    // Save a local copy for future fast lookup
                                    File.WriteAllText(metadataPath, metaJson);
                                    using var doc = System.Text.Json.JsonDocument.Parse(metaJson);
                                    if (doc.RootElement.TryGetProperty("originalName", out var p2))
                                    {
                                        var orig2 = p2.GetString();
                                        if (!string.IsNullOrEmpty(orig2)) dirName = orig2;
                                    }
                                }
                                catch (Exception ex2)
                                {
                                    Debug.WriteLine($"フォルダmetadata保存失敗: {dir} - {ex2.Message}");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"フォルダmetadata読み込み失敗: {dir} - {ex.Message}");
                }
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
            var ankplsFiles = Directory.GetFiles(currentFolder, "*.ankpls");
            foreach (var file in ankplsFiles)
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                // .ankpls の中に metadata.json があれば originalName を表示名に使う
                try
                {
                    bool found = false;
                    try
                    {
                        using var archive = ZipFile.OpenRead(file);
                        var metaEntry = archive.Entries.FirstOrDefault(e => e.Name.Equals("metadata.json", StringComparison.OrdinalIgnoreCase));
                        if (metaEntry != null)
                        {
                            using var stream = metaEntry.Open();
                            using var reader = new StreamReader(stream);
                            var json = reader.ReadToEnd();
                            using var doc = System.Text.Json.JsonDocument.Parse(json);
                            if (doc.RootElement.TryGetProperty("originalName", out var p))
                            {
                                var orig = p.GetString();
                                if (!string.IsNullOrEmpty(orig)) fileName = orig;
                            }
                            found = true;
                        }
                    }
                    catch { }

                    if (!found)
                    {
                        // try metadataIndex by id
                        if (metadataIndex.TryGetValue(fileName, out var metaJson))
                        {
                            try
                            {
                                using var doc = System.Text.Json.JsonDocument.Parse(metaJson);
                                if (doc.RootElement.TryGetProperty("originalName", out var p2))
                                {
                                    var orig2 = p2.GetString();
                                    if (!string.IsNullOrEmpty(orig2)) fileName = orig2;
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($".ankpls metadata 読み込み失敗: {file} - {ex.Message}");
                }
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

        private void UpdateAppTitle()
        {
            try
            {
                var currentFolder = _currentPath.Peek();
                var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var flashnotePath = Path.Combine(documentsPath, "Flashnote");

                Debug.WriteLine($"UpdateAppTitle - 現在のフォルダ: {currentFolder}");
                Debug.WriteLine($"UpdateAppTitle - Flashnoteパス: {flashnotePath}");
                Debug.WriteLine($"UpdateAppTitle - SubFolderSyncButton存在確認: {SubFolderSyncButton != null}");

                // ルートフォルダの場合は通常のタイトル
                if (currentFolder.Equals(flashnotePath, StringComparison.OrdinalIgnoreCase))
                {
                    AppTitleLabel.Text = "📚 Flashnote";
                    // サブフォルダ同期ボタンを非表示
                    if (SubFolderSyncButton != null)
                    {
                        SubFolderSyncButton.IsVisible = false;
                        Debug.WriteLine("UpdateAppTitle - ルートフォルダ: サブフォルダ同期ボタンを非表示");
                    }
                }
                else
                {
                    // サブフォルダの場合はフォルダ名を追加
                    var folderName = Path.GetFileName(currentFolder);

                    // Try to read metadata.json in the folder to get originalName
                    try
                    {
                        var metadataPath = Path.Combine(currentFolder, "metadata.json");
                        if (File.Exists(metadataPath))
                        {
                            var json = File.ReadAllText(metadataPath);
                            using var doc = System.Text.Json.JsonDocument.Parse(json);
                            if (doc.RootElement.TryGetProperty("originalName", out var pOrig))
                            {
                                var orig = pOrig.GetString();
                                if (!string.IsNullOrEmpty(orig))
                                {
                                    folderName = orig;
                                    Debug.WriteLine($"UpdateAppTitle - metadata originalName を使用: {folderName}");
                                }
                            }
                        }
                        else
                        {
                            // try LocalApplicationData temp location for UUID-named folders
                            var localTempMeta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Flashnote", folderName + "_temp", "metadata.json");
                            if (File.Exists(localTempMeta))
                            {
                                var json = File.ReadAllText(localTempMeta);
                                using var doc = System.Text.Json.JsonDocument.Parse(json);
                                if (doc.RootElement.TryGetProperty("originalName", out var pOrig2))
                                {
                                    var orig2 = pOrig2.GetString();
                                    if (!string.IsNullOrEmpty(orig2))
                                    {
                                        folderName = orig2;
                                        Debug.WriteLine($"UpdateAppTitle - local temp metadata originalName を使用: {folderName}");
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"UpdateAppTitle metadata read error: {ex.Message}");
                    }

                    AppTitleLabel.Text = $"📚 Flashnote - {folderName}";

                    // 現在のフォルダがサブフォルダかどうかをチェック
                    var currentSubFolder = CheckIfSharedFolder();
                    Debug.WriteLine($"UpdateAppTitle - CheckIfSharedFolder結果: {currentSubFolder ?? "null"}");

                    if (SubFolderSyncButton != null)
                    {
                        if (!string.IsNullOrEmpty(currentSubFolder))
                        {
                            // サブフォルダ同期ボタンを表示
                            SubFolderSyncButton.IsVisible = true;
                            Debug.WriteLine($"UpdateAppTitle - サブフォルダ同期ボタンを表示: {currentSubFolder}");
                        }
                        else
                        {
                            // 共有フォルダの場合は非表示
                            SubFolderSyncButton.IsVisible = false;
                            Debug.WriteLine("UpdateAppTitle - 共有フォルダ: サブフォルダ同期ボタンを非表示");
                        }
                    }
                    else
                    {
                        Debug.WriteLine("UpdateAppTitle - SubFolderSyncButtonがnullです");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"タイトル更新中にエラー: {ex.Message}");
                // エラーが発生した場合はデフォルトタイトルを設定
                AppTitleLabel.Text = "📚 Flashnote";
                if (SubFolderSyncButton != null)
                {
                    SubFolderSyncButton.IsVisible = false;
                }
            }
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
                        try
                        {
                            // ネットワーク状態を確認
                            var networkStateService = MauiProgram.Services?.GetService<NetworkStateService>();
                            bool isNetworkAvailable = networkStateService?.IsNetworkAvailable ?? false;
                            
                            // オンラインの場合のみ同期処理を実行
                            if (isNetworkAvailable)
                            {
                                var uid = App.CurrentUser?.Uid;
                                if (!string.IsNullOrEmpty(uid))
                                {
                                    var noteName = Path.GetFileNameWithoutExtension(note.FullPath);
                                    string subFolder = null;
                                    
                                    // サブフォルダ情報を取得
                                    var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                                    var flashnotePath = Path.Combine(documentsPath, "Flashnote");
                                    if (Path.GetDirectoryName(note.FullPath).StartsWith(flashnotePath, StringComparison.OrdinalIgnoreCase))
                                    {
                                        var relativePath = Path.GetRelativePath(flashnotePath, Path.GetDirectoryName(note.FullPath));
                                        if (relativePath != "." && !relativePath.StartsWith("."))
                                        {
                                            subFolder = relativePath;
                                        }
                                    }
                                    
                                    Debug.WriteLine($"ノート開時同期開始: {noteName}, サブフォルダ: {subFolder ?? "なし"}");
                                    await _cardSyncService.SyncNoteOnOpenAsync(uid, noteName, subFolder);
                                    Debug.WriteLine($"ノート開時同期完了: {noteName}");
                                }
                            }
                            else
                            {
                                Debug.WriteLine("オフラインのため、同期処理をスキップします");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"ノート開時同期エラー: {ex.Message}");
                            ﻿
                            // 同期エラーをユーザーに通知
                            string errorMessage;
                            if (ex.Message.Contains("オフラインのため") || ex.Message.Contains("インターネット接続") || 
                                ex.Message.Contains("ネットワーク接続") || ex.Message.Contains("タイムアウト"))
                            {
                                errorMessage = "オフラインのため、最新のカードを取得できませんでした。ローカルに保存されているカードで学習を開始します。";
                            }
                            else
                            {
                                errorMessage = "同期処理中にエラーが発生しました。ローカルに保存されているカードで学習を開始します。";
                            }
                            
                            // オフライン時はトーストで軽く通知
                            if (ex.Message.Contains("オフラインのため") || ex.Message.Contains("インターネット接続"))
                            {
                                // トーストメッセージで軽く通知（アラートダイアログではない）
                                Debug.WriteLine($"オフライン通知: {errorMessage}");
                            }
                            else
                            {
                                await Microsoft.Maui.Controls.Application.Current.MainPage.DisplayAlert("同期エラー", errorMessage, "OK");
                            }
                        }
                        
                        try
                        {
                            Debug.WriteLine($"Confirmation画面への遷移開始: {note.FullPath}");
                            await Navigation.PushAsync(new Confirmation(note.FullPath));
                            Debug.WriteLine($"Confirmation画面への遷移完了: {note.FullPath}");
                        }
                        catch (Exception confirmationEx)
                        {
                            Debug.WriteLine($"Confirmation画面への遷移でエラー: {confirmationEx.Message}");
                            Debug.WriteLine($"スタックトレース: {confirmationEx.StackTrace}");
                            await DisplayAlert("エラー", $"ノートを開く際にエラーが発生しました: {confirmationEx.Message}", "OK");
                        }
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
            
            // 現在のフォルダが共有フォルダかどうかをチェック
            var currentFolder = _currentPath.Peek();
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var flashnotePath = Path.Combine(documentsPath, "Flashnote");
            
            string subFolder = null;
            if (currentFolder.StartsWith(flashnotePath, StringComparison.OrdinalIgnoreCase))
            {
                var relativePath = Path.GetRelativePath(flashnotePath, currentFolder);
                if (relativePath != "." && !relativePath.StartsWith("."))
                {
                    subFolder = relativePath;
                }
            }
            
            // 共有フォルダかどうかをチェック（情報取得のため）
            bool isInSharedFolder = !string.IsNullOrEmpty(subFolder) && _sharedKeyService.IsInSharedFolder("", subFolder);
            
            string newNoteName = await DisplayPromptAsync("新規ノート作成", "ノートの名前を入力してください");
            if (!string.IsNullOrWhiteSpace(newNoteName))
            {
                // 共有フォルダの場合は特別な処理を行う
                if (isInSharedFolder)
                {
                    await SaveNewNoteInSharedFolderAsync(newNoteName, subFolder);
                }
                else
                {
                    await SaveNewNoteAsync(newNoteName);
                }
            }
        }

        // フォルダ作成ボタンのクリックイベント
        private async void OnCreateFolderClicked(object sender, EventArgs e)
        {
            // ポップアップを閉じる
            CreatePopupFrame.IsVisible = false;
            
            // 現在のフォルダが共有フォルダかどうかをチェック
            var currentFolder = _currentPath.Peek();
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var flashnotePath = Path.Combine(documentsPath, "Flashnote");
            
            string subFolder = null;
            if (currentFolder.StartsWith(flashnotePath, StringComparison.OrdinalIgnoreCase))
            {
                var relativePath = Path.GetRelativePath(flashnotePath, currentFolder);
                if (relativePath != "." && !relativePath.StartsWith("."))
                {
                    subFolder = relativePath;
                }
            }
            
            // 共有フォルダかどうかをチェック（情報取得のため）
            bool isInSharedFolder = !string.IsNullOrEmpty(subFolder) && _sharedKeyService.IsInSharedFolder("", subFolder);
            
            string newFolderName = await DisplayPromptAsync("新規フォルダ作成", "フォルダの名前を入力してください");
            if (!string.IsNullOrWhiteSpace(newFolderName))
            {
                // 共有フォルダの場合は特別な処理を行う
                if (isInSharedFolder)
                {
                    await CreateNewFolderInSharedFolderAsync(newFolderName, subFolder);
                }
                else
                {
                    CreateNewFolder(newFolderName);
                }
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
            
            // 現在のフォルダが共有フォルダかどうかをチェック
            var currentFolder = _currentPath.Peek();
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var flashnotePath = Path.Combine(documentsPath, "Flashnote");
            
            string subFolder = null;
            if (currentFolder.StartsWith(flashnotePath, StringComparison.OrdinalIgnoreCase))
            {
                var relativePath = Path.GetRelativePath(flashnotePath, currentFolder);
                if (relativePath != "." && !relativePath.StartsWith("."))
                {
                    subFolder = relativePath;
                }
            }
            
            // 共有フォルダかどうかをチェック（情報取得のため）
            bool isInSharedFolder = !string.IsNullOrEmpty(subFolder) && _sharedKeyService.IsInSharedFolder("", subFolder);
            
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
                List<CardData> cards = await importer.ImportApkg(result.FullPath);

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

                // 共有フォルダの場合は特別な処理を行う
                string savedPath;
                if (isInSharedFolder)
                {
                    savedPath = await ImportAnkiInSharedFolderAsync(importer, cards, noteName, subFolder);
                }
                else
                {
                    // 通常のフォルダにノートを保存
                    savedPath = await importer.SaveImportedCards(cards, currentFolder, noteName);
                }

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

                // metadata.json を作成
                try
                {
                    var metadata = new Dictionary<string, object>();
                    var nowUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
                    metadata["updatedAt"] = nowUtc;
                    metadata["version"] =1;
                    metadata["createdAt"] = nowUtc;

                    // サブフォルダ情報がある場合は追加
                    var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    var flashnotePath = Path.Combine(documentsPath, "Flashnote");
                    if (currentFolder.StartsWith(flashnotePath, StringComparison.OrdinalIgnoreCase))
                    {
                        var relativePath = Path.GetRelativePath(flashnotePath, currentFolder);
                        if (!string.IsNullOrEmpty(relativePath) && relativePath != "." && !relativePath.StartsWith("."))
                        {
                            metadata["subfolder"] = relativePath;
                        }
                    }

                    metadata["id"] = Guid.NewGuid().ToString();
                    metadata["originalName"] = noteName;

                    var metadataJson = System.Text.Json.JsonSerializer.Serialize(metadata, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    var metadataPath = Path.Combine(tempFolder, "metadata.json");
                    File.WriteAllText(metadataPath, metadataJson);
                    Debug.WriteLine($"metadata.json を作成: {metadataPath}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"metadata.json 作成中にエラー: {ex.Message}");
                }

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
                        string uploadSubFolder = null;
                        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                        var flashnotePath = Path.Combine(documentsPath, "Flashnote");

                        if (currentFolder.StartsWith(flashnotePath, StringComparison.OrdinalIgnoreCase))
                        {
                            var relativePath = Path.GetRelativePath(flashnotePath, currentFolder);
                            if (relativePath != ".")
                            {
                                uploadSubFolder = relativePath;
                            }
                        }

                        Debug.WriteLine($"サブフォルダ: {uploadSubFolder ?? "ルート"}");

                        // cards.txtをBlob Storageにアップロード
                        var cardsContent = "0\n";
                        await _blobStorageService.SaveNoteAsync(uid, noteName, cardsContent, uploadSubFolder);
                        Debug.WriteLine($"cards.txtをBlob Storageにアップロード完了: {noteName}");

                        // metadata.json をアップロード
                        try
                        {
                            var localMetadataPath = Path.Combine(tempFolder, "metadata.json");
                            if (File.Exists(localMetadataPath))
                            {
                                var metadataContent = File.ReadAllText(localMetadataPath);
                                await _blobStorageService.SaveNoteAsync(uid, $"{noteName}/metadata.json", metadataContent, uploadSubFolder);
                                Debug.WriteLine($"metadata.json をBlob Storageにアップロードしました: {noteName}/metadata.json");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"metadata.json Blobアップロード中にエラー: {ex.Message}");
                        }

                        // 一時フォルダを正しい場所にコピー（同期処理で使用されるため）
                        var correctTempFolder = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "Flashnote",
                            uploadSubFolder ?? "",
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

        private async void CreateNewFolder(string folderName)
        {
            try
            {
                Debug.WriteLine($"新規フォルダ作成開始: {folderName}");
                var currentFolder = _currentPath.Peek();
                var newFolderPath = Path.Combine(currentFolder, folderName);
                Directory.CreateDirectory(newFolderPath);
                Debug.WriteLine($"ローカルフォルダを作成: {newFolderPath}");

                // フォルダをノートリストに追加
                _viewModel.Notes.Insert(0, new Note { Name = folderName, Icon = "folder.png", IsFolder = true, FullPath = newFolderPath, LastModified = Directory.GetLastWriteTime(newFolderPath) });

                // Blob Storageにフォルダ構造を反映（プレースホルダーファイルを作成）
                try
                {
                    var uid = App.CurrentUser?.Uid;
                    if (!string.IsNullOrEmpty(uid))
                    {
                        Debug.WriteLine($"Blob Storageへのフォルダ作成開始: {folderName}");
                        
                        // サブフォルダのパスを取得
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
                        
                        // フォルダのパスを構築
                        string folderPath = subFolder != null ? $"{subFolder}/{folderName}" : folderName;
                        Debug.WriteLine($"フォルダパス: {folderPath}");
                        
                        // Blob Storageではフォルダを直接作成できないため、プレースホルダーファイルを作成
                        // これにより、フォルダ構造が維持される
                        var placeholderContent = "# This is a placeholder file to maintain folder structure";
                        await _blobStorageService.SaveNoteAsync(uid, folderPath, ".folder_placeholder", placeholderContent);
                        Debug.WriteLine($"フォルダプレースホルダーをBlob Storageに作成: {folderPath}/.folder_placeholder");
                    }
                    else
                    {
                        Debug.WriteLine("ユーザーIDが取得できないため、Blob Storageへのフォルダ作成をスキップ");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Blob Storageへのフォルダ作成中にエラー: {ex.Message}");
                    // フォルダ作成エラーはローカル作成を妨げないため、警告のみ表示
                    await DisplayAlert("警告", "フォルダはローカルに作成されましたが、サーバーへの同期に失敗しました。", "OK");
                }

                Debug.WriteLine($"新規フォルダ作成完了: {folderName}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"フォルダ作成中にエラー: {ex.Message}");
                await DisplayAlert("エラー", $"フォルダの作成に失敗しました: {ex.Message}", "OK");
            }
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
                
                // ネットワーク状態を事前チェック
                var networkStateService = MauiProgram.Services?.GetService<NetworkStateService>();
                if (networkStateService != null && !networkStateService.IsNetworkAvailable)
                {
                    await DisplayAlert("ネットワークエラー", "インターネット接続がありません。ネットワーク接続を確認してから再試行してください。", "OK");
                    return;
                }
                
                // Azure接続状態をリセット（ネットワーク状態変化に対応）
                _blobStorageService.ResetConnectionState();

                // 0. すべての metadata.json を取得してローカルへ反映（isFolder と subfolder に応じて）
                await DownloadAllMetadataAndMaterializeAsync(uid);
                
                // 1. 通常のノート同期
                // メタデータのみを同期（cards.txt や img はダウンロードしない）
                // 軽量メタデータ同期を先に実行（metadata.json のタイムスタンプ比較による双方向同期）
                await LightweightSyncMetadataAsync(uid);
                
                await _cardSyncService.SyncAllNotesMetadataAsync(uid);
                
                // 2. 共有キーの同期
                await _sharedKeyService.SyncSharedKeysAsync(uid);

                await DisplayAlert("同期完了", "すべてのノートと共有キーの同期が完了しました。", "OK");
                
                // 同期完了後にノートリストを更新
                Debug.WriteLine("同期完了、ノートリストを更新します");
                LoadNotes();
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("オフライン") || ex.Message.Contains("接続"))
            {
                Debug.WriteLine($"ネットワーク関連エラー: {ex.Message}");
                await DisplayAlert("ネットワークエラー", "インターネット接続に問題があります。ネットワーク接続を確認してから再試行してください。", "OK");
            }
            catch (TimeoutException ex)
            {
                Debug.WriteLine($"タイムアウトエラー: {ex.Message}");
                await DisplayAlert("タイムアウトエラー", "サーバーへの接続がタイムアウトしました。ネットワーク接続を確認してから再試行してください。", "OK");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"同期中にエラー: {ex.Message}");
                Debug.WriteLine($"エラータイプ: {ex.GetType().Name}");
                Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                
                // エラーメッセージをユーザーフレンドリーに
                string userMessage = ex.Message;
                if (ex.Message.Contains("CancellationTokenSource has been disposed"))
                {
                    userMessage = "ネットワーク接続が不安定です。しばらく待ってから再試行してください。";
                }
                else if (ex.Message.Contains("Azure") || ex.Message.Contains("Blob"))
                {
                    userMessage = "クラウドストレージへの接続に問題があります。ネットワーク接続を確認してください。";
                }
                
                await DisplayAlert("同期エラー", $"同期中にエラーが発生しました: {userMessage}", "OK");
            }
            finally
            {
                _isSyncing = false;
                var syncButton = (Button)sender;
                syncButton.IsEnabled = true;
                syncButton.Text = "同期";
            }
        }

        /// <summary>
        /// サブフォルダ同期ボタンがクリックされた時の処理
        /// </summary>
        private async void OnSubFolderSyncClicked(object sender, EventArgs e)
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

            // 現在のフォルダがサブフォルダかどうかをチェック
            var currentSubFolder = CheckIfSharedFolder();
            if (string.IsNullOrEmpty(currentSubFolder))
            {
                await DisplayAlert("エラー", "現在のフォルダはサブフォルダではありません。", "OK");
                return;
            }

            try
            {
                _isSyncing = true;
                var syncButton = (Button)sender;
                syncButton.IsEnabled = false;
                syncButton.Text = "同期中...";

                var uid = App.CurrentUser.Uid;
                
                // ネットワーク状態を事前チェック
                var networkStateService = MauiProgram.Services?.GetService<NetworkStateService>();
                if (networkStateService != null && !networkStateService.IsNetworkAvailable)
                {
                    await DisplayAlert("ネットワークエラー", "インターネット接続がありません。ネットワーク接続を確認してから再試行してください。", "OK");
                    return;
                }
                
                // Azure接続状態をリセット（ネットワーク状態変化に対応）
                _blobStorageService.ResetConnectionState();

                // 0. すべての metadata.json を取得してローカルへ反映（サブフォルダに限定）
                await DownloadAllMetadataAndMaterializeAsync(uid, currentSubFolder);
                
                // サブフォルダのメタデータのみを同期（cards.txt や img はダウンロードしない）
                // 軽量メタデータ同期を先に実行（metadata.json のタイムスタンプ比較による双方向同期）
                await LightweightSyncMetadataAsync(uid, currentSubFolder);

                await DisplayAlert("同期完了", $"サブフォルダ「{currentSubFolder}」内のノートの同期が完了しました。", "OK");
                
                // 同期完了後にノートリストを更新
                Debug.WriteLine("サブフォルダ同期完了、ノートリストを更新します");
                LoadNotes();
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("オフライン") || ex.Message.Contains("接続"))
            {
                Debug.WriteLine($"ネットワーク関連エラー: {ex.Message}");
                await DisplayAlert("ネットワークエラー", "インターネット接続に問題があります。ネットワーク接続を確認してから再試行してください。", "OK");
            }
            catch (TimeoutException ex)
            {
                Debug.WriteLine($"タイムアウトエラー: {ex.Message}");
                await DisplayAlert("タイムアウトエラー", "サーバーへの接続がタイムアウトしました。ネットワーク接続を確認してから再試行してください。", "OK");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"サブフォルダ同期中にエラー: {ex.Message}");
                Debug.WriteLine($"エラータイプ: {ex.GetType().Name}");
                Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
                
                // エラーメッセージをユーザーフレンドリーに
                string userMessage = ex.Message;
                if (ex.Message.Contains("CancellationTokenSource has been disposed"))
                {
                    userMessage = "ネットワーク接続が不安定です。しばらく待ってから再試行してください。";
                }
                else if (ex.Message.Contains("Azure") || ex.Message.Contains("Blob"))
                {
                    userMessage = "クラウドストレージへの接続に問題があります。ネットワーク接続を確認してください。";
                }
                
                await DisplayAlert("同期エラー", $"サブフォルダ同期中にエラーが発生しました: {userMessage}", "OK");
            }
            finally
            {
                _isSyncing = false;
                var syncButton = (Button)sender;
                syncButton.IsEnabled = true;
                syncButton.Text = "📁 同期";
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
            
            // 自動同期完了イベントを解除
            if (_blobStorageService != null)
            {
                _blobStorageService.AutoSyncCompleted -= OnAutoSyncCompleted;
            }
        }

        /// <summary>
        /// 自動同期完了時の処理
        /// </summary>
        private void OnAutoSyncCompleted(object sender, EventArgs e)
        {
            try
            {
                Debug.WriteLine("自動同期完了イベントを受信、ノートリストを更新します");
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    LoadNotes();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"自動同期完了処理エラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 現在のフォルダが共有フォルダかどうかをチェックする
        /// </summary>
        /// <returns>subFolderパス（共有フォルダの場合はnull、そうでなければサブフォルダパス）</returns>
        private string CheckIfSharedFolder()
        {
            var currentFolder = _currentPath.Peek();
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var flashnotePath = Path.Combine(documentsPath, "Flashnote");
            
            Debug.WriteLine($"CheckIfSharedFolder - 現在のフォルダ: {currentFolder}");
            Debug.WriteLine($"CheckIfSharedFolder - Flashnoteパス: {flashnotePath}");
            
            string subFolder = null;
            if (currentFolder.StartsWith(flashnotePath, StringComparison.OrdinalIgnoreCase))
            {
                var relativePath = Path.GetRelativePath(flashnotePath, currentFolder);
                Debug.WriteLine($"CheckIfSharedFolder - 相対パス: {relativePath}");
                
                if (relativePath != "." && !relativePath.StartsWith("."))
                {
                    subFolder = relativePath;
                    Debug.WriteLine($"CheckIfSharedFolder - サブフォルダパス設定: {subFolder}");
                }
            }
            
            // 共有フォルダの場合はnullを返す
            if (!string.IsNullOrEmpty(subFolder))
            {
                var isShared = _sharedKeyService.IsInSharedFolder("", subFolder);
                Debug.WriteLine($"CheckIfSharedFolder - 共有フォルダチェック: {isShared}");
                
                if (isShared)
                {
                    Debug.WriteLine("CheckIfSharedFolder - 共有フォルダのためnullを返す");
                    return null;
                }
            }
            
            Debug.WriteLine($"CheckIfSharedFolder - 最終結果: {subFolder ?? "null"}");
            return subFolder;
        }

        /// <summary>
        /// 共有フォルダ内で新しいノートを作成する
        /// </summary>
        private async Task SaveNewNoteInSharedFolderAsync(string noteName, string subFolder)
        {
            try
            {
                Debug.WriteLine($"共有フォルダ内で新規ノート作成開始: {noteName}, サブフォルダ: {subFolder}");
                
                // 共有フォルダの情報を取得
                var sharedInfo = _sharedKeyService.GetSharedNoteInfo(subFolder);
                if (sharedInfo == null)
                {
                    await DisplayAlert("エラー", "共有フォルダの情報が見つかりません。", "OK");
                    return;
                }
                
                Debug.WriteLine($"共有フォルダ情報 - 元UID: {sharedInfo.OriginalUserId}, パス: {sharedInfo.NotePath}");
                
                // ローカルに一時ノートを作成
                var currentFolder = _currentPath.Peek();
                var filePath = Path.Combine(currentFolder, $"{noteName}.ankpls");
                
                // 一時フォルダを作成
                var tempFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Flashnote",
                    subFolder,
                    noteName + "_temp");

                if (!Directory.Exists(tempFolder))
                {
                    Directory.CreateDirectory(tempFolder);
                }
                
                // cards.txtを作成
                var cardsFilePath = Path.Combine(tempFolder, "cards.txt");
                File.WriteAllText(cardsFilePath, "0\n");

                // metadata.json を作成（共有ノート用）
                try
                {
                    var metadata = new Dictionary<string, object>();
                    var nowUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
                    metadata["updatedAt"] = nowUtc;
                    metadata["version"] =1;
                    metadata["createdAt"] = nowUtc;

                    // subfolder 情報を追加
                    if (!string.IsNullOrEmpty(subFolder))
                    {
                        metadata["subfolder"] = subFolder;
                    }

                    metadata["id"] = Guid.NewGuid().ToString();
                    metadata["originalName"] = noteName;

                    var metadataJson = System.Text.Json.JsonSerializer.Serialize(metadata, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    var metadataPath = Path.Combine(tempFolder, "metadata.json");
                    File.WriteAllText(metadataPath, metadataJson);
                    Debug.WriteLine($"shared: metadata.json を作成: {metadataPath}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"shared: metadata.json 作成中にエラー: {ex.Message}");
                }

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
                        string uploadSubFolder = null;
                        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                        var flashnotePath = Path.Combine(documentsPath, "Flashnote");

                        if (currentFolder.StartsWith(flashnotePath, StringComparison.OrdinalIgnoreCase))
                        {
                            var relativePath = Path.GetRelativePath(flashnotePath, currentFolder);
                            if (relativePath != ".")
                            {
                                uploadSubFolder = relativePath;
                            }
                        }

                        Debug.WriteLine($"サブフォルダ: {uploadSubFolder ?? "ルート"}");

                        // cards.txtをBlob Storageにアップロード
                        var cardsContent = "0\n";
                        await _blobStorageService.SaveNoteAsync(uid, noteName, cardsContent, uploadSubFolder);
                        Debug.WriteLine($"cards.txtをBlob Storageにアップロード完了: {noteName}");

                        // metadata.json をアップロード
                        try
                        {
                            var localMetadataPath = Path.Combine(tempFolder, "metadata.json");
                            if (File.Exists(localMetadataPath))
                            {
                                var metadataContent = File.ReadAllText(localMetadataPath);
                                await _blobStorageService.SaveNoteAsync(uid, $"{noteName}/metadata.json", metadataContent, uploadSubFolder);
                                Debug.WriteLine($"metadata.json をBlob Storageにアップロードしました: {noteName}/metadata.json");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"metadata.json Blobアップロード中にエラー: {ex.Message}");
                        }

                        // 一時フォルダを正しい場所にコピー（同期処理で使用されるため）
                        var correctTempFolder = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "Flashnote",
                            uploadSubFolder ?? "",
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

        /// <summary>
        /// 共有フォルダ内で新しいフォルダを作成する
        /// </summary>
        private async Task CreateNewFolderInSharedFolderAsync(string folderName, string subFolder)
        {
            try
            {
                Debug.WriteLine($"共有フォルダ内で新規フォルダ作成開始: {folderName}, サブフォルダ: {subFolder}");
                
                // 共有フォルダの情報を取得
                var sharedInfo = _sharedKeyService.GetSharedNoteInfo(subFolder);
                if (sharedInfo == null)
                {
                    await DisplayAlert("エラー", "共有フォルダの情報が見つかりません。", "OK");
                    return;
                }
                
                Debug.WriteLine($"共有フォルダ情報 - 元UID: {sharedInfo.OriginalUserId}, パス: {sharedInfo.NotePath}");
                
                // ローカルフォルダを作成
                var currentFolder = _currentPath.Peek();
                var newFolderPath = Path.Combine(currentFolder, folderName);
                Directory.CreateDirectory(newFolderPath);
                
                // フォルダをリストに追加
                _viewModel.Notes.Insert(0, new Note 
                { 
                    Name = folderName, 
                    Icon = "folder.png", 
                    IsFolder = true, 
                    FullPath = newFolderPath, 
                    LastModified = Directory.GetLastWriteTime(newFolderPath) 
                });
                
                // 共有フォルダとして元のUID配下にプレースホルダーを作成
                var fullFolderPath = $"{sharedInfo.NotePath}/{folderName}";
                var placeholderContent = "# This is a placeholder file to maintain folder structure";
                await _blobStorageService.SaveSharedNoteFileAsync(sharedInfo.OriginalUserId, fullFolderPath, ".folder_placeholder", placeholderContent);
                Debug.WriteLine($"フォルダプレースホルダーを作成: {fullFolderPath}/.folder_placeholder");
                
                await DisplayAlert("成功", $"共有フォルダ内に新しいフォルダ「{folderName}」を作成しました。", "OK");
                Debug.WriteLine($"共有フォルダ内でのフォルダ作成完了: {folderName}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"共有フォルダ内でのフォルダ作成中にエラー: {ex.Message}");
                await DisplayAlert("エラー", $"フォルダの作成に失敗しました: {ex.Message}", "OK");
            }
        }

        /// <summary>
        /// 共有フォルダ内でAnkiファイルをインポートする
        /// </summary>
        private async Task<string> ImportAnkiInSharedFolderAsync(AnkiImporter importer, List<CardData> cards, string noteName, string subFolder)
        {
            try
            {
                Debug.WriteLine($"共有フォルダ内でAnkiインポート開始: {noteName}, サブフォルダ: {subFolder}");
                
                // 共有フォルダの情報を取得
                var sharedInfo = _sharedKeyService.GetSharedNoteInfo(subFolder);
                if (sharedInfo == null)
                {
                    await DisplayAlert("エラー", "共有フォルダの情報が見つかりません。", "OK");
                    return null;
                }
                
                Debug.WriteLine($"共有フォルダ情報 - 元UID: {sharedInfo.OriginalUserId}, パス: {sharedInfo.NotePath}");
                
                // 通常のインポート処理を実行（ローカル保存）
                var currentFolder = _currentPath.Peek();
                var savedPath = await importer.SaveImportedCards(cards, currentFolder, noteName);
                
                // 共有ノートとして元のUID配下にもアップロード
                var fullNotePath = $"{sharedInfo.NotePath}/{noteName}";
                
                // cards.txtをアップロード
                var cardsContent = $"{cards.Count}\n{string.Join("\n", cards.Select((card, index) => $"{card.id},{DateTime.Now:yyyy-MM-dd HH:mm:ss}"))}";

                await _blobStorageService.SaveSharedNoteFileAsync(sharedInfo.OriginalUserId, fullNotePath, "cards.txt", cardsContent);
                
                // 各カードファイルをアップロード
                foreach (var card in cards)
                {
                    var cardJson = System.Text.Json.JsonSerializer.Serialize(card);
                    await _blobStorageService.SaveSharedNoteFileAsync(sharedInfo.OriginalUserId, $"{fullNotePath}/cards", $"{card.id}.json", cardJson);
                }
                
                Debug.WriteLine($"共有ノートとしてAnkiインポート完了: {fullNotePath}");
                
                return savedPath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"共有フォルダ内でのAnkiインポート中にエラー: {ex.Message}");
                await DisplayAlert("エラー", $"インポートに失敗しました: {ex.Message}", "OK");
                return null;
            }
        }

        private async Task LightweightSyncMetadataAsync(string uid, string subFolder = null)
        {
            try
            {
                Debug.WriteLine($"Lightweight metadata sync start - UID: {uid}, subFolder: {subFolder ?? "(root)"}");

                // Ensure blob service is ready
                _blobStorageService.ResetConnectionState();

                // Get list of notes on server for this uid (or for a subfolder)
                List<string> serverNotes;
                try
                {
                    serverNotes = await _blobStorageService.GetNoteListAsync(uid, subFolder);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"GetNoteListAsync failed: {ex.Message}");
                    return;
                }

                Debug.WriteLine($"Server notes count: {serverNotes?.Count ?? 0}");

                var docsBase = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Flashnote");
                var localTempBase = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Flashnote");
                Directory.CreateDirectory(docsBase);
                Directory.CreateDirectory(localTempBase);

                foreach (var noteName in serverNotes)
                {
                    try
                    {
                        // Try to download remote metadata.json for this note
                        var remoteJson = await _blobStorageService.GetUserFileAsync(uid, $"{noteName}/metadata.json", subFolder);
                        if (string.IsNullOrEmpty(remoteJson))
                        {
                            Debug.WriteLine($"No remote metadata for {noteName}");
                            continue;
                        }

                        DateTime? remoteUpdated = null;
                        try
                        {
                            using var doc = System.Text.Json.JsonDocument.Parse(remoteJson);
                            if (doc.RootElement.TryGetProperty("updatedAt", out var p))
                            {
                                if (DateTime.TryParse(p.GetString(), out var dt)) remoteUpdated = dt.ToUniversalTime();
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"remote metadata parse error for {noteName}: {ex.Message}");
                        }

                        // Locate local metadata
                        string localMetaPath = Path.Combine(localTempBase, subFolder ?? string.Empty, noteName + "_temp", "metadata.json");
                        string localMetaFromFolder = Path.Combine(docsBase, noteName, "metadata.json");

                        string localContent = null;
                        DateTime? localUpdated = null;

                        if (File.Exists(localMetaPath))
                        {
                            localContent = File.ReadAllText(localMetaPath);
                            try
                            {
                                using var doc = System.Text.Json.JsonDocument.Parse(localContent);
                                if (doc.RootElement.TryGetProperty("updatedAt", out var p)
                                    && DateTime.TryParse(p.GetString(), out var dt))
                                {
                                    localUpdated = dt.ToUniversalTime();
                                }
                            }
                            catch { }
                        }
                        else if (File.Exists(localMetaFromFolder))
                        {
                            localContent = File.ReadAllText(localMetaFromFolder);
                            try
                            {
                                using var doc = System.Text.Json.JsonDocument.Parse(localContent);
                                if (doc.RootElement.TryGetProperty("updatedAt", out var p)
                                    && DateTime.TryParse(p.GetString(), out var dt))
                                {
                                    localUpdated = dt.ToUniversalTime();
                                }
                            }
                            catch { }
                        }
                        else
                        {
                            // Try to read metadata.json inside local .ankpls if present
                            var ankplsPath = Path.Combine(docsBase, noteName + ".ankpls");
                            if (File.Exists(ankplsPath))
                            {
                                try
                                {
                                    using var archive = ZipFile.OpenRead(ankplsPath);
                                    var metaEntry = archive.Entries.FirstOrDefault(e => e.Name.Equals("metadata.json", StringComparison.OrdinalIgnoreCase));
                                    if (metaEntry != null)
                                    {
                                        using var s = metaEntry.Open();
                                        using var r = new StreamReader(s);
                                        localContent = r.ReadToEnd();
                                        try
                                        {
                                            using var doc = System.Text.Json.JsonDocument.Parse(localContent);
                                            if (doc.RootElement.TryGetProperty("updatedAt", out var p)
                                                && DateTime.TryParse(p.GetString(), out var dt))
                                            {
                                                localUpdated = dt.ToUniversalTime();
                                            }
                                        }
                                        catch { }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"Error reading metadata from .ankpls {ankplsPath}: {ex.Message}");
                                }
                            }
                        }

                        Debug.WriteLine($"Note: {noteName}, remoteUpdated={remoteUpdated?.ToString() ?? "null"}, localUpdated={localUpdated?.ToString() ?? "null"}");

                        if (localUpdated.HasValue && remoteUpdated.HasValue)
                        {
                            if (localUpdated > remoteUpdated)
                            {
                                // Local is newer -> upload local
                                Debug.WriteLine($"Local metadata newer for {noteName}, uploading to blob");
                                try
                                {
                                    await _blobStorageService.SaveNoteAsync(uid, $"{noteName}/metadata.json", localContent, subFolder);
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"Failed uploading metadata for {noteName}: {ex.Message}");
                                }
                            }
                            else if (remoteUpdated > localUpdated)
                            {
                                // Remote is newer -> save remote locally (LocalApplicationData temp path)
                                Debug.WriteLine($"Remote metadata newer for {noteName}, saving to local temp");
                                try
                                {
                                    var targetDir = Path.Combine(localTempBase, subFolder ?? string.Empty, noteName + "_temp");
                                    if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
                                    var targetPath = Path.Combine(targetDir, "metadata.json");
                                    File.WriteAllText(targetPath, remoteJson);
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"Failed writing remote metadata locally for {noteName}: {ex.Message}");
                                }
                            }
                            else
                            {
                                Debug.WriteLine($"Metadata equal timestamp for {noteName}, no action");
                            }
                        }
                        else if (localUpdated.HasValue && !remoteUpdated.HasValue)
                        {
                            // Remote missing updatedAt -> upload local
                            Debug.WriteLine($"Remote metadata missing timestamp for {noteName}, uploading local if exists");
                            if (!string.IsNullOrEmpty(localContent))
                            {
                                try { await _blobStorageService.SaveNoteAsync(uid, $"{noteName}/metadata.json", localContent, subFolder); }
                                catch (Exception ex) { Debug.WriteLine($"Upload failed: {ex.Message}"); }
                            }
                        }
                        else if (!localUpdated.HasValue && remoteUpdated.HasValue)
                        {
                            // Local missing -> save remote locally
                            Debug.WriteLine($"Local metadata missing for {noteName}, saving remote to local");
                            try
                            {
                                var targetDir = Path.Combine(localTempBase, subFolder ?? string.Empty, noteName + "_temp");
                                if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
                                var targetPath = Path.Combine(targetDir, "metadata.json");
                                File.WriteAllText(targetPath, remoteJson);
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Failed writing remote metadata locally for {noteName}: {ex.Message}");
                            }
                        }
                        else
                        {
                            Debug.WriteLine($"Neither local nor remote metadata contains timestamps for {noteName}, skipping");
                        }
                    }
                    catch (Exception exNote)
                    {
                        Debug.WriteLine($"Error processing metadata for note {noteName}: {exNote.Message}");
                    }
                }

                Debug.WriteLine("Lightweight metadata sync completed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LightweightSyncMetadataAsync error: {ex.Message}");
            }
        }

        private async Task DownloadAllMetadataAndMaterializeAsync(string uid, string subFolderFilter = null)
        {
            try
            {
                Debug.WriteLine($"DownloadAllMetadataAndMaterializeAsync start - UID:{uid}, Filter:{subFolderFilter ?? "(all)"}");
                var allMetas = await _blobStorageService.GetAllMetadataJsonAsync(uid);
                Debug.WriteLine($"metadata.json count: {allMetas?.Count ?? 0}");

                var docsBase = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Flashnote");
                var localTempBase = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Flashnote");
                Directory.CreateDirectory(docsBase);
                Directory.CreateDirectory(localTempBase);

                foreach (var (blobName, content) in allMetas)
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(content)) continue;

                        string subfolder = null;
                        string id = null;
                        bool isFolder = false;

                        // Parse JSON fields (do not use originalName for naming)
                        try
                        {
                            using var doc = System.Text.Json.JsonDocument.Parse(content);
                            var root = doc.RootElement;
                            if (root.TryGetProperty("subfolder", out var pSub)) subfolder = pSub.GetString();
                            if (root.TryGetProperty("id", out var pId)) id = pId.GetString();
                            if (root.TryGetProperty("isFolder", out var pIsFolder)) isFolder = pIsFolder.ValueKind == System.Text.Json.JsonValueKind.True;
                            else if (root.TryGetProperty("isfolder", out var pIsfolder)) isFolder = pIsfolder.ValueKind == System.Text.Json.JsonValueKind.True;
                        }
                        catch (Exception jex)
                        {
                            Debug.WriteLine($"metadata parse error: {blobName} - {jex.Message}");
                        }

                        // Derive note name from blob path (uid/[sub.../]{noteSegment}/metadata.json)
                        string pathNoteName = null;
                        try
                        {
                            var parts = blobName.Split('/', StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 3)
                            {
                                pathNoteName = parts[^2];
                            }
                        }
                        catch { }

                        // Normalize subfolder
                        if (string.IsNullOrWhiteSpace(subfolder)) subfolder = null;

                        // Filter by requested subfolder
                        if (!string.IsNullOrEmpty(subFolderFilter))
                        {
                            if (!string.Equals(subfolder ?? string.Empty, subFolderFilter, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }
                        }

                        // Decide materialized name:
                        // - If id is a GUID (new format), use id (UUID) as name
                        // - Else if path segment is GUID, use it
                        // - Else use path segment as-is (legacy)
                        string materializedName = null;
                        if (!string.IsNullOrEmpty(id) && Guid.TryParse(id, out _))
                        {
                            materializedName = id;
                        }
                        else if (!string.IsNullOrEmpty(pathNoteName) && Guid.TryParse(pathNoteName, out _))
                        {
                            materializedName = pathNoteName;
                        }
                        else
                        {
                            materializedName = pathNoteName ?? Guid.NewGuid().ToString();
                        }

                        // Target base directory in Documents
                        var targetBase = string.IsNullOrEmpty(subfolder) ? docsBase : Path.Combine(docsBase, subfolder);
                        if (!Directory.Exists(targetBase))
                        {
                            try { Directory.CreateDirectory(targetBase); } catch { }
                        }

                        if (isFolder)
                        {
                            // Create folder and write metadata.json (folder name based on materializedName)
                            var folderPath = Path.Combine(targetBase, materializedName);
                            try { Directory.CreateDirectory(folderPath); } catch { }
                            var metaPath = Path.Combine(folderPath, "metadata.json");
                            try
                            {
                                File.WriteAllText(metaPath, content);
                                Debug.WriteLine($"Materialized folder and metadata: {folderPath}");
                            }
                            catch (Exception wex)
                            {
                                Debug.WriteLine($"Write metadata failed: {metaPath} - {wex.Message}");
                            }
                        }
                        else
                        {
                            // Create minimal .ankpls that includes metadata.json (file name based on materializedName)
                            var ankplsPath = Path.Combine(targetBase, materializedName + ".ankpls");
                            try
                            {
                                var tempWork = Path.Combine(Path.GetTempPath(), "Flashnote_meta_" + Guid.NewGuid().ToString("N"));
                                Directory.CreateDirectory(tempWork);
                                var metaPath = Path.Combine(tempWork, "metadata.json");
                                File.WriteAllText(metaPath, content);

                                // Optional: include empty cards.txt so downstream can handle
                                var cardsPath = Path.Combine(tempWork, "cards.txt");
                                File.WriteAllText(cardsPath, "0\n");

                                if (File.Exists(ankplsPath))
                                {
                                    try { File.Delete(ankplsPath); } catch { }
                                }
                                ZipFile.CreateFromDirectory(tempWork, ankplsPath);

                                // Copy to LocalApplicationData temp so runtime can find it fast
                                var localTempNoteDir = string.IsNullOrEmpty(subfolder)
                                    ? Path.Combine(localTempBase, materializedName + "_temp")
                                    : Path.Combine(localTempBase, subfolder, materializedName + "_temp");
                                try
                                {
                                    Directory.CreateDirectory(localTempNoteDir);
                                    File.WriteAllText(Path.Combine(localTempNoteDir, "metadata.json"), content);
                                    if (!File.Exists(Path.Combine(localTempNoteDir, "cards.txt")))

                                        File.WriteAllText(Path.Combine(localTempNoteDir, "cards.txt"), "0\n");
                                }
                                catch (Exception ltex)
                                {
                                    Debug.WriteLine($"Local temp write failed: {ltex.Message}");
                                }

                                try { Directory.Delete(tempWork, true); } catch { }
                                Debug.WriteLine($"Materialized ankpls with metadata: {ankplsPath}");
                            }
                            catch (Exception zex)
                            {
                                Debug.WriteLine($"ankpls create failed: {ankplsPath} - {zex.Message}");
                            }
                        }
                    }
                    catch (Exception exEach)
                    {
                        Debug.WriteLine($"metadata materialize error: {exEach.Message}");
                    }
                }

                Debug.WriteLine("DownloadAllMetadataAndMaterializeAsync completed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DownloadAllMetadataAndMaterializeAsync error: {ex.Message}");
            }
        }

    }
}
