# Lighting gaps

Plan for closing the known gaps the lighting left (Architecture.md's Known gaps), with the ocean's waves along coasts that run north to south, which the user found looking off on 2026-11-03. [Lighting.md](Lighting.md) is the plan these gaps come from, [TerrainTypes.md](TerrainTypes.md) the ocean's. Tick off the phase checklist as steps land, and move each settled behaviour into Architecture.md as its code arrives, taking the gap it closes off the Known gaps.

Every rule in [Multiplayer.md](Multiplayer.md#rules-for-new-code-starting-now) applies. Multiplayer is checked by the PlayMode tests that run in the editor (an in-process host and client); checking by hand with Multiplayer Play Mode or a second machine is out of scope here and comes with a later feature of its own ([Roadmap.md](Roadmap.md)). Everything here is drawn by each machine on its own, as the lighting and the waves are, so nothing new is sent.

## Where we start

- **Waves along a coast running north to south.** The shader measured along a wave with world x: its stretches out at sea, the gaps in its crest and the turn of its front. Along a coast running east to west that is along the waves; along one running north to south it is across them, so there the waves ran as long unbroken streaks with evenly dashed crests, and the turn spread them further apart on the west coast and squeezed them on the east instead of turning them (the Overworld's island has both).
- **Props' shadows fall on the ground alone**: a blocking prop's light shadow darkens only the `Default` and `Terrain` sorting layers, since URP darkens a sprite by its screen pixels, so a unit or a prop standing in a lamp's shadow behind a trunk keeps the lamp's light (Lighting.md's Phase 7).
- **Sun shadows on casters**: a caster darkens whole or not at all, by the art pixel two pixels sunward of its feet, so a unit walking into a shadow darkens in one step; and the props' shadows wait for the sun to move, the view to leave the mask's area or a scene to load, so a prop moved by hand in play mode keeps its old shadow until then (Lighting.md's Phase 4).
- **Light shadows hold still from two screen pixels an art pixel**: the light textures are half the screen's size, so at one screen pixel an art pixel, the smallest window the Pixel Perfect Camera allows, a light's shadow edge flickers by an art pixel as the camera moves (Lighting.md's Phase 7).
- **A lava chunk's load builds its glow map**, about 1 ms in a development player for each of the Overworld's four chunks with lava near them (Lighting.md's Phase 5).
- **The first caster woken in a session** has Mono compile URP's shadow code: the Overworld's gate took 12 to 14 ms one frame at dusk (Lighting.md's Phase 7).
- **The night's lights and water's reflections show only in play**: the editor's Scene view, a probe render and an edit-mode scene show the light props' lights and lava's glow off and the water reflecting nothing (Lighting.md's Phases 5 and 10).
- **The pixel-lit sprite shader compiles on first use in the editor**, so the first sprites drawn with it in a session show nothing for a frame or two; and in the editor update right after it is imported, sprites sharing its material under the SRP Batcher can draw with another sprite's texture (Lighting.md's Phase 1).
- **URP's 2D resources hold our shadow shader**, hidden from the inspector; a URP upgrade may reset it to URP's, which `PixelLightTests` catches and a menu item sets again, and saving those settings from the editor empties their `m_RuntimeSettings` list (Lighting.md's Phase 7).
- **The camera does not snap to the art grid**, so the art, the blended ground and the screen-space light textures change shape by fractions of an art pixel as it moves. Snapping it alone (tried in TerrainBlending.md's Phase 1) made the moving player shake against the screen.

## Goals

- Waves that look the same along a coast whichever way it runs.
- Lamps' shadows on the units and props standing in them, and sun shadows that sweep across a unit as it walks into them, both on the art grid.
- No light shadow flickering at any zoom; no hitch the first time a caster wakes or a lava chunk loads.
- The night's lights and the reflections previewed in the editor, and the editor's own shader oddities gone where they can be.
- A camera on the art grid without the player shaking, decided on a spike.

## Non-goals (for now)

- New lights, looks or materials beyond what closing a gap needs.
- Weather (Lighting.md's Later).
- The other known gaps: the combat ones, the walls' gap at the ground's corners, wave crests on end at the Overworld's top edge (past every view), one layer with waves per area, the ocean's coast map stutter under the brush and its recipe's unused glint phases.

## Decisions

| Topic | Decision |
|---|---|
| Lamps' shadows on the casters | A unit or prop standing in a prop's lamp shadow loses that lamp's light by the shadow at its feet (Phase 2): the casters' sprites keep the share of the lights other than the sky that the ground keeps half an art pixel above their feet, read from the ground's light texture, which holds the props' shadows the casters' own leaves out. A unit reads it under each column of its sprite, so a trunk's narrow shadow crosses it as it walks through; a prop under its middle, whole. The sky stays. The walls' shadows still fall by screen pixel. Chosen with the user on Phase 2's previews over every caster darkened whole and today's, where a caster kept the light. |
| Sun shadows on units | A unit reads the sun's shadow under each column of its sprite on its feet' row (Phase 3), from the mask without the units' shadows, so a shadow's edge sweeps across it as it walks and it never darkens itself, with the clouds over it; it no longer takes another unit's shadow. Props still darken whole, two art pixels sunward of their feet. Chosen with the user on Phase 3's previews over today's, where a unit darkened whole in one step. |
| Waves | Measured along the coast, not along x (Phase 1): each pixel at sea takes its foot on the coast, the pixel moved back down the coast map's slope by its distance, read a texel to either side (`TBCoastFoot`), which a wave keeps as it rolls in and whose run along a wave follows the coast whichever way it lies. The stretches out at sea and the gaps in a crest are smooth noise and short noise over the foot, each term running across x and y, so they lie along any coast; the front's turn is the tangent of the angle times the foot's x and y together (`TBWaveTurn`), so a front reaches a straight shore at one end first by the angle along coasts running east to west and north to south alike, up to 1.4 times it along a diagonal one and not at all along the other diagonal. The waves' spacing no longer stretches with the turn. The north and south shores keep their look but not their pixels. Chosen with the user on Phase 1's previews over today's. |

## Phases

Each phase ships on its own, keeps the game playable offline and hosted, and comes with tests. A phase whose look can change settles it on previews first.

### Phase 1: waves along every coast

- [x] Previews of today's waves against waves measured along the coast, on the Overworld's west, south and north shores and its north-west corner, from a throwaway switch in the shader.
- [x] The waves measured along the coast (Decisions: Waves): `TBCoastFoot`, `TBWaveTurn`, and the stretches, the crest's and the swash's gaps over the foot.
- [x] Tests: waves rolling in toward a coast on the sea's east side, a spacing apart and turned by their angle; the waves toward a north shore and the swash as before. Architecture.md and TerrainTypes.md updated; the `ShaderCompile` tests run.

Phase 1 notes:

- **Settled first**, with the user on 2026-11-03, on in-engine previews in `Recordings/Waves/` (gitignored), from a throwaway switch in `TerrainBlendWaves.hlsl` (a global the shader read, so both looks rendered without recompiling) and an edit-mode script, both deleted after: `west_today_vs_proposal_2x.gif`, `south_today_vs_proposal_2x.gif`, `north_today_vs_proposal_2x.gif` and `corner_today_vs_proposal_2x.gif`, today against waves measured along the coast over a 3.25 s loop at 8 frames a second. The shipped shader draws the previews' pixels but for 32 to 60 of 1.3 million a frame, where a wave's strength sits at a threshold and the compiled arithmetic rounds the other way.
- **Why a foot on the coast.** A wave front runs along the coast, so anything measured along a wave has to be measured along the coast; but the coast is a curve, and any coordinate along it taken from the world's x and y turned by the coast's direction multiplies the direction's slightest change by the world's coordinates (the Overworld lies 262,144 px from the origin), breaking the waves up. The foot is a point, not a coordinate: noise over it varies along the coast and stays put as a wave rolls in, near enough where the coast map's slope bends, and no turned coordinate is ever formed. The turn is the one term that must grow steadily along a straight coast; the foot's x and y together grow along every straight coast but one diagonal, and change little across a wave since the foot hardly moves along the slope.
- **Cost**: four more reads of the coast map at each pixel of a layer with waves. Timed in the editor (`TerrainBlendProbe.MeasureCost`, 1920 x 1080 at zoom 3 over the Overworld's west coast, the view about 60 % ocean, 300 frames, the old and new shaders alternated): the chunks' cost 0.52 ms a frame either way, within 0.01 ms.
- **Tests**: `TerrainBlendRendererTests` (`ALayerWithWaves_RollsThemInTowardACoastOnItsSide`: a sea on the west, its waves turned 20 degrees, their bands a spacing apart along a row, moving toward the coast, and each front the turn nearer the coast 16 px higher), and the waves toward a coast on the sea's north side and the swash passing unchanged.

### Phase 2: units and props in lamps' shadows

- [x] A spike with previews: a lamp's shadow behind a trunk falling on a unit and on a bush standing in it, read where each stands rather than by screen pixel, against today.
- [x] The look chosen built (Decisions: Lamps' shadows on the casters): a unit or prop in a lamp's shadow darkened, no dark patch on a crown in front of a bush's shadow.
- [x] Tests: a render test of units and props in a prop's lamp shadow, across its edge and beside it; today's pixels where no prop casts. Architecture.md updated; the `ShaderCompile` tests run.

Phase 2 notes:

- **Settled first**, with the user on 2026-11-03, on in-engine previews in `Recordings/LightingGaps/Phase2/` (gitignored) from a throwaway pass and a switch in the sprite shader, deleted after: the Sandbox's lamp post by the walls spot at night (the clock at 0.88), its casters woken by hand, a rogue walking across the narrow shadow of the trunk beside the lamp (`trunk_band_walk_3x.gif`) and down past its wider shadow, where a copied bush stands (`behind_trunk_walk_3x.gif`), each today, every caster whole, and units by column. A prop read by column split the trees' crowns where the trunk's own shadow lay beside them, so props read their feet' middle. The shipped shaders draw the chosen previews' every pixel (`built/`).
- **Where the shadow is.** URP lays a prop's lamp shadow into the light texture of the sorting layers its caster shadows, the ground's alone since Lighting.md's Phase 7, and draws a sorting layer whose lights or casters differ in a batch of its own with its own light texture: `Entities` gets one without the props' shadows whenever a prop's caster is in view. URP's 2D renderer hands a pass every batch's light textures (`Universal2DResourceData.lightTextures`) and runs it at a sorting layer's events, so `LampShadowPass` passes the first batch's to the sprite shader before the casters draw. The two textures differ only by the props' shadows, so the ground's local light over the caster's own at a point is the share of the lamps' light a shadow there leaves, whichever lamps light it. Where the first batch is the casters' own (no prop casting in view), the share is 1.
- **Why the first batch.** URP's render graph does not say which batch holds a sorting layer, but it batches consecutive sorting layers that every visible light and caster treats alike, in the sorting layers' order; `Default` and `Terrain` come first and every light the game makes lights both (the global lights, the prop lights, the spells', the lantern and lava's glow), so they are always the first batch.
- **Cost**: two passes that record no draws, and two more reads of a light texture where a unit's or prop's pixel is drawn. Timed in the editor at the lamp (1920 x 1080 at zoom 3, 100 frames, the lamp burning and its casters woken): about 2 ms a frame, its run-to-run noise (0.1 ms) larger than any difference.
- **Tests**: `PixelLightTests` (`UnderALampsShadow_AUnitDarkensByColumn_APropWhole_AndOtherSpritesKeepTheLight`), and every render test of the sprites before it passing unchanged.

### Phase 3: sun shadows across a caster, props' shadows redrawn when moved

- [x] Previews: a unit walking into a tree's shadow at noon and at dusk, darkened whole as today against column by column.
- [x] The look chosen built (Decisions: Sun shadows on units): units by column, props whole as before.
- [x] A prop moved, added or removed in the editor in play mode redrawing the props' shadows at once (an editor hook marking the mask out of date; nothing changes in a build, where props never move).
- [x] Tests: a unit half in a shadow darkened only where it lies in it; the props' shadows marked out of date by a change in the editor. Architecture.md updated; the `ShaderCompile` tests run.

Phase 3 notes:

- **Settled first**, with the user on 2026-11-03, on in-engine previews in `Recordings/LightingGaps/Phase3/` (gitignored) from a throwaway switch in the sprite shader, deleted after: at the Sandbox's lamp post by day, a rogue walking at a sixteenth of a unit a frame through a tree crown's shadow at noon (`noon_walk_3x.gif`) and along the path through the boulder's long shadow at dusk (`dusk_walk_3x.gif`, frames in `dusk_frames_3x.png`), today against by column. Today a unit darkened whole the frame the middle of its feet crossed an edge; by column the edge sweeps across it in two to six frames. The shipped shaders draw the previews' every pixel (`built/`).
- **Why the props' texture.** A unit's own shadow starts at its feet, so its columns read on its feet' row would read their own shadow; the mask's props' texture, which the feature copies into the frame's before the units' shadows and the clouds are drawn over it, has none of the units', so a unit reads that, and the clouds from the frame's alpha, which holds the clouds alone. It then no longer takes another unit's shadow, which at their size reached little. Props keep reading the frame's mask two art pixels sunward of their feet: by column a crown read its own trunk's shadow (Phase 2's previews).
- **Redrawn when moved.** `PropShadowRedraw` marks the props' shadows out of date on each change the editor publishes (moving, adding, removing, an inspector edit), in play mode or not; it costs one more draw of the props' shadows after an edit, nothing in a build.
- **Cost**: one more read of the mask where a unit's pixel is drawn, and of its alpha; no pass. Within the editor's noise, as Phase 2's.
- **Tests**: `SunShadowTests` (`AUnitAcrossAShadowsEdge_DarkensOnlyItsColumnsInIt`, `AChangeInTheEditor_DrawsThePropsShadowsAgain`), and the units darkening whole in a tree's shadow, under a cloud and where a shadow drops off a top passing unchanged.

### Phase 4: light shadows at one screen pixel an art pixel

- [x] Measure the light textures at the screen's full size against half: the frame's cost in a player at each zoom.
- [x] The light textures at full size where an art pixel is under two screen pixels (a second 2D renderer the camera switches to by zoom, or full size always if the cost allows), so every shadow edge holds still: full size always (`Renderer2D.asset`'s light render texture scale 1).
- [x] Tests: a light's shadow at one screen pixel an art pixel holding still as the camera moves. Architecture.md updated.

Phase 4 notes:

- **Why it flickered.** URP draws its light textures, and each casting light's shadow texture, at a share of the screen's size, half until now, so at one screen pixel an art pixel (windows under 1280 x 720, where the Pixel Perfect Camera's reference of 640 x 360 fits once) a texel covered two art pixels each way, and which art pixel decided it changed as the camera moved by fractions of a pixel. At the screen's size each art pixel's centre has a texel of its own at every zoom.
- **Cost**, measured in a Windows development player (Mono, D3D11, every build scene) on the same machine as Lighting.md's Phases 5 to 11, through a throwaway benchmark run in batchmode, kept in `Recordings/LightingGaps/Phase4/` (gitignored) and deleted from the project: a new game, the rogue at the Overworld's gate between its torches at night (0.88), renders through render requests in 4 batches of 30 waiting for the GPU and the player loop over 240 frames, in 5 rounds of alternating order, median per render, at half and full size. Three runs:

  | View | Half size | Full size |
  |---|---|---|
  | 1920 x 1080 at zoom 3, a render | 0.685 to 0.715 ms | +0.007 to +0.021 ms |
  | 1278 x 718 at zoom 1, a render | 0.750 to 0.765 ms | -0.015 to +0.005 ms |
  | The player loop without rendering | 0.267 to 0.270 ms | within the noise |

  So full size always, rather than a second renderer for the smallest windows: one setting, and the light textures, the relief's light direction and the reflections read the same texels at every zoom.
- **Tests**: `PixelLightTests` (`AtOneScreenPixelAnArtPixel_ALightsShadow_HoldsStillAsTheCameraMoves`, which fails at half size), and every render test of the lights before it passing unchanged. The camera is moved by fractions of a screen pixel but never by exactly half of one, where a screen pixel's centre lies on the edge between two art pixels and either may show.

### Phase 5: loads and the editor's shaders

- [x] Lava's glow map baked beside each world chunk, as its coast map is (`WorldCoastBake`), and read at load instead of built (`WorldGlowBake`).
- [x] The first caster's wake taken off play: URP's shadow code run once while the world loads (`LightWarmUp`).
- [x] `SpritePixelLit` compiled when first used in the editor rather than in the background (`#pragma editor_sync_compilation`), and the SRP Batcher's texture after an import looked into.
- [x] Our shadow shader set again in URP's 2D resources whenever the editor finds URP's there (after an upgrade), writing only that field.
- [x] Tests: the baked glow map the one the renderer builds; the shadow shader restored; the warm-up drawing once and leaving nothing. Architecture.md updated.

Phase 5 notes:

- **The first caster's wake**, measured in a Windows development player through a throwaway benchmark run in batchmode, kept in `Recordings/LightingGaps/Phase5/` (gitignored) and deleted from the project: a new game, the rogue at the Overworld's gate before dusk (0.62), a render request of a 1920 x 1080 view at zoom 3 every frame while the clock steps 0.0004 a frame through dusk; 53 casters wake as the gate's lights first burn. Before: the median frame 1.67 ms, and 12 to 13 ms then 14 to 15 ms in the first and third frames of the night's lights (two runs). With `LightWarmUp`: those frames 2.4 to 3.4 ms and 6.2 to 7.7 ms (eight runs), the rest of the session's frames as before; a second dusk in the same run peaks under 4 ms. The third frame's remainder is URP making the screen-sized light and shadow textures of the batch the props' casters split off (Phase 2): a warm-up drawn at the screen's size took it to 4.3 to 4.5 ms with dusk 15 s after the start, but not with dusk two minutes after, URP's pool having let them go, so the warm-up draws into 32 x 32 and the rest stays in the Known gaps. A light casting soft shadows, or a caster shadowing every sorting layer, made no difference.
- **The glow maps baked**, with the user's leave on 2026-11-03 for a bake step, a PNG per lava chunk and the chunks' `WorldChunk` changes. `WorldGlowBake` builds the glow map a chunk's renderer would build with its neighbours loaded from the whole world's cells (the skirt's 4 cells reach past lava's 2.5), and the World Editor's map buttons (Check maps, Bake changed maps, Bake every map) and the world generation writer's steps (Bake changed glows, after the coasts) bake and check them as they do the coast maps; a chunk's renderer takes the baked map (`TerrainGlowMap.UseBaked`) when it fits its area in its chunk, and builds one otherwise, as an area scene always does. Baked: the Overworld's chunks (2, 0), (3, 0), (2, 1) and (3, 1), their scenes changing only by the `WorldChunk`'s three fields, and the Sandbox's (1, 0), whose scene, saved with its neighbours open, also had its walls' render tilemap drawn again as every save with its neighbours now draws it (an earlier save without them had left it otherwise). In the editor the glow map's part of a lava chunk's build (its profiler marker over five builds) went from 2.1 to 3.5 ms to 0.19 to 0.23 ms, the sprite and the light left.
- **The editor's shaders.** `#pragma editor_sync_compilation` in both of `SpritePixelLit`'s passes makes the editor compile a variant the first time a sprite draws with it, rather than drawing nothing until a background compile ends; builds compile ahead anyway. The SRP Batcher drawing a unit with the props' atlas in the editor update right after the shader's import (Lighting.md's Phase 1 notes) no longer shows: the Sandbox's lamp post scene, a rogue on the shared material beside atlas-packed props, rendered at once after a forced import, with the pragma and without it, draws every sprite with its own texture.
- **The shadow shader restored.** `LightShadowResources` is `[InitializeOnLoad]` and, once the editor has loaded (`EditorApplication.delayCall`, after a URP upgrade's reload too), applies ours when URP's resources hold another, with a warning. Applying saves URP's global settings, which empties their `m_RuntimeSettings` list (Known gaps), so it only runs when the shader was lost.

### Phase 6: the night previewed in the editor

- [x] A time of day for the editor (the lighting panel's in play, a Scene view control in edit mode) that burns the light props' lights and lava's glow, sets the sky, the sun and the reflections as `DayNightPresenter` would, and puts everything back when turned off, writing nothing to the scenes.
- [x] Tests: the editor's night burning the lights and showing the reflections, and leaving the scene unchanged when turned off. Architecture.md updated.

Phase 6 notes:

- **Settled** with the user on 2026-11-03: a Scene view overlay over menu items. The Time of Day overlay (`TimeOfDayOverlay`, shown by default; the Scene view's overlay menu hides it) has a Preview toggle and a Phase slider, and `NightPreview` lights the open scenes as play would at that phase: the sky and the sun, the sun's shadows, the light props and lava's glow at the night lights' share without flicker, the reflections and the relief. Not the region skies (they follow the local player, which the editor has none of), the clouds (they drift with the world clock), nor the props' lamp shadows: a prop's Shadow Caster 2D woken in the editor writes the state of its mesh (`m_HasRenderer`, `m_ShadowMesh`, its trim and source) into its scene as prefab overrides, which putting `enabled` back does not undo. The walls' casters, always awake, still stop the lights. `Recordings/LightingGaps/Phase6/editor_night.png` is the Sandbox's lamp post and lava field at 0.88 in the editor.
- **Writing nothing.** A Light 2D works out its mesh's bounds again while it burns, a serialized field, so the preview records each light's whole serialized state before changing it (`EditorJsonUtility.ToJson`) and writes it back (`FromJsonOverwrite`) when hidden and before any scene saves, showing the preview again once the save is done. Saved under the preview at night, the Sandbox's chunk (1, 0) came out byte for byte as it did saved with the preview off. Play starts with the preview hidden and shows it again when it ends; its state is a `ScriptableSingleton`'s, so a script reload keeps it.
- **Tests**: `NightPreviewTests` (a light prop's light burning at night and off at noon, the sun and the reflections' globals, everything put back when hidden and for a save).

### Phase 7: the camera on the art grid

- [ ] A spike with previews and costs: the world drawn at one pixel an art pixel with the camera on the art grid and shown scaled up, moved by what is left of the camera's position under an art pixel (no shake, every art pixel square); against the camera and every sprite snapped; against today. The look, the cost and what it changes (the light textures' size, the UI, the Pixel Perfect Camera) decide whether it goes ahead.
- [ ] The way chosen built.
- [ ] Tests: the ground and a light's shadow holding their shape as the camera moves. Architecture.md and TerrainBlending.md updated; the `ShaderCompile` tests run.

## Testing

- EditMode: render tests through the pipeline, as the lighting's: waves toward a coast on any side; a unit and a prop in a lamp's shadow; a unit half in the sun's shadow; a light's shadow at one screen pixel an art pixel; the baked glow map; the editor's night.
- PlayMode: nothing new is sent; the PlayMode suites pass.
- `ShaderCompile` after every shader change.

## Open questions

- Whether reading a lamp's light at a sprite's base changes how the lamps light the tall props (a crown far above its trunk), and whether that reads better or worse (Phase 2).
- Whether the camera's spike leaves the light textures and the art-grid shaders as they are (Phase 7).
