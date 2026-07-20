using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Flashnote.Services.Sync
{
    /// <summary>
    /// 有界並列実行ヘルパー。iOS版 SyncConcurrency.swift に対応。
    /// </summary>
    public static class SyncConcurrency
    {
        /// <summary>
        /// items を最大 maxConcurrency 件まで同時実行しながら body を適用する。
        /// </summary>
        public static async Task RunBoundedAsync<T>(
            IEnumerable<T> items,
            int maxConcurrency,
            Func<T, Task> body,
            CancellationToken cancellationToken = default)
        {
            if (maxConcurrency < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxConcurrency), "maxConcurrency must be at least 1");
            }

            using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            var tasks = items.Select(async item =>
            {
                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await body(item).ConfigureAwait(false);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
    }
}
