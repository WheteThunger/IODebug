using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Oxide.Core.Libraries.Covalence;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static IOEntity;

namespace Oxide.Plugins
{
    [Info("IO Debug", "WhiteThunder", "0.6.0")]
    [Description("Helps debugging electrical/fluid/industrial entities not working.")]
    internal class IODebug : CovalencePlugin
    {
        #region Fields

        private const string PermissionUse = "iodebug.use";
        private const int MaxQueueOccupantsToShow = 5;

        private Configuration _config;
        private readonly Dictionary<IOEntity, int> _queueCountByEntity = new();

        #endregion

        #region Hooks

        private void Init()
        {
            permission.RegisterPermission(PermissionUse, this);
        }

        private void OnServerInitialized()
        {
            _config.OnServerInitialized(this);

            var autoUnplugConfig = _config.AutoUnplugConfig;
            if (autoUnplugConfig.Enabled)
            {
                timer.Every(autoUnplugConfig.CheckIntervalSeconds, () =>
                {
                    foreach (var queueType in autoUnplugConfig.MonitoredQueueTypes)
                    {
                        var queue = GetQueue(queueType);
                        if (queue == null || queue.Count < autoUnplugConfig.OverallQueueThreshold)
                            continue;

                        var (ioEntity, count) = RankQueueMembers(queue).FirstOrDefault();
                        if (ioEntity == null)
                            continue;

                        if (count < autoUnplugConfig.OccupancyThreshold)
                            continue;

                        if (UnplugIOEntity(ioEntity))
                        {
                            var logMessage = $"Entity {ioEntity.ShortPrefabName} in {queueType} queue was automatically unplugged at @ {FormatPosition(ioEntity)}";
                            var originalSize = queue.Count;

                            if (HasConsecutiveDuplicatesOrDestroyedEntities(queue))
                            {
                                RemoveConsecutiveDuplicatesAndDestroyedEntities(queue);
                                if (queue.Count != originalSize)
                                {
                                    logMessage += $" {queueType} queue reduced from {originalSize} to {queue.Count}";
                                }
                            }

                            LogWarning(logMessage);
                        }
                    }
                });
            }

            var autoRemoveConfig = _config.AutoRemoveConfig;
            if (autoRemoveConfig.Enabled)
            {
                timer.Every(autoRemoveConfig.CheckIntervalSeconds, () =>
                {
                    foreach (var queueType in autoRemoveConfig.MonitoredQueueTypes)
                    {
                        var queue = GetQueue(queueType);
                        if (queue == null || queue.Count < autoRemoveConfig.OverallQueueThreshold)
                            continue;

                        if (!HasUnpluggedEntities(queue))
                            continue;

                        var originalSize = queue.Count;
                        RemoveUnpluggedEntities(queue);
                        LogWarning($"Removed unplugged entities from {queueType} queue. Reduced size from {originalSize} to {queue.Count}.");
                    }
                });
            }
        }

        #endregion

        #region Commands

        [Command("iodebug")]
        private void CommandIODebug(IPlayer player, string cmd, string[] args)
        {
            if (!VerifyPermission(player))
                return;

            if (args.Length == 0)
            {
                player.Reply(GetOverallReport());
                return;
            }

            if (!Enum.TryParse(args[0], ignoreCase: true, result: out QueueType queueType))
            {
                player.Reply($"Syntax: {cmd} <queue name>");
                return;
            }

            var queue = GetQueue(queueType);
            if (queue == null)
            {
                player.Reply($"Queue {queueType} not found.");
                return;
            }

            player.Reply(GetQueueReport(queueType, queue));
        }

        [Command("iodebug.unplug")]
        private void CommandUnplug(IPlayer player, string cmd, string[] args)
        {
            if (!VerifyPermission(player))
                return;

            if (args.Length < 1 || !uint.TryParse(args[0], out var netId))
            {
                player.Reply($"Syntax: {cmd} <entity net ID>");
                return;
            }

            var ioEntity = BaseNetworkable.serverEntities.Find(new NetworkableId(netId)) as IOEntity;
            if (ioEntity == null || ioEntity.IsDestroyed)
            {
                player.Reply($"No IOEntity found with net ID {netId}.");
                return;
            }

            if (UnplugIOEntity(ioEntity))
            {
                player.Reply($"Unplugged inputs to entity {netId} ({ioEntity.ShortPrefabName}).");
            }
            else
            {
                player.Reply($"Entity {netId} ({ioEntity.ShortPrefabName}) has nothing plugged into its inputs.");
            }
        }

        [Command("iodebug.reducequeue")]
        private void CommandReduceQueue(IPlayer player, string cmd, string[] args)
        {
            if (!VerifyPermission(player)
                || !VerifyQueueExists(player, cmd, args, out var queueType, out var queue)
                || !VerifyQueueNotEmpty(player, queueType, queue))
                return;

            var originalSize = queue.Count;
            RemoveConsecutiveDuplicatesAndDestroyedEntities(queue);

            if (originalSize == queue.Count)
            {
                player.Reply($"We were unable to reduce the {queueType} queue to due to no consecutive duplicates.");
                return;
            }

            player.Reply($"{queueType} queue reduced from {originalSize} to {queue.Count}");
        }

        [Command("iodebug.clearqueue")]
        private void CommandClearQueue(IPlayer player, string cmd, string[] args)
        {
            if (!player.IsServer && !player.IsAdmin)
                return;

            if (!VerifyQueueExists(player, cmd, args, out var queueType, out var queue)
                || !VerifyQueueNotEmpty(player, queueType, queue))
                return;

            queue.Clear();
            player.Reply($"Queue {queueType} has been cleared. Entities that were in the queue might be in an incorrect state.");
        }

        #endregion

        #region Helpers

        private bool VerifyPermission(IPlayer player)
        {
            if (player.IsServer || player.IsAdmin)
                return true;

            if (permission.UserHasPermission(player.Id, PermissionUse))
                return true;

            player.Reply($"You don't have permission to use this command.");
            return false;
        }

        private bool VerifyQueueExists(IPlayer player, string cmd, string[] args, out QueueType queueType, out Queue<IOEntity> queue)
        {
            queue = null;
            queueType = QueueType.Generic;

            if (args.Length < 1 || !Enum.TryParse(args[0], ignoreCase: true, result: out queueType))
            {
                player.Reply($"Syntax: {cmd} <queue name>");
                return false;
            }

            queue = GetQueue(queueType);
            if (queue == null)
            {
                player.Reply($"Queue {queueType} not found.");
                return false;
            }

            return true;
        }

        private bool VerifyQueueNotEmpty(IPlayer player, QueueType queueType, Queue<IOEntity> queue)
        {
            if (queue.Count == 0)
            {
                player.Reply($"Queue {queueType} is currently empty. Nothing to do.");
                return false;
            }

            return true;
        }

        private Queue<IOEntity> GetQueue(QueueType queueType)
        {
            return IOEntity._processQueues.TryGetValue(queueType, out var queue)
                ? queue
                : null;
        }

        private KeyValuePair<IOEntity, int>[] RankQueueMembers(Queue<IOEntity> queue)
        {
            _queueCountByEntity.Clear();

            foreach (var entity in queue)
            {
                if (!_queueCountByEntity.TryGetValue(entity, out var count))
                {
                    count = 0;
                }

                _queueCountByEntity[entity] = ++count;
            }

            if (_queueCountByEntity.Count == 0)
                return Array.Empty<KeyValuePair<IOEntity, int>>();

            var result = _queueCountByEntity
                .OrderByDescending(entry => entry.Value)
                .ToArray();

            _queueCountByEntity.Clear();

            return result;
        }

        private string FormatPosition(IOEntity ioEntity)
        {
            if (ioEntity == null || ioEntity.IsDestroyed)
                return "Destroyed";

            var position = ioEntity.transform.position;
            return $"{position.x:f1},{position.y:f1},{position.z:f1}";
        }

        private string GetIOEntityInfo(IOEntity ioEntity)
        {
            var output = "";

            output += $"\nEntity type: {ioEntity.GetType()}";
            output += $"\nEntity prefab: {ioEntity.PrefabName}";
            if (ioEntity.net != null)
            {
                output += $"\nEntity net id: {ioEntity.net.ID}";
            }

            output += $"\nEntity position: teleportpos {FormatPosition(ioEntity)}";

            output += $"\nEntity inputs: {ioEntity.inputs.Count(x => x.connectedTo.Get() != null)}";
            foreach (var slot in ioEntity.inputs)
            {
                var ent = slot.connectedTo.Get();
                if (ent == null)
                    continue;

                output += $"\n  - {ent.GetType()} :: {ent.net.ID}";
            }

            output += $"\nEntity outputs: {ioEntity.outputs.Count(x => x.connectedTo.Get() != null)}";
            foreach (var slot in ioEntity.outputs)
            {
                var ent = slot.connectedTo.Get();
                if (ent == null) continue;
                output += $"\n  - {ent.GetType()} :: {ent.net.ID}";
            }

            output += $"\nEntity valid: {BaseNetworkableEx.IsValid(ioEntity)}";
            output += $"\nEntity.lastUpdateTime: {ioEntity.lastUpdateTime}";
            output += $"\nIOEntity.responsetime: {IOEntity.responsetime}";
            output += $"\nEntity.lastUpdateBlockedFrame: {ioEntity.lastUpdateBlockedFrame}";
            output += $"\nEntity.HasBlockedUpdatedOutputsThisFrame: {Time.frameCount == ioEntity.lastUpdateBlockedFrame}";

            return output;
        }

        private string GetOverallReport()
        {
            var output = "Showing the size of each IO queue.";

            foreach (var entry in IOEntity._processQueues)
            {
                output += $"\n- {entry.Key}: {entry.Value.Count}";
            }

            output += "\nUse iodebug <queue name> (e.g., iodebug ElectricLowPriority) for more info about a specific queue.";

            return output;
        }

        private string GetQueueReport(QueueType queueType, Queue<IOEntity> queue)
        {
            if (queue.Count == 0)
                return $"The {queueType} queue is empty. That means entities that use that queue should be working fine.";

            var output = $"{queueType} queue size: {queue.Count}";

            if (queue.Count > 0)
            {
                output += $"\n-----Top {queueType} queue occupants-----";

                var topRanked = RankQueueMembers(queue)
                    .Take(MaxQueueOccupantsToShow)
                    .ToArray();

                if (topRanked.Length > 0)
                {
                    foreach (var entry in topRanked)
                    {
                        output += $"\n[{entry.Value} times] {entry.Key.ShortPrefabName} @ {FormatPosition(entry.Key)}";
                    }

                    var topEntity = topRanked.FirstOrDefault().Key;

                    output += $"\n\n-----Showing top entity-----";
                    output += GetIOEntityInfo(topEntity);
                    output += $"\nTime.frameCount: {Time.frameCount}";
                }
            }

            return output;
        }

        private bool UnplugIOEntity(IOEntity ioEntity)
        {
            var didUnplug = false;

            for (var inputSlotIndex = 0; inputSlotIndex < ioEntity.inputs.Length; inputSlotIndex++)
            {
                var inputSlot = ioEntity.inputs[inputSlotIndex];
                var sourceEntity = inputSlot.connectedTo.Get();
                if (sourceEntity == null)
                    continue;

                foreach (var outputSlot in sourceEntity.outputs)
                {
                    var destinationEntity = outputSlot.connectedTo.Get();
                    if (destinationEntity != ioEntity)
                        continue;

                    ioEntity.UpdateFromInput(0, inputSlotIndex);

                    sourceEntity.outputs[inputSlot.connectedToSlot].Clear();
                    inputSlot.Clear();

                    sourceEntity.SendChangedToRoot(forceUpdate: true);
                    ioEntity.MarkDirtyForceUpdateOutputs();

                    sourceEntity.SendNetworkUpdate();
                    ioEntity.SendNetworkUpdate();

                    didUnplug = true;
                }
            }

            return didUnplug;
        }

        private bool HasAnyConnection(IOEntity ioEntity)
        {
            foreach (var slot in ioEntity.inputs)
            {
                if (slot.connectedTo.Get() != null)
                    return true;
            }

            foreach (var slot in ioEntity.outputs)
            {
                if (slot.connectedTo.Get() != null)
                    return true;
            }

            return false;
        }

        private bool HasUnpluggedEntities(Queue<IOEntity> queue)
        {
            foreach (var ioEntity in queue)
            {
                if (!HasAnyConnection(ioEntity))
                    return true;
            }

            return false;
        }

        private void RemoveUnpluggedEntities(Queue<IOEntity> queue)
        {
            var newQueueItems = new List<IOEntity>();

            foreach (var ioEntity in queue)
            {
                if (HasAnyConnection(ioEntity) || !_config.AutoRemoveConfig.CanRemoveEntity(ioEntity))
                {
                    newQueueItems.Add(ioEntity);
                }
            }

            ReplaceQueueItems(queue, newQueueItems);
        }

        private bool HasConsecutiveDuplicatesOrDestroyedEntities(Queue<IOEntity> queue)
        {
            IOEntity previousItem = null;
            foreach (var ioEntity in queue)
            {
                if ((object)previousItem == null)
                {
                    previousItem = ioEntity;
                    continue;
                }

                if (ioEntity == previousItem)
                    return true;
            }

            return false;
        }

        private void RemoveConsecutiveDuplicatesAndDestroyedEntities(Queue<IOEntity> queue)
        {
            var newQueueItems = queue.ToList();

            IOEntity previousItem = null;
            for (var i = newQueueItems.Count - 1; i >= 0; i--)
            {
                var item = newQueueItems[i];
                if (item == null || item.IsDestroyed || item == previousItem)
                {
                    newQueueItems.RemoveAt(i);
                    continue;
                }

                previousItem = item;
            }

            ReplaceQueueItems(queue, newQueueItems);
        }

        private void ReplaceQueueItems(Queue<IOEntity> queue, List<IOEntity> newItems)
        {
            queue.Clear();

            foreach (var item in newItems)
            {
                queue.Enqueue(item);
            }
        }

        #endregion

        #region Configuration

        [JsonObject(MemberSerialization.OptIn)]
        private class BaseMonitorConfig
        {
            [JsonProperty("Enabled", Order = -5)]
            public bool Enabled;

            [JsonProperty("Check interval (seconds)", Order = -4)]
            public float CheckIntervalSeconds = 60;

            [JsonProperty("Monitored queues", ItemConverterType = typeof(StringEnumConverter), Order = -3)]
            public QueueType[] MonitoredQueueTypes = {};

            [JsonProperty("Only check the queue if it has at least this many items", Order = -2)]
            public int OverallQueueThreshold = 100;
        }

        [JsonObject(MemberSerialization.OptIn)]
        private class AutoUnplugConfig : BaseMonitorConfig
        {
            [JsonProperty("Only unplug the top entity if it appears at least this many times in the queue")]
            public int OccupancyThreshold = 10;
        }

        [JsonObject(MemberSerialization.OptIn)]
        private class AutoRemoveConfig : BaseMonitorConfig
        {
            [JsonProperty("Only remove entities with these prefabs")]
            private string[] _prefabStrings =
            {
                "assets/content/props/sentry_scientists/sentry.bandit.static.prefab",
                "assets/content/props/sentry_scientists/sentry.scientist.static.prefab",
            };

            private HashSet<uint> _prefabIds = new();

            public void OnServerInitialized(IODebug plugin)
            {
                if (!Enabled)
                    return;

                foreach (var prefabPath in _prefabStrings)
                {
                    var entityTemplate = GameManager.server.FindPrefab(prefabPath)?.GetComponent<IOEntity>();
                    if (entityTemplate == null)
                    {
                        plugin.LogError($"Prefab {prefabPath} does not exist or does not correspond to an IO Entity.");
                        continue;
                    }

                    _prefabIds.Add(entityTemplate.prefabID);
                }
            }

            public bool CanRemoveEntity(IOEntity ioEntity)
            {
                return _prefabIds.Contains(ioEntity.prefabID);
            }
        }

        [JsonObject(MemberSerialization.OptIn)]
        private class Configuration : BaseConfiguration
        {
            [JsonProperty("Auto unplug top entity in the queue")]
            public AutoUnplugConfig AutoUnplugConfig = new()
            {
                MonitoredQueueTypes = new[] { QueueType.ElectricLowPriority }
            };

            [JsonProperty("Auto remove entities from the queue that do not have any connections")]
            public AutoRemoveConfig AutoRemoveConfig = new()
            {
                MonitoredQueueTypes = new[] { QueueType.ElectricHighPriority }
            };

            public void OnServerInitialized(IODebug plugin)
            {
                AutoRemoveConfig.OnServerInitialized(plugin);
            }
        }

        private Configuration GetDefaultConfig() => new();

        #region Configuration Helpers

        [JsonObject(MemberSerialization.OptIn)]
        private class BaseConfiguration
        {
            private string ToJson() => JsonConvert.SerializeObject(this);

            public Dictionary<string, object> ToDictionary() => JsonHelper.Deserialize(ToJson()) as Dictionary<string, object>;
        }

        private static class JsonHelper
        {
            public static object Deserialize(string json) => ToObject(JToken.Parse(json));

            private static object ToObject(JToken token)
            {
                switch (token.Type)
                {
                    case JTokenType.Object:
                        return token.Children<JProperty>()
                                    .ToDictionary(prop => prop.Name,
                                                  prop => ToObject(prop.Value));

                    case JTokenType.Array:
                        return token.Select(ToObject).ToList();

                    default:
                        return ((JValue)token).Value;
                }
            }
        }

        private bool MaybeUpdateConfig(BaseConfiguration config)
        {
            var currentWithDefaults = config.ToDictionary();
            var currentRaw = Config.ToDictionary(x => x.Key, x => x.Value);
            return MaybeUpdateConfigSection(currentWithDefaults, currentRaw);
        }

        private bool MaybeUpdateConfigSection(Dictionary<string, object> currentWithDefaults, Dictionary<string, object> currentRaw)
        {
            var changed = false;

            foreach (var key in currentWithDefaults.Keys)
            {
                if (currentRaw.TryGetValue(key, out var currentRawValue))
                {
                    var defaultDictValue = currentWithDefaults[key] as Dictionary<string, object>;
                    var currentDictValue = currentRawValue as Dictionary<string, object>;

                    if (defaultDictValue != null)
                    {
                        if (currentDictValue == null)
                        {
                            currentRaw[key] = currentWithDefaults[key];
                            changed = true;
                        }
                        else if (MaybeUpdateConfigSection(defaultDictValue, currentDictValue))
                            changed = true;
                    }
                }
                else
                {
                    currentRaw[key] = currentWithDefaults[key];
                    changed = true;
                }
            }

            return changed;
        }

        protected override void LoadDefaultConfig() => _config = GetDefaultConfig();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null)
                {
                    throw new JsonException();
                }

                if (MaybeUpdateConfig(_config))
                {
                    PrintWarning("Configuration appears to be outdated; updating and saving");
                    SaveConfig();
                }
            }
            catch (Exception e)
            {
                PrintError(e.Message);
                PrintWarning($"Configuration file {Name}.json is invalid; using defaults");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            Puts($"Configuration changes saved to {Name}.json");
            Config.WriteObject(_config, true);
        }

        #endregion

        #endregion
    }
}
