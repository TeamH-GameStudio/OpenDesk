using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OpenDesk.AgentCreation.Persistence
{
    // File-based persistence for AgentDraftRecord.
    //
    // Default location: Application.persistentDataPath/agents/<id>.json
    // One agent per file — simple to inspect, simple to back up, no migration
    // needed when a single agent is added/removed.
    public sealed class AgentDraftJsonStore
    {
        private readonly string _root;

        public AgentDraftJsonStore(string rootDirectory = null)
        {
            _root = rootDirectory ?? Path.Combine(Application.persistentDataPath, "agents");
        }

        public string RootDirectory => _root;

        public string Save(AgentDraftRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (string.IsNullOrWhiteSpace(record.id)) throw new InvalidOperationException("AgentDraftRecord.id is required.");

            Directory.CreateDirectory(_root);
            var path = Path.Combine(_root, $"{record.id}.json");
            File.WriteAllText(path, JsonUtility.ToJson(record, true));
            return path;
        }

        public AgentDraftRecord Load(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            var path = Path.Combine(_root, $"{id}.json");
            if (!File.Exists(path)) return null;
            return JsonUtility.FromJson<AgentDraftRecord>(File.ReadAllText(path));
        }

        public IReadOnlyList<AgentDraftRecord> LoadAll()
        {
            if (!Directory.Exists(_root)) return Array.Empty<AgentDraftRecord>();
            var list = new List<AgentDraftRecord>();
            foreach (var path in Directory.GetFiles(_root, "*.json"))
            {
                try
                {
                    var record = JsonUtility.FromJson<AgentDraftRecord>(File.ReadAllText(path));
                    if (record != null) list.Add(record);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AgentDraftJsonStore] Skipped corrupt file {path}: {ex.Message}");
                }
            }
            return list;
        }

        public bool Delete(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            var path = Path.Combine(_root, $"{id}.json");
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
    }
}
