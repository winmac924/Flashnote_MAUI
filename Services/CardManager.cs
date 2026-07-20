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
    /// <summary>
    /// カード作成・編集の共通機能を提供するサービス
    /// </summary>
    public partial class CardManager : IDisposable
    {
        private readonly string _cardsFilePath;
        private readonly string _tempExtractPath;
        private readonly string _ankplsFilePath;
        private List<string> _cards = new List<string>();
        private List<string> _imagePaths = new List<string>();
        private int _imageCount = 0;
        private readonly string _subFolder;
        private readonly BlobStorageService _blobStorageService;
        
        // UI要素
        private Picker _cardTypePicker;
        private Editor _frontTextEditor, _backTextEditor;
        private RichTextLabel _frontPreviewLabel, _backPreviewLabel;
        private RichTextLabel _choiceQuestionPreviewLabel, _choiceExplanationPreviewLabel;
        private VerticalStackLayout _basicCardLayout;
        private StackLayout _multipleChoiceLayout, _imageFillLayout;
        private Editor _choiceQuestion, _choiceQuestionExplanation;
        private StackLayout _choicesContainer;
        private Editor _lastFocusedEditor = null;
        
        // プレビュー更新のデバウンス用
        private System.Timers.Timer _frontPreviewTimer;
        private System.Timers.Timer _backPreviewTimer;
        private System.Timers.Timer _choiceQuestionPreviewTimer;
        private System.Timers.Timer _choiceExplanationPreviewTimer;
        
        // 選択肢カード用
        private bool _removeNumbers = false;
        
        // 画像穴埋めカード用
        private string _selectedImagePath = "";
        private List<SkiaSharp.SKRect> _selectionRects = new List<SkiaSharp.SKRect>();  // 画像座標（ピクセル単位）
        private SkiaSharp.SKBitmap _imageBitmap;
        private SkiaSharp.SKPoint _startPoint, _endPoint;
        private bool _isDragging = false;
        private bool _isMoving = false;
        private bool _isResizing = false;
        private int _selectedRectIndex = -1;
        private int _resizeHandle = -1; // 0:左上, 1:右上, 2:左下, 3:右下
        private SkiaSharp.SKPoint _dragOffset;
        private const float HANDLE_SIZE = 25; // ハンドルの表示サイズ
        private const float HANDLE_HIT_SIZE = 35; // ハンドルのクリック判定サイズ（より大きい）
        private const float MAX_CANVAS_WIDTH = 600f;  // キャンバスの最大幅
        private const float MAX_CANVAS_HEIGHT = 800f; // キャンバスの最大高さ
        private SkiaSharp.Views.Maui.Controls.SKCanvasView _canvasView;
        
        // 編集対象のカードID（編集モード用）
        private string _editCardId = null;
        
        // ページ画像追加機能のコールバック（NotePage専用）
        private Func<Editor, Task> _addPageImageCallback;

        // ページ選択機能のコールバック（NotePage専用）
        private Func<int, Task> _selectPageCallback;
        private Func<Task> _loadCurrentImageCallback;
        private Func<string, Task> _showToastCallback;
        private Func<string, string, Task> _showAlertCallback;
        private Func<Editor, int, Task> _selectPageForImageCallback; // 新しく追加：ページ選択用画像追加

        // 元のAdd.xaml.csから追加するフィールド
        private bool _isDirty = false;
        private System.Timers.Timer _autoSaveTimer;
        private string _tempPath;
        private string _ankplsPath;

        // Ctrlキー押下状態を管理
        private bool _isCtrlDown = false;
        private bool _isShiftDown = false;

        private Flashnote.Models.DefaultMaterial _defaultMaterial;


        public CardManager(string cardsPath, string tempPath, string cardId = null, string subFolder = null)
        {
            _cardsFilePath = Path.Combine(tempPath, "cards.txt");
            _tempExtractPath = tempPath;
            _ankplsFilePath = cardsPath;
            _editCardId = cardId;
            _subFolder = subFolder;
            
            // BlobStorageServiceを取得
            _blobStorageService = MauiProgram.Services.GetService<BlobStorageService>();
            LoadMetadataDefaultMaterial();
            LoadCards();
            LoadImageCount();
            InitializeAutoSaveTimer();
        }

        // メタデータ(defaultMaterial)を読み込む
        /// <summary>
        /// ページ画像追加機能のコールバックを設定（NotePage専用）
        /// </summary>
        public void SetPageImageCallback(Func<Editor, Task> callback)
        {
            _addPageImageCallback = callback;
        }
        /// <summary>
        /// ページ選択機能のコールバックを設定（NotePage専用）
        /// </summary>
        public void SetPageSelectionCallbacks(
            Func<int, Task> selectPageCallback,
            Func<Task> loadCurrentImageCallback,
            Func<string, Task> showToastCallback,
            Func<string, string, Task> showAlertCallback)
        {
            _selectPageCallback = selectPageCallback;
            _loadCurrentImageCallback = loadCurrentImageCallback;
            _showToastCallback = showToastCallback;
            _showAlertCallback = showAlertCallback;
        }
        /// <summary>
        /// ページ選択用画像追加コールバックを設定
        /// </summary>
        public void SetPageSelectionImageCallback(Func<Editor, int, Task> callback)
        {
            _selectPageForImageCallback = callback;
        }
        /// <summary>
        /// 現在フォーカスされているエディターを取得
        /// </summary>
        public Editor GetCurrentEditor()
        {
            // 基本カードの表面エディター
            if (_frontTextEditor != null && _frontTextEditor.IsFocused)
            {
                _lastFocusedEditor = _frontTextEditor;
                return _frontTextEditor;
            }
            
            // 基本カードの裏面エディター
            if (_backTextEditor != null && _backTextEditor.IsFocused)
            {
                _lastFocusedEditor = _backTextEditor;
                return _backTextEditor;
            }
            
            // 選択肢カードの問題エディター
            if (_choiceQuestion != null && _choiceQuestion.IsFocused)
            {
                _lastFocusedEditor = _choiceQuestion;
                return _choiceQuestion;
            }
            
            // 選択肢カードの解説エディター
            if (_choiceQuestionExplanation != null && _choiceQuestionExplanation.IsFocused)
            {
                _lastFocusedEditor = _choiceQuestionExplanation;
                return _choiceQuestionExplanation;
            }

            // 動的に作成された選択肢エディターを確認
            if (_choicesContainer != null)
            {
                foreach (var stack in _choicesContainer.Children.OfType<StackLayout>())
                {
                    var editor = stack.Children.OfType<Editor>().FirstOrDefault();
                    if (editor != null && editor.IsFocused)
                    {
                        _lastFocusedEditor = editor;
                        return editor;
                    }
                }
            }

            // フォーカスされているエディターがない場合、最後にフォーカスされたエディターを使用
            if (_lastFocusedEditor != null)
            {
                return _lastFocusedEditor;
            }

            // デフォルトは表面
            _lastFocusedEditor = _frontTextEditor;
            return _frontTextEditor;
        }
        /// <summary>
        /// 自動保存タイマーを初期化
        /// </summary>
        private void InitializeAutoSaveTimer()
        {
            _autoSaveTimer = new System.Timers.Timer(10000); // 10秒ごとに保存
            _autoSaveTimer.Elapsed += AutoSaveTimer_Elapsed;
            _autoSaveTimer.AutoReset = true;
            _autoSaveTimer.Enabled = true;
        }
        /// <summary>
        /// 自動保存タイマーイベント
        /// </summary>
        private async void AutoSaveTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (_isDirty)
            {
                try
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        OnSaveCardClicked(null, null);
                    });
                    _isDirty = false;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"自動保存中にエラー: {ex.Message}");
                }
            }
        }
        /// <summary>
        /// リソース解放
        /// </summary>
        public void Dispose()
        {
            try
            {
                // タイマーを停止・解放
                _frontPreviewTimer?.Stop();
                _frontPreviewTimer?.Dispose();
                
                _backPreviewTimer?.Stop();
                _backPreviewTimer?.Dispose();
                
                _choiceQuestionPreviewTimer?.Stop();
                _choiceQuestionPreviewTimer?.Dispose();
                
                _choiceExplanationPreviewTimer?.Stop();
                _choiceExplanationPreviewTimer?.Dispose();
                
                _autoSaveTimer?.Stop();
                _autoSaveTimer?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CardManagerリソース解放エラー: {ex.Message}");
            }
        }
    }
}
