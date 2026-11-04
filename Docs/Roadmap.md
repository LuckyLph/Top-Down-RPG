# Roadmap

Where every plan in `Docs/` stands, what comes next, and the features planned for later that have no plan of their own yet. [Architecture.md](Architecture.md) describes what exists; each plan holds its own decisions, phases and notes. Keep this page current: update it when a plan is added, a phase lands, a branch merges into `main`, or work is moved to later.

## Plans

| Plan | What it covers | Where it stands |
|---|---|---|
| [Multiplayer.md](Multiplayer.md) | Online co-op, host and play, 2 to 4 players: authority, players, mobs, areas and joining, saves | Phases 1 to 6 done, on `main`. A hosted relay for internet play is deferred (below). |
| [PlayerControls.md](PlayerControls.md) | Click-to-move controls, auto attacks and abilities | Done, on `main`. |
| [DamageAndStatusEffects.md](DamageAndStatusEffects.md) | Typed damage, resistances, immunities and status effects | Done, on `main`. |
| [KeyBindings.md](KeyBindings.md) | Remappable controls, the options and in-game menus, the local settings file | Done, on `main`. |
| [Animation.md](Animation.md) | The character rig and baker, the runtime animator, gaits, the attack timeline, weapon movesets | Done, on `main`. |
| [AsepriteCharacters.md](AsepriteCharacters.md) | Characters, effects and projectiles drawn by hand in Aseprite | Done, on `main`. |
| [TerrainBlending.md](TerrainBlending.md) | Drawing the ground with a blending shader instead of dual-grid tilesets | Done, on `main`, but for two checks by hand in play mode (Phases 5 and 6). |
| [TerrainTypes.md](TerrainTypes.md) | The ground materials past grass, 16 layers per area, per-area layer sets, raised ground | Done, on `main`; lava damage waits for gameplay (below). |
| [TerrainLayers32.md](TerrainLayers32.md) | A terrain renderer's layers raised from 16 to 32 | Done, on `main`. |
| [SceneGeneration.md](SceneGeneration.md) | The area generators that fill a scene with terrain, props and spawn points | In use; no phase checklist. |
| [DualGrid.md](DualGrid.md) | How the dual-grid walls are painted and drawn | Reference; no phase checklist. |
| [WorldStreaming.md](WorldStreaming.md) | Seamless worlds: chunk scenes streamed around the players, worlds at separate origins, world saves | Done, on `main`. |
| [WorldGeneration.md](WorldGeneration.md) | Generating worlds in the editor from the area generators, starting with the Overworld | Done, on `main`, but for one item: measuring a 16 x 16 world from nothing, left while the editor's memory falls short. |
| [PropGeneration.md](PropGeneration.md) | Natural props generated in the editor, in looks for each terrain, and their placement | Done, on `main`. |
| [WorldScale.md](WorldScale.md) | Worlds up to 16 x 16 chunks: sorting by the custom axis, the streaming benchmark, chunk load costs | Done, on `main`. |
| [Lighting.md](Lighting.md) | URP 2D light read per art pixel, spell lights, day and night, sun and cloud shadows, light props, a lantern, reflections on water, relief | Done, on `main`. |
| [LightingGaps.md](LightingGaps.md) | The known gaps the lighting left and the waves along coasts running north to south: lamps' shadows on units, sun shadows across a caster, light shadows at the smallest zoom, load hitches, the night in the editor, a camera on the art grid | Done, on `main`: Phase 7's spike kept today's camera. |
| [DesignSystem.md](DesignSystem.md) | The design system: the specs' language and generated design tables, content rules and tuning views, one UI scale, the UI theme, skin and components, reduce flashing and motion | Proposal, not adopted yet; its open questions come first. |

## Next

[DesignSystem.md](DesignSystem.md) is a proposal waiting to be adopted. What the plans above leave: TerrainBlending.md's two checks by hand in play mode and WorldGeneration.md's measurement of a 16 x 16 world from nothing; the features below wait for plans of their own.

## Later

Features planned for later, out of scope until they get a plan of their own:

- **Multiplayer testing.** Checking the game by hand with other players: Multiplayer Play Mode with 2 to 4 players in one editor, then a second machine on the network and over the internet. Until it comes, every feature is designed for multiplayer and checked by the EditMode and PlayMode tests that run in the editor (the in-process host and client), and plans carry no Multiplayer Play Mode checks (CLAUDE.md). The steps the plans kept for it: [Multiplayer.md](Multiplayer.md#running-two-players-in-the-editor) (two players, saves, runtime terrain edits, a virtual LAN, a bad connection), [WorldStreaming.md](WorldStreaming.md#checking-with-multiplayer-play-mode) (one world, different worlds, trips, saves), WorldGeneration.md's Phase 4 (players across the Overworld's regions), AsepriteCharacters.md's Phase 2 (a baked and a hand-drawn look), KeyBindings.md's Testing (rebinding and the menus), PlayerControls.md's Testing (moving, attacking and casting together).
- **A hosted relay** for internet play without a virtual LAN (Multiplayer.md's Phase 6; Steam is the likely transport if the game ships there).
- **Lava damage**: burning a unit standing in or next to lava, with a fire damage type and a burning status (TerrainTypes.md's Phase 9, DamageAndStatusEffects.md).
- **Weather**: rain, snow and fog with the light under them (Lighting.md's Later).
- **Known gaps**: closing the items in [Architecture.md](Architecture.md#known-gaps-and-oddities)'s Known gaps that no plan covers, each fixed or recorded as accepted, in a plan of its own that picks the order; gameplay polish and the code cleanup are the likely first phases. Left out, since they are covered elsewhere or settled: the fixed build version (multiplayer testing, above), lava only blocking (lava damage, above), the camera off the art grid (LightingGaps.md's Phase 7), the editor's night as a preview (LightingGaps.md's Phase 6) and every player visible to every client (fine for 2 to 4 players). The rest, by area:
  - *Gameplay polish*: waystones and world exits without art; placeholder status icons; a trip's arrival unprotected while the player is faded out; a bow shooting into a wall in range; unit picks ignoring the ability's filter; the weapon switch not saved; nothing physical at a world's edge, water, the ocean, lava or the cliff of raised ground; the walk and sprint never shown at today's speeds; feet sliding going north or south.
  - *Multiplayer*: one save per install, a client keeping no copy of its place; remote arrows late and stopping elsewhere; a copy's cast and windup on its own clock; a client's attacks seldom holding their contact frame; hit directions each machine's own; chunk holds outliving trips.
  - *Worlds and streaming*: chunk load hitches and per-renderer texture arrays in the editor; a generated chunk keeping layers it no longer needs; a moved place taking the nearest walkable cell, reachable or not; coast and glow maps baked only on request and unchecked by a build; persistent ids repaired only for undoable edits; world search limits as code defaults; saved terrain edits naming layers by index; the Overworld's wave crests on end at its top edge and the sand nubs at the Coast region's joins.
  - *Terrain and the Terrain Brush*: one layer with waves per area and the coast map's rebuild while painting; the walls' gap at the ground's corners; diagonal paths stepping; planks picking a way per cell; a layer showing a lower one under its corners; a bridge's end depending on the layers between; no preview for a flood fill, on raised ground or for walls across a chunk border; one level of raised ground; ground tiles found by name; the layer order unchecked; a scratch scene's grass layer; the soft look's first frames in the editor.
  - *Lighting*: props darkening whole under the sun; the walls' light shadows by screen pixel; soft light shadows off the art grid; restoring our shadow shader rewriting URP's settings; Shadow Caster 2Ds making meshes in the editor.
  - *Animation and the editor*: a buffered cast replacing the release; a cast aimed aside running toward its aim; the recipe inspector's cast over the run as one strip; TMP's fallback font changing in the editor.
  - *Code cleanup* (CLAUDE.md): inline comments; `MobConfig`'s hard-coded layer mask and create-menu paths outside `TopDownRPG/`; static mutable state (`SceneQuery`, `UnityClock.Shared`); two classes in `SceneDefinitionEditor.cs`; the status dev panel finding its scope itself; duplicated tuning; unused or test-only API, the old sword icon and the dual-grid leftovers.
