using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;

namespace EmojiWindowEcommerceWorkspaceSketchDemo
{
    internal sealed partial class EcommerceWorkspaceSketchApp
    {
        private string GroupConfigPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "groups.json");
        }

        private IEnumerable<(string GroupName, string DefaultStartUrl)> LoadEffectiveGroupDefinitions()
        {
            PersistedGroupConfig persisted = LoadGroupConfiguration();
            if (persisted?.Groups != null && persisted.Groups.Count > 0)
            {
                foreach (PersistedGroupEntry entry in persisted.Groups)
                {
                    if (string.IsNullOrWhiteSpace(entry?.Name))
                    {
                        continue;
                    }

                    string defaultUrl = NormalizeUrl(entry.DefaultStartUrl);
                    if (string.IsNullOrWhiteSpace(defaultUrl))
                    {
                        if (_groupSeed.TryGetValue(entry.Name, out (string Name, string Domain, string Proxy, string Status, int Score)[] seed) && seed.Length > 0)
                        {
                            defaultUrl = DefaultStartUrl(seed[0].Domain);
                        }
                        else
                        {
                            defaultUrl = DefaultStartUrl("new-env.local");
                        }
                    }

                    yield return (entry.Name, defaultUrl);
                }

                yield break;
            }

            foreach (KeyValuePair<string, (string Name, string Domain, string Proxy, string Status, int Score)[]> group in _groupSeed)
            {
                string defaultUrl = (group.Value != null && group.Value.Length > 0)
                    ? DefaultStartUrl(group.Value[0].Domain)
                    : DefaultStartUrl("new-env.local");
                yield return (group.Key, defaultUrl);
            }
        }

        private PersistedGroupConfig LoadGroupConfiguration()
        {
            string path = GroupConfigPath();
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                using (FileStream stream = File.OpenRead(path))
                {
                    var serializer = new DataContractJsonSerializer(typeof(PersistedGroupConfig));
                    return serializer.ReadObject(stream) as PersistedGroupConfig;
                }
            }
            catch
            {
                return null;
            }
        }

        private void SaveGroupConfiguration()
        {
            string path = GroupConfigPath();
            PersistedGroupConfig config = new PersistedGroupConfig();
            foreach (string groupName in _groupOrder)
            {
                config.Groups.Add(new PersistedGroupEntry
                {
                    Name = groupName,
                    DefaultStartUrl = GetGroupDefaultUrl(groupName, DefaultStartUrl("new-env.local")),
                });
            }

            using (FileStream stream = File.Create(path))
            {
                var serializer = new DataContractJsonSerializer(typeof(PersistedGroupConfig));
                serializer.WriteObject(stream, config);
            }
        }
    }
}
