// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Azure.Devices.Edge.Agent.Diagnostics
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    public interface IMetricsScraper
    {
        IAsyncEnumerable<Metric> ScrapeEndpointsAsync(CancellationToken cancellationToken);
    }
}
