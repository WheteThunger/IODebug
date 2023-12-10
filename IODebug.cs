using Oxide.Core.Libraries.Covalence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static IOEntity;

namespace Oxide.Plugins
{
    [Info("IO Debug", "WhiteThunder", "0.5.1")]
    [Description("Helps debugging electrical entities not working.")]
    internal class IODebug : CovalencePlugin
    {
        #region Fields

        private const int MaxQueueOccupantsToShow = 5;

        private readonly Dictionary<IOEntity, int> _queueCountByEntity = new Dictionary<IOEntity, int>();

        private Dictionary<QueueType, Queue<IOEntity>> _queues = typeof(IOEntity)
            .GetField("_processQueues", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(null) as Dictionary<QueueType, Queue<IOEntity>>;

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

            QueueType queueType;
            if (!Enum.TryParse(args[0], ignoreCase: true, result: out queueType))
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

            uint netId;
            if (args.Length < 1 || !uint.TryParse(args[0], out netId))
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
            QueueType queueType;
            Queue<IOEntity> queue;
            if (!VerifyPermission(player)
                || !VerifyQueueExists(player, cmd, args, out queueType, out queue)
                || !VerifyQueueNotEmpty(player, queueType, queue))
                return;

            var originalSize = queue.Count;
            RemoveConsecutiveDuplicates(queue);

            var newSize = queue.Count;
            if (originalSize == newSize)
            {
                player.Reply($"We were unable to reduce the {queueType} queue to due to no consecutive duplicates.");
                return;
            }

            player.Reply($"{queueType} queue reduced from {originalSize} to {newSize}");
        }

        [Command("iodebug.clearqueue")]
        private void CommandClearQueue(IPlayer player, string cmd, string[] args)
        {
            if (!player.IsServer && !player.IsAdmin)
                return;

            QueueType queueType;
            Queue<IOEntity> queue;
            if (!VerifyQueueExists(player, cmd, args, out queueType, out queue)
                || !VerifyQueueNotEmpty(player, queueType, queue))
                return;

            queue.Clear();
            player.Reply($"Queue {queueType} has been cleared. Entities that were in the queue might be in an incorrect state.");
        }

        #endregion

        #region Helpers

        private bool VerifyPermission(IPlayer player)
        {
            return player.IsServer || player.IsAdmin;
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
            Queue<IOEntity> queue;
            return _queues.TryGetValue(queueType, out queue)
                ? queue
                : null;
        }

        private KeyValuePair<IOEntity, int>[] RankQueueMembers(Queue<IOEntity> queue)
        {
            _queueCountByEntity.Clear();

            foreach (var entity in queue)
            {
                int count;
                if (!_queueCountByEntity.TryGetValue(entity, out count))
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

        private string FormatPosition(Vector3 position)
        {
            return $"{position.x:f1},{position.y:f1},{position.z:f1}";
        }

        private string GetIOEntityInfo(IOEntity ioEntity)
        {
            var output = "";

            output += $"\nEntity type: {ioEntity.GetType()}";
            output += $"\nEntity prefab: {ioEntity.PrefabName}";
            output += $"\nEntity net id: {ioEntity.net.ID}";
            output += $"\nEntity position: teleportpos {FormatPosition(ioEntity.transform.position)}";

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

            foreach (var entry in _queues)
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
                        output += $"\n[{entry.Value} times] {entry.Key.ShortPrefabName} @ {FormatPosition(entry.Key.transform.position)}";
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

        private void RemoveConsecutiveDuplicates(Queue<IOEntity> queue)
        {
            var newQueue = queue.ToList();
            IOEntity previousItem = null;
            for (var i = newQueue.Count - 1; i >= 0; i--)
            {
                var item = newQueue[i];
                if (item == previousItem)
                {
                    newQueue.RemoveAt(i);
                    continue;
                }

                previousItem = item;
            }

            queue.Clear();
            foreach (var item in newQueue)
            {
                queue.Enqueue(item);
            }
        }

        #endregion
    }
}
