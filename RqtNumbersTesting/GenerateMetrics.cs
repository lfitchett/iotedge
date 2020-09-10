using Microsoft.Azure.Devices.Edge.Agent.Core;
using Microsoft.Azure.Devices.Edge.Agent.Diagnostics;
using Microsoft.Azure.Devices.Edge.Util.Metrics;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace RqtNumbersTesting
{
    public class MetricsGenerator
    {
        IMetricSource[] metricSources;

        public MetricsGenerator(int numModules, int numRoutes)
        {
            var moduleIndependentMetrics = new IMetricSource[]
            {
                new GenericGuage("edgeAgent_command_latency_seconds", true, 1, 100, ("command", "create")),
                new GenericGuage("edgeAgent_command_latency_seconds", true, 1, 100, ("command", "update")),
                new GenericGuage("edgeAgent_command_latency_seconds", true, 1, 100, ("command", "remove")),
                new GenericGuage("edgeAgent_command_latency_seconds", true, 1, 100, ("command", "start")),
                new GenericGuage("edgeAgent_command_latency_seconds", true, 1, 100, ("command", "stop")),
                new GenericGuage("edgeAgent_command_latency_seconds", true, 1, 100, ("command", "restart")),
                new GenericGuage("edgeAgent_iothub_syncs_total", true, 1, 100),
                new GenericGuage("edgeAgent_unsuccessful_iothub_syncs_total", true, 1, 100),
                new GenericGuage("edgeAgent_deployment_time_seconds", true, 1, 1000000000),
                new GenericGuage("edgeagent_direct_method_invocations_count", true, 1, 100, ("method_name", "Ping")),
                new GenericGuage("edgeagent_direct_method_invocations_count", true, 1, 100, ("method_name", "Restart")),
                new GenericGuage("edgeagent_direct_method_invocations_count", true, 1, 100, ("method_name", "GetModuleLogs")),
                new GenericGuage("edgeagent_direct_method_invocations_count", true, 1, 100, ("method_name", "UploadModuleLogs")),
                new GenericGuage("edgeagent_direct_method_invocations_count", true, 1, 100, ("method_name", "UploadSupportBundle")),
                new GenericGuage("edgeAgent_host_uptime_seconds", true, 1, 1000000000),
                new GenericGuage("edgeAgent_iotedged_uptime_seconds", true, 1, 1000000000),
                new GenericGuage("edgeAgent_available_disk_space_bytes", true, 1, 1000000000, ("disk_name", "sda1"), ("disk_filesystem", "ext4"), ("disk_filetype","SSD")),
                new GenericGuage("edgeAgent_available_disk_space_bytes", true, 1, 1000000000, ("disk_name", "sda15"), ("disk_filesystem", "vfat"), ("disk_filetype","SSD")),
                new GenericGuage("edgeAgent_total_disk_space_bytes", true, 1, 1000000000, ("disk_name", "sda1"), ("disk_filesystem", "ext4"), ("disk_filetype","SSD")),
                new GenericGuage("edgeAgent_total_disk_space_bytes", true, 1, 1000000000, ("disk_name", "sda15"), ("disk_filesystem", "vfat"), ("disk_filetype","SSD")),
                new GenericGuage("edgeAgent_metadata", true, 0, 0, ("edge_agent_version", "1.0.10-rc2.34217022 (029016ef1bf82dec749161d95c6b73aa5ee9baf1)"), ("experimental_features", "{\"Enabled\":false,\"DisableCloudSubscriptions\":false}"), ("host_information","{\"OperatingSystemType\":\"linux\",\"Architecture\":\"x86_64\",\"Version\":\"1.0.10~rc2\",\"ServerVersion\":\"19.03.12+azure\",\"KernelVersion\":\"5.0.0-25-generic\",\"OperatingSystem\":\"Ubuntu 18.04.3 LTS\",\"NumCpus\":6}")),
            };

            // edgehub metrics with cardnality by number of metrics
            var EhModuleDependentMetrics = Enumerable.Range(1, numModules).Select(i => $"module_{i}").SelectMany(moduleName =>
                new IMetricSource[]
                {
                    new GenericGuage("edgehub_gettwin_total", true, 1, 100, ("source", "edgehub"), ("id", $"device1/modules/{moduleName}")),
                    new GenericGuage("edgehub_messages_received_total", true, 1, 100, ("route_output", "output1"), ("id", $"device1/modules/{moduleName}")),
                    new GenericGuage("edgehub_reported_properties_total", true, 1, 100, ("target", "asdfasdf"), ("id", $"device1/modules/{moduleName}")),
                    new GenericHistogram("edgehub_message_size_bytes", true, 1, 100, ("id", $"device1/modules/{moduleName}")),
                    new GenericHistogram("edgehub_gettwin_duration_seconds", true, 1, 100, ("source", "edgehub"), ("id", $"device1/modules/{moduleName}")),
                    new GenericHistogram("edgehub_reported_properties_update_duration_seconds", false, 1, 100, ("target", "edgehub"), ("id", $"device1/modules/{moduleName}")),
                    new GenericGuage("edgehub_offline_count_total", false, 1, 100, ("id", $"device1/modules/{moduleName}")),
                    new GenericHistogram("edgehub_offline_duration_seconds", false, 1, 100, ("id", $"device1/modules/{moduleName}")),
                    new GenericGuage("edgehub_operation_retry_total", false, 1, 100, ("id", $"device1/modules/{moduleName}")),
                    new GenericGuage("edgehub_client_connect_failed_total", false, 1, 100, ("id", $"device1/modules/{moduleName}")),
                }
            );

            // edgeAgent metrics with cardnality number of routes
            var EhRouteMetrics = Enumerable.Range(1, numRoutes).SelectMany(i =>
                 new IMetricSource[]
                 {
                    new GenericGuage("edgehub_messages_sent_total", true, 1, 100,
                        ("from", $"module_A_{i}"),
                        ("to", $"module_B_{i}"),
                        ("from_route_output", "A"),
                        ("to_route_input", "B"),
                        ("priority", "1")),
                    new GenericHistogram("edgehub_message_send_duration_seconds", true, 1, 100,
                        ("from", $"module_A_{i}"),
                        ("to", $"module_B_{i}"),
                        ("from_route_output", "A"),
                        ("to_route_input", "B")),
                    new GenericHistogram("edgehub_message_process_duration_seconds", true, 1, 100,
                        ("from", $"module_A_{i}"),
                        ("to", $"module_B_{i}"),
                        ("priority", "1")),
                    new GenericHistogram("edgehub_direct_method_duration_seconds", false, 1, 100,
                        ("from", $"module_A_{i}"),
                        ("to", $"module_B_{i}")),
                    new GenericGuage("edgehub_direct_methods_total", true, 1, 100,
                        ("from", $"module_A_{i}"),
                        ("to", $"module_B_{i}")),
                    new GenericGuage("edgehub_queue_length", true, 1, 100,
                        ("endpoint", $"module_{i}"),
                        ("priority", "1")),
                    new GenericGuage("edgehub_messages_dropped_total", true, 1, 100,
                        ("from", $"module_A_{i}"),
                        ("from_route_output", "A"),
                        ("reason", "bad")),
                 }
             );

            // edgeAgent metrics with cardnality number of modules
            string[] systemModules = { "$edgeHub", "$edgeAgent" };
            var EaModuleDependentMetrics = Enumerable.Range(1, numModules).Select(i => $"module_{i}")
                .Concat(systemModules)
                .SelectMany(moduleName =>
                {
                    bool isSystem = systemModules.Contains(moduleName);
                    return new IMetricSource[]
                    {
                        new GenericGuage("edgeAgent_total_time_running_correctly_seconds", isSystem, 1, 1000000, ("module_name", moduleName)),
                        new GenericGuage("edgeAgent_total_time_expected_running_seconds", isSystem, 1, 1000000, ("module_name", moduleName)),
                        new GenericGuage("edgeAgent_module_start_total", isSystem, 1, 1000000, ("module_name", moduleName), ("module_version", "")),
                        new GenericGuage("edgeAgent_module_stop_total", isSystem, 1, 1000000, ("module_name", moduleName), ("module_version", "")),
                        new GenericGuage("edgeAgent_used_memory_bytes", isSystem, 1, 1000000, ("module_name", moduleName)),
                        new GenericGuage("edgeAgent_total_memory_bytes", isSystem, 1, 1000000, ("module_name", moduleName)),
                        new GenericHistogram("edgeAgent_used_cpu_percent", isSystem, 1, 1000000, ("module_name", moduleName)),
                        new GenericGuage("edgeAgent_created_pids_total", false, 1, 1000000, ("module_name", moduleName)),
                        new GenericGuage("edgeAgent_total_network_in_bytes", false, 1, 1000000, ("module_name", moduleName)),
                        new GenericGuage("edgeAgent_total_network_out_bytes", false, 1, 1000000, ("module_name", moduleName)),
                        new GenericGuage("edgeAgent_total_disk_read_bytes", false, 1, 1000000, ("module_name", moduleName)),
                        new GenericGuage("edgeAgent_total_disk_write_bytes", false, 1, 1000000, ("module_name", moduleName)),
                    };
                }
            );
        }

        public IEnumerable<Metric> GenerateDailyMetrics(int numScrapes, TimeSpan scrapeFrequency)
        {
            for (DateTime time = DateTime.UtcNow; time < DateTime.UtcNow.AddDays(1); time += scrapeFrequency)
            {
                foreach (IMetricSource metricSource in this.metricSources)
                {
                    foreach (Metric metric in metricSource.GetNext(time))
                    {
                        yield return metric;
                    }
                }
            }

        }
    }

    interface IMetricSource
    {
        IEnumerable<Metric> GetNext(DateTime dateTime);
    }

    class GenericGuage : IMetricSource
    {
        string name;
        int min;
        int max;
        IReadOnlyDictionary<string, string> tags;

        Random random = new Random();

        public GenericGuage(string name, bool msTelemetry, int min, int max, params (string tag, string value)[] tags)
        {
            this.name = name;
            this.min = min;
            this.max = max;

            var tagDict = tags.ToDictionary(t => t.tag, t => t.value);
            tagDict.Add(MetricsConstants.MsTelemetry, msTelemetry.ToString());
            tagDict.Add("iothub", "testhub.fakecustomer.net");
            tagDict.Add("edge_device", "device1");
            tagDict.Add("instance_number", "7da7f05c-8d32-4500-b23a-f0ca59b5294d");

            this.tags = new ReadOnlyDictionary<string, string>(tagDict);
        }

        public IEnumerable<Metric> GetNext(DateTime dateTime)
        {
            return new Metric[] { new Metric(dateTime, this.name, random.Next(min, max), this.tags) };
        }
    }

    class GenericHistogram : IMetricSource
    {
        string name;
        int min;
        int max;
        Dictionary<string, string> tags;

        Random random = new Random();

        public GenericHistogram(string name, bool msTelemetry, int min, int max, params (string tag, string value)[] tags)
        {
            this.name = name;
            this.min = min;
            this.max = max;

            this.tags = tags.ToDictionary(t => t.tag, t => t.value);
            this.tags.Add(MetricsConstants.MsTelemetry, msTelemetry.ToString());
            this.tags.Add("iothub", "testhub.fakecustomer.net");
            this.tags.Add("edge_device", "device1");
            this.tags.Add("instance_number", "7da7f05c-8d32-4500-b23a-f0ca59b5294d");
        }

        public IEnumerable<Metric> GetNext(DateTime dateTime)
        {
            foreach (string quantile in new string[] { "0.1", "0.5", "0.9", "0.99" })
            {
                this.tags["quantile"] = quantile;
                yield return new Metric(dateTime, this.name, random.Next(min, max), new ReadOnlyDictionary<string, string>(this.tags));
            }

            this.tags.Remove("quantile");
            yield return new Metric(dateTime, $"{this.name}_count", random.Next(min, max), new ReadOnlyDictionary<string, string>(this.tags));
            yield return new Metric(dateTime, $"{this.name}_sum", random.Next(min, max), new ReadOnlyDictionary<string, string>(this.tags));
        }
    }
}
