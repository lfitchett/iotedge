using Microsoft.Azure.Devices.Edge.Agent.Diagnostics.Util;
using Microsoft.Azure.Devices.Edge.Agent.Diagnostics.Util.Aggregation;
using Microsoft.Azure.Devices.Edge.Util.Metrics;
using System;
using System.Linq;
using Microsoft.Azure.Devices.Edge.Util;
using Microsoft.Azure.Devices.Edge.Agent.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace RqtNumbersTesting
{
    class Program
    {
        static void Main(string[] args)
        {
            BasicTest();
            //var frequencies = new double[] { 24, 12, 6, 3, 1, .5, .25 }.Select(TimeSpan.FromHours);
            //GenerateCsv(@"C:\Users\Lee\Downloads\rqt.csv", Enumerable.Range(0, 200), Enumerable.Range(1, 205), frequencies);
        }

        static void BasicTest()
        {
            void PrintStats(string description, Metric[] metrics)
            {
                byte[] serialized = MetricsSerializer.MetricsToBytes(metrics).ToArray();
                byte[] compressed = Compression.CompressToGzip(serialized);
                Console.WriteLine($"{description}\n\tNumber of metrics:\t\t{metrics.Length}\n\tBytes after serialization:\t{serialized.Length}\n\tBytes after compression:\t{compressed.Length}");
            }

            var generator = new MetricsGenerator(5, 6);

            var metrics = generator.GenerateDailyMetrics(TimeSpan.FromHours(1)).ToArray();
            PrintStats("Metrics Scraped", metrics);

            metrics = metricFilter.TransformMetrics(metrics).ToArray();
            PrintStats("After Filtering", metrics);

            metrics = metricAggregator.AggregateMetrics(metrics).ToArray();
            PrintStats("After Aggregating", metrics);
        }

        static void GenerateCsv(string path, IEnumerable<int> numModules, IEnumerable<int> numRoutes, IEnumerable<TimeSpan> scrapeFrequencies)
        {
            using (var file = new StreamWriter(File.Open(path, FileMode.OpenOrCreate)))
            {
                file.WriteLine($"Number of Modules,Number of Routes,Scrape Frequency,Metrics Collected,Metrics after Filtering,Metrics after Aggregating,Serialized Bytes,Compressed Bytes");

                foreach (int modules in numModules)
                {
                    foreach (int routes in numRoutes)
                    {
                        var generator = new MetricsGenerator(modules, routes);

                        foreach (TimeSpan freq in scrapeFrequencies)
                        {
                            Metric[] metrics = generator.GenerateDailyMetrics(freq).ToArray();
                            Metric[] filtered = metricFilter.TransformMetrics(metrics).ToArray();
                            Metric[] aggregated = metricAggregator.AggregateMetrics(filtered).ToArray();
                            byte[] serialized = MetricsSerializer.MetricsToBytes(aggregated).ToArray();
                            byte[] compressed = Compression.CompressToGzip(serialized);

                            file.WriteLine($"{modules},{routes},{freq.TotalHours},{metrics.Length},{filtered.Length},{aggregated.Length},{serialized.Length},{compressed.Length}");
                        }
                    }
                }
            }
        }

        static MetricTransformer metricFilter = new MetricTransformer()
                .AddAllowedTags((MetricsConstants.MsTelemetry, true.ToString()))
                .AddDisallowedTags(
                    ("quantile", "0.1"),
                    ("quantile", "0.5"),
                    ("quantile", "0.99"))
                .AddTagsToRemove(MetricsConstants.MsTelemetry, MetricsConstants.IotHubLabel, MetricsConstants.DeviceIdLabel)
                .AddTagsToModify(
                    ("id", name => name.CreateSha256()),
                    ("module_name", name => name.CreateSha256()),
                    ("to", name => name.CreateSha256()),
                    ("from", name => name.CreateSha256()),
                    ("to_route_input", name => name.CreateSha256()),
                    ("from_route_output", name => name.CreateSha256()));

#pragma warning disable SA1111 // Closing parenthesis should be on line of last parameter
        static MetricAggregator metricAggregator = new MetricAggregator(
            new AggregationTemplate("edgehub_gettwin_total", "id", new Summer()),
            new AggregationTemplate(
                "edgehub_messages_received_total",
                ("route_output", new Summer()),
                ("id", new Summer())
            ),
            new AggregationTemplate(
                "edgehub_messages_sent_total",
                ("from", new Summer()),
                ("to", new Summer()),
                ("from_route_output", new Summer()),
                ("to_route_input", new Summer())
            ),
            new AggregationTemplate(
                new string[]
                {
                        "edgehub_message_size_bytes",
                        "edgehub_message_size_bytes_sum",
                        "edgehub_message_size_bytes_count"
                },
                "id",
                new Averager()),
            new AggregationTemplate(
                new string[]
                {
                        "edgehub_message_process_duration_seconds",
                        "edgehub_message_process_duration_seconds_sum",
                        "edgehub_message_process_duration_seconds_count",
                },
                ("from", new Averager()),
                ("to", new Averager())
            ),
            new AggregationTemplate(
                "edgehub_direct_methods_total",
                ("from", new Summer()),
                ("to", new Summer())
            ),
            new AggregationTemplate("edgehub_queue_length", "endpoint", new Summer()),
            new AggregationTemplate(
                new string[]
                {
                        "edgehub_messages_dropped_total",
                        "edgehub_messages_unack_total",
                },
                ("from", new Summer()),
                ("from_route_output", new Summer())
            ),
            new AggregationTemplate("edgehub_client_connect_failed_total", "id", new Summer())
       );
#pragma warning restore SA1111 // Closing parenthesis should be on line of last parameter
    }
}
