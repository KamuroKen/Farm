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
Growth has four stages: seeds → sprout → growing → ripe. Each transition takes 5 seconds (15 seconds total), after watering. One watering covers a cycle. Seeds are unlimited. There is no harvest counter in the interface.

Water, paths, shores, decorations, obstacles and occupied cells block placement.
Till plays Bunny Scythe; Water plays Bunny WateringCan. Both face the plot and finish in one second. Soil and water update only after the clip finishes. Movement and repeated actions are locked during the clip. During approach, WASD or Esc cancels the command; another action replaces it. Selected seeds are remembered until arrival. Water and solid colliders block paths. If no route exists, the command reports "Can't reach this plot". Plant remains instant.
No saves yet. Leaving Play resets plots and harvest state.

## Configuration

`Gardening` in the scene holds camera, player, tilemap and crop references.
`Assets/Farm/Gardening/Crops` contains each crop's four sprites and growth duration.
The `Interactive soil` tilemap is populated by gameplay; do not paint decorative cells into it.
`Tools → Farm → Set Up Gardening Prototype` reinstalls the scene setup and makes a backup.
