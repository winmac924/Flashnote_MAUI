using Microsoft.Extensions.Logging;
using Plugin.Maui.KeyListener;
using SkiaSharp.Views.Maui.Controls.Hosting;
using CommunityToolkit.Maui;
using Flashnote.Services;
using Flashnote.ViewModels;
using System.Text;

namespace Flashnote;

public static class MauiProgram
{
	public static IServiceProvider Services { get; private set; }

	public static MauiApp CreateMauiApp()
	{
		// エンコーディングプロバイダーを登録（Shift_JIS等のサポート）
		Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
		
		// WebView2の初期化を実行
		_ = WebView2InitializationService.InitializeAsync();
		
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
            .UseSkiaSharp()
            .UseKeyListener()
            .UseMauiCommunityToolkit()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansRegular");
				fonts.AddFont("FluentUI-Regular.ttf", "FluentUI");
			});

		// サービスの登録
		builder.Services.AddSingleton<ConfigurationService>();
		builder.Services.AddSingleton<UnsynchronizedNotesService>();
		builder.Services.AddSingleton<NetworkStateService>();
		builder.Services.AddSingleton<BlobStorageService>();
		builder.Services.AddSingleton<SharedKeyService>();
		builder.Services.AddSingleton<FileWatcherService>();
		builder.Services.AddSingleton<AnimationService>();
		builder.Services.AddSingleton<CardSyncService>(serviceProvider => 
			new CardSyncService(
				serviceProvider.GetRequiredService<BlobStorageService>(),
				serviceProvider.GetRequiredService<SharedKeyService>()
			));
		builder.Services.AddSingleton<AnkiExporter>();
		builder.Services.AddSingleton<AnkiImporter>();
		
		// HTTP Client の登録（遅延初期化）
		builder.Services.AddHttpClient<GitHubUpdateService>(client =>
		{
			client.Timeout = TimeSpan.FromSeconds(30);
		});
		
		// アップデート関連サービス（遅延初期化）
		builder.Services.AddSingleton<GitHubUpdateService>();
		builder.Services.AddSingleton<UpdateNotificationService>();

		// ViewModels の登録
		builder.Services.AddTransient<MainPageViewModel>();

		// Pages の登録
		builder.Services.AddTransient<MainPage>(serviceProvider => 
			new MainPage(
				serviceProvider.GetRequiredService<CardSyncService>(),
				serviceProvider.GetRequiredService<UpdateNotificationService>(),
				serviceProvider.GetRequiredService<BlobStorageService>(),
				serviceProvider.GetRequiredService<SharedKeyService>(),
				serviceProvider.GetRequiredService<FileWatcherService>()
			));

		// App の登録
		builder.Services.AddSingleton<App>(serviceProvider => 
			new App(
				serviceProvider.GetRequiredService<ConfigurationService>(),
				serviceProvider.GetRequiredService<FileWatcherService>()
			));

#if DEBUG
		builder.Services.AddLogging(logging =>
		{
			logging.AddDebug();
		});
        builder.Logging.AddDebug();
#endif

		var app = builder.Build();
		Services = app.Services;
		return app;
	}
}
