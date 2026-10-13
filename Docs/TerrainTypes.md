# Terrain types

Plan for the next ground materials after grass, weeds, sand, mud, gravel, stone and water: dirt, dry grass, a flower meadow, tall grass, wooden planks, plaza tiles, shallow water, an ocean, lava, snow, ice and raised ground. [TerrainBlending.md](TerrainBlending.md) is the renderer they are drawn with and the phases that built it; [Architecture.md](Architecture.md) describes what exists. This file describes what to add and in which order.

Every rule in [Multiplayer.md](Multiplayer.md#rules-for-new-code-starting-now) applies. Ground is scene content every machine loads the same way, so a new material is drawn locally with nothing replicated. A terrain that changes gameplay (damage, slipping) does so on the server, through the systems that already own that gameplay.

## Where we start

- An area's ground is up to 8 layers: a control map stores each cell's layers as the bits of one byte (`TerrainControlMap`, `R8`). Every generator, every area and the area template list the same 7: grass, weeds, sand, mud, gravel, stone and water, in that draw order, which is also the navigation priority.
- A material is a `TerrainMaterial` asset with textures written by its recipe (`TerrainMaterialRecipe` subclasses), a plain tile, a palette and a `TerrainType2D`, plus rules in `TerrainMovement_Default` when it is not walked like grass.
- What a material can already do in the shader: chips and stones from a cell field (gravel, stone), glints and sparkles (water), a swelling rim (water), sinking with a bank (water), swaying weeds (weeds), and a seamless edge over a material of the same look (weeds over grass).
- Generators: the Clearing, the Village Forest and the Lakeside (see [SceneGeneration.md](SceneGeneration.md)).

## Goals

- Every terrain listed above, paintable in any area that lists it, each with its own look, edge rules and movement.
- No area pays for layers it does not use, and no area is limited to 8 materials out of a project that has more.
- Shader features stay general: each new effect is a material setting another material can reuse, not a special case for one material.
- Every terrain a generator can lay is laid by at least one generator, and every terrain is covered by tests like the existing ones (recipe, lookup texture, generator layouts).

## Non-goals (for now)

- Footprints, tracks or any ground that changes while playing (snow you walk into, lava that spreads). Ground stays static scene content.
- Real elevation for gameplay: raised ground is a look, and its edges block movement only where walls would.
- Weather and seasons that switch materials at runtime.
- Water that flows. Shallows and the ocean animate inside a fixed shape, like the pond.

## Decisions

- **Per-area layer sets before new materials.** Each generator lists only the layers its area uses, at most 8, in its own draw order. Most areas need far fewer than the project has: a desert needs no weeds or mud, a town no lava. The renderer already draws whatever list a grid gives it, so this is generator, writer, template and test work, not shader work. Widening the control map to 16 bits stays open (see Open questions) for an area that genuinely needs more than 8.
- **One shared draw order.** Layers keep a project-wide order, so a pair looks the same in every area and pair overrides keep their meaning: each area's list is a subsequence of it. The order, bottom first: grass, dry grass, meadow, weeds, tall grass, snow, dirt, sand, mud, ice, gravel, plaza, stone, shallows, water, ocean, lava, planks. Raised ground is not a layer (see Phase 9).
- **Planks are drawn over water.** A boardwalk or a dock is drawn last, so it covers the water it crosses, and since the top layer decides a cell for navigation, a plank cell over water is walkable.
- **Movement costs stay at or above grass's 10.** The A* heuristic uses the profile's minimum cost as its step cost (see [SceneGeneration.md](SceneGeneration.md#terrain-layers-and-drawing-order)), so nothing is cheaper than grass.

## Terrains

| Terrain | Look | Movement | Shader features | Edges |
|---|---|---|---|---|
| Dirt | packed brown earth, small stones and cracks; trails, farm plots, camps | like grass | none new | fraying tufts of dry grass over grass; a rim over sand and mud |
| Dry grass | the grass recipe in straw yellows and browns | like grass | none new | seamless over grass, like weeds |
| Meadow | grass with clusters of small flowers in a few colours | like grass | sway (flower heads), or glints for petals catching the light | seamless over grass |
| Tall grass | dense tall blades, stronger sway | like grass for now (cover later) | sway with a larger amplitude and margin | a fringe of blades over grass |
| Snow | white with blue shading, soft drifts, sparkles | cost 15 | sparkles only (no glints) | a soft white rim over everything |
| Ice | pale blue sheets with cracks and glints | like grass, slippery later | glints | an outlined edge over snow and water |
| Plaza | regular stone slabs with mortar | like grass | the stone recipe's cell field with a grid of seeds | as stone |
| Planks | boards across the path's direction, nails, gaps | like grass, over water too | a cell field of planks | a dark outline and a shadow on the water |
| Shallows | light water over a visible bed, ripples | cost 20 | glints, a small sink (1 to 2 px) | a soft shore over sand and grass; seamless under water |
| Ocean | the caustic net, deep blues | not walkable | glints, sparkles, rim swell, a deeper sink | a stronger surf rim |
| Lava | glowing cracks over dark crust | not walkable; damage later | glints and sparkles in hot colours, a small sink | a charred rim |
| Raised ground | any material, raised | edges block like walls | a raised bank: the sinking drawn on the lower side | a cliff face on top edges' lower side |

## Phases

Each phase is done when its materials, tiles, palettes, terrain types and movement rules exist, the generators that should lay it do, the tests pass, and [Architecture.md](Architecture.md), [SceneGeneration.md](SceneGeneration.md) and this file are up to date. Tick the boxes as steps land.

### Phase 1: per-area layer sets

- [ ] A project-wide draw order of ground materials (a `TerrainLayerOrder` asset or a list on `TerrainStyle`), and each generator's layers checked against it: a subsequence, at most 8, with a warning or an error otherwise.
- [ ] Generators list only their own layers. The Clearing and the Village Forest drop the ones they never use only if nothing is painted on them; Convert Ground keeps a painted layer an area has even when its generator no longer lists it, and warns.
- [ ] `AreaLayoutBuilder` indexes layers by material or terrain type instead of fixed constants, since an index now depends on the area.
- [ ] The area template holds the full current set, so a new area can be painted with anything, and its generator trims it.
- [ ] The Terrain Brush and `TilePaletteFollower` work with any set, and the palette window only offers palettes for the area's layers (or says when a palette's material is not one of them).
- [ ] Tests: generators' lists in the shared order, an area without a layer still drawing and navigating, Convert Ground adding and keeping layers.

### Phase 2: dirt, dry grass, plaza

The materials that need nothing new in the shader.

- [ ] `Dirt` from a dirt recipe (the mud recipe's ground without puddles, with cracks and stones), edges over grass with a fringe of dry blades, over sand and mud with a rim.
- [ ] `DryGrass` from the grass recipe in straw colours, seamless over grass.
- [ ] `Plaza` from the stone recipe on a regular grid of seeds (no jitter), with larger slabs and straighter mortar.
- [ ] Generators: dirt along the Village Forest's trails near the village and around a campsite in the Clearing; the plaza for the village square; dry grass in patches of the Lakeside's open grass.

### Phase 3: shallows

- [ ] `Shallows`: a light water texture over a sandy bed (the highlights recipe with a bed tone and fewer strokes), sunken 1 to 2 px, glinting, cost 20.
- [ ] Edges: a soft shore over grass and sand, seamless under water so a lake fades from shallows into deep water.
- [ ] The Lakeside rings its lake with shallows on the beach side and lays a ford where the trail meets the water; a test checks the ford is walkable and the water is not.

### Phase 4: planks

- [ ] A planks recipe: a cell field of boards (like stone's), oriented across the plank layer's long direction or in two fixed orientations.
- [ ] Drawn last, over water: an outline and a shadow cast on the water; a test checks a plank cell over water is walkable.
- [ ] The Lakeside lays a dock from the beach into the lake.

### Phase 5: meadow and tall grass

- [ ] `Meadow`: grass with flower clusters, the flower heads swaying, seamless over grass.
- [ ] `TallGrass`: dense blades with a larger sway amplitude (the recipe's sway margin grows with it), a fringe of blades over grass.
- [ ] Shader: check that a larger amplitude keeps boxes inside cell columns; lift `MaxSwayAmplitude` if needed.
- [ ] Generators: meadow clearings in the Village Forest's clearing region, tall grass at the edges of forests.

### Phase 6: snow and ice

- [ ] Shader: sparkles without glints for a material (snow), already possible by writing only sparkle phases; check and document.
- [ ] `Snow`: white with blue shading and drifts, cost 15, a soft rim over everything.
- [ ] `Ice`: pale sheets with cracks and glints, an outlined edge.
- [ ] A snowy area generator, or a snow variant of the Clearing, using a per-area layer set without grass-only materials.
- [ ] Slipping on ice is gameplay: plan it with [PlayerControls.md](PlayerControls.md) before building it; until then ice walks like grass.

### Phase 7: ocean

- [ ] `Ocean` from `Recipe_WaterCaustics`, sunken deeper than the pond, with a stronger rim swell and surf colours, not walkable.
- [ ] Edges: surf over sand, seamless under water where a coast deepens.
- [ ] A coastal area generator: a beach along one edge of the map giving onto the ocean.

### Phase 8: lava

- [ ] `Lava`: dark crust with glowing cracks, glints and sparkles in hot colours, a small sink, a charred rim, not walkable.
- [ ] Damage from standing next to or in lava is gameplay: plan it with [DamageAndStatusEffects.md](DamageAndStatusEffects.md) (a fire damage type, a burning status) and run it on the server; until then lava only blocks.
- [ ] A volcanic area generator, or lava in a later dungeon generator.

### Phase 9: raised ground

- [ ] Shader: a raised material setting (the sinking mirrored): along edges facing down the screen, the material's own bank colour shows below its edge, as a cliff face over the lower ground, with a shadow cast below it.
- [ ] Raised ground is not one material: any material can be raised. Decide whether a raised variant is a second material asset (`Grass_Raised`) or a per-layer setting on the renderer (see Open questions).
- [ ] The cliff blocks movement: the writer puts wall cells (the collision tilemap) along a raised layer's lower edges, except where a ramp or stairs are laid.
- [ ] A generator that lays a plateau with a ramp.

## Testing

Each phase adds the tests the existing materials have: the recipe's determinism, PNGs and look (`TerrainMaterialRecipeTests`), the lookup texture's new settings (`TerrainBlendLutTests`), the tile and palette (`TerrainTileTests`), the generators' layouts over many seeds (`AreaGenerationTests`), and the template (`AreaTemplateTests`). Shader effects are checked by eye with `TerrainBlendProbe` captures at fixed times, in both looks, and noted in the phase's notes, as in TerrainBlending.md.

## Open questions

- **More than 8 layers in one area.** Per-area sets should cover every area we plan. If one needs more, the control map can move to two bytes per cell (`RG8` or `R16`): the shader's per-pixel loop over layers grows, so measure it before deciding.
- **Raised variants:** a second material asset per raised material is simple but doubles assets; a per-layer "raised" flag on the renderer keeps one material but makes the same material look different in two layers. To decide in Phase 9.
- **Tall grass as cover:** hiding units in tall grass is gameplay (visibility, targeting) and belongs in its own spec when we want it.
