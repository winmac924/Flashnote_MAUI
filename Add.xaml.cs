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

namespace Flashnote
{
    public partial class Add : ContentPage
    {
        private CardManager _cardManager;

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

        // CardManagerに移行済み - 削除
    }
}