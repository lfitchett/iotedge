// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Azure.Devices.Edge.Agent.Diagnostics.Util.Aggregation
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Akka.Event;
    using Microsoft.Azure.Devices.Edge.Util;
    using Newtonsoft.Json;

    /// <summary>
    /// This class acts as a pass through for a group of metrics.
    /// It will aggregate metrics that share a given tag.
    /// </summary>
    public class MetricAggregator
    {
        AggregationTemplate[] metricsToAggregate;

        public MetricAggregator(params AggregationTemplate[] metricsToAggregate)
        {
            this.metricsToAggregate = metricsToAggregate;
        }

        // Will aggregate all metrics for all aggregtion templates
        public IAsyncEnumerable<Metric> AggregateMetricsAsync(IAsyncEnumerable<Metric> metrics)
        {
            // Aggregate is way overused in this class, but this Aggregate function is from linq
            return this.metricsToAggregate.Aggregate(metrics, this.AggregateMetric);
        }

        // Will aggregate metrics for a single aggregation template
        IAsyncEnumerable<Metric> AggregateMetric(IAsyncEnumerable<Metric> metrics, AggregationTemplate aggregation)
        {
            return aggregation.TagsToAggregate.Aggregate(metrics, (m, tagAggregation) => this.AggregateTag(m, aggregation.TargetMetricNames, tagAggregation.targetTag, tagAggregation.aggregator));
        }

        // Will aggregate metrics for a single tag of a single template
        async IAsyncEnumerable<Metric> AggregateTag(IAsyncEnumerable<Metric> metrics, IEnumerable<string> targetMetricNames, string targetTag, IAggregator aggregator, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var aggregateValues = new DefaultDictionary<AggregateMetric, IAggregator>(_ => aggregator.New());
            await foreach (Metric metric in metrics.WithCancellation(cancellationToken))
            {
                // if metric is the aggregation target and it has a tag that should be aggregated
                if (targetMetricNames.Contains(metric.Name) && metric.Tags.ContainsKey(targetTag))
                {
                    var aggregateMetric = new AggregateMetric(metric, targetTag);
                    aggregateValues[aggregateMetric].PutValue(metric.Value);
                }
                else
                {
                    yield return metric;
                }
            }

            // aggregate all and construct new metrics from result
            foreach (var aggregatePair in aggregateValues)
            {
                double aggregatedValue = aggregatePair.Value.GetAggregate();
                yield return aggregatePair.Key.ToMetric(aggregatedValue);
            }
        }
    }
}
