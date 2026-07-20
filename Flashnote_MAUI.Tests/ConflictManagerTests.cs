using System;
using System.IO;
using Flashnote.Services.Sync;

namespace Flashnote_MAUI.Tests
{
    [Collection("OfflineSyncQueueFile")]
    public class ConflictManagerTests
    {
        [Fact]
        public void DetectConflict_NoPendingChange_ReturnsNull()
        {
            var queue = new OfflineSyncQueue();
            var manager = new ConflictManager(queue);
            var noteName = $"note-{Guid.NewGuid()}";

            var conflict = manager.DetectConflict(noteName, "Sub");

            Assert.Null(conflict);
        }

        [Fact]
        public void DetectConflict_PendingChangeExists_ReturnsConflict()
        {
            var queue = new OfflineSyncQueue();
            var manager = new ConflictManager(queue);
            var noteName = $"note-{Guid.NewGuid()}";

            queue.AddUnsynchronizedNote(noteName, "Sub", "offline");
            try
            {
                var conflict = manager.DetectConflict(noteName, "Sub");

                Assert.NotNull(conflict);
                Assert.Equal(noteName, conflict.NoteName);
                Assert.Equal("Sub", conflict.SubFolder);
            }
            finally
            {
                queue.RemoveUnsynchronizedNote(noteName, "Sub");
            }
        }

        [Fact]
        public void DetectConflict_WithLocalPath_RecordsLastModified()
        {
            var queue = new OfflineSyncQueue();
            var manager = new ConflictManager(queue);
            var noteName = $"note-{Guid.NewGuid()}";
            var localPath = Path.Combine(Path.GetTempPath(), $"{noteName}.ankpls");
            File.WriteAllText(localPath, "content");

            queue.AddUnsynchronizedNote(noteName, null, "offline");
            try
            {
                var conflict = manager.DetectConflict(noteName, null, localPath);

                Assert.NotNull(conflict);
                Assert.NotNull(conflict.LocalLastModified);
            }
            finally
            {
                queue.RemoveUnsynchronizedNote(noteName, null);
                File.Delete(localPath);
            }
        }

        [Fact]
        public void DetectConflict_DifferentSubFolder_ReturnsNull()
        {
            var queue = new OfflineSyncQueue();
            var manager = new ConflictManager(queue);
            var noteName = $"note-{Guid.NewGuid()}";

            queue.AddUnsynchronizedNote(noteName, "SubA", "offline");
            try
            {
                var conflict = manager.DetectConflict(noteName, "SubB");

                Assert.Null(conflict);
            }
            finally
            {
                queue.RemoveUnsynchronizedNote(noteName, "SubA");
            }
        }
    }
}
