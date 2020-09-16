// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Azure.Devices.Edge.Agent.Diagnostics.Storage
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Devices.Edge.Util;
    using Newtonsoft.Json;

    public sealed class MetricsFileStorage : IMetricsStorage
    {
        readonly string directory;
        readonly ISystemTime systemTime;
        readonly List<string> filesToDelete = new List<string>();

        public MetricsFileStorage(string directory, ISystemTime systemTime = null)
        {
            this.directory = Preconditions.CheckNonWhiteSpace(directory, nameof(directory));
            this.systemTime = systemTime ?? SystemTime.Instance;
        }

        public Task StoreMetricsAsync(IAsyncEnumerable<Metric> metrics, CancellationToken cancellationToken)
        {
            return this.WriteData(JsonConvert.SerializeObject(metrics.ToListAsync(cancellationToken)), cancellationToken);
        }

        public async IAsyncEnumerable<Metric> GetAllMetricsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var files = Directory.GetFiles(this.directory).OrderBy(filename => filename);
            foreach (string filename in files)
            {
                try
                {
                    string rawMetrics = await DiskFile.ReadAllAsync(filename);
                    cancellationToken.ThrowIfCancellationRequested();

                    foreach (Metric metric in JsonConvert.DeserializeObject<Metric[]>(rawMetrics) ?? Enumerable.Empty<Metric>())
                    {
                        yield return metric;
                    }
                }
                finally
                {
                    this.filesToDelete.Add(filename);
                }
            }
        }

        public async Task RemoveAllReturnedMetricsAsync()
        {
            await Task.Yield();
            foreach (string filename in this.filesToDelete)
            {
                File.Delete(filename);
            }

            this.filesToDelete.Clear();
        }

        Task WriteData(string data, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(this.directory);
            string file = Path.Combine(this.directory, this.systemTime.UtcNow.Ticks.ToString());

            cancellationToken.ThrowIfCancellationRequested();
            return DiskFile.WriteAllAsync(file, data);
        }
    }
}
