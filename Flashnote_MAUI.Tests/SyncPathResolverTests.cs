using System;
using System.IO;
using Flashnote.Services.Sync;

namespace Flashnote_MAUI.Tests
{
    public class SyncPathResolverTests
    {
        private static string ExpectedRoot =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Flashnote");

        [Fact]
        public void GetLocalNoteRoot_ReturnsDocumentsFlashnoteFolder()
        {
            var root = SyncPathResolver.GetLocalNoteRoot();

            Assert.Equal(ExpectedRoot, root);
        }

        [Fact]
        public void GetLocalNoteRoot_NeverReturnsEnumName()
        {
            // Environment.SpecialFolder.MyDocuments.ToString() バグの再発防止テスト。
            // 過去に MainPage.xaml.cs で "MyDocuments" という文字列がパスとして
            // 誤って使われていた（実際のフォルダパスではなく enum 名がそのまま返る）。
            var root = SyncPathResolver.GetLocalNoteRoot();

            Assert.DoesNotContain("MyDocuments" + Path.DirectorySeparatorChar + "Flashnote", root);
            Assert.True(Path.IsPathRooted(root));
        }

        [Fact]
        public void GetLocalFolderPath_WithNullSubFolder_ReturnsRoot()
        {
            var path = SyncPathResolver.GetLocalFolderPath(null);

            Assert.Equal(ExpectedRoot, path);
        }

        [Fact]
        public void GetLocalFolderPath_WithSubFolder_CombinesUnderRoot()
        {
            var path = SyncPathResolver.GetLocalFolderPath("Sub\\Folder");

            Assert.Equal(Path.Combine(ExpectedRoot, "Sub\\Folder"), path);
        }

        [Fact]
        public void GetLocalNotePath_AppendsAnkplsExtension()
        {
            var path = SyncPathResolver.GetLocalNotePath(null, "MyNote");

            Assert.Equal(Path.Combine(ExpectedRoot, "MyNote.ankpls"), path);
        }

        [Fact]
        public void GetSubFolderFromLocalPath_RootItself_ReturnsNull()
        {
            var subFolder = SyncPathResolver.GetSubFolderFromLocalPath(ExpectedRoot);

            Assert.Null(subFolder);
        }

        [Fact]
        public void GetSubFolderFromLocalPath_PathOutsideRoot_ReturnsNull()
        {
            var subFolder = SyncPathResolver.GetSubFolderFromLocalPath(Path.Combine(Path.GetTempPath(), "SomewhereElse"));

            Assert.Null(subFolder);
        }

        [Fact]
        public void GetSubFolderFromLocalPath_NestedPath_ReturnsRelativePath()
        {
            var fullPath = Path.Combine(ExpectedRoot, "Deck1", "Sub");

            var subFolder = SyncPathResolver.GetSubFolderFromLocalPath(fullPath);

            Assert.Equal(Path.Combine("Deck1", "Sub"), subFolder);
        }

        [Fact]
        public void GetSubFolderFromLocalPath_NullOrEmpty_ReturnsNull()
        {
            Assert.Null(SyncPathResolver.GetSubFolderFromLocalPath(null!));
            Assert.Null(SyncPathResolver.GetSubFolderFromLocalPath(string.Empty));
        }
    }
}
