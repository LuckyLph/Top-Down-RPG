# Lighting

Plan for lighting the game: spells that light the world around them, and a day and night with a sun that moves and shadows that follow it, in light that keeps the 32 px art crisp. [Architecture.md](Architecture.md) describes what exists today. [TerrainBlending.md](TerrainBlending.md) is the ground's renderer, [Animation.md](Animation.md) and [PropGeneration.md](PropGeneration.md) make the sprites this lights. Tick off the phase checklist as steps land, and move each settled behaviour into Architecture.md as its code arrives.

Every rule in [Multiplayer.md](Multiplayer.md#rules-for-new-code-starting-now) applies. Lighting is drawn by each machine on its own. The time of day is shared, so every player sees the same sky, but nothing about it is sent each frame, and nothing in the game's rules reads it.

## Where we start

- **Lights.** Each world's root scene holds one Global Light 2D under its `WorldLighting`, white at 0.85, on the `Default`, `Terrain`, `Entities` and `Overlay` sorting layers, with normal maps off and no shadow casters anywhere. No other light exists. `LocalWorldPresentation` shows the lights of the world on screen and hides the others', since URP allows one global light per sorting layer.
- **The 2D renderer** (`Assets/Settings/Renderer2D.asset`): light textures at half the screen's resolution, HDR emulation scale 1, the four default blend styles, no renderer features. The light textures are `B10G11R11` floats where the platform has them, so they hold light above 1; a lit sprite's colour is `_HDREmulationScale * (albedo * modulate + additive)`, clamped only by the camera's target. Post-processing is off on the camera.
- **Lit shaders.** The terrain is a Shader Graph on the Sprite Lit target (`TerrainBlend.shadergraph`, a thin wrapper around `TerrainBlend_float`), lit after the graph by URP. Units draw with our hand-written `SpriteLitFlash.shader`, props with URP's `Sprite-Lit-Default`, the impact with `Sprite-Unlit-Default`. URP 17.4 also has the Sprite Custom Lit target, whose 2D Light Texture node hands a graph the light texture of a blend style to sample itself.
- **The light is painted in.** Everything is shaded from the top left, (-0.6, 0.8) on screen: the rig's four-colour ramps, the props' ramps, the terrain's chips, cobbles and raised ground (`TB_LIGHT`, a constant in `TerrainBlendInputs.hlsl`), and the cliffs' cast shadow.
- **Shadows.** A prop has a `Shadow` child drawing a baked sprite on the `Terrain` sorting layer: a hard ellipse of black at 35 % under the crown or body, shifted a little east, away from the light. Units have none. Raised ground casts a shadow below its cliff in the terrain shader (`TBCliffShadow`).
- **Casts.** A class cast's light (a glow at the casting hand, a star on the release, a ring and sparks) is baked into the cast's frames in its motion's colours: frost blue for Bolt, green for Mend, gold for Smite. Effects are pooled (`EffectPool`).
- **What later steps can reuse.** The rig keeps a depth and a surface normal for every pixel it draws, and so does `ArtCanvas` for props. Each terrain material has a height texture, and a cell field gives every pixel its offset to its chip's or cobble's centre. Shadow Caster 2D takes its shape from a Collider2D, a SpriteRenderer, a SpriteShape or a SpriteSkin.
- **The camera** is not snapped to the art grid (Architecture.md's Known gaps), so the screen-space light textures move against the world by fractions of an art pixel.

## Goals

- Light that keeps the art crisp: one value per art pixel, changing only from one whole art pixel to the next, never in gradients across an art pixel.
- Spells light the world: the ground, props and units around a cast, a bolt in flight and an impact, on every machine.
- A day and night: the sky's colour and brightness change over the day, the sun rises in the east and sets in the west, and the shadows of props, units and cliffs swing and lengthen with it. Night is dark enough for spell light to matter.
- Under the painted light, at the afternoon's sun, the game looks exactly as it does today.
- The same time of day for every player of a session, and after a save is loaded.

## Non-goals (for now)

- Darkness in the game's rules: sight, stealth, spawns or anything else that reads the light.
- Repainting the art unlit so the sun can light it. The painted shading stays (Decisions).
- URP's normals pass, normal maps and relief under local light (Later).
- Lights that are not spells: torches, lanterns, campfires, lava's glow, glowing props (Later).
- Walls and props blocking light (Shadow Caster 2D), reflections on water, weather, cloud shadows (Later).
- A player option for any of it.

## Decisions

The rules for crisp light, the painted light and the time of day were chosen with the user before Phase 0 (2026-10-30), on mockups composed outside the engine. Phase 0's previews in the engine then dropped the steps the mockups had, kept URP's lights over a light list of our own, and chose how sun shadows are drawn, with the user. The rest are proposals, confirmed as their phases land.

| Topic | Decision |
|---|---|
| Pipeline | URP's 2D lights for every light, combined by our own shaders: the terrain graph moves to the Sprite Custom Lit target and samples the light texture itself, and one hand-written lit sprite shader (`SpritePixelLit`, `SpriteLitFlash`'s successor) serves units, props, effects and the walls. No URP normals pass: it would draw the terrain's shader a second time. Chosen in Phase 0 over a list of lights the shaders compute in world space, which looks the same without steps and does not flicker with them, but gives up the editor's tooling, light shapes and Shadow Caster 2D; it stays the way back if steps or relief are ever wanted (Phase 0 notes). |
| Blend styles | Our shaders read only blend style 0's light texture (Multiply, URP's default for a new light), so every light uses that style: one light keyword (`USE_SHAPE_LIGHT_TYPE_0`) instead of URP's four, two light variants of a lit pass instead of sixteen. Chosen in Phase 1. |
| Crisp light | Light is sampled once per art pixel: the terrain at its snapped pixel, a sprite at the centre of the texel it draws, so a light's falloff changes only from one art pixel to the next. It is not banded into steps: chosen on Phase 0's previews over steps of 0.2 and 0.3 and a dithered version, which also flicker as the camera moves (Phase 0 notes). |
| Painted light | Stays. Every sprite and the ground keep their top-left shading, which reads as the afternoon sun. The sun's colour and the shadows tell the time of day. |
| Light model | `colour = albedo * (sky + sun * unshadowed + lights)`. The sky is the world's Global Light 2D. The sun is ours: its colour and direction come from the time of day, and it is taken away where a sun shadow falls, so shadows take the sky's colour and two shadows never darken each other. At the afternoon's sun, sky plus sun is today's 0.85 white. |
| Sun shadows | A caster's own silhouette laid on the ground away from the sun: flipped at its base, stretched as the sun gets lower, leaned toward where it is, by a vertex shader from one global sun vector, so nothing runs per prop. Shadows lean a little toward the camera so they show instead of hiding behind their casters. A unit's comes from its current frame. They replace the props' baked ellipses, which go. |
| Shadow mask | The silhouettes are drawn into a sun shadow mask of one texel per art pixel, aligned to the world's art grid, so it never moves against the ground as the camera does; overlapping shadows are one shadow. A 2D renderer feature draws it before the frame (`RenderPassEvent2D.BeforeRendering`) from the casters' own sprite renderers with the caster material, over the view and a margin. The ground reads it at each art pixel; a sprite reads it two art pixels toward the sun from its feet, past the start of its own shadow, so a unit standing in another caster's shadow darkens whole and no caster darkens itself. Static casters are redrawn when the sun has moved about an art pixel at the tip of the longest shadow (every few seconds over a 20-minute day), units every frame. Chosen in Phase 0 over shadows multiplied onto the ground through the stencil, which cannot darken units. |
| Sun path | Rises in the east, passes high and sets in the west: long shadows to the lower left at dawn, short ones under the casters at noon, long ones to the lower right at dusk. The painted light is the afternoon. At night there is no sun, and the sky is a dark blue. |
| Time of day | Cosmetic and shared: the host owns the day clock (the time of day at a network time, and its rate), saves it with the game and sends it to joining players; each machine computes the time locally from the network time. A day lasts 20 minutes and a night 5, in a settings asset. |
| Worlds | A lighting profile per world: outdoor worlds follow the day, indoor ones (`SandboxInterior`) keep a fixed sky and have no sun. |
| Spell lights | A Light 2D on what draws a spell's light, played on every machine from what already plays there, nothing networked. A cast's light is on the caster and follows the glow baked into the frame it shows: where the baker drew it, how strong it is drawn there (0.5, 0.75 and 1 over the gather, 1 on the release, 0.5 on the ring, 0.25 on the sparks) and in the motion's rim tone, at 1.2 and radius 3. An elemental impact (Fire, Frost, Lightning, Poison) lights in its spark's middle colour at 1.2 and radius 2, fading to nothing over the impact. Physical and True impacts and the weapon casts have none, and Bolt does not fly, so it has no flight light. Chosen in Phase 2. |

## Phases

Each phase ships on its own, keeps the game playable offline and hosted, and comes with tests.

### Phase 0: the look

- [x] Mockups composed outside the engine from the game's own sprites and textures, in `Recordings/Lighting/` (gitignored): the day from dawn to night with silhouette shadows (`DayCycle_3x.png`), and a Smite light at night, smooth against snapped and banded (`SpellLight_SmoothVsSnapped_3x.png`).
- [x] An in-engine spike, kept out of the shipped code: the terrain and sprites reading the light textures at art-pixel centres, banded, with a Smite-coloured point light in an Overworld chunk at night. Previews in `Recordings/Lighting/`.
- [x] A silhouette sun shadow in the engine, for a tree and the player, even where two shadows overlap.
- [x] Today's look kept: with only the world's global light, the spike's shaders render today's pixels.
- [x] The cost: the terrain's and the sprites' frame time with the spike's shaders against today's, and what a light and the shadows add.
- [x] Decisions taken on the previews and recorded here: the steps, the falloff, how the shadows are drawn.

- **The mockups.** Python composites of the broadleaf trees, a rock, a bush and the rogue on the grass and gravel textures, lit by the light model above, drawn at 1x and scaled 3x.
  - Silhouette shadows read better than the baked ellipses, most of all when long.
  - Long shadows straight east or west came out as thin slivers, since a sprite has no depth: the crown's width and its shadow's length lie along the same line. Leaning them toward the camera (about half as far down as across) gave the crowns their body back.
  - A caster sampling the shadows at its feet sat in its own shadow, so the trees darkened all day: a caster's own shadow has to be left out.
  - The rogue darkening as its feet stand in a tree's shadow at dawn and dusk sells the shadows as light.
  - The snapped bands keep the art crisp; the smooth light runs across art pixels. The outermost band of a light reads as a hard disc, which the falloff and the number of steps tune.
- **The spike.** Throwaway files in `Assets/Tests/Editor/LightingSpikeTemp/` and `LightingSpikeTemp.cs`, never committed and deleted when the phase closed.
  - The terrain: the graph's Sprite Lit pass as Shader Graph generates it, with URP's lighting step replaced by one that samples the light textures once at the art pixel's centre and bands the light above the sky's. The sprites: a hand-written lit shader doing the same at the centre of the texel each fragment draws.
  - Rendered through `TerrainBlendProbe` on the Overworld's chunk (1, 1), where the stone road, the plaza and a gravel path meet, at 1920 x 1080 and zoom 3: a night sky of (0.13, 0.16, 0.30), a Smite light at the rogue's hand (gold, radius 3.5, intensity 1.2) and a Bolt light over the cobbles (frost blue, radius 2.2, intensity 1.1). Previews: `Recordings/Lighting/Spike_NightBands_3x.png` and `Spike_ListedLights_3x.png`.
- **Today's look, exactly.** Under the global light alone, with the terrain's animation at a fixed time, the spike's shaders render today's pixels: 0 of 2,073,600 differ, for the ground alone and with the props and the rogue. Without the fixed time a few swaying weeds differ between any two renders.
- **Cost.** The terrain's draw on that chunk at 1920 x 1080, zoom 3, chunks shown against hidden: 0.65 ms with either shader (mean of six alternating measurements, each 0.2 to 1.2 ms; the noise is larger than any difference).
- **Bands from the light textures flicker.** With the camera moved by 0.25 to 1 screen pixel, light sampled from URP's light textures and banded changes 1,800 to 2,600 screen pixels by more than 8 levels (about 200 to 290 art pixels along the band edges), where smooth light and snapped light without steps change none. The light textures are in screen space at half resolution, so the light at an art pixel's centre drifts with the camera's sub-pixel position and pixels near a step flip back and forth. Snapping the camera was ruled out in TerrainBlending.md's Phase 1 (the moving player wobbles).
- **Lights computed in world space do not.** The same two lights computed by the shaders from a list of positions (colour times (1 - distance / radius) to a power, summed, then banded) change no pixel at those offsets; at 1.5 px they change the same 782 pixels the art itself does without any light (Architecture.md's Known gaps). A power of 2 matches the look of URP's default falloff.
- **Steps.** Steps of 0.2 give three or four rings for a spell; steps of 0.3 give two or three, bolder. A 2 x 2 ordered dither between steps covers the whole light in a checker and reads as a screen door. Chosen with the user: no steps, the light snapped to art pixels and otherwise smooth, which looked best.
- **URP's lights, kept.** Without steps nothing flickers, so the plan's URP lights stay (Decisions: Pipeline), chosen with the user.
- **Sun shadows in the engine.** A caster shader lays each prop's sprite and the rogue's frame on the ground from one sun vector (the height above the pivot times the vector, the parts below the pivot kept at it), drawn into a mask of one texel per art pixel by a camera aligned with the art grid that sees only temporary casters on an unused layer; the spike's terrain and sprite shaders add the sun where the mask is clear, the sprites reading it two art pixels sunward of their feet. The props' baked ellipses were hidden.
  - Rendered on the forest's edge in chunk (1, 1) from dawn to night with the mockups' colours and sun vectors: `Recordings/Lighting/Spike_DayCycle_3x.png`. The shadows swing from the lower left to the lower right and lengthen at dawn and dusk; the forest's overlapping shadows are one shade; the rogue darkens whole in a tree's shadow at dawn and dusk and never in its own; at the afternoon's sun the scene reads as today's.
- **What lights and shadows cost.** In the editor at 1920 x 1080 and zoom 3 on that chunk, with the frame drifting from 3 to 7 ms over the run with the machine's load, the first round compares like for like: today 3.0 ms, the spike's shaders at night 3.5 ms, with two point lights 3.5 ms, with the sun and its mask read 3.3 ms; two lights and reading the mask cost nothing measurable. Drawing the mask through a second camera took 2.0 ms for the chunk's 474 casters, of which 1.5 ms is the camera's own overhead with nothing to draw: about 0.5 ms for the casters, which a renderer feature inside the main camera pays without the overhead, and only when the sun has moved (Decisions: Shadow mask). Light counts and the mask's cost are to be measured again in a player in Phases 2 and 4.
- **Two editor traps.** A render request skips a shader variant it has not compiled, and the first render after a shader import skips them all: the spike compiles the swapped materials' pass with `USE_SHAPE_LIGHT_TYPE_0` enabled, and renders once to throw away after an import. And a sprite sharing the spike's material with the atlas-packed props drew with the atlas's pixels; giving it a material of its own fixed it, and why is left to Phase 1.

### Phase 1: crisp light

- [x] The lighting HLSL in `Assets/Shaders/Include/PixelLight.hlsl`: the light at an art pixel (`PixelLit`) and the centre of the texel a sprite draws (`PixelLightTexelCentre`). The sky's colour the plan listed was for the steps, which went in Phase 0.
- [x] The terrain graph on the Sprite Custom Lit target, `TerrainBlend_float` reading the light texture itself.
- [x] One lit sprite shader (`SpritePixelLit`) for units, props, effects, the markers and the walls; props and their prefabs off `Sprite-Lit-Default`.
- [x] A render test (`PixelLightTests`): with only the global light, a sprite draws URP's pixels and the terrain its unlit colour times the light; under a point light, the light changes only at whole art pixels.
- [x] Architecture.md updated (a Lighting section, the terrain graph, the effects, the units, the tests); the `ShaderCompile` tests run.
- [ ] Checked in Multiplayer Play Mode with a host and a client.

- **The ground.** The graph's sub-target was switched by Shader Graph's own serializer (the target's `TrySetActiveSubTarget`, then `WriteShaderGraphToDisk`): the only change in the file is the sub-target. The Custom Function includes `TerrainBlend.hlsl` with pragmas, and it declares the light keyword under `#if SHADERPASS == SHADERPASS_SPRITELIT`, so only the lit pass has it: declared in every pass, it doubled the other four passes' variants (the `ShaderCompile` test took 327 s, against 225 s with the keyword in the lit pass alone), and a pragma left in a file a pass includes plainly warns as unknown. `TerrainBlend_float` lights its composite and the debug view's colours at the centre of the art pixel it snapped to. The lit pass keeps its name, Sprite Lit.
- **The sprites.** `SpriteLitFlash.shader` became `SpritePixelLit.shader` and `SpriteLitFlash.mat` became `SpritePixelLit.mat`, moved with their GUIDs, so the units kept their references. The spike found a fragment's texel centre by its offset in texels over 32 px per unit, which holds only for an unflipped, unscaled sprite; the shader now maps the offset through the screen-space derivatives of the texel coordinates and the position, and reads the texture's size from the texture rather than `_MainTex_TexelSize`. The normals pass went (no light uses normals), and so did the skinned-sprite and debug-display variants (nothing is skinned).
- **The materials.** `Sprite-Lit-Default` gave way to `SpritePixelLit.mat` on `Prop.prefab`'s `Visuals` and `Shadow` (every prop prefab and placed prop inherits them), the status aura, the move marker, the hover ring in `Gameplay`, `Walls.prefab` and the walls' render tilemaps in the Sandbox's, Testlands' and the stress world's chunks; `SpriteLit.mat` (the arrow, the death puff) was retired into it. It is also the 2D renderer's default material now, so a sprite or tilemap renderer a tool makes lights the same way. The Overworld has no walls. Saving the Sandbox's chunks from the editor also rewrote their render tilemaps' tile sprite lists in another order, so only the material line of each was kept. Saving the move marker wrote the default of its `attackMoveColor`, which it had never stored.
- **Today's pixels, exactly.** On the Overworld's chunk (1, 1), the forest's edge (20 x 11.25 units at 1 screen pixel per art pixel and 10 x 5.625 at 3), with the terrain's animation pinned, under the global light alone, the props and a rogue on the new material: 0 of 230,400 and 0 of 518,400 pixels differ from the same renders of the branch's previous commit (the graph on Sprite Lit, the props on `Sprite-Lit-Default`, the rogue on `SpriteLitFlash`), stashed and rendered in turn.
- **Crisp light.** By day and at night with the Phase 0 spike's Smite light on the rogue, with the props' own prefabs: `Recordings/Lighting/Phase1/Phase1_DayNight_3x.png`. The test holds it: every 3 x 3 block of screen pixels, one art pixel, is one colour under a point light, on the ground and on a flipped sprite and one cut from the middle of a larger texture; sprites lit at each screen pixel's own position instead fail it.
- **The atlas glitch, found.** In the editor update right after the sprite shader is imported, a rogue sharing the material with the atlas-packed props drew with the atlas's pixels, in every render of that update; from the next update it drew right. It never did with a material of its own, nor with the SRP Batcher off. The spike gave its rogue a material of its own, and so never saw it again. A build never imports a shader, so it is editor-only (Architecture.md's Known gaps).
- **Two variants to compile.** `TerrainBlendProbe` now compiles every material a render draws, unlit and with the light keyword, as the spike had to.

### Phase 2: spell lights

- [x] A light on the class casts' glow (the gather and the release) of Bolt, Mend and Smite, following the glow frame by frame, and on Bolt's impacts (every elemental impact), faded over the impact's life and returned with it to the pool. Bolt has no flight to light, and no weapon cast calls for one (Phase 2 notes).
- [x] The same lights on the copies of other players' casts and their impacts.
- [x] The light's colours from the motion's light colours, so the baked glow at the hand and the light on the ground agree.
- [x] Tests: a pooled effect's light on and fading with its effect, and off when returned.
- [ ] Checked in Multiplayer Play Mode with a host and a client.

- **What lights.** Settled with the user before the look: Bolt does not fly (it resolves at once along its line when the cast completes), so its light is the glow at the hand and its impacts, and a flight light waits for a projectile or a streak with art of its own. Impacts light for the elemental damage types only, so no sword, bow or Weasel hit does, nor Smite's Physical strike. Mend and Smite light at the casting hand only, where the art glows: Mend has no art at its target, and Smite's target only shows a Physical spark. No weapon cast lights: none of their motions has light colours, and the bow's flash on Volley is a weapon's, not a spell's.
- **The look**, settled on in-engine previews of the Overworld's chunk (1, 1) at 3 screen pixels per art pixel, by day (0.85) and at night (0.13, 0.16, 0.30), with the terrain's animation pinned, in `Recordings/Lighting/Phase2/`: Bolt, Mend and Smite cast frame by frame at a Weasel (`try_bolt_cast.gif`, `try_mend_cast.gif`, `try_smite_cast.gif`, Bolt also 4x slower), the releases on the cobbled road (`try_road.png`), each damage type's impact (`try_impacts.png`), the light's tone (`try_tone_night.png`, `try_tone_day.png`) and its intensity (`try_bolt_intensity.png`, `try_burn_day.png`).
  - The cast's light is a point light at the glow, peak 1.2 and radius 3 with URP's default falloff (0.5), in the motion's rim tone (its light colours' `Shadow`): frost blue (90, 180, 240), green (90, 210, 110), gold (240, 170, 60). The paler glow tone, compared beside it, lights the ground nearly white. On grass either reads mostly as brighter green, since the light multiplies a green ground; on stone and gravel the colours show.
  - Its strength follows the frames: the baker marks where it draws a cast's light (`CharacterCanvas.MarkLight`: the light's centre on the screen, between the hands for Mend, kept even where the body hides the hand, as Bolt's facing north) and how strongly (`MotionEffect.LightStrength`: a gathering glow as its size, the release's star 1, the ring 0.5, the sparks 0.25). The light steps with the frames rather than fading between them, as the art does; Smite's channel pulses between 0.75 and 1 with its last two gather frames.
  - An impact's light shines from where its spark is drawn, peak 1.2 and radius 2 in its palette's middle colour (Frost's (124, 204, 255) is close to Bolt's rim), falling linearly to nothing over its 0.16 s.
- **How bright by day.** By day (sky 0.85), Smite's and Bolt's releases on the cobbled road and the gravel path, peaks 0.8 to 2.4 against no light, counting art pixels with a channel at 255: up to 1.2 at most one art pixel clips beyond the spell's own star (33 to 40 art pixels, as at 0.8); at 1.6 the brightest stones start to (up to 16 art pixels more); at 2.0 dozens (up to 70 more); at 2.4 a pale disc (Smite's glow tone 131 more on the cobbles, its rim tone 112). Chosen: 1.2, which reads clearly at night, most of all on stone, and burns nothing by day.
- **Where the colours come from.** The colours live in editor-only data (`KeyedMotion`'s light colours and the impact recipe's palettes), so the editor tools write them into what the runtime reads: the character baker writes each frame's light (`CharacterFrameLight`: its offset from the feet in units and its strength) and the clip's light colour into the animation set (`CharacterAnimationClip.LightAt`, `LightColour`), from the frames it bakes (`CharacterSheet.LightCentres` and `LightStrengths`, `CharacterSheetWriter.Lights`); the effect writer writes each variant's light colour into the effect's sprites (`EffectRecipe.LightColour`, `ImpactPalette.CastsLight`, `EffectSprites.LightColour`). Regenerating the rogue rewrote only its set (every sheet's bytes stayed), and the impact only its sprite set.
- **On every machine.** A cast's light shows with the sprite each update shows (`CharacterAnimator.Light` and `LightColour`, put on the light by `CharacterVisuals`), so it is held with its sprite through a hit-stop, follows the gait under a cast drawn over the run, and plays on another player's copy from its cast messages exactly as the copy animates; an impact's light plays with the impact, which every machine plays from the hits it learns of. Nothing new is sent, and nothing in the game reads the lights.
- **The assemblies.** `Light2D` is in `Unity.RenderPipelines.Universal.2D.Runtime`, now referenced by `TopDownRPG.Gameplay`, which owns the characters and the effects, and by the PlayMode tests, which read a copy's light.
- **How many lights a frame can afford**, measured in a Windows development player (Mono, D3D11, every build scene) on an RTX 5070 Ti and a Ryzen 7 5800X3D, run in batchmode through a throwaway benchmark kept out of the shipped code (a DevTools entry on a command-line flag, deleted after): it starts a new game as the streaming benchmark does, waits 5 s in the Overworld's Clearing for its chunks, copies the player's cast light (radius 3, 1.2) 256 times at random over a 1920 x 1080 view at zoom 3 around the player, and for 0 to 256 of them on renders the view through render requests into a texture (a batchmode player renders no camera itself), 4 batches of 30 renders each waiting for the GPU, in 5 rounds of alternating order, median per render. Three runs agree (the player loop timed in the last two):

  | Lights on screen | 0 | 1 to 8 | 16 | 32 | 64 | 128 | 256 |
  |---|---|---|---|---|---|---|---|
  | A render | 0.54 to 0.57 ms | within the noise (0.01 ms) | +0.01 to 0.02 ms | +0.03 to 0.04 ms | +0.11 to 0.15 ms | +0.34 to 0.41 ms | +0.83 to 0.93 ms |
  | The player loop without rendering | 0.18 ms | within the noise | +0.005 to 0.009 ms | +0.008 to 0.017 ms | +0.013 to 0.016 ms | +0.03 to 0.04 ms | +0.06 ms |

  A light costs about 1 to 3.6 µs a render and a fraction of that in the loop (its `LateUpdate`). Play makes a few at once: a cast light per casting player and an impact light for 0.16 s per elemental hit, so a fight of four players stays under 32. No cap is needed. A slower GPU pays more for each light's fill of the half-resolution light texture, but 32 lights cost 0.04 ms here against 16.7 ms a frame at 60 frames a second.
- **Allocation.** Turning a Light 2D on or off allocates in the editor only: its `OnEnable` and `OnDisable` subscribe to the editor's sorting layer callbacks under `UNITY_EDITOR`; in a player they only add it to and remove it from the light list. So the allocation test measures the stretches a light shines through, and those allocate nothing.

### Phase 3: day and night

- [ ] A day clock (Gameplay scope) driven by the host's start and the network time, saved with the game and sent to joining players.
- [ ] A day and night settings asset: the day's and the night's length, the sky's and the sun's colours over the day, the sun's direction and the shadows' length.
- [ ] A lighting profile per world, outdoor or indoor, driving its global light.
- [ ] A dev panel slider that moves the time of day on this machine.
- [ ] Tests: the time from a start, a rate and the network time; the colours and sun at set times; a loaded save resuming its time; a joining player taking the host's.
- [ ] Checked in Multiplayer Play Mode with a host and a client: the same sky on both, and after a client joins late.

### Phase 4: moving shadows

- [ ] The sun shadow mask: a 2D renderer feature drawing the casters' own sprite renderers with the caster material into a mask aligned to the art grid (Decisions: Shadow mask), static casters only when the sun has moved, units every frame.
- [ ] The lit shaders reading it: the ground at each art pixel, sprites two art pixels sunward of their feet, so shadows never darken each other and units darken in another caster's shadow.
- [ ] The cliffs' shadow cast toward the sun across the raised cells, and the ground's chips and cobbles lit from the sun's side (`TB_LIGHT` from the sun).
- [ ] The prop baker's ellipse shadows retired: the shadow row of the prop sheets and each prop's `Shadow` child go, every prefab and placed prop updated in place.
- [ ] A render test of a tree's and a unit's shadow at dawn, noon and dusk; the Overworld's props drawn with the new shadows at no new cost per prop.
- [ ] Checked in Multiplayer Play Mode with a host and a client.

## Testing

- EditMode: the day clock's arithmetic, the settings' colours and sun at set times, the save's and the join's time; a pooled effect's light. Render tests through the pipeline, as the terrain's: today's pixels under the global light alone; a point light changing only at whole art pixels; the shadows at set suns.
- PlayMode: a session's time of day running and resuming from a save; spell lights in a real cast.
- `ShaderCompile` after every shader change.
- Manual: Multiplayer Play Mode checks per phase.

## Later

- Lights that are not spells: torches, lanterns and campfires as props, a light carried at night, lava and embers glowing (the renderer could build a glow map with the control map, as it builds the coast map, which the ground and the sprites sample instead of lights along every lava edge), glowing pixels on sprites.
- Relief under local light: dome normals for chips and cobbles from the cell fields, normals from the materials' heights and the cliffs' faces, normal sheets written by the character baker and `ArtCanvas`. The light's direction could come from the light texture's gradient rather than from URP's normals pass.
- Walls blocking light in interiors and caves (Shadow Caster 2D from the walls' collider); props blocking a torch at their trunk.
- Reflections: a light on a dock shimmering on the water below it, glints and sparkles near lights.
- Cloud shadows drifting over the Overworld, a sky that varies by region, weather.

## Open questions

- Whether band edges flicker as the camera moves, since the light textures are in screen space at half resolution and the camera is not snapped. Answered in Phase 0: they do with URP's light textures, and they do not with lights the shaders compute from a list in world space; without steps neither does.
- How the sun shadows reach the sprites: a mask drawn before the `Entities` layer (a renderer feature) that units sample at their feet, or shadows multiplied onto the ground with a stencil, which cannot darken units. Answered in Phase 0: the mask (Decisions: Shadow mask).
- Whether sampling at a sprite's texel centres holds for props packed in the sprite atlas. Answered in Phase 0: it does; the atlas-packed props lit and matched today's pixels.
- Why a sprite sharing one lit material with the atlas-packed props drew with the atlas's pixels in the spike (Phase 0 notes), before Phase 1 puts every sprite on one shader. Answered in Phase 1: only in the editor update after the sprite shader is imported, through the SRP Batcher; never in a build (Phase 1 notes).
- How bright a spell may be in daylight before the art's light colours burn to white. Answered in Phase 2: 1.2 burns nothing on the cobbles or the gravel; from 1.6 the brightest stones start to clip (Phase 2 notes).
- How many lights a frame can afford, measured in a player. Answered in Phase 2: far more than play makes. In a Windows development player, 32 spell lights on screen add about 0.04 ms to a 1920 x 1080 frame and 256 under 1 ms, against a few at once in a fight (Phase 2 notes).
