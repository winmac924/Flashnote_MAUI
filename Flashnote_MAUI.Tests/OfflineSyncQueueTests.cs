using System;
using Flashnote.Services.Sync;

namespace Flashnote_MAUI.Tests
{
    // 注意: OfflineSyncQueue は実アプリと同じ unsynchronized_notes.json を読み書きするため、
    // 既存データを壊さないようテストごとに一意なノート名を使い、終了時に必ず削除する。
    [Collection("OfflineSyncQueueFile")]
    public class OfflineSyncQueueTests
    {
        [Fact]
        public void AddAndHasPending_ReturnsTrueForAddedNote()
        {
            var queue = new OfflineSyncQueue();
            var noteName = $"test-note-{Guid.NewGuid()}";
            try
            {
                queue.AddUnsynchronizedNote(noteName, "SubA", "offline");

                Assert.True(queue.HasPending(noteName, "SubA"));
                Assert.False(queue.HasPending(noteName, "SubB"));
            }
            finally
            {
                queue.RemoveUnsynchronizedNote(noteName, "SubA");
            }
        }

        [Fact]
        public void RemoveUnsynchronizedNote_ClearsPendingState()
        {
            var queue = new OfflineSyncQueue();
            var noteName = $"test-note-{Guid.NewGuid()}";

            queue.AddUnsynchronizedNote(noteName, null, "not_logged_in");
            Assert.True(queue.HasPending(noteName, null));

            queue.RemoveUnsynchronizedNote(noteName, null);
            Assert.False(queue.HasPending(noteName, null));
        }

        [Fact]
        public void HasPending_UnknownNote_ReturnsFalse()
        {
            var queue = new OfflineSyncQueue();
            var noteName = $"never-added-{Guid.NewGuid()}";

            Assert.False(queue.HasPending(noteName));
        }

        [Fact]
        public void AddUnsynchronizedNote_Twice_UpdatesInPlaceRatherThanDuplicating()
        {
            var queue = new OfflineSyncQueue();
            var noteName = $"test-note-{Guid.NewGuid()}";
            try
            {
                queue.AddUnsynchronizedNote(noteName, "Sub", "not_logged_in");
                queue.AddUnsynchronizedNote(noteName, "Sub", "offline");

                var matches = queue.GetUnsynchronizedNotes()
                    .FindAll(n => n.NoteName == noteName && n.SubFolder == "Sub");

                Assert.Single(matches);
                Assert.Equal("offline", matches[0].Reason);
            }
            finally
            {
                queue.RemoveUnsynchronizedNote(noteName, "Sub");
            }
        }
    }
}
