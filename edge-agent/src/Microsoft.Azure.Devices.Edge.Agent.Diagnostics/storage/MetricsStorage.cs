// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Azure.Devices.Edge.Agent.Diagnostics.Storage
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Devices.Edge.Storage;
    using Microsoft.Azure.Devices.Edge.Util;

    public class MetricsStorage : IMetricsStorage
    {
        readonly ISequentialStore<IEnumerable<Metric>> dataStore;
        readonly TimeSpan timeToLive;
        readonly HashSet<long> sequencesReturned = new HashSet<long>();

        public MetricsStorage(ISequentialStore<IEnumerable<Metric>> sequentialStore, TimeSpan timeToLive)
        {
            this.dataStore = Preconditions.CheckNotNull(sequentialStore, nameof(sequentialStore));
            this.timeToLive = Preconditions.CheckNotNull(timeToLive, nameof(timeToLive));
        }

        public async Task StoreMetricsAsync(IAsyncEnumerable<Metric> metrics, CancellationToken cancellationToken)
        {
            await this.dataStore.Append(await metrics.ToListAsync(cancellationToken), cancellationToken);
        }

        public async IAsyncEnumerable<Metric> GetAllMetricsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            HashSet<long> ttlEntriesToRemove = new HashSet<long>();

            foreach ((long offset, IEnumerable<Metric> metrics) in await this.dataStore.GetBatch(0, 1000, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                // This makes the assumption that metrics are stored in batches with matching timestamps.
                // This is the case b/c the metrics are stored immediately after being scraped.
                if (metrics.First().TimeGeneratedUtc < DateTime.Now - this.timeToLive)
                {
                    // TODO: log removed
                    ttlEntriesToRemove.Add(offset);
                }
                else
                {
                    this.sequencesReturned.Add(offset);
                    foreach (var metric in metrics)
                    {
                        yield return metric;
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            // remove metrics past ttl
            await this.RemoveOffsets(ttlEntriesToRemove);
        }

        public Task RemoveAllReturnedMetricsAsync()
        {
            return this.RemoveOffsets(this.sequencesReturned);
        }

        async Task RemoveOffsets(HashSet<long> entriesToRemove)
        {
            bool notLast = false;
            do
            {
                // Only delete entries in entriesToRemove. Also remove from entriesToRemove.
                Task<bool> ShouldRemove(long offset, object _)
                {
                    return Task.FromResult(entriesToRemove.Remove(offset));
                }

                notLast = await this.dataStore.RemoveFirst(ShouldRemove);
            }
            while (notLast);
        }
    }
}
