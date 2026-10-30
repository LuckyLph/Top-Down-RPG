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

- Light that keeps the art crisp: it changes from one whole art pixel to the next, in a few steps, as hand-drawn light does, never in gradients across an art pixel.
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

The rules for crisp light, the painted light and the time of day were chosen with the user before Phase 0 (2026-10-30), on mockups composed outside the engine. The rest are proposals, confirmed as their phases land.

| Topic | Decision |
|---|---|
| Pipeline | URP's 2D lights for every light, combined by our own shaders: the terrain graph moves to the Sprite Custom Lit target and samples the light texture itself, and one hand-written lit sprite shader (`SpriteLitFlash`'s successor) serves units, props and effects. No URP normals pass: it would draw the terrain's shader a second time. |
| Crisp light | Light is sampled once per art pixel: the terrain at its snapped pixel, a sprite at the centre of the texel it draws. The light above the sky's is banded into steps by its brightness, keeping its hue, so a light draws rings of whole pixels. |
| Painted light | Stays. Every sprite and the ground keep their top-left shading, which reads as the afternoon sun. The sun's colour and the shadows tell the time of day. |
| Light model | `colour = albedo * (sky + sun * unshadowed + lights)`. The sky is the world's Global Light 2D. The sun is ours: its colour and direction come from the time of day, and it is taken away where a sun shadow falls, so shadows take the sky's colour and two shadows never darken each other. At the afternoon's sun, sky plus sun is today's 0.85 white. |
| Sun shadows | A caster's own silhouette laid on the ground away from the sun: flipped at its base, stretched as the sun gets lower, leaned toward where it is, by a vertex shader from one global sun vector, so nothing runs per prop. Shadows lean a little toward the camera so they show instead of hiding behind their casters. A unit's comes from its current frame. They replace the props' baked ellipses. A caster's own shadow never darkens it. |
| Sun path | Rises in the east, passes high and sets in the west: long shadows to the lower left at dawn, short ones under the casters at noon, long ones to the lower right at dusk. The painted light is the afternoon. At night there is no sun, and the sky is a dark blue. |
| Time of day | Cosmetic and shared: the host owns the day clock (the time of day at a network time, and its rate), saves it with the game and sends it to joining players; each machine computes the time locally from the network time. A day lasts 20 minutes and a night 5, in a settings asset. |
| Worlds | A lighting profile per world: outdoor worlds follow the day, indoor ones (`SandboxInterior`) keep a fixed sky and have no sun. |
| Spell lights | A Light 2D on the pooled effects of casts, projectiles and impacts, faded over the effect's life, played on every machine from the events that already reach it. |

## Phases

Each phase ships on its own, keeps the game playable offline and hosted, and comes with tests.

### Phase 0: the look

- [x] Mockups composed outside the engine from the game's own sprites and textures, in `Recordings/Lighting/` (gitignored): the day from dawn to night with silhouette shadows (`DayCycle_3x.png`), and a Smite light at night, smooth against snapped and banded (`SpellLight_SmoothVsSnapped_3x.png`).
- [ ] An in-engine spike, kept out of the shipped code: the terrain and sprites reading the light textures at art-pixel centres, banded, with a Smite-coloured point light in an Overworld chunk at night. Previews in `Recordings/Lighting/`.
- [ ] A silhouette sun shadow in the engine, for a tree and the player, even where two shadows overlap.
- [ ] Today's look kept: with only the world's global light, the spike's shaders render today's pixels.
- [ ] The cost: the terrain's and the sprites' frame time with the spike's shaders against today's, and what a light and the shadows add.
- [ ] Decisions taken on the previews and recorded here: the steps, the falloff, how the shadows are drawn.

- **The mockups.** Python composites of the broadleaf trees, a rock, a bush and the rogue on the grass and gravel textures, lit by the light model above, drawn at 1x and scaled 3x.
  - Silhouette shadows read better than the baked ellipses, most of all when long.
  - Long shadows straight east or west came out as thin slivers, since a sprite has no depth: the crown's width and its shadow's length lie along the same line. Leaning them toward the camera (about half as far down as across) gave the crowns their body back.
  - A caster sampling the shadows at its feet sat in its own shadow, so the trees darkened all day: a caster's own shadow has to be left out.
  - The rogue darkening as its feet stand in a tree's shadow at dawn and dusk sells the shadows as light.
  - The snapped bands keep the art crisp; the smooth light runs across art pixels. The outermost band of a light reads as a hard disc, which the falloff and the number of steps tune.

### Phase 1: crisp light

- [ ] The lighting HLSL in `Assets/Shaders/Include`: the light at an art pixel, the sky's colour, the banding.
- [ ] The terrain graph on the Sprite Custom Lit target, passing the light texture to `TerrainBlend_float`.
- [ ] One lit sprite shader for units, props and effects; props and their prefabs off `Sprite-Lit-Default`.
- [ ] A render test: with only the global light, the terrain and sprites draw today's pixels; under a point light, the light changes only at whole art pixels and in its steps.
- [ ] Architecture.md's camera and sorting, terrain and effects sections updated; the `ShaderCompile` tests run.
- [ ] Checked in Multiplayer Play Mode with a host and a client.

### Phase 2: spell lights

- [ ] A light on the effects of the class casts (the gather and the release), Bolt in flight and its impact, Mend and Smite, and the weapon casts that call for one, faded over the effect's life and returned with it to the pool.
- [ ] The same lights on the copies of other players' casts and projectiles.
- [ ] The light's colours from the motion's light colours, so the baked glow at the hand and the light on the ground agree.
- [ ] Tests: a pooled effect's light on and fading with its effect, and off when returned.
- [ ] Checked in Multiplayer Play Mode with a host and a client.

### Phase 3: day and night

- [ ] A day clock (Gameplay scope) driven by the host's start and the network time, saved with the game and sent to joining players.
- [ ] A day and night settings asset: the day's and the night's length, the sky's and the sun's colours over the day, the sun's direction and the shadows' length.
- [ ] A lighting profile per world, outdoor or indoor, driving its global light.
- [ ] A dev panel slider that moves the time of day on this machine.
- [ ] Tests: the time from a start, a rate and the network time; the colours and sun at set times; a loaded save resuming its time; a joining player taking the host's.
- [ ] Checked in Multiplayer Play Mode with a host and a client: the same sky on both, and after a client joins late.

### Phase 4: moving shadows

- [ ] Silhouette shadows for props, from their `Shadow` child drawing their sprite with the shadow material, and for units from their current frame.
- [ ] Shadows that never darken each other, and units darkened while their feet stand in another caster's shadow.
- [ ] The cliffs' shadow cast toward the sun across the raised cells, and the ground's chips and cobbles lit from the sun's side (`TB_LIGHT` from the sun).
- [ ] The prop baker's ellipse shadows retired or kept for the noon shape, as Phase 0 decides.
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

- Whether band edges flicker as the camera moves, since the light textures are in screen space at half resolution and the camera is not snapped. If they do: snap the camera, render the light textures at full resolution, or band with some hysteresis.
- How the sun shadows reach the sprites: a mask drawn before the `Entities` layer (a renderer feature) that units sample at their feet, or shadows multiplied onto the ground with a stencil, which cannot darken units.
- Whether sampling at a sprite's texel centres holds for props packed in the sprite atlas.
- How bright a spell may be in daylight before the art's light colours burn to white.
- How many lights a frame can afford, measured in a player.
