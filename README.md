## Features

This plugin helps Rust server owners debug and mitigate issues where electrical entities are not working.

## How to use

If you are seeing IO entities not working (e.g., lights not responding when switches are flipped), install this plugin and run the `iodebug` command. This will print out the size of each IO queue. Since you are here, at least one of these queues will likely be pretty backed up, with hundreds or thousands of items. For each queue that appears backed up, run `iodebug <queue name>` (e.g., `iodebug ElectricLowPriority`) to show the top entities in the queue, as well as detailed information about the top entity. Most likely, the reason the queue is backed up is that one of the entities at the top of the queue (most likely the top one) is somehow getting spam added to the queue faster than the queue can be processed, causing the queue to back up over time.

Once you have identified a suspicious entity, go to the location of that entity with `teleportpos <position>` (e.g., `teleportpos -251.7,76.0,-482.5`), and unplug it with a wire/hose/pipe tool. Alternatively, you can unplug it remotely with `iodebug.unplug <entity net ID>` (e.g., `iodebug.unplug 2537861`). After unplugging it, re-run the `iodebug` command periodically over the next few minutes to confirm that the queue size is going down. It may take minutes or even hours to fully drain a backlogged queue, so the important thing is whether the queue size is going down instead of up. A healthy queue has a size of 0.

Once the queue has begun draining, if it's taking too long to drain, you can try temporarily increasing the following convars to grant the server more performance budget to process the queue faster. Note: Increasing the budget may decrease server FPS, so use with caution, and change it back once the queue reaches 0.

- `ioentity.framebudgetelectrichighpriorityms` (for `ElectricHighPriority` queue)
- `ioentity.framebudgetelectriclowpriorityms` (for `ElectricLowPriority` queue)
- `ioentity.framebudgetfluidms` (for `Fluidic` queue)
- `ioentity.framebudgetkineticms` (for `Kinetic` queue)
- `ioentity.framebudgetgenericms` (for `Generic` queue)
- `ioentity.framebudgetindustrialms` (for `Industrial` queue)

The convar value represents how many milliseconds (ms) per frame can be spent processing the queue. For example, if you are aiming for 30 FPS, you want each frame to be processed within approximately 33ms (1000ms / 30 = 33.33ms), so you would want the IO budget to be much lower than that to allow the server to process everything else not IO related. The default budget size is less than or equal to 1ms for every queue. To start with, try increasing the budget by one ms at a time (e.g., `ioentity.framebudgetelectriclowpriorityms 1.5` then `ioentity.framebudgetelectriclowpriorityms 2.5`).

## Commands

- `iodebug` -- Prints the name and size of each IO queue. If a given queue is empty (size 0), that means electricity/fluid/etc. is probably working fine, for entities that use that queue. If the queue is non-empty, lots of information will be printed.
- `iodebug <queue name>` -- Prints information about a specific queue.
  - `ElectricHighPriority` -- Used by monument electrical entities (i.e., puzzles).
  - `ElectricLowPriority` -- Used by other electrical entities (i.e., player deployables).
  - `Fluidic` -- Used by fluid entities (e.g, water barrels, sprinklers).
  - `Kinetic` -- Used by kinetic entities (e.g., wheel switch, blast door).
  - `Generic` -- Not used.
  - `Industrial` -- Used by industrial entities (e.g, industrial conveyor, industrial crafter).
- `iodebug.unplug <entity net ID>` -- Disconnects any wires/hoses/pipes that are connected to the entities inputs. This is useful if you want to remotely disconnect an entity without visiting it in-game. The entity net ID will be visible in the output of `iodebug <queue name>`.
- `iodebug.reducequeue <queue name>` -- Removes consecutive duplicates from the specified IO queue. For example, if the queue consists of `switch1, timer1, timer1, timer1, switch1, switch2`, this command will reduce the queue to `switch1, timer1, switch1, switch2`. This command is believed to be safe, having no adverse effects on the entities in the queue. This may have no effect if the duplicates in the queue are not consecutive.
- `iodebug.clearqueue <queue name>` -- Clears all entities from the specified IO queue. Caution is advised because this could leave entities that were in the queue in an incorrect state (e.g., not powered when they should have been). This should be used as a last resort if other queue reduction options have been exhausted.

## Example usage

```
> iodebug

Showing the size of each IO queue.
- ElectricHighPriority: 0
- ElectricLowPriority: 1596
- Fluidic: 0
- Kinetic: 0
- Generic: 0
- Industrial: 0
Use iodebug <queue name> (e.g., iodebug ElectricLowPriority) for more info about a specific queue.
```

```
> iodebug ElectricLowPriority

-----Top ElectricLowPriority queue occupants-----
[381 times] electrical.blocker.deployed.prefab @ -251.7,76.0,-482.5
[370 times] sam_site_turret_deployed.prefab @ 195.2,17.7,-143.0
[263 times] electrical.blocker.deployed.prefab @ 675.0,29.4,975.8
[132 times] large.rechargable.battery.deployed.prefab @ 777.2,29.3,881.8
[119 times] autoturret_deployed.prefab @ 157.1,26.3,1167.1

-----Showing top entity-----
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
