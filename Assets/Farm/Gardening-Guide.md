# Gardening prototype

Open FarmLevel and press Play.

- Click the small **Build** button or press **B**. Click a green cell to place a plot.
- A compact radial menu opens around the plot. Click any existing plot to reopen it.
- **Till** prepares the soil. **Plant** opens the seed ring; click a packet to plant immediately.
- **Water** starts growth. **Harvest** collects a ripe crop and leaves the soil ready to replant.
- Dim icons are unavailable. Hover an icon to see its name and the reason.
- The center arrow in the seed ring returns to actions. **Esc**, right-click, or clicking outside closes the menu.
- **WASD** moves the character. Building allows 4.5 units. Choosing Till, Plant, Water or Harvest automatically walks to a reachable working position beside the plot.

Six crops: Carrot, Cabbage, Pumpkin, Strawberry, Corn and Wheat.
Growth has four stages: seeds → sprout → growing → ripe. Each transition takes 5 seconds (15 seconds total), after watering. One watering covers a cycle. Each planting uses one seed from your inventory. There is no harvest counter in the interface.

Water, paths, shores, decorations, obstacles and occupied cells block placement.
Till plays Bunny Scythe; Water plays Bunny WateringCan. Both face the plot and finish in one second. Soil and water update only after the clip finishes. Movement and repeated actions are locked during the clip. During approach, WASD or Esc cancels the command; another action replaces it. Selected seeds are remembered until arrival. Water and solid colliders block paths. If no route exists, the command reports "Can't reach this plot". Plant remains instant.
No saves yet. Leaving Play resets plots and harvest state.

## Configuration

`Gardening` in the scene holds camera, player, tilemap and crop references.
`Assets/Farm/Gardening/Crops` contains each crop's four sprites and growth duration.
The `Interactive soil` tilemap is populated by gameplay; do not paint decorative cells into it.
`Tools → Farm → Set Up Gardening Prototype` reinstalls the scene setup and makes a backup.

## Clearing nature

With Build mode off (Esc), click grass, clover, flowers, a small bush or a small/medium blue stone. Click the scythe icon **Clear**. Bunny walks to a free side and plays Scythe; the whole object, including child sprites and colliders, disappears after the animation. WASD or Esc cancels the approach. Cleared ground is available for walking and building if its terrain permits it.

Trees, stumps, large rocks, buildings and player crops are protected. Clearing adds resources to your inventory; there is no regrowth yet. Clearing resets when Play ends.
The FarmClearable component identifies a whole removable object. Existing named scenery is registered automatically inside Farm Environment at runtime; new removable prefabs can carry that component explicitly.

## Inventory

Click the backpack beside Build or press **I** to open the 24-slot inventory. **I**, **Esc**, the cross, or clicking outside closes it. Hover to read item details; drag whole stacks to move, swap or merge them. Stacks hold up to 99. Dropping outside a slot never destroys an item.

A fresh Play session starts with five seeds of each crop. Seeds and harvested produce are separate items. The plot seed ring shows current quantities; empty seed types are disabled. One seed is deducted at actual planting, not while approaching. Cancelling or replacing a command costs nothing. Counts are checked again on arrival.

Harvest adds produce only when the entire reward fits. If inventory is full, the ripe plant remains. Clearing grants one Fiber (grass/clover), Twigs (bush), Stone (rocks/pebbles), Mushrooms, or Flowers. Grass, clover and flowers have a 25% chance to also grant one random crop seed. Each object's reward is rolled once per session and never rerolled by cancelling. The entire reward must fit before the object can disappear. Rewards are rechecked at animation completion.

No saves, selling or crafting yet. Inventory and clearing reset on leaving Play.
Inventory UI artwork is copied from `Assets/Tileset/UI` into `Assets/Farm/Resources/FarmInventory`; Tools → Farm → Import Inventory UI refreshes those copies.
