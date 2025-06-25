using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Maui.Dispatching;

namespace Flashnote.Services
{
    public class FileWatcherService : IDisposable
    {
        private FileSystemWatcher _watcher;
        private readonly string _folderPath;
        private IDispatcher _dispatcher;
        
        public event EventHandler<FileSystemEventArgs> FileCreated;
        public event EventHandler<FileSystemEventArgs> FileDeleted;
        public event EventHandler<FileSystemEventArgs> FileChanged;
        public event EventHandler<RenamedEventArgs> FileRenamed;
        public event EventHandler<FileSystemEventArgs> DirectoryCreated;
        public event EventHandler<FileSystemEventArgs> DirectoryDeleted;

        public FileWatcherService()
        {
            _folderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Flashnote");
            // _dispatcher = Application.Current?.Dispatcher ?? throw new InvalidOperationException("Dispatcher not available");
            // Dispatcherの取得はイベント発火時に行う
            InitializeWatcher();
        }

        private void InitializeWatcher()
        {
            if (!Directory.Exists(_folderPath))
            {
                Directory.CreateDirectory(_folderPath);
            }

            _watcher = new FileSystemWatcher(_folderPath)
            {
                NotifyFilter = NotifyFilters.CreationTime
                             | NotifyFilters.LastWrite
                             | NotifyFilters.FileName
                             | NotifyFilters.DirectoryName,
                Filter = "*",
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            // ファイルイベント
            _watcher.Created += OnFileCreated;
            _watcher.Deleted += OnFileDeleted;
            _watcher.Changed += OnFileChanged;
            _watcher.Renamed += OnFileRenamed;

            // ディレクトリイベント
            _watcher.Created += OnDirectoryCreated;
            _watcher.Deleted += OnDirectoryDeleted;

            System.Diagnostics.Debug.WriteLine($"FileWatcherService: 監視開始 - パス: {_folderPath}");
        }

        private IDispatcher GetDispatcher()
        {
            if (_dispatcher == null)
            {
                _dispatcher = Application.Current?.Dispatcher;
                if (_dispatcher == null)
                {
                    throw new InvalidOperationException("Dispatcher not available");
                }
            }
            return _dispatcher;
        }

        private void OnFileCreated(object sender, FileSystemEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"FileWatcherService: File created event fired for: {e.FullPath}");
            
            // ディレクトリの場合はファイルイベントとして処理しない
            if (Directory.Exists(e.FullPath))
            {
                System.Diagnostics.Debug.WriteLine($"FileWatcherService: Path is a directory, ignoring file event");
                return;
            }
            
            if (Path.GetExtension(e.FullPath).Equals(".ankpls", StringComparison.OrdinalIgnoreCase))
            {
                System.Diagnostics.Debug.WriteLine($"FileWatcherService: Dispatching FileCreated event for .ankpls file");
                GetDispatcher().Dispatch(() => FileCreated?.Invoke(this, e));
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"FileWatcherService: File is not .ankpls, ignoring");
            }
        }

        private void OnFileDeleted(object sender, FileSystemEventArgs e)
        {
            if (Path.GetExtension(e.FullPath).Equals(".ankpls", StringComparison.OrdinalIgnoreCase))
            {
                GetDispatcher().Dispatch(() => FileDeleted?.Invoke(this, e));
            }
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            if (Path.GetExtension(e.FullPath).Equals(".ankpls", StringComparison.OrdinalIgnoreCase))
            {
                GetDispatcher().Dispatch(() => FileChanged?.Invoke(this, e));
            }
        }

        private void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            bool isOldFileAnkpls = Path.GetExtension(e.OldFullPath).Equals(".ankpls", StringComparison.OrdinalIgnoreCase);
            bool isNewFileAnkpls = Path.GetExtension(e.FullPath).Equals(".ankpls", StringComparison.OrdinalIgnoreCase);
            
            if (isOldFileAnkpls || isNewFileAnkpls)
            {
                GetDispatcher().Dispatch(() => FileRenamed?.Invoke(this, e));
            }
        }

        private void OnDirectoryCreated(object sender, FileSystemEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"FileWatcherService: Directory created event fired for: {e.FullPath}");
            System.Diagnostics.Debug.WriteLine($"FileWatcherService: Is directory: {Directory.Exists(e.FullPath)}");
            
            if (Directory.Exists(e.FullPath))
            {
                System.Diagnostics.Debug.WriteLine($"FileWatcherService: Dispatching DirectoryCreated event");
                GetDispatcher().Dispatch(() => DirectoryCreated?.Invoke(this, e));
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"FileWatcherService: Directory does not exist, ignoring event");
            }
        }

        private void OnDirectoryDeleted(object sender, FileSystemEventArgs e)
        {
            if (!Path.HasExtension(e.Name))
            {
                GetDispatcher().Dispatch(() => DirectoryDeleted?.Invoke(this, e));
            }
        }

        public void StartWatching()
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = true;
            }
        }

        public void StopWatching()
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
            }
        }

        public void Dispose()
        {
            _watcher?.Dispose();
        }
    }
} 