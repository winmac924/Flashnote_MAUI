using Microsoft.UI.Xaml;
using System.IO;
using System;
using Flashnote.Services;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Flashnote.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
	/// <summary>
	/// Initializes the singleton application object.  This is the first line of authored code
	/// executed, and as such is the logical equivalent of main() or WinMain().
	/// </summary>
	public App()
	{
		this.InitializeComponent();
		
		// WebView2の初期化を実行
		_ = InitializeWebView2Async();
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
	
	/// <summary>
	/// WebView2の初期化を非同期で実行
	/// </summary>
	private async Task InitializeWebView2Async()
	{
		try
		{
			await WebView2InitializationService.InitializeAsync();
			System.Diagnostics.Debug.WriteLine("✅ Windowsプラットフォーム - WebView2初期化完了");
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"❌ Windowsプラットフォーム - WebView2初期化エラー: {ex.Message}");
		}
	}
}

