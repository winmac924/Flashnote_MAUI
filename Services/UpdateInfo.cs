namespace Flashnote.Services
{
    public class UpdateInfo
    {
        public string Version { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public string ReleaseNotes { get; set; } = string.Empty;
        public DateTime PublishDate { get; set; }
        public bool IsPrerelease { get; set; }
        public string TagName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        
        // ヘルパープロパティ
        public bool IsUpdateAvailable { get; set; }
        public string LatestVersion { get; set; } = string.Empty;
        public DateTime? ReleaseDate { get; set; }
    }
} 