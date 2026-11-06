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
| [TerrainBlending.md](TerrainBlending.md) | Drawing the ground with a blending shader instead of dual-grid tilesets | Done, on `main`. |
| [TerrainTypes.md](TerrainTypes.md) | The ground materials past grass, 16 layers per area, per-area layer sets, raised ground | Done, on `main`; lava damage waits for gameplay (below). |
| [TerrainLayers32.md](TerrainLayers32.md) | A terrain renderer's layers raised from 16 to 32 | Done, on `main`. |
| [SceneGeneration.md](SceneGeneration.md) | The area generators that fill a scene with terrain, props and spawn points | In use; no phase checklist. |
| [DualGrid.md](DualGrid.md) | How the dual-grid walls are painted and drawn | Reference; no phase checklist. |
| [WorldStreaming.md](WorldStreaming.md) | Seamless worlds: chunk scenes streamed around the players, worlds at separate origins, world saves | Done, on `main`. |
| [WorldGeneration.md](WorldGeneration.md) | Generating worlds in the editor from the area generators, starting with the Overworld | Done, on `main`: Phase 6 (2026-11-05) writes the 16 x 16 world from nothing in 3.4 minutes, within its 4, where it took 14, and composes it in 9.7 s on its own. |
| [PropGeneration.md](PropGeneration.md) | Natural props generated in the editor, in looks for each terrain, and their placement | Done, on `main`. |
| [WorldScale.md](WorldScale.md) | Worlds up to 16 x 16 chunks: sorting by the custom axis, the streaming benchmark, chunk load costs | Done, on `main`. |
| [Lighting.md](Lighting.md) | URP 2D light read per art pixel, spell lights, day and night, sun and cloud shadows, light props, a lantern (removed from the player since), reflections on water, relief | Done, on `main`. |
| [LightingGaps.md](LightingGaps.md) | The known gaps the lighting left and the waves along coasts running north to south: lamps' shadows on units, sun shadows across a caster, light shadows at the smallest zoom, load hitches, the night in the editor, a camera on the art grid | Done, on `main`: Phase 7's spike kept today's camera. |
| [KnownGaps.md](KnownGaps.md) | The known gaps no other plan covers, each fixed or accepted: the code within CLAUDE.md's rules, orders and the save, waystones and exits, what each machine shows of the others, worlds and saves, the terrain and its brush, light and animation | Done, on `main`: every phase (1 to 7); Architecture.md's Known gaps now hold only gaps marked accepted and the six left to other plans. |
| [PlayerCharacter.md](PlayerCharacter.md) | The player's base character in place of the rogue: its body plan, face, hair and outfit, stylized gaits, attacks and casts, and customization (colours, parts, gear that shows) | Phases 0 and 1 done (the lantern removed from the player, the base look settled, the adventurer in play as the default look, the rogue retired from the looks) on `feature/rogue-rework`; Phase 2 next. |
| [DesignSystem.md](DesignSystem.md) | The design system: the specs' language and generated design tables, content rules and tuning views, one UI scale, the UI theme, skin and components, reduce flashing and motion | Proposal, not adopted yet; its open questions come first. |

## Next

[KnownGaps.md](KnownGaps.md) is done, on `main`. [PlayerCharacter.md](PlayerCharacter.md) is in progress on `feature/rogue-rework`: Phases 0 and 1 are done, and Phase 2 (stylized gaits) comes next. [DesignSystem.md](DesignSystem.md) is a proposal waiting to be adopted, and its open questions come first; the features below wait for plans of their own. The plans above leave no box unticked; `NetworkDelayPlayModeTests`' slow reliable round trip failed in both full PlayMode runs before WorldGeneration.md's Phase 6 merged, in an editor that had loaded a 16 x 16 world a dozen times, and passed alone (WorldGeneration.md's Phase 6), so it waits on a full run in a fresh editor.

## Later

Features planned for later, out of scope until they get a plan of their own:

- **Multiplayer testing.** Checking the game by hand with other players: Multiplayer Play Mode with 2 to 4 players in one editor, then a second machine on the network and over the internet. Until it comes, every feature is designed for multiplayer and checked by the EditMode and PlayMode tests that run in the editor (the in-process host and client), and plans carry no Multiplayer Play Mode checks (CLAUDE.md). The steps the plans kept for it: [Multiplayer.md](Multiplayer.md#running-two-players-in-the-editor) (two players, saves, runtime terrain edits, a virtual LAN, a bad connection), [WorldStreaming.md](WorldStreaming.md#checking-with-multiplayer-play-mode) (one world, different worlds, trips, saves), WorldGeneration.md's Phase 4 (players across the Overworld's regions), AsepriteCharacters.md's Phase 2 (a baked and a hand-drawn look), KeyBindings.md's Testing (rebinding and the menus), PlayerControls.md's Testing (moving, attacking and casting together).
- **A hosted relay** for internet play without a virtual LAN (Multiplayer.md's Phase 6; Steam is the likely transport if the game ships there).
- **Lava damage**: burning a unit standing in or next to lava, with a fire damage type and a burning status (TerrainTypes.md's Phase 9, DamageAndStatusEffects.md).
- **Weather**: rain, snow and fog with the light under them (Lighting.md's Later).
