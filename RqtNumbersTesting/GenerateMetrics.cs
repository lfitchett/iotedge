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

        public MetricsGenerator(int numModules, int numIntermoduleRoutes)
        {
            var moduleIndependentMetrics = new IMetricSource[]
            {
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
                }
            );

            var EhIntermoduleMetrics = Enumerable.Range(1, numModules).SelectMany(i => 
                 new IMetricSource[]
                 {
                    new GenericGuage("edgehub_messages_sent_total", true, 1, 100,
                        ("from", $"module_A_{i}"),
                        ("to", $"module_B_{i}"),
                        ("from_route_output", "A"),
                        ("to_route_input", "B"),
                        ("priority", "1")),

                 }
             );
        }

        public IEnumerable<Metric> GenerateMetrics(int numScrapes)
        {

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
}
