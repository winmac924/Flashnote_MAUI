using System;
using Flashnote.Services.Sync;

namespace Flashnote_MAUI.Tests
{
    // IncrementalSyncService は実アプリと同じ sync-index.json を読み書きするため、
    // 他クラスとのファイル競合を避けるために専用コレクションで直列実行する。
    [CollectionDefinition("IncrementalSyncIndexFile", DisableParallelization = true)]
    public class IncrementalSyncIndexFileCollection
    {
    }

    [Collection("IncrementalSyncIndexFile")]
    public class IncrementalSyncServiceTests
    {
        [Fact]
        public void HasChangedSinceLastSync_FirstTime_ReturnsTrue()
        {
            var service = new IncrementalSyncService();
            var key = $"key-{Guid.NewGuid()}";

            try
            {
                Assert.True(service.HasChangedSinceLastSync(key, "content-v1"));
            }
            finally
            {
                service.Reset(key);
            }
        }

        [Fact]
        public void HasChangedSinceLastSync_SameContentTwice_ReturnsFalseSecondTime()
        {
            var service = new IncrementalSyncService();
            var key = $"key-{Guid.NewGuid()}";

            try
            {
                Assert.True(service.HasChangedSinceLastSync(key, "content-v1"));
                Assert.False(service.HasChangedSinceLastSync(key, "content-v1"));
            }
            finally
            {
                service.Reset(key);
            }
        }

        [Fact]
        public void HasChangedSinceLastSync_ContentChanges_ReturnsTrueAgain()
        {
            var service = new IncrementalSyncService();
            var key = $"key-{Guid.NewGuid()}";

            try
            {
                Assert.True(service.HasChangedSinceLastSync(key, "content-v1"));
                Assert.False(service.HasChangedSinceLastSync(key, "content-v1"));
                Assert.True(service.HasChangedSinceLastSync(key, "content-v2"));
                Assert.False(service.HasChangedSinceLastSync(key, "content-v2"));
            }
            finally
            {
                service.Reset(key);
            }
        }

        [Fact]
        public void Reset_ForgetsPreviousHash_SoNextCallReturnsTrue()
        {
            var service = new IncrementalSyncService();
            var key = $"key-{Guid.NewGuid()}";

            service.HasChangedSinceLastSync(key, "content-v1");
            Assert.False(service.HasChangedSinceLastSync(key, "content-v1"));

            service.Reset(key);

            Assert.True(service.HasChangedSinceLastSync(key, "content-v1"));
            service.Reset(key);
        }

        [Fact]
        public void HasChangedSinceLastSync_EmptyKey_AlwaysReturnsTrue()
        {
            var service = new IncrementalSyncService();

            Assert.True(service.HasChangedSinceLastSync(string.Empty, "content"));
            Assert.True(service.HasChangedSinceLastSync(string.Empty, "content"));
        }
    }
}
