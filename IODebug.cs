using Oxide.Core.Libraries.Covalence;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("IO Debug", "WhiteThunder", "0.4.0")]
    [Description("Helps debugging electrical entities not working.")]
    internal class IODebug : CovalencePlugin
    {
        private const float CheckIntervalSeconds = 10;
        private const int Threshold = 100;

        private void OnServerInitialized()
        {
            timer.Every(CheckIntervalSeconds, () =>
            {
                var queue = IOEntity._processQueue;
                if (queue.Count < Threshold)
                    return;

                var entity = queue.Peek();
                if (entity == null)
                    return;

                // Ignore unowned entities since they are probably at monuments and not the cause.
                if (entity.OwnerID == 0)
                    return;

                for (var inputSlotIndex = 0; inputSlotIndex < entity.inputs.Length; inputSlotIndex++)
                {
                    var inputSlot = entity.inputs[inputSlotIndex];
                    var sourceEntity = inputSlot.connectedTo.Get();
                    if (sourceEntity == null)
                        continue;

                    foreach (var outputSlot in entity.outputs)
                    {
                        var destinationEntity = outputSlot.connectedTo.Get();
                        if (destinationEntity != sourceEntity)
                            continue;

                        entity.UpdateFromInput(0, inputSlotIndex);

                        sourceEntity.outputs[inputSlot.connectedToSlot].Clear();
                        inputSlot.Clear();

                        sourceEntity.SendChangedToRoot(forceUpdate: true);
                        entity.MarkDirtyForceUpdateOutputs();

                        sourceEntity.SendNetworkUpdate();
                        entity.SendNetworkUpdate();

                        var pos = entity.transform.position;
                        LogWarning($"Electrical queue size is {queue.Count}. Disconnected suspicious entity ({entity.name}) at {pos.x:f1},{pos.y:f1},{pos.z:f1}.");
                        timer.Once(3, () => LogWarning($"Electrical queue size is now {queue.Count}."));
                        return;
                    }
                }
            });
        }

        [Command("iodebug")]
        private void CommandIODebug(IPlayer player)
        {
            if (!player.IsServer && !player.IsAdmin)
                return;

            var queue = IOEntity._processQueue;
            if (queue.Count == 0)
            {
                player.Reply($"The IO queue is empty. That means electricity should be working fine.");
                return;
            }

            var report = $"IO Queue Size: {queue.Count}";
            report += $"\nframebudgetms: {IOEntity.framebudgetms}";

            if (queue.Count > 0)
            {
                var nextEntity = queue.Peek();
                if (nextEntity == null)
                    return;

                var pos = nextEntity.transform.position;
                var positionString = $"{pos.x:f1},{pos.y:f1},{pos.z:f1}";

                report += $"\nEntity type: {nextEntity.GetType()}";
                report += $"\nEntity prefab: {nextEntity.PrefabName}";
                report += $"\nEntity net id: {nextEntity.net.ID}";
                report += $"\nEntity position: teleportpos {positionString}";

                report += $"\nEntity inputs: {nextEntity.inputs.Count(x => x.connectedTo.Get() != null)}";
                foreach (var slot in nextEntity.inputs)
                {
                    var ent = slot.connectedTo.Get();
                    if (ent == null)
                        continue;

                    report += $"\n  - {ent.GetType()} :: {ent.net.ID}";
                }

                report += $"\nEntity outputs: {nextEntity.outputs.Count(x => x.connectedTo.Get() != null)}";
                foreach (var slot in nextEntity.outputs)
                {
                    var ent = slot.connectedTo.Get();
                    if (ent == null) continue;
                    report += $"\n  - {ent.GetType()} :: {ent.net.ID}";
                }

                report += $"\nEntity valid: {BaseNetworkableEx.IsValid(nextEntity)}";
                report += $"\nEntity.lastUpdateTime: {nextEntity.lastUpdateTime}";
                report += $"\nIOEntity.responsetime: {IOEntity.responsetime}";
                report += $"\nEntity.lastUpdateBlockedFrame: {nextEntity.lastUpdateBlockedFrame}";
                report += $"\nEntity.HasBlockedUpdatedOutputsThisFrame: {Time.frameCount == nextEntity.lastUpdateBlockedFrame}";
                report += $"\nTime.frameCount: {Time.frameCount}";
            }

            player.Reply(report);
        }
    }
}
