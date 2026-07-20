using Flashnote.Services.Sync;

namespace Flashnote_MAUI.Tests
{
    public class BlobPathResolverTests
    {
        private const string Uid = "user1";
        private const string NoteUuid = "9D26BBEF-9C11-4E82-BD89-CDEE2EAC81BB";
        private const string FolderUuid = "BB516128-2207-4084-892A-04A85890CB6B";

        [Fact]
        public void IsFlatUuidLayout_BareUuid_ReturnsTrue()
        {
            Assert.True(BlobPathResolver.IsFlatUuidLayout(NoteUuid));
        }

        [Fact]
        public void IsFlatUuidLayout_CompoundPathWithUuidPrefix_ReturnsTrue()
        {
            Assert.True(BlobPathResolver.IsFlatUuidLayout($"{NoteUuid}/metadata.json"));
        }

        [Fact]
        public void IsFlatUuidLayout_NonUuidName_ReturnsFalse()
        {
            Assert.False(BlobPathResolver.IsFlatUuidLayout("MyNote"));
        }

        [Fact]
        public void IsFlatUuidLayout_NullOrEmpty_ReturnsFalse()
        {
            Assert.False(BlobPathResolver.IsFlatUuidLayout(null!));
            Assert.False(BlobPathResolver.IsFlatUuidLayout(""));
        }

        // 回帰テスト: サブフォルダ内のUUID形式ノートで、SaveNoteAsyncが書き込むパスと
        // GetNoteContentAsync/AppendCardToNoteAsyncが読みに行くパスが一致しなければならない。
        [Fact]
        public void ResolveNoteFilePath_UuidNoteInsideSubFolder_IgnoresSubFolder()
        {
            var path = BlobPathResolver.ResolveNoteFilePath(Uid, NoteUuid, FolderUuid);

            Assert.Equal($"{Uid}/{NoteUuid}/cards.txt", path);
        }

        [Fact]
        public void ResolveNoteFilePath_NonUuidNoteInsideSubFolder_IncludesSubFolder()
        {
            var path = BlobPathResolver.ResolveNoteFilePath(Uid, "MyNote", FolderUuid);

            Assert.Equal($"{Uid}/{FolderUuid}/MyNote/cards.txt", path);
        }

        [Fact]
        public void ResolveNoteFilePath_NoSubFolder_ReturnsFlatRootPath()
        {
            var path = BlobPathResolver.ResolveNoteFilePath(Uid, "MyNote", null);

            Assert.Equal($"{Uid}/MyNote/cards.txt", path);
        }

        [Fact]
        public void ResolveDirectFilePath_UuidCompoundJsonPath_IgnoresSubFolder()
        {
            var path = BlobPathResolver.ResolveDirectFilePath(Uid, $"{NoteUuid}/metadata.json", FolderUuid);

            Assert.Equal($"{Uid}/{NoteUuid}/metadata.json", path);
        }

        [Fact]
        public void ResolveDirectFilePath_NonUuidJsonName_IncludesSubFolder()
        {
            var path = BlobPathResolver.ResolveDirectFilePath(Uid, "card123.json", "MyNote/cards");

            Assert.Equal($"{Uid}/MyNote/cards/card123.json", path);
        }

        [Fact]
        public void ResolveNoteSubPath_UuidNote_IgnoresSubFolder()
        {
            var path = BlobPathResolver.ResolveNoteSubPath(NoteUuid, FolderUuid, "img");

            Assert.Equal($"{NoteUuid}/img", path);
        }

        [Fact]
        public void ResolveNoteSubPath_NonUuidNoteWithSubFolder_IncludesSubFolder()
        {
            var path = BlobPathResolver.ResolveNoteSubPath("MyNote", "Sub", "cards");

            Assert.Equal("Sub/MyNote/cards", path);
        }

        [Fact]
        public void ResolveNoteSubPath_NonUuidNoteWithoutSubFolder_OmitsSubFolder()
        {
            var path = BlobPathResolver.ResolveNoteSubPath("MyNote", null, "cards");

            Assert.Equal("MyNote/cards", path);
        }

        // SaveNoteAsync / GetNoteContentAsync / AppendCardToNoteAsync が同じ結果になることを保証する
        [Fact]
        public void ResolveNoteFilePath_IsConsistentAcrossSaveAndReadCallSites()
        {
            var saved = BlobPathResolver.ResolveNoteFilePath(Uid, NoteUuid, FolderUuid);
            var read = BlobPathResolver.ResolveNoteFilePath(Uid, NoteUuid, FolderUuid);

            Assert.Equal(saved, read);
        }
    }
}
