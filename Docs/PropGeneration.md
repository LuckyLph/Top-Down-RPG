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

The look, the scope (Non-goals), the layout and the shadows were settled in Phase 0, on previews (its notes say what was compared and why); the rest are proposals, confirmed as their phases land.

| Topic | Decision |
|---|---|
| Art | Drawn by code in the editor through the characters' 3/4 canvas. Trunks and branches are tapering capsules. Broadleaf crowns and bushes are clusters of leaf clumps of three sizes: spheres whose normals lean toward the whole crown's, so each clump catches the light on its top left while the crown is lit as one mass, over a core one tone darker. Pines are five tiers hanging under their apex, with branch tips along each lower edge and needle streaks. Dead and charred trees are bare, twisted and three forks deep. Rocks are faceted (convex solids cut by random planes), and basalt spires are stacked crags. Everything is shaded as the characters are: three tones (shadow, middle, light) and no highlight, at the characters' light thresholds, with an outline in each ramp's darkest colour and inner lines between parts (clumps, tiers, limbs, lumps). Thin parts (twigs, reed blades, fern stems) are unlined, as the bow string is. Small plants (blades, reeds, fern fronds, mushroom caps) are thin capsules, discs, dots and small flattened ellipsoids on the same canvas. One renderer for everything keeps the characters' light, outlines and pixel density, and gives a prop's look from every side for free if one is ever needed. Settled in Phase 0. |
| The canvas | `CharacterCanvas` becomes a canvas of material ids with a table saying which are flat and which are outlined, shared by the characters and the props; the characters' frames come out byte for byte the same (`CharacterRecipeTests` already compares them with the project's sheets). Props add what they need to it, each used by the Phase 0 previews: ellipsoids at any orientation; convex solids (an intersection of half-spaces, each pixel shaded by the face its view ray enters) for faceted rocks, crags and columns, instead of triangles; fields ray-marched along each pixel's view ray, their gradient the normal, for a pine's tiers and a stump with its flared foot and broken top, with a material per pixel from its position and normal (a stump's cut end); a normal blended toward an envelope's (crowns); points under the ground hidden, so rocks, roots and trunks sink into it; each pixel's world position and world normal, kept for covers and patterns; per-material surface patterns (bark grooves, stone speckles, wood rings, needle streaks, basalt joints), read in the world or, since Phase 2, in a frame of their own (a fallen log's grooves along it and rings around its axis), a tone offset (a crown's darker core) and a tone by height (blades); and a cover material taken where the world normal faces up past a threshold, broken up by value noise (snow on pines, rocks and stumps; moss; ash). |
| Recipes | Editor-only ScriptableObjects under `Assets/Settings/Props/`, one abstract `PropRecipe` (the prop's name, its category folder, a seed, the variant count, the frame size and base pixel, blocking or walk-through, the palette as a ramp per material, an optional cover) with a kind per shape: `TreeRecipe` (trunk, branching, a crown that is round, conical as a pine, or bare as a dead tree), `BushRecipe`, `RockRecipe` (lumps, flatness, facets, height, so boulders and spires too), `WoodRecipe` (stumps, fallen logs, driftwood) and `PlantRecipe` (ferns, reeds, cattails, beach grass, mushrooms). Since Phase 2 a plant is one kind with a form (a fern, a tuft of blades or a cluster of mushrooms), a bush can have flowers, hanging strands or twigs, a rock can scatter its lumps as small stones, and wood can lie as a log. Since Phase 3 a tree's form is round, a pine or bare, a rock can be a crag, and rocks, wood and trees share one optional cover. |
| Looks per terrain | A look is a recipe asset: the same kind with the palette and cover of the terrain it stands on (`Rock_Grey` with moss for the woods, `Rock_Sand` for the beach, `Rock_Snow` capped with snow, `Boulder_Basalt` for the lava fields). Palettes are chosen against each terrain's albedo and previewed on it, never on a blank background. Phase 0 chose the first looks' palettes and covers (its notes list the ramps): snow past 0.55 on pines and 0.75 on rocks and stumps, moss past 0.8, ash past 0.7. |
| Variants | Each recipe bakes `variantCount` variants (4 by default) from its seed, each its own prefab, so scatter layers, which pick a random prefab per try, mix them. A recipe's count only grows: lowering it is refused while a prefab of a dropped variant exists, since placed props reference it. |
| Output | Through `SpriteSheetFiles`: `Assets/Sprites/Props/<Name>.png`, the variants in one row over a row of their shadows, sliced into `<Name>_<variant>` pivoted on the base pixel and `<Name>_<variant>_Shadow` pivoted on the shadow frame's base pixel, keeping slice IDs; and `Assets/Prefabs/Props/<Category>/<Name>_<variant>.prefab`, a variant of `Prop` or `BlockingProp`, created once and updated in place afterwards, so its GUID and every placed instance stay. Categories: `Trees`, `Bushes`, `Rocks`, `Wood`, `Plants`, which the Prop Placement window lists. |
| Layout | Props take the units' layout: the sprite pivots on the prop's base pixel (the middle of a trunk's foot, the middle of a rock's base) and `Visuals` sits on the root, where the player's and the Weasel's do since Animation.md's Phase 2. The bases moved `Visuals` from half a unit up to the root in Phase 1 and gained a `Shadow` child drawing the shadow on the ground. Roots, a log's far end or a shadow may then reach below the base (the Phase 0 bakes reach 5 to 8 px below it with their roots and outline), and a prop sorts exactly at its foot. Confirmed in Phase 0. |
| Footprint | Measured from the drawing, not set by hand: the box around what touches the ground (a trunk's foot and root flare, a rock's or bush's lower body), in whole pixels, as the `BlockingProp` collider; `PropGridSnapping` then centres it on the fewest whole cells, as for any prop. Since Phase 1 the box lies evenly about the base (its rows mirrored above it, its columns widened), so a snapped root lands on whole pixels. Trees, rocks, boulders, spires, stumps, logs and bushes block; plants, small stumps and small stones are walked through. |
| Size | 32 px a unit, as everything; a recipe's sizes are in cells, heights on screen from the base. Settled in Phase 0 against the 46 px (1.45 cells) rogue, keeping the plan's starting points: a broadleaf tree 3 to 4 cells tall and 2.5 wide on a one-cell trunk, a pine 3.5 to 4.5 tall and 2 wide, a dead tree 2.5 to 3.5, a bush 1 to 1.3, a rock 0.6 to 1.2 (its lumps included), a boulder 1.5 to 2, a spire 2 to 3 tall on one cell, a stump 0.7, a log 1.5 to 2 long, plants 0.4 to 1. A forest edge at a spacing of 2 cells closes into one canopy in which each tree still reads. |
| Shadows | A shadow sprite of its own, drawn on the ground under every unit: a hard ellipse of black at 35% under the crown or body, shifted a little east, away from the light. A shadow drawn into the prop's sprite sorts with the prop and darkens a unit standing in it behind the prop; with no shadow, trees stand on the grass rather than in it. Units keep having none. Settled in Phase 0; the recipe bakes the shadow with the prop (Phase 1). Plants cast a small one, small stones none (Phase 2). |
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
| Dirt as scorched earth: the Lava Fields; scorched edge, dead trees, lava rocks, spires and boulders, stumps | `Tree_Charred` (twisted, bare, ash cover), `Spire_Basalt` (a stacked crag), `Boulder_Basalt`, `Rock_Basalt`, `Stump_Charred`, `StumpSmall_Charred` (walked through) | yes, small stump no |
| Gravel, stone, plaza, planks, shallows, water, ocean, lava | none: paths and water stay clear, as the generators already keep them | |

## How it works

1. A recipe asset (`Assets/Settings/Props/Tree_Broadleaf.asset`) holds a kind's shape settings, a seed, a variant count, the palette and cover of its look, its frame size and base pixel, and whether it blocks.
2. Its inspector bakes every variant in memory and shows them on a strip of each terrain the look is for (the terrain's own albedo, at the worlds' 0.85 light), beside the rogue's idle frame for scale, at 1x and 3x, with the footprint's cells outlined. Changing a setting bakes again.
3. Baking a variant seeds a `System.Random` from the recipe's seed and the variant's index, builds the shape in world units (trunk, branches, crown clumps; lumps; blades), rasterises it through the 3/4 canvas at 32 px a unit with the base on the base pixel, applies the cover and patterns, shades it in the look's ramps, measures the footprint from the pixels whose ground contact the shape marks, and draws its shadow.
4. Generate writes the sheet through `SpriteSheetFiles` (an unchanged sheet is not rewritten) and creates or updates one prefab per variant: a variant of `Prop` or `BlockingProp`, its sprite the variant's slice, its collider the footprint. Nothing that did not change is saved.
5. The area generators' scatter layers list the prefabs for their terrain; generating an area places them as they placed the borrowed ones, and the world generator writes the regions' props and the wilds' forest into the chunk scenes under `GeneratedProps`.
6. In play a prop sorts once at its base, draws from the props' atlas, and fades while it hides the local player.

## Phases

Each phase ships on its own, keeps the game playable offline and hosted, and comes with tests.

### Phase 0: the look

- [x] Previews baked in the editor from throwaway code (a temporary file in the test assembly, not kept) and composed on the in-game terrain at the worlds' 0.85 light beside the rogue, written to `Recordings/Props/`: a broadleaf tree, a pine with snow, a charred tree, a bush, a grey rock, a basalt spire, a stump, reeds and a fern.
- [x] Compared and chosen: crown styles (leaf clumps of a few sizes, one smooth mass, clumps with a dithered edge), outline or not and its colour, three or four tones, sizes against the rogue and the cells, the cover's threshold, shadows (none, in the sprite, a sprite on the ground), and a forest edge at a spacing of 2 cells.
- [x] Decisions above updated with what was chosen, and the notes recorded here.

Phase 0 notes:
- **How the previews were made.** Throwaway code in the test assembly (`PropLookTemp`, deleted afterwards) baked every preview in memory. It was a copy of the canvas extended as The canvas row lists, at 32 px a unit with the base at the world's origin. Each preview was composed in PIL on its in-game terrain: the material's albedo tiled at one texel a pixel, with terrain and props both at the worlds' 0.85 light, beside the rogue's committed idle frame facing south. Every sheet comes at 1x and at 3x; the 3x has the cells drawn faintly, each footprint and the cells it snaps to. Each prop is shown in two or three variants from different seeds. All of it is in `Recordings/Props/` (each `_1x.png` and `_3x.png`):
  - the chosen looks together: `final_looks`;
  - every subject on its terrain: `q0_subjects`;
  - the comparisons: `q1a_crowns`, `q1b_pines`, `q1c_bare`, `q2_outlines`, `q2b_thin`, `q3_tones`, `q4_sizes`, `q4_edges`, `q5_snow`, `q5_moss_ash`, `q6_shadows`, `q7_rocks`, `q8_palettes_woods` and `q8_palettes_others`.
- **Crowns.** Compared:
  - leaf clumps of a few sizes;
  - one smooth mass (a body and seven lobes, their normals 80% the crown's);
  - clumps with a dithered edge (tones checkered within 0.07 of each threshold, every other edge pixel removed).

  Chosen: clumps. The mass reads as a lollipop at 1x, the dither reads as noise, and nothing else in the game is dithered. How a broadleaf crown is built:
  - an envelope ellipsoid 2.5 cells wide, 2.25 deep and 2.45 tall, its top on screen at the tree's height (2.4 cells of crown over 1.1 of trunk);
  - a core ellipsoid 0.3 cells inside it, one tone darker, which shows between the clumps;
  - 26 clumps of three sizes (0.28, 0.39 and 0.5 cells in radius, ±8%) on the core's surface, only where it faces the camera, each clump its own part so inner lines scallop the crown;
  - each clump's normal blended 45% toward the envelope's, so the crown is lit as one mass from the top left.

  A bush is built the same way, with 12 clumps of 0.15 to 0.27 cells set at 78 to 95% of the core's radius so few stick out. The trunk is a capsule from 0.16 to 0.1 cells in radius, with four root capsules splayed on the ground and three branches into the crown.
- **Pines.** Compared tiers with branch tips and needle streaks, smooth tiers, and seven narrower tiers. Chosen: the first. Smooth tiers look like stacked lampshades, and seven tiers blur together at 1x. How a pine is built:
  - five tiers, each a field: under its apex the radius is R·u^0.6, where u is the depth below the apex over the tier's height, so a tier's top is flat and its edge steep;
  - radii from half the pine's width at the bottom to 0.32 of that at the top; each tier is 1.25 radii tall (the top one 1.6 times pointier), and the tier bottoms are evenly spaced from just above the trunk to under the top tier;
  - each lower edge cut in 7 to 9 teeth whose tips hang 0.14 cells lower and reach 12% further out;
  - radial needle streaks one tone darker; each tier its own part.
- **Bare branching** (the charred tree, and the snow's dead tree later). Compared twisted and three forks deep, gnarled (thicker, two deep), and finer (four deep). Chosen: twisted, three deep. Gnarled reads as broken posts and finer as fuzz at 1x. How a tree is built:
  - each limb is a chain of tapering capsules in segments of 0.16 cells, each segment turned by a random jitter of 0.3 and pulled 35% back toward the limb's aim;
  - the trunk is 0.42 of the height, 0.15 to 0.09 cells in radius, and stays within about 30 degrees of up;
  - each limb forks into two (three at 30%) at 22 to 42 degrees, each fork 0.54 to 0.72 of its length, with a side branch at 22% per segment;
  - twigs under 1.1 px in radius are unlined.
- **Outlines.** Compared:
  - none;
  - the ramp's darkest colour with inner lines;
  - the same without inner lines;
  - one near-black (18, 14, 22) with inner lines.

  Chosen: the ramp's darkest with inner lines, the characters' rule and gap (2.6 px). Without an outline, crowns and bushes melt into the grass along their lower edge. Without inner lines, crowns flatten and a forest's back rows merge into one mass. The single near-black is harsher than the characters' outlines. Thin parts (twigs, reed blades, fern stems) are unlined, as the bow string is: lined, a 1 px twig becomes 3 px of dark.
- **Tones.** Compared three and four tones, each with and without a highlight past 0.86. Chosen: three tones (shadow, middle, light, beside the outline) at the characters' thresholds of 0.28 and 0.64, with no highlight. A fourth tone (under 0.15) barely shows at 1x and runs into the outline, and a highlight puts a gloss on crowns and rocks that nothing else in the game has.
- **Sizes.** The plan's numbers hold. Measured on screen from the base:
  - broadleaf: 3.4 to 3.6 cells tall and 2.7 to 2.8 wide with its clumps and outline, on a footprint of 18 to 23 by 9 to 11 px (one cell);
  - pine: 4.1 by 2.5;
  - charred tree: 2.9 to 3.0;
  - bush: 1.1 by 1.25;
  - crag: 2.5 by 1.1, on one cell;
  - stump: 0.56 by 0.9;
  - fern: 0.45 by 1;
  - reeds: 0.8.

  A rock set 1.1 wide came out 1.5 wide on two cells with its two side lumps, so a rock's size has to count its lumps. Edges of broadleaf trees and of pines, placed at least 2 cells apart in a band 4 cells deep, close into a canopy in which each tree still reads.
- **Covers.** A pixel takes the cover where its world normal's up component, plus value noise (6 per cell, ±0.15 to ±0.2), passes a threshold.
  - Snow: compared 0.45, 0.55 and 0.65 on pines, and 0.65, 0.75 and 0.85 on rocks and stumps. Chosen: 0.55 and 0.75. At 0.45 the pines turn into white blobs on the snow, and at 0.65 they keep hardly any. Rocks need the higher threshold because a rounded top faces up almost everywhere.
  - `Rock_Snow` needs a darker, cooler stone than `Rock_Grey` for its cap to show.
  - Moss: compared none, past 0.8 and past 0.65. Ash: compared none, past 0.7 and past 0.5. Chosen: moss past 0.8 (a cap) and ash past 0.7 (a dusting on the upper side of the limbs). Lower, moss covers half a rock and ash turns the tree grey.
  - Ash on basalt was tried and dropped: the flat tops became pale caps that read as snow.
- **Shadows.** Compared none, a shadow drawn into the prop's sprite, and a shadow sprite of its own on the ground, with the rogue beside, behind and in front of a tree. Chosen: a sprite of its own on the ground, under every unit. A shadow in the sprite sorts with the prop and darkens a unit standing in it behind the prop (the rogue's boots). With no shadow, trees stand on the grass rather than in it. The previews drew a hard ellipse of black at 35%:
  - a half width of 0.42 of the sprite's width (0.4 for pines, 0.26 for bare trees, about half for bushes, rocks and stumps), and a half height of about a third of that;
  - shifted east, away from the light, by about a twelfth of the width.

  Overlapping shadows in a forest darken each other, which looked right.
- **Rocks.** Compared round rocks (squashed spheres with lumps) and faceted rocks (convex solids of 14 random planes leaning up, each cut at 88 to 100% of the ellipsoid's reach, with a flattened top). Chosen: faceted. Round rocks read as eggs, domes or snowballs.
- **Basalt.** Compared columns (hexagonal prisms, the middle one tallest, tops tilted), a single tapered spire, and a stacked crag. Chosen: the crag:
  - three six-sided frustums, 0.42, 0.33 and 0.25 of the height, each about a quarter narrower than the one below and offset a little;
  - their tops tilted, the last one pointed;
  - a smaller spire beside it.

  The single spire reads as a smooth cone. The columns are striking but look like organ pipes at this size; they could become a look of their own.
- **Palettes.** Each was compared three ways on its terrain. All three read at 1x, and none vanishes as the Bandit's first green cloak did. The olive crowns stood out on the grass but looked sickly as a forest; the darkest options (deep blue-green crowns, burnt brown wood, warm dark basalt, straw reeds) sank into their ground. Chosen ramps (outline, shadow, middle, light):

  | Look | Material | Ramp |
  |---|---|---|
  | `Tree_Broadleaf` | leaves | `0f2a22`, `1f4f38`, `33753f`, `6ea24a` (teal-green) |
  | trees and stumps | bark | `1e140e`, `3e2a1c`, `604230`, `8a6644` |
  | `Bush_Green` | leaves | `12301c`, `2a5e34`, `438a3e`, `86b850` (lighter than the trees, so bushes under them separate) |
  | `Rock_Grey` | stone | `26282e`, `5e626c`, `868a92`, `b4b6b4` (neutral; the warm grey compared, `2e2a26`, `6a645c`, `928a7e`, `bcb4a4`, suits `Rock_Sand`) |
  | `Rock_Grey` | moss | `1c3412`, `3e6a24`, `5e8e30`, `92b648` |
  | `Stump_Oak` | cut end | `3a2618`, `9a7048`, `c09464`, `e0bc86` |
  | `Fern` | fronds | `133a18`, `2f7a2c`, `52a03a`, `92c856` (yellow-green, apart from both the grass and the trees) |
  | `Pine_Snow` | needles | `0a1e22`, `173d3d`, `245b4f`, `3f7f62` (blue-green, apart from the broadleaf) |
  | snow covers | snow | `5a6a8c`, `a9b8d6`, `d6e0f0`, `f6f9ff` |
  | `Rock_Snow` | stone | `1c2028`, `464e5e`, `646e80`, `8c96a8` |
  | `Tree_Charred` | wood | `0c0909`, `211b1a`, `3a302d`, `5e504a` (warm charcoal) |
  | `Tree_Charred` | ash | `2c2826`, `5e5854`, `867f78`, `ada59b` |
  | `Spire_Basalt` | basalt | `100e18`, `34303e`, `524c5e`, `7a7488` (purple-grey) |
  | `Reeds` | blades | `2e3a14`, `5c7a2c`, `8ca23c`, `c6c464` (yellow-green) |
- **Bake times.** Props of capsules, ellipsoids and convex solids took 5 to 60 ms each (16 ms median) on a 360 px canvas, most of it shading the whole canvas. Pines took 0.43 to 0.86 s (0.59 s on average) because their five tiers are ray-marched. The 248 previews baked in 25 s, 21 s of it the 35 pines. Phase 1 can make the tiers an analytic surface, or march them in larger steps and refine.
- **Canvas features Phase 1 needs that the plan did not list** (The canvas row and Phase 1's first box now name them): convex solids instead of faceted triangles; ray-marched fields; a material per pixel from position and normal; envelope normals; the ground hidden; world positions and normals per pixel; tone offsets; tones by height. The baker also needs cropping to the drawing with the base pixel kept, and a footprint measured from the pixels of ground-contact materials lower than 0.12 cells.
- **Scope and layout confirmed.** No crafted, changing or animated props for now, so no campsites (Non-goals). Props move to the units' layout (Layout).

### Phase 1: the prop baker and the woods' core

- [x] The canvas shared by characters and props (material ids and their table; ellipsoids, convex solids, ray-marched fields, envelope normals, the ground hidden, world positions and normals, patterns, tone offsets and tones by height, cover), the characters' sheets unchanged byte for byte.
- [x] `PropRecipe` with `TreeRecipe`, `BushRecipe`, `RockRecipe` and `WoodRecipe`; the writer (sheet, shadow sprites, prefabs created or updated in place, footprints measured); the bases moved to the units' layout, with a shadow renderer drawing under the units; the inspector with its previews on the terrain and Generate.
- [x] The woods' looks generated: `Tree_Broadleaf`, `Tree_BroadleafSmall`, `Bush_Green`, `Rock_Grey`, `Boulder_Grey`, `Stump_Oak`.
- [ ] They show in the Prop Placement window and pass `PropPrefabTests`; placed by hand in Testlands, they block their cells and sort with the player walking around them.

Phase 1 notes:
- **The canvas.** `ArtCanvas` (`Assets/Editor/Art/`) took over `CharacterCanvas`'s drawing and shading with material and part ids and a table of `ArtMaterial`s: a `ColourRamp`, flat or not, outlined or not, a pattern, a tone offset, a tone height and a cover. The ramp keeps its four colours (outline, shadow, middle, light), since Phase 0 chose three tones.
  - `CharacterCanvas` is now a subclass with the characters' enums as ids and typed overloads of the calls the painters use, so no painter changed. Its `Shade` builds the table from `CharacterMaterials` and hides nothing under the ground.
  - The character, Weasel, weapon and Aseprite tests (80, among them the byte-for-byte checks against the project's sheets) pass unchanged.
  - Patterns so far: `Bark`, `Speckle` and `Rings`. The pines' needle streaks and the crags' joints come with their looks in Phase 3. The tone by height is in already, for Phase 2's blades.
- **Recipes.** `TreeRecipe` draws the round crown only, and `WoodRecipe` a stump only; conical and bare crowns come in Phase 3, logs and driftwood in Phase 2. The crown and the roots are shared serializable settings (`PropCrown`, `PropRoots`), and every recipe's defaults are its Phase 0 look.
  - `RockRecipe`'s width counts its lumps: a body of 0.42 of the width, and lumps of 0.15 to 0.22 of it set 0.22 to 0.3 of it to either side along the rock's turn.
  - Each lump now lies inside an octagon around its ellipsoid before the random planes cut it. Without it, a side the planes missed left the solid unbounded there, and two of the first rock bakes ran to their frame's edge.
- **Footprints.** Measured as planned, but laid evenly about the base: the rows mirrored from the lowest ground-touching pixel to as far above the base, the columns widened to the farther side.
  - An odd-width box centred on its cells would put the root on a half pixel; this way the root lands on whole pixels.
  - `PropPrefabTests`' rule that a footprint starts at its root became "around the root and under the sprite".
  - `Bush_Green` counts surfaces up to 0.3 cells as touching the ground. At 0.12, one variant's footprint was 12 x 2 px. Bushes 1.2 wide block two cells, one of them a single cell.
- **Shadows.** The shadow sprites lie in the prop's own sheet, in a row under the variants, each in a frame as wide as the variant's and tall enough for the widest shadow, pivoted on the base.
  - The base prefabs gained a `Shadow` child on the root: unsorted, drawing on the `Terrain` sorting layer at order 5, over the ground (0) and under status auras (9), move markers and walls (10). No sorting layer was added.
  - Each look's shadow shares are Phase 0's: the trees' 0.42, the bush's 0.5, the rocks' 0.52, the stump's 0.55.
- **The looks, measured from their assets** (four variants each, previewed on the grass):

  | Look | Tall | Wide | Footprint | Blocks |
  |---|---|---|---|---|
  | `Tree_Broadleaf` | 3.5 to 3.6 | 2.5 to 2.8 | 20 to 26 x 10 to 12 px | 1 cell |
  | `Tree_BroadleafSmall` (2.6 tall, crown 1.8 x 1.7, trunk 0.12, 16 clumps of 0.22 to 0.38) | 2.7 | 1.75 to 2.0 | 16 to 18 x 8 to 10 px | 1 cell |
  | `Bush_Green` | 1.0 to 1.1 | 1.1 to 1.2 | 26 to 38 x 2 to 12 px | 1 or 2 cells |
  | `Rock_Grey` | 0.7 | 0.9 to 1.0 | 26 to 32 x 12 to 14 px | 1 cell |
  | `Boulder_Grey` (1.8 wide, 1.2 tall, three lumps) | 1.2 to 1.3 | 1.3 to 1.7 | 40 to 54 x 20 to 24 px | 2 cells |
  | `Stump_Oak` | 0.5 to 0.56 | 0.84 to 0.91 | 26 to 28 x 12 px | 1 cell |
- **Bake times, in the editor.** Each look's four variants baked in 52 ms (`Tree_Broadleaf`), 22 ms (`Tree_BroadleafSmall`), 9 ms (`Bush_Green`), 27 ms (`Rock_Grey`), 72 ms (`Boulder_Grey`) and 149 ms (`Stump_Oak`, a ray-marched field).
  - A first Generate (sheet, import, slices, four prefabs) took 0.46 to 0.74 s a look.
  - Generating again unchanged took 8 to 146 ms and wrote nothing.
- **Testlands.** Chunk (0, 0) holds 22 of the 24 variants (all but two boulders) on the grass south of the road by the `start` entry, in rows 28 and 33, under its new `Props` root. They were placed through `PropGridSnapping` as the Prop Placement window places, and the window lists all 24 prefabs under Bushes, Rocks, Trees and Wood.
  - On the chunk's navigation grid, each blocks exactly the cells it snapped to.
  - Rendered through the game's pipeline (`TerrainBlendProbe`), with the rogue's idle frame and a `YPositionSorter`, as units sort, standing behind, beside and in front of trees, rocks, boulders and stumps: each sorts by where it stands, and the shadows lie on the ground under the stand-ins.
  - The chunk was saved with all four chunks open. With only its own open, the walls' dual-grid render tiles along its border were written as well.
  - While the editor does not tick, the props keep their prefab's sorting order of 0 until the next editor update. So the scene was saved first, and the render applied the sorters afterwards (Architecture.md, Known gaps: saving after touching a prop's renderer).
  - The PlayMode tests that boot into Testlands and move the player and the mobs around the start chunk (`PlayerControlsPlayModeTests`, `MobPlayModeBehaviorTests`, 24) pass with the props there.
- **Previews.** In `Recordings/Props/` (gitignored):
  - `phase1_woods_1x.png` and `phase1_woods_3x.png`: the six looks on the grass, from their assets, with their shadows;
  - `testlands_render_1x.png`, `testlands_render_3x.png` and `testlands_render_trees_4x.png`: the in-editor render with the stand-ins;
  - `inspector_strip_tree.png`: the inspector's strip of `Tree_Broadleaf`.
- **Still to check by hand,** for the last box: in play in Testlands, walk behind, beside and in front of the trees, and into the bushes, rocks and stumps. Each should block its cells, sort with the player as it walks around it, and draw its shadow under the player.

### Phase 2: forest floor and shores

- [x] `PlantRecipe`; `Fern`, `Mushrooms_Brown`, `Mushrooms_Red`, `Stones_Small`, `Bush_Flowering`, `Log_Oak`.
- [x] Shores: `Reeds`, `Cattails`, `Bush_Willow`, `BeachGrass`, `Driftwood`, `Rock_Sand`, and `Bush_Dry` for dry grass.

Phase 2 notes:
- **Previews.** Baked from the recipe assets and composed as in Phase 1, on each look's terrains at the worlds' 0.85 light beside the rogue, in `Recordings/Props/` (gitignored, each `_1x.png` and `_3x.png`):
  - every look, four variants on each of its preview terrains, with the cells and footprints at 3x: `p2_forest` and `p2_shore`;
  - scenes with the Phase 1 looks: `p2_scene_forest`, `p2_scene_shore`, `p2_scene_beach` and `p2_scene_dry`.

  One round, accepted as drawn. What the first bakes got wrong is in the notes below.
- **Plants.** `PlantRecipe` is one kind with a form (fern, blades or mushrooms) rather than a kind per plant, since the forms share the recipe's colours and differ only in shape.
  - A fern's fronds are spread evenly around the base. Each is an arching curve (a quadratic from the base through a pull to its tip, peaking at its rise) with a thin unlined stem and pairs of flattened ellipsoid leaflets that lean forward, droop and shrink toward the tip, each frond its own part. `Fern` keeps Phase 0's look: 8 fronds reaching 0.36 to 0.5 cells and rising 0.2 to 0.28, with 7 pairs of leaflets from 0.12 long to 0.04.
  - Blades are curves rising from feet scattered over a small disc, leaning out from its middle and arching over by their droop, unlined and toned by height. Cattails add stems 1.1 to 1.25 times the tallest blade, each holding a lined head below its tip; at the blades' height the heads hid among the blade tips. `Reeds`: 13 blades 0.5 to 0.85 tall. `Cattails`: 11 blades up to 1.0 and three heads 0.14 to 0.18 long. `BeachGrass`: 22 blades 0.5 to 0.8 tall, leaning out 0.25 to 0.7 of their height and drooping 0.2. The first beach grass (16 thin blades up to 0.65) read as sprouts.
  - Mushrooms: one stands at the base and the rest around it, the larger caps on the taller stems, each leaning out a little. A cap is a flattened ellipsoid marched as a field; its underside, where it faces down, takes the stems' colours as gills. The first caps were solid ellipsoids on stems as tall as the caps' lower half, and read as brown and red pebbles at 1x. `Mushrooms_Brown`: 2 to 4 caps 0.1 to 0.16 in radius, 0.55 as tall as wide, on stems 0.14 to 0.26. `Mushrooms_Red`: 1 to 3 rounder caps (0.65), with three cream spots each where they face the camera.
  - Plants cast a small shadow (0.35 to 0.5 of their width), so they stand on the ground rather than over it.
- **Bushes.** `BushRecipe` gained flowers, hanging strands, twigs and a leaf pattern, and `PropCrown` a clump stretch (clumps taller than wide).
  - Flowers (`PropFlowers`) are the meadow's: a heart pixel with a petal on each side, in flat colours, on pixels where a clump faces the camera and shows. Each variant picks one of the meadow's four colours. They are lined as the leaves are, in the leaves' outline, so they change no line. `Bush_Flowering` has 13 on a deeper green than `Bush_Green`'s, so they show.
  - Strands (`PropStrands`) are thin unlined curves from the crown's surface facing the camera, bulging out and falling to just above the ground, toned by height. `Bush_Willow` has 48 over a crown sunk only a little into the ground (it sat on it at first, and one variant missed its base pixel), with 12 clumps 1.4 times as tall as wide. Clumps streaked with the `Bark` pattern instead read as a sage bush, not a willow.
  - Twigs (`PropTwigs`) are bare, unlined capsules poking out past the crown, some forking once. `Bush_Dry` has 10, reaching 0.12 to 0.28 cells out of 24 small clumps (0.09 to 0.16) speckled one tone up or down. With 14 smooth clumps and 6 twigs it read as a sponge.
- **Rocks.** A scattered rock's lumps lie apart around a middle stone 1.2 times their size, at 0.5 to 0.85 of half the width. `Stones_Small`: 0.8 wide and 0.28 tall, three stones around the middle one, grey without moss and with no shadow (each stone's outline grounds it). `Rock_Sand`: `Rock_Grey`'s shape, 0.6 tall, in the warm grey Phase 0 kept for it, without moss.
- **Logs.** `WoodRecipe` gained a log form: a field around an axis turned from lying east and west, bowed, tapering from its thick end (either way), its girth swelling and narrowing in knots, its broken ends jagged. Where the surface faces along it past `endFacing`, it is the cut end. A few branch stubs stick out of it.
  - A log straight across shows neither end and reads as a plank, so `Log_Oak` turns up to 0.9 radians either way and one end usually shows.
  - Its grooves run along it and its rings go around its axis: `ArtMaterial` can read its pattern in a frame of its own (an axis through an origin, taking the up axis's place). The canvas's other users and every Phase 1 look are unchanged.
  - `WoodRecipe` can take a cover on its bark and cut end alike, which Phase 3's snow on stumps will use. `Log_Oak`'s moss lies past 0.85, broken by noise of 0.5, in patches; at 0.75 the whole top was moss and the log read as a green plank.
  - `Log_Oak`: 1.6 long, 0.2 in radius, sunk a fifth of it. `Driftwood`: 1.6 long, 0.14 tapering to half, bowed up to 0.12, knotted, with two stubs, bleached, its ends lighter. The first driftwood (0.12 thick, 1.5 long) read as sticks.
- **Palettes** (outline, shadow, middle, light), chosen on their terrains; `Fern`, `Reeds`, `Stones_Small` and `Rock_Sand` use Phase 0's:

  | Look | Material | Ramp |
  |---|---|---|
  | `Mushrooms_Brown` | caps | `2a1a10`, `6a4428`, `946440`, `c09060` |
  | `Mushrooms_Red` | caps | `3a0e0e`, `8a1e1a`, `c03428`, `e8644a`; spots `f0e8d8` |
  | mushrooms | stems and gills | `4a3e30`, `b4a488`, `d4c8aa`, `ece2c8` |
  | `Bush_Flowering` | leaves | `0f2a1c`, `22502e`, `367838`, `6aa446`; flowers the meadow's (petal, heart): `ecead6`/`e8be40`, `f0ce4a`/`c48028`, `e28ab0`/`f6d4e2`, `8c80de`/`ece096` |
  | `Log_Oak` | bark, cut end, moss | `Stump_Oak`'s and `Rock_Grey`'s moss |
  | `Cattails` | blades | `1e3214`, `3e6a26`, `5e8a34`, `9cb454` |
  | `Cattails` | heads | `24140c`, `5a3420`, `7a4a2c`, `9c6a40` |
  | `Bush_Willow` | leaves | `16302a`, `3a5e4a`, `5e8a64`, `9ab890` (sage) |
  | `BeachGrass` | blades | `2c3824`, `5a6e44`, `8a9a5e`, `c0c88c` (sage, apart from the sand) |
  | `Driftwood` | wood | `3a3630`, `7c766a`, `a8a090`, `d2cab6` |
  | `Driftwood` | ends | `4a443a`, `a49c8c`, `c4bcaa`, `e4dccb` |
  | `Bush_Dry` | leaves | `342610`, `6e5628`, `9a7e40`, `c8ac64` (straw) |
  | `Bush_Dry` | twigs | `24180e`, `4a3220`, `6c4c30`, `8e6a48` |
- **The looks, measured from their assets** (four variants each):

  | Look | Previewed on | Tall | Wide | Footprint | Blocks |
  |---|---|---|---|---|---|
  | `Fern` | grass | 0.34 to 0.38 | 0.81 to 0.94 | | no |
  | `Mushrooms_Brown` | grass | 0.28 to 0.41 | 0.41 to 0.62 | | no |
  | `Mushrooms_Red` | grass | 0.28 to 0.38 | 0.28 to 0.44 | | no |
  | `Stones_Small` | grass | 0.28 to 0.31 | 0.75 to 0.88 | | no |
  | `Bush_Flowering` | grass | 0.91 to 1.0 | 1.06 to 1.22 | 32 to 36 x 10 to 14 px | 1 or 2 cells |
  | `Log_Oak` | grass | 0.38 to 0.59 | 1.69 to 1.72 | 52 to 54 x 6 to 12 px | 2 cells |
  | `Reeds` | mud, grass | 0.66 to 0.84 | 0.34 to 0.47 | | no |
  | `Cattails` | mud, grass | 1.09 to 1.16 | 0.5 to 0.53 | | no |
  | `Bush_Willow` | mud, grass | 1.28 to 1.38 | 1.41 to 1.44 | 44 to 46 x 16 to 18 px | 2 cells |
  | `BeachGrass` | sand | 0.78 to 0.84 | 0.97 to 1.19 | | no |
  | `Driftwood` | sand | 0.44 to 0.5 | 1.53 to 1.69 | 48 to 52 x 8 to 12 px | 2 cells |
  | `Rock_Sand` | sand | 0.62 to 0.75 | 0.88 to 1.06 | 26 to 34 x 12 to 14 px | 1 or 2 cells |
  | `Bush_Dry` | dry grass, tall grass | 0.97 to 1.09 | 1.25 to 1.47 | 30 to 34 x 6 to 12 px | 1 or 2 cells |

  Mushrooms and the fern stand lower than the plan's 0.4 to 1 for plants. They are as wide as that, and at 1x they read beside the rogue.
- **Bake times, in the editor.** Each look's four variants bake in 10 to 37 ms, and the logs in 137 to 173 ms, since they are marched fields. A first Generate took 0.36 to 0.54 s a look. Generating again unchanged took 8 to 169 ms and wrote nothing.
- **Phase 1's looks are unchanged.** Generating them again wrote nothing. The new settings default to what they drew: clumps stretched by 1, no flowers, strands or twigs, no scatter, a stump, no cover. Their assets were not rewritten.
- **Not placed yet.** Testlands keeps Phase 1's props. The new prefabs show in the Prop Placement window under Bushes, Plants, Rocks and Wood, and the generators will place them in Phase 5.

### Phase 3: snow and scorched earth

- [x] Covers in use: snow on the Snowfield's looks, ash on the Lava Fields'.
- [x] Conical and bare crowns: `Pine_Snow`, `PineSmall_Snow`, `Tree_DeadSnow`, `Tree_Charred`.
- [x] `Rock_Snow`, `Boulder_Snow`, `Stump_Snow`, `StumpSmall_Snow`; `Spire_Basalt` (a stacked crag), `Boulder_Basalt`, `Rock_Basalt`, `Stump_Charred`, `StumpSmall_Charred`.

Phase 3 notes:
- **Previews.** Baked from the recipe assets and composed as in Phase 2, in `Recordings/Props/` (gitignored, each `_1x.png` and `_3x.png`):
  - every look, four variants on each of its preview terrains: `p3_snow` (snow, and ice for the snow rocks) and `p3_lava` (dirt, as the Lava Fields' scorched earth);
  - scenes: `p3_scene_snow` (a pine edge at a spacing of 2 cells, with dead trees, rocks and stumps) and `p3_scene_lava`.

  One round, accepted as drawn. What the first bakes got wrong is in the notes below.
- **Covers.** One `PropCover` (whether it lies on the prop, its ramp, the facing past which it lies, its noise and the noise's cycles a cell) is shared by rocks, wood and trees. It replaces the rocks' moss settings and Phase 2's wood cover. Their assets were moved onto it, and every Phase 1 and 2 look generated byte for byte the same afterwards.
  - Snow lies past 0.55 on the pines, 0.6 on the dead tree's limbs and 0.75 on rocks and stumps, each with noise of 0.35.
  - Ash lies past 0.7 on the charred tree, a dusting on the limbs' upper sides. On the charred stumps it lies past 0.88 with noise of 0.6: at 0.7 their flat tops turned into pale caps that read as snow, as ash on basalt had in Phase 0. Basalt takes none.
- **Pines.** `TreeRecipe`'s pine form draws Phase 0's tiers (`PropPine`) on a short trunk with roots. Each tier is a field whose radius grows as its depth below the apex to the power 0.6, 1.25 radii tall, the top one 1.6 times taller. The radii run from the lowest tier's down to 0.32 of it, the bottoms are spaced evenly, and 7 to 9 teeth hang 0.14 cells lower and reach 12% further out.
  - The tiers are marched in steps of half a pixel: 0.3 s a pine, 0.12 s a small one (Phase 0's took 0.6 s on a canvas of 360 px in steps of 0.35).
  - With the lowest tier 0.3 cells up and as wide as Phase 0's, its front hid the trunk entirely and reached below the frame: a tier's front edge shows half its radius lower on screen than its bottom. So the lowest tier sits 1 cell up (0.75 on the small pine) and the tiers are 2 cells wide (2.2 with their teeth, against Phase 0's measured 2.5). The trunk and its roots show under them, as in Phase 0's look.
  - Needle streaks (`ArtPattern.Streaks`) darken a third of the needles by one tone. They are value noise on a circle around the up axis, which has no seam where the angle wraps, stretched up, at 6 cycles a radian.
  - `PineSmall_Snow`: 2.8 tall, four tiers 1.5 across, the top one 0.36 of the lowest.
- **Bare trees.** `PropLimbs` grows Phase 0's limbs: segments of 0.16 cells turned by a jitter of 0.3 and pulled 35% back toward their aim, the trunk 0.42 of the height, forks into two (three at 30%) at 22 to 42 degrees and 0.54 to 0.72 as long, three deep, side branches at 22% a segment, and twigs under 1.1 px in radius unlined. Two changes:
  - the trunk leans at most 20 degrees rather than 30: at 30, with the jitter, some trunks looked like they were falling;
  - side branches grow only on a limb's upper 60%: low on the trunk they made a shrub.
- **Crags.** `RockRecipe`'s crag form stacks blocks with 6 flat sides (convex solids). Each block is a quarter narrower than the one under it, its sides lean in by 12%, it sits up to 0.05 cells off the one under it, and its top tilts by up to 0.3. Their heights are in the shares 5, 4 and 3 (Phase 0's 0.42, 0.33 and 0.25), and the last block is pointed. A smaller spire half as tall, a block fewer, stands 0.42 of the width east or west. Its material does not count as touching the ground, so the crag blocks one cell, as in Phase 0. Basalt joints (`ArtPattern.Joints`) are lines across the up axis, 5 a cell, wobbling with noise; only the spire has them, and the basalt rocks and boulders are speckled.
- **Rock forms.** The rock's form became an enum (lumps, scattered, crag) in place of Phase 2's scatter flag, and `Stones_Small` was moved onto it.
- **Stumps.** `Stump_Snow` and `Stump_Charred` take `Stump_Oak`'s shape, the charred one with a more jagged top (0.12). The small stumps are 0.17 in radius and 0.26 tall on three roots, and are walked through.
- **Palettes** (outline, shadow, middle, light) are Phase 0's: needles, snow, `Rock_Snow`'s stone (on `Boulder_Snow` too), the charred wood, ash and basalt. `Stump_Snow` keeps the oak's bark and cut end. Two are new:

  | Look | Material | Ramp |
  |---|---|---|
  | `Tree_DeadSnow` | bark | `1e1814`, `40342c`, `5e4e42`, `847060` (weathered grey-brown) |
  | `Stump_Charred` | cut end | `140e0c`, `2e2420`, `4a3a32`, `6a5446` (charcoal) |
- **The looks, measured from their assets** (four variants each):

  | Look | Previewed on | Tall | Wide | Footprint | Blocks |
  |---|---|---|---|---|---|
  | `Pine_Snow` | snow | 4.12 | 2.22 to 2.25 | 10 to 14 x 4 to 6 px | 1 cell |
  | `PineSmall_Snow` | snow | 2.84 | 1.69 to 1.75 | 8 to 10 x 4 to 6 px | 1 cell |
  | `Tree_DeadSnow` | snow | 2.41 to 3.03 | 1.81 to 2.56 | 20 to 24 x 10 to 12 px | 1 cell |
  | `Rock_Snow` | snow, ice | 0.66 to 0.75 | 0.78 to 0.97 | 24 to 30 x 14 to 16 px | 1 cell |
  | `Boulder_Snow` | snow, ice | 1.16 to 1.34 | 1.19 to 1.53 | 36 to 48 x 22 to 26 px | 2 cells |
  | `Stump_Snow` | snow | 0.5 to 0.56 | 0.84 to 0.91 | 26 to 28 x 12 px | 1 cell |
  | `StumpSmall_Snow` | snow | 0.31 to 0.38 | 0.53 to 0.56 | | no |
  | `Tree_Charred` | dirt | 2.38 to 3.06 | 1.91 to 2.41 | 20 to 24 x 8 to 10 px | 1 cell |
  | `Spire_Basalt` | dirt | 2.53 | 1.09 to 1.12 | 22 to 24 x 10 to 12 px | 1 cell |
  | `Boulder_Basalt` | dirt | 1.16 to 1.19 | 1.59 to 1.69 | 50 to 54 x 22 to 24 px | 2 cells |
  | `Rock_Basalt` | dirt | 0.69 to 0.78 | 0.75 to 1.0 | 22 to 30 x 14 px | 1 cell |
  | `Stump_Charred` | dirt | 0.47 to 0.59 | 0.81 to 0.84 | 24 to 26 x 12 px | 1 cell |
  | `StumpSmall_Charred` | dirt | 0.31 to 0.38 | 0.53 to 0.56 | | no |
- **Bake times, in the editor.** Each look's four variants bake in 26 to 82 ms, the stumps in 52 to 154 ms (fields), and the pines in 0.43 s (small) and 1.2 s. A first Generate took 0.38 to 0.84 s a look, and 1.5 s for `Pine_Snow`. Generating again unchanged took 8 to 1044 ms and wrote nothing.
- **Not placed yet.** The new prefabs show in the Prop Placement window under Trees, Rocks and Wood, and the generators will place them in Phase 5.

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
- Fade or silhouette for a unit hidden behind a prop (Phase 4).
- Sorting range: `YPositionSorter` writes `-round(y * 100)` into a 16-bit sorting order, which runs out about 327 units from y = 0. The Overworld reaches 256, but a world 16 chunks tall would stop sorting past its sixth chunk row. This is not a prop matter, but a forest in such a world would show it first.
