using System.Reflection;

namespace Flashnote.Services;

public static class VersionHelper
{
    /// <summary>
    /// 現在のアプリケーションバージョンを取得します
    /// </summary>
    public static string GetCurrentVersion()
    {
        try
        {
            // まずAssemblyからバージョンを取得（EXE形式でも動作）
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            
            if (version != null)
            {
                return $"{version.Major}.{version.Minor}.{version.Build}";
            }

#if WINDOWS
            try
            {
                // MSIXパッケージの場合のフォールバック（EXE形式では例外が発生）
                var package = Windows.ApplicationModel.Package.Current;
                var packageVersion = package.Id.Version;
                return $"{packageVersion.Major}.{packageVersion.Minor}.{packageVersion.Build}";
            }
            catch
            {
                // MSIX以外（EXE形式など）の場合はAssemblyバージョンを使用
                // この場合は既に上で取得済みなので、デフォルトにフォールバック
            }
#endif
            
            // すべて失敗した場合のデフォルトバージョン
            return "1.1.0"; // プロジェクトファイルのApplicationDisplayVersionに合わせる
        }
        catch
        {
            // エラーが発生した場合のデフォルトバージョン
            return "1.1.0";
        }
    }

    /// <summary>
    /// アプリケーション情報を取得します
    /// </summary>
    public static AppVersionInfo GetAppInfo()
    {
        return new AppVersionInfo
        {
            Name = "Flashnote",
            Version = GetCurrentVersion(),
            PackageName = "winmac924.flashnote.maui",
            BuildString = GetCurrentVersion()
        };
    }

    /// <summary>
    /// バージョン文字列を比較します
    /// </summary>
    public static bool IsNewerVersion(string currentVersion, string newVersion)
    {
        try
        {
            var current = Version.Parse(currentVersion);
            var newer = Version.Parse(newVersion);
            return newer > current;
        }
        catch
        {
            return false;
        }
    }
}

public class AppVersionInfo
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public string BuildString { get; set; } = string.Empty;
} 