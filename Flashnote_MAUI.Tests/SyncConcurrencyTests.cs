using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Flashnote.Services.Sync;

namespace Flashnote_MAUI.Tests
{
    public class SyncConcurrencyTests
    {
        [Fact]
        public async Task RunBoundedAsync_ProcessesAllItems()
        {
            var items = Enumerable.Range(1, 20).ToList();
            var processed = new System.Collections.Concurrent.ConcurrentBag<int>();

            await SyncConcurrency.RunBoundedAsync(items, maxConcurrency: 4, async item =>
            {
                await Task.Delay(1);
                processed.Add(item);
            });

            Assert.Equal(items.Count, processed.Count);
            Assert.Equal(items.OrderBy(x => x), processed.OrderBy(x => x));
        }

        [Fact]
        public async Task RunBoundedAsync_NeverExceedsMaxConcurrency()
        {
            const int maxConcurrency = 3;
            var current = 0;
            var maxObserved = 0;
            var lockObj = new object();

            await SyncConcurrency.RunBoundedAsync(Enumerable.Range(1, 30), maxConcurrency, async _ =>
            {
                lock (lockObj)
                {
                    current++;
                    if (current > maxObserved) maxObserved = current;
                }

                await Task.Delay(10);

                lock (lockObj)
                {
                    current--;
                }
            });

            Assert.True(maxObserved <= maxConcurrency, $"Observed concurrency {maxObserved} exceeded max {maxConcurrency}");
        }

        [Fact]
        public async Task RunBoundedAsync_PropagatesExceptionFromBody()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                SyncConcurrency.RunBoundedAsync(Enumerable.Range(1, 5), 2, item =>
                {
                    if (item == 3) throw new InvalidOperationException("boom");
                    return Task.CompletedTask;
                }));
        }

        [Fact]
        public async Task RunBoundedAsync_InvalidMaxConcurrency_Throws()
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                SyncConcurrency.RunBoundedAsync(Enumerable.Range(1, 3), 0, _ => Task.CompletedTask));
        }

        [Fact]
        public async Task RunBoundedAsync_RespectsCancellation()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                SyncConcurrency.RunBoundedAsync(Enumerable.Range(1, 5), 2, _ => Task.CompletedTask, cts.Token));
        }
    }
}
