## Features

- Helps debug electrical entities not working
- Automatically disconnects some bad circuits

## Commands

- `iodebug` -- Prints information about the electrical queue. If the queue is empty, almost no information will be printed. If the queue is non-empty, lots of information will be printed.

Example output:

```
IO Queue Size: 9
framebudgetms: 1
Entity type: ElectricalBlocker
Entity prefab: assets/prefabs/deployable/playerioents/gates/blocker/electrical.blocker.deployed.prefab
Entity net id: 2537861
Entity position: teleportpos -251.7,76.0,-482.5
Entity inputs: 2
  - ElectricalDFlipFlop :: 2538675
  - ElectricalDFlipFlop :: 2538675
Entity outputs: 1
  - ElectricalDFlipFlop :: 2538675
Entity valid: True
Entity.lastUpdateTime: 75695.31
IOEntity.responsetime: 0.1
Entity.lastUpdateBlockedFrame: 4469921
Entity.HasBlockedUpdatedOutputsThisFrame: False
Time.frameCount: 4469922
```

## How to use

If you are seeing electrical entities not working (e.g., not responding when switches are flipped), install this plugin and run the `iodebug` command. This will print out information about the number of the entities in the processing queue, with additional information about the entity that is at the head of the queue.

A healthy queue should only have a few entities in it. If you see hundreds or thousands of entities in the queue, that means something is probably wrong, and the queue size will likely continue to increase. The root cause of such an increase is usually particular circuits that the game doesn't process efficiently. To resolve, you will need to locate the entities which are the problem and unplug or remove them. The output of the `iodebug` command can help you determine which entity is the problem. If a particular entity is the issue, typically repeat runs of the `iodebug` commands will refer to the same entity.

In some cases, the plugin will auto detect the issue and remedy it for you by disconnecting a suspicious entity. This will show up in the logs like the following.

```
[IO Debug] Electrical queue size is 120. Disconnected suspicious entity (assets/prefabs/deployable/playerioents/gates/blocker/electrical.blocker.deployed.prefab) at -253.9,74.8,-477.6.
```

A short time later, an additional message will show up stating the new queue size. If the new queue size is significantly lower than the original queue size, that indicates that automatically disconnecting the suspicious entity has solved the problem, and the queue size will likely continue to decrease.

```
[IO Debug] Electrical queue size is now 79.
```

If the queue size has not significantly decreased in the second message, you will likely have to run the `iodebug` command a few times to identify which entity is the problem, then go disconnect or remove it yourself.
