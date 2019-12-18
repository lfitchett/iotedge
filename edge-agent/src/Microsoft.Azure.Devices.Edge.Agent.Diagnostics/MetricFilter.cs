// Copyright (c) Microsoft. All rights reserved.

namespace Microsoft.Azure.Devices.Edge.Agent.Diagnostics
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using Microsoft.Azure.Devices.Edge.Util;
    using Newtonsoft.Json;

    /// <summary>
    /// This class acts as a pass through for a group of metrics.
    /// It will exclude metrics based on a tag whitelist and blacklist.
    /// After filtering, it will add or remove tags.
    /// </summary>
    public class MetricFilter
    {
        Option<List<KeyValuePair<string, string>>> tagsWhitelist = Option.None<List<KeyValuePair<string, string>>>();
        Option<List<KeyValuePair<string, string>>> tagsBlacklist = Option.None<List<KeyValuePair<string, string>>>();
        Option<List<KeyValuePair<string, string>>> tagsToAdd = Option.None<List<KeyValuePair<string, string>>>();
        Option<List<string>> tagsToRemove = Option.None<List<string>>();

        public IEnumerable<Metric> FilterMetrics(IEnumerable<Metric> metrics)
        {
            foreach (Metric metric in metrics)
            {
                Dictionary<string, string> tags = JsonConvert.DeserializeObject<Dictionary<string, string>>(metric.Tags);

                // Skip if the blacklist has any or the whitelist does not contain it.
                if (this.tagsBlacklist.Exists(bl => bl.Any(tags.Contains)) || this.tagsWhitelist.Exists(wl => !wl.Any(tags.Contains)))
                {
                    continue;
                }

                // Add or remove tags if needed.
                if (this.tagsToAdd.HasValue || this.tagsToRemove.HasValue)
                {
                    this.tagsToAdd.ForEach(tta => tta.ForEach(toAdd => tags.Add(toAdd.Key, toAdd.Value)));
                    this.tagsToRemove.ForEach(ttr => ttr.ForEach(toRemove => tags.Remove(toRemove)));

                    string newTags = JsonConvert.SerializeObject(tags);
                    yield return new Metric(metric.TimeGeneratedUtc, metric.Name, metric.Value, newTags);
                }
                else
                {
                    yield return metric;
                }
            }
        }

        public void AddToWhitelist(params KeyValuePair<string, string>[] pairs)
        {
            if (this.tagsWhitelist.HasValue)
            {
                this.tagsWhitelist.ForEach(wl => wl.AddRange(pairs));
            }
            else
            {
                this.tagsWhitelist = Option.Some(new List<KeyValuePair<string, string>>(pairs));
            }
        }

        public void AddToBlacklist(params KeyValuePair<string, string>[] pairs)
        {
            if (this.tagsBlacklist.HasValue)
            {
                this.tagsBlacklist.ForEach(wl => wl.AddRange(pairs));
            }
            else
            {
                this.tagsBlacklist = Option.Some(new List<KeyValuePair<string, string>>(pairs));
            }
        }

        public void AddTagsToAdd(params KeyValuePair<string, string>[] pairs)
        {
            if (this.tagsToAdd.HasValue)
            {
                this.tagsToAdd.ForEach(wl => wl.AddRange(pairs));
            }
            else
            {
                this.tagsToAdd = Option.Some(new List<KeyValuePair<string, string>>(pairs));
            }
        }

        public void AddTagsToRemove(params string[] keys)
        {
            if (this.tagsToRemove.HasValue)
            {
                this.tagsToRemove.ForEach(wl => wl.AddRange(keys));
            }
            else
            {
                this.tagsToRemove = Option.Some(new List<string>(keys));
            }
        }
    }
}
