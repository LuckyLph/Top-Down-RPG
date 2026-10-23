# Prop generation

Plan for natural props generated in the editor: trees, bushes, rocks, stumps, logs and plants, drawn by code onto the pixel grid in looks that belong on each of our terrains, written as sprites and prefabs, then placed by the area generators and the world generator. [Architecture.md](Architecture.md) describes what exists today; [SceneGeneration.md](SceneGeneration.md) and [WorldGeneration.md](WorldGeneration.md) are the generators that will place them. Tick off the phase checklist as steps land, and move each settled behaviour into Architecture.md as its code arrives.

Every rule in [Multiplayer.md](Multiplayer.md#rules-for-new-code-starting-now) applies. Props are static scenery in chunk scenes, the same on every machine, so nothing about them is networked; what changes per machine (a tree fading over the local player) is local and visual only.

## Where we start

- **No props.** The borrowed prop art and its 65 prefabs were removed. What stays is the layout every prop follows (`Prop.prefab` and `BlockingProp.prefab`: a root at the prop's base, a `Visuals` child half a unit up holding the sprite and a `YPositionSorter` sorting at the root; a blocking prop's root on `Obstacles` with a solid `BoxCollider2D` footprint), the Prop Placement window (a palette of the prefabs under `Assets/Prefabs/Props/<category>`), `PropGridSnapping` (a footprint centred on the fewest whole cells that hold it) and `PropPrefabTests`, which check every prefab under the props folder.
- **The generators are waiting for props.** Each area generator has scatter layers built for the borrowed props, with counts, spacings and zones but no prefabs (`AreaGenerator.PropScatter`), so they place nothing:
  - the Clearing: a forest edge (160 at a spacing of 2), trees, bushes and rocks, plants;
  - the Village Forest: village trees, bushes and flowers; clearing trees, bushes and rocks and plants; forest undergrowth, forest trees (up to 2000 at 2.2) and the forest floor;
  - the Lakeside and the Coast: a forest edge (280 at 2), trees, shore bushes, bushes and rocks, shore plants, plants;
  - the Snowfield: an edge of pines and dead trees (280), pines, shore rocks by the ice, rocks and stumps, small stumps;
  - the Lava Fields: a scorched edge of twisted dead trees, dark spires, dark boulders and large stumps (280), dead trees, rocks by the lava, spires and boulders, small stumps.
  
  `AreaGenerationTests` runs every step with art-free test props (`TestPropPrefabs`). World generation skips props with a warning and keeps a `GeneratedProps` root and the wilds' forest for later (WorldGeneration.md's Later).
- **Generated art we can build on.** The character baker's `CharacterCanvas` draws depth-tested spheres, capsules, discs, dots and triangles through the game's 3/4 view (pitched 30 degrees), keeps each pixel's material, part and normal, and shades them in four-colour ramps (outline, shadow, middle, light) lit from the top left, with inner lines between parts and an outline around everything. It is tied to the characters' `CharacterMaterial` and `CharacterPart` enums. The terrain recipes draw small glyphs (weed blades, flower heads) straight onto textures. `SpriteSheetFiles` writes sheets keeping slice IDs by name, and the effect and weapon sprite recipes show the recipe, writer and inspector pattern.
- **The terrains** (TerrainTypes.md): grass, dry grass, weeds, meadow, tall grass, snow, dirt, sand, mud, ice, gravel, plaza, stone, shallows, water, ocean, lava, planks and raised ground. Their base colours run from the grass's (35, 114, 40) and tall grass's (30, 80, 26) through dirt (132, 102, 70), sand (212, 184, 126) and mud (104, 80, 56) to snow (228, 234, 244), ice (172, 212, 236) and lava (196, 40, 22).

## Goals

- Natural props for every terrain our areas use, in looks that sit on it: green woods on the grass, shore plants by the water and on the beach, snow-laden pines and snow-capped rocks on the snow, charred trees and dark basalt on the lava fields' scorched earth.
- Generated in the editor from recipes, like the characters, effects and terrain: the same recipe and seed always give the same pixels; tuning a recipe and generating again updates every prop of it everywhere, keeping prefab GUIDs and sprite IDs, so placed props follow.
- Several variants of each prop from one recipe, so a forest of 2000 trees does not repeat.
- Props that read in the game: the same light, palette logic and outlines as the characters, sized against the 46 px rogue, sorted where they stand, blocking exactly the cells their base covers.
- The area generators place them through their scatter layers, and world generation through its writer, so the Overworld gets its forests.
- No per-frame cost for a prop that just stands there, and no hitch from thousands of them in loaded chunks.

## Non-goals (for now)

- Built and crafted props: campfires, seats, fences, lamp posts, fountains, furniture, buildings. Campsites, the Village Forest's square and its cottages stay unlaid until those come (Later).
- Props that change in play: harvesting, cutting, breaking, burning. They would need authority, replication and persistence (Later).
- Animated props: swaying foliage, flickering, glowing. A prop is one still sprite (Later).
- Props on water (lily pads, reeds standing in the shallows): scatter stays on ground a unit can walk (Later).
- Mirrored sprites: the light comes from the top left, so a prop is never flipped to make another variant.
- Seasons or weather switching looks at runtime.

## Decisions

Proposals to confirm; the ones about the look are settled on previews in Phase 0.

| Topic | Decision |
|---|---|
| Art | Drawn by code in the editor through the characters' 3/4 canvas: trunks and branches as tapering capsules, crowns as clusters of leaf clumps (small spheres, so each clump catches the light on its top-left and the crown reads as scalloped masses), rocks as squashed spheres or faceted solids, shaded in four-colour ramps lit from the top left with an outline in each ramp's darkest colour, as the characters are. Small plants (blades, reeds, fern fronds, mushroom caps) are thin capsules, discs and dots on the same canvas. One renderer for everything keeps the light, outlines and pixel density the characters', and gives a prop's look from every side for free if one is ever needed. |
| The canvas | `CharacterCanvas` becomes a canvas of material ids with a table saying which are flat and which are outlined, shared by the characters and the props; the characters' frames come out byte for byte the same (`CharacterRecipeTests` already compares them with the project's sheets). Props add what they need to it: ellipsoids (spheres squashed or stretched along an axis), triangles shaded by their own normal (faceted rock and basalt), per-material surface patterns (bark grooves, stone speckles and cracks, wood rings), and a cover material taken where a surface faces up (snow on pines and rocks, moss, ash). |
| Recipes | Editor-only ScriptableObjects under `Assets/Settings/Props/`, one abstract `PropRecipe` (the prop's name, its category folder, a seed, the variant count, the frame size and base pixel, blocking or walk-through, the palette as a ramp per material, an optional cover) with a kind per shape: `TreeRecipe` (trunk, branching, a crown that is round, conical as a pine, or bare as a dead tree), `BushRecipe`, `RockRecipe` (lumps, flatness, facets, height, so boulders and spires too), `WoodRecipe` (stumps, fallen logs, driftwood) and `PlantRecipe` (ferns, reeds, cattails, beach grass, mushrooms). |
| Looks per terrain | A look is a recipe asset: the same kind with the palette and cover of the terrain it stands on (`Rock_Grey` with moss for the woods, `Rock_Sand` for the beach, `Rock_Snow` capped with snow, `Boulder_Basalt` for the lava fields). Palettes are chosen against each terrain's albedo and previewed on it, never on a blank background. |
| Variants | Each recipe bakes `variantCount` variants (4 by default) from its seed, each its own prefab, so scatter layers, which pick a random prefab per try, mix them. A recipe's count only grows: lowering it is refused while a prefab of a dropped variant exists, since placed props reference it. |
| Output | Through `SpriteSheetFiles`: `Assets/Sprites/Props/<Name>.png`, the variants in one row, sliced into `<Name>_<variant>` pivoted on the base pixel, keeping slice IDs; and `Assets/Prefabs/Props/<Category>/<Name>_<variant>.prefab`, a variant of `Prop` or `BlockingProp`, created once and updated in place afterwards, so its GUID and every placed instance stay. Categories: `Trees`, `Bushes`, `Rocks`, `Wood`, `Plants`, which the Prop Placement window lists. |
| Layout | Props take the units' layout: the sprite pivots on the prop's base pixel (the middle of a trunk's foot, the middle of a rock's base) and `Visuals` sits on the root, where the player's and the Weasel's do since Animation.md's Phase 2. The bases move `Visuals` from half a unit up to the root; nothing uses them yet. Roots, a log's far end or a shadow may then reach below the base, and a prop sorts exactly at its foot. |
| Footprint | Measured from the drawing, not set by hand: the box around what touches the ground (a trunk's foot and root flare, a rock's or bush's lower body), in whole pixels, as the `BlockingProp` collider; `PropGridSnapping` then centres it on the fewest whole cells, as for any prop. Trees, rocks, boulders, spires, stumps, logs and bushes block; plants, small stumps and small stones are walked through. |
| Size | 32 px a unit, as everything; a recipe's sizes are in cells. Starting points against the 46 px (1.45 cells) rogue, settled in Phase 0: a broadleaf tree 3 to 4 cells tall and 2.5 wide on a one-cell trunk, a pine 3.5 to 4.5 tall and 2 wide, a dead tree 2.5 to 3.5, a bush 1 to 1.3, a rock 0.6 to 1.2, a boulder 1.5 to 2, a spire 2 to 3 tall on one cell, a stump 0.7, a log 1.5 to 2 long, plants 0.4 to 1. A forest edge at a spacing of 2 cells then closes into one canopy. |
| Shadows | Settled in Phase 0 among none (as the characters have none), a shadow drawn into the prop's sprite, and a shadow sprite of its own drawn on the ground under every unit; the last is the recommendation if props need one, since a shadow in the sprite sorts with the prop and darkens a unit standing in it from behind. |
| Light | Lit like units (`SpriteLit`) by each world's Global Light 2D; nothing glows. |
| In play | Props never move, so they sort once: a static mode on `YPositionSorter` (applied when enabled and in the editor, no `LateUpdate`), and the prop sheets packed in one Sprite Atlas so a forest draws in few batches. A tall prop hiding the local player fades while it does, on that machine only. Settled and measured in Phase 4. |
| Hand-drawn props | Later: an Aseprite prop recipe reading a file through `AsepriteClipReader` and writing the same sheet and prefabs ([AsepriteCharacters.md](AsepriteCharacters.md)). |

## Natural props per terrain

What each terrain gets, from what the generators' scatter layers already ask for. Names are the recipes'; each bakes several variants.

| Terrain, areas, scatter layers | Props | Blocks |
|---|---|---|
| Grass, weeds, meadow: the Clearing, the Village Forest, the Lakeside, the Coast, the wilds; forest edges, trees, village and clearing trees, forest trees | `Tree_Broadleaf` (round crowns in the grass's greens), `Tree_BroadleafSmall` | yes |
| The same; bushes, undergrowth, village bushes | `Bush_Green`, `Bush_Flowering` (dots of the meadow's flower colours) | yes |
| The same; bushes and rocks, forest floor | `Rock_Grey` (moss cover), `Boulder_Grey`, `Stump_Oak`, `Log_Oak` | yes |
| The same; plants, forest floor, village flowers | `Fern`, `Mushrooms_Brown`, `Mushrooms_Red`, `Stones_Small` | no |
| Mud and the grass by water: the Lakeside's shore; shore bushes, shore plants | `Reeds`, `Cattails`, `Bush_Willow` | bush only |
| Sand: the Coast's and the Lakeside's beaches; shore bushes, shore plants | `BeachGrass`, `Driftwood`, `Rock_Sand` | driftwood and rock |
| Dry grass, tall grass: patches in the Lakeside and the Village Forest | `Bush_Dry` (straw and brown) | yes |
| Snow: the Snowfield; its edge, pines, rocks and stumps, small stumps | `Pine_Snow`, `PineSmall_Snow`, `Tree_DeadSnow`, `Rock_Snow`, `Boulder_Snow`, `Stump_Snow`, `StumpSmall_Snow` (walked through) | yes, small stump no |
| Ice: the Snowfield's shore rocks | `Rock_Snow` (as above); ice shards Later | yes |
| Dirt as scorched earth: the Lava Fields; scorched edge, dead trees, lava rocks, spires and boulders, stumps | `Tree_Charred` (twisted, bare, ash cover), `Spire_Basalt` (faceted columns), `Boulder_Basalt`, `Rock_Basalt`, `Stump_Charred`, `StumpSmall_Charred` (walked through) | yes, small stump no |
| Gravel, stone, plaza, planks, shallows, water, ocean, lava | none: paths and water stay clear, as the generators already keep them | |

## How it works

1. A recipe asset (`Assets/Settings/Props/Tree_Broadleaf.asset`) holds a kind's shape settings, a seed, a variant count, the palette and cover of its look, its frame size and base pixel, and whether it blocks.
2. Its inspector bakes every variant in memory and shows them on a strip of each terrain the look is for (the terrain's own albedo, at the worlds' 0.85 light), beside the rogue's idle frame for scale, at 1x and 3x, with the footprint's cells outlined. Changing a setting bakes again.
3. Baking a variant seeds a `System.Random` from the recipe's seed and the variant's index, builds the shape in world units (trunk, branches, crown clumps; lumps; blades), rasterises it through the 3/4 canvas at 32 px a unit with the base on the base pixel, applies the cover and patterns, shades it in the look's ramps, and measures the footprint from the pixels whose ground contact the shape marks.
4. Generate writes the sheet through `SpriteSheetFiles` (an unchanged sheet is not rewritten) and creates or updates one prefab per variant: a variant of `Prop` or `BlockingProp`, its sprite the variant's slice, its collider the footprint. Nothing that did not change is saved.
5. The area generators' scatter layers list the prefabs for their terrain; generating an area places them as they placed the borrowed ones, and the world generator writes the regions' props and the wilds' forest into the chunk scenes under `GeneratedProps`.
6. In play a prop sorts once at its base, draws from the props' atlas, and fades while it hides the local player.

## Phases

Each phase ships on its own, keeps the game playable offline and hosted, and comes with tests.

### Phase 0: the look

- [ ] Previews baked in the editor from throwaway code (a temporary file in the test assembly, not kept) and composed on the in-game terrain at the worlds' 0.85 light beside the rogue, written to `Recordings/Props/`: a broadleaf tree, a pine with snow, a charred tree, a bush, a grey rock, a basalt spire, a stump, reeds and a fern.
- [ ] Compared and chosen: crown styles (leaf clumps of a few sizes, one smooth mass, clumps with a dithered edge), outline or not and its colour, three or four tones, sizes against the rogue and the cells, the cover's threshold, shadows (none, in the sprite, a sprite on the ground), and a forest edge at a spacing of 2 cells.
- [ ] Decisions above updated with what was chosen, and the notes recorded here.

### Phase 1: the prop baker and the woods' core

- [ ] The canvas shared by characters and props (material ids and their table; ellipsoids, faceted triangles, patterns, cover), the characters' sheets unchanged byte for byte.
- [ ] `PropRecipe` with `TreeRecipe`, `BushRecipe`, `RockRecipe` and `WoodRecipe`; the writer (sheet, prefabs created or updated in place, footprints measured); the bases moved to the units' layout; the inspector with its previews on the terrain and Generate.
- [ ] The woods' looks generated: `Tree_Broadleaf`, `Tree_BroadleafSmall`, `Bush_Green`, `Rock_Grey`, `Boulder_Grey`, `Stump_Oak`.
- [ ] They show in the Prop Placement window and pass `PropPrefabTests`; placed by hand in Testlands, they block their cells and sort with the player walking around them.

### Phase 2: forest floor and shores

- [ ] `PlantRecipe`; `Fern`, `Mushrooms_Brown`, `Mushrooms_Red`, `Stones_Small`, `Bush_Flowering`, `Log_Oak`.
- [ ] Shores: `Reeds`, `Cattails`, `Bush_Willow`, `BeachGrass`, `Driftwood`, `Rock_Sand`, and `Bush_Dry` for dry grass.

### Phase 3: snow and scorched earth

- [ ] Covers in use: snow on the Snowfield's looks, ash on the Lava Fields'.
- [ ] Conical and bare crowns: `Pine_Snow`, `PineSmall_Snow`, `Tree_DeadSnow`, `Tree_Charred`.
- [ ] `Rock_Snow`, `Boulder_Snow`, `Stump_Snow`, `StumpSmall_Snow`; `Spire_Basalt` (faceted), `Boulder_Basalt`, `Rock_Basalt`, `Stump_Charred`, `StumpSmall_Charred`.

### Phase 4: props in play

- [ ] Static sorting: props sort once, with no per-frame work.
- [ ] The props' Sprite Atlas, its batches measured.
- [ ] A tall prop hiding the local player fades while it does, on that machine only (fade or a silhouette, compared in play).
- [ ] Measured in a scene of the Village Forest's 2000 forest trees: frame time and batches against the same scene without props, and no allocation per frame once warm.
- [ ] PlayMode: the player walks behind and in front of a tree and around its trunk; a remote player behind a tree does not fade it on another machine.

### Phase 5: the area generators

- [ ] Every scatter layer of the six generator assets lists the props of its terrain (Natural props per terrain).
- [ ] `AreaGenerationTests` run with the real props over their 20 seeds as they do with the test props; the Scene Generator's map and counts show them; generation times recorded.
- [ ] SceneGeneration.md's "No props for now" replaced by what each generator places.

### Phase 6: props in the world

- [ ] `WorldGenerationWriter` writes each region's props into its chunks under `GeneratedProps`, every footprint inside one chunk, and the chunk's written hash covers them, so props placed or moved by hand are kept as hand work (WorldGeneration.md's Later).
- [ ] The wilds' forest: props on the wilds' grass along the regions' bands of trees and in patches, per chunk on its own stream, as the wilds' patches and mobs are.
- [ ] `WorldValidation` and the Prop Placement window read `GeneratedProps`.
- [ ] The Overworld written with its props; its streaming measured with them loaded.
- [ ] Checked in Multiplayer Play Mode with a host and a client in the Overworld: both see the same props, are blocked by the same footprints, and each sees only its own player fade a tree.

## Testing

- EditMode:
  - **The canvas:** the characters' sheets unchanged byte for byte.
  - **Recipes:** each kind bakes the same pixels for the same seed and different variants for different indices; every variant of every recipe asset keeps its drawing inside its frame and stands on its base pixel.
  - **Footprints:** each measured footprint lies under the drawing's ground contact and within the sprite's width.
  - **Writing:** prefabs are variants of the bases with the units' layout (`PropPrefabTests`, which already checks every prefab under the folder); regenerating keeps every prefab's GUID and every slice's ID and writes nothing when unchanged; lowering a variant count with prefabs left is refused.
  - **Generators and the world writer:** the generators place the real props over their seeds, and the world writer writes and keeps `GeneratedProps`.
- PlayMode:
  - **Sorting and blocking:** the player sorts against a tree and is blocked by its trunk's cells.
  - **Fading:** the fade follows the local player only.
  - **Cost:** a scene full of props allocates nothing per frame once warm.
- Manual: the looks on every terrain in play; Multiplayer Play Mode in the Overworld (Phase 6).

## Later

- Built and crafted props: campfires and log seats (which bring the campsites back), fences, lamp posts, a fountain, furniture, buildings.
- Swaying crowns and plants (a per-pixel sway weight, as the terrain's weeds have).
- Props on water: lily pads, reeds in the shallows; ice shards by the frozen lake.
- Hand-drawn props imported from Aseprite through a prop recipe.
- Props that change in play (cut, broken, harvested), with authority, replication and persistence.
- Glowing props (embers in basalt, luminous mushrooms) with 2D lights.

## Open questions

- Whether a dense forest of GameObjects costs too much once whole regions are loaded. If Phase 4's measurements say so, the forest edge's trees could be drawn by one renderer per chunk instead, sorted per tree.
- Whether props need shadows at all, given that units have none (Phase 0).
- Fade or silhouette for a unit hidden behind a prop (Phase 4).
- Sorting range: `YPositionSorter` writes `-round(y * 100)` into a 16-bit sorting order, which runs out about 327 units from y = 0. The Overworld reaches 256, but a world 16 chunks tall would stop sorting past its sixth chunk row. This is not a prop matter, but a forest in such a world would show it first.
