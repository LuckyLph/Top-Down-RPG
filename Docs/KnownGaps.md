# Known gaps

Plan for closing the known gaps that no other plan covers (Architecture.md's Known gaps and oddities): each one fixed, or recorded as accepted with its reason. It turns the Known gaps feature from [Roadmap.md](Roadmap.md)'s Later into phases. Tick off the phase checklist as steps land; a gap fixed comes off Architecture.md's list in the same commit, and a gap accepted stays on it, marked accepted, with the reason and a link here.

Every rule in [Multiplayer.md](Multiplayer.md#rules-for-new-code-starting-now) applies. Multiplayer is checked by the PlayMode tests that run in the editor (an in-process host and client); checking by hand with Multiplayer Play Mode or a second machine is out of scope here and comes with a later feature of its own ([Roadmap.md](Roadmap.md)).

## Where we start

Architecture.md lists about 60 known gaps. Six are covered elsewhere or settled and stay out of this plan: the fixed build version (multiplayer testing, in Roadmap.md's Later), lava only blocking (lava damage, in Later), the placeholder status icons (DesignSystem.md's Later, by the user's choice), the camera off the art grid (LightingGaps.md's Phase 7), the editor's night as a preview (LightingGaps.md's Phase 6) and every player visible to every client (fine for 2 to 4 players). The rest, by area, with what this plan proposes for each in the triage below:

- **Gameplay**: waystones and world exits with no art; a trip's arrival unprotected; a bow shooting into a wall in range; unit picks ignoring the ability's filter; the weapon switch not saved; nothing physical at a world's edge, water, the ocean, lava or the cliff of raised ground; the walk and sprint never shown; feet sliding going north or south.
- **Multiplayer**: one save per install, a client keeping no copy of its place; remote arrows late; a copy's cast and windup on its own clock; a client's attacks seldom holding their contact frame; hit directions each machine's own; chunk holds outliving trips.
- **Worlds and streaming**: chunk load hitches and per-renderer texture arrays in the editor; a generated chunk keeping layers it no longer needs; a moved place taking the nearest walkable cell, reachable or not; coast and glow maps baked only on request; persistent ids repaired only for undoable edits; world search limits as code defaults; saved terrain edits naming layers by index; the Overworld's wave crests on end at its top edge and the sand nubs at the Coast region's joins.
- **Terrain and the Terrain Brush**: one layer with waves per area and the coast map's rebuild while painting; the walls' gap at the ground's corners; diagonal paths stepping; planks picking a way per cell; a layer showing a lower one under its corners; a bridge's end depending on the layers between; no preview for a flood fill, on raised ground or for walls across a chunk border; one level of raised ground; ground tiles found by name; the layer order unchecked; a scratch scene's grass layer; the soft look's first frames in the editor.
- **Lighting**: props darkening whole under the sun; the walls' light shadows by screen pixel; soft light shadows off the art grid; restoring our shadow shader rewriting URP's settings; Shadow Caster 2Ds making meshes in the editor, and the first dusk's frame.
- **Animation and the editor**: a buffered cast replacing the release; a cast aimed aside running toward its aim; the recipe inspector's cast over the run as one strip; TMP's fallback font changing in the editor.
- **Code against CLAUDE.md**: inline comments; `MobConfig`'s hard-coded layer mask and create-menu paths outside `TopDownRPG/`; static mutable state (`SceneQuery`, `UnityClock.Shared`); two classes in `SceneDefinitionEditor.cs`; the status dev panel finding its scope itself; duplicated tuning; unused or test-only API, the old sword icon and the dual-grid leftovers.

## Goals

- Every gap no plan covers either fixed or accepted with a reason, so Architecture.md's list holds only what is known and chosen.
- What a player meets first fixed first: the places they look for, the orders that misbehave, what they lose between sessions.
- The code back within CLAUDE.md's rules.
- Nothing a phase fixes changing what the other machines see without a PlayMode test of the host and client.

## Non-goals (for now)

- New features a gap points to but does not need: a second level of raised ground, split animation layers, knockbacks and dashes and the colliders they would need, a slot list for the saves in a menu.
- The status icons, which wait for DesignSystem.md, and adopting that proposal.
- The six gaps covered elsewhere (Where we start).

## Triage

Settled with the user on 2026-11-03: the triage as proposed, with the colliders accepted until a move leaves the paths, a save per session, the status icons left to DesignSystem.md and the phases in this order. Fix: closed in the phase named. Accept: stays on Architecture.md's list, marked accepted, for the reason given, when that phase lands. Decide: settled in its phase on a spike, a measurement or previews.

| Gap | Settled | Phase | Why |
|---|---|---|---|
| Inline comments, `MobConfig`'s layer mask and menu paths, static mutable state, two classes in one file, the status dev panel's lookup, duplicated tuning, unused or test-only API, the old sword icon, the dual-grid leftovers | Fix | 1 | CLAUDE.md's rules; no behaviour changes. A test-only API that a test needs stays `internal`. |
| Unit picks ignoring the ability's filter | Fix | 2 | The pick takes the ability's target filter, so an ally-only cast picks the closest ally. |
| A bow shooting into a wall in range | Fix | 2 | An attack order keeps closing in until the target is in range and in line of sight (the walls' layer), as a move order would. |
| A trip's arrival unprotected | Fix | 2 | The host keeps a travelling player untargetable and its statuses paused until its machine reports the fade in, with a time limit. |
| The weapon switch not saved | Fix | 2 | The equipped weapon's id in the player's save, the save's format version raised. |
| Waystones and world exits with no art | Fix | 3 | Generated in the project: a waystone prop (a standing stone that glows once discovered) and exit markers (a door for an interior, a worn mat or signpost for a road), placed by the world writer from the world's entries. Settled on previews. |
| Hit directions each machine's own | Fix | 4 | An arrow's hit carries the point it was shot from, so every machine turns the nudge and the kick from there. |
| A copy's windup holding until the contact | Fix | 4 | The hold ends after its clip's contact time plus a margin, the copy then returning to its locomotion; the late contact still plays its hit. |
| Chunk holds outliving trips | Fix | 4 | A trip releases the client's holds of the world it leaves, so a respawn waits for its chunk as the host does. |
| A copy's cast releasing on its own clock | Decide | 4 | Release on the completion message, on its own clock, or whichever comes first, measured with a delayed connection in the in-process tests (which Phase 4 adds). |
| A client's attacks seldom holding their contact frame | Decide | 4 | Hold on the client's own prediction of the hit (its target in range at contact), or accept as a round trip's cost. |
| Remote arrows late | Decide | 4 | Fly them from the shot's time, ahead by the round trip, or accept: they are visual and only the host's hit counts. |
| One save per install, a client keeping no copy of its place | Fix | 5 | One save file per session, New Game starting a new one, Continue loading the latest; a client keeps its own place per host session, used only when that host is gone. The menu shows no slot list (Non-goals). Chosen with the user over keeping one save and over a slot list. |
| Saved terrain edits naming layers by index | Fix | 5 | Edits name their layer by material, the save's format version raised, so a chunk's layers can be reordered or pruned. |
| A generated chunk keeping layers it no longer needs | Fix | 5 | The writer prunes empty layers (after the edits name layers by material), and `CreateChunk` lists the chunk's own ground layer instead of grass. |
| A moved place taking the nearest walkable cell, reachable or not | Fix | 5 | The nearest walkable cell reachable from the world's entry or a waystone. |
| Coast and glow maps baked on request | Fix | 5 | A build checks them and stops on a stale one; the brush marks its chunk stale as it paints the sea, its shore or lava. |
| Persistent ids repaired only for undoable edits | Fix | 5 | Repaired as a scene saves, too. |
| A world's search limits as code defaults | Fix | 5 | Fields on `WorldDefinition`, defaulting to today's values. |
| The sand nubs at the Coast region's joins | Fix | 5 | The world generator smooths the row step where the region's beach meets the world's. |
| The Overworld's wave crests on end at its top edge | Accept | 5 | 20 cells or more from every walkable cell, past every view; recorded unless Phase 5's coast work fixes the seams on the way. |
| Chunk loads hitching in the editor | Accept | 5 | Editor only; a player's load fits a frame (WorldScale.md). |
| Renderers outside play building their own texture arrays | Decide | 5 | Share them in edit mode too, by style, if the World Editor's whole-world steps run short of memory; else accept. |
| Ground tiles found by name | Fix | 6 | The tile keeps a reference to its material, so a rename keeps it. |
| The layer order unchecked | Fix | 6 | `TerrainBlendChecks` warns when a renderer's layers leave `TerrainStyle.LayerOrder`. |
| A scratch scene's grass layer | Fix | 6 | The generator points the entry at its own ground layer. |
| No preview for a flood fill, on raised ground or for walls across a chunk border | Fix | 6 | The Terrain Brush previews each: a flood fill up to the shape it would fill, raised ground through the renderer's preview, walls with both chunks' modules. |
| Planks picking a way per cell | Fix | 6 | A painter's choice: a tile per direction, the automatic one kept as the default. |
| The coast map's rebuild while painting | Fix | 6 | Rebuilt around the change only; the ocean recipe's unused glint phases removed. |
| One layer with waves per area | Accept | 6 | Only the ocean has waves; a second sea in one area is not planned. |
| The walls' gap at the ground's corners | Decide | 6 | The ground under a wall's cells, or the walls' fillet matched to the ground's corner; settled on previews. |
| A bridge's end depending on the layers between | Decide | 6 | One layer list for every chunk of a world, or the shader skipping empty layers between; settled on its cost. |
| The soft look's first frames in the editor | Fix | 6 | The editor compiles the soft variant before switching to it. |
| Diagonal paths stepping, a layer showing a lower one under its corners | Accept | 6 | The crisp look's style and a painting rule already documented; the generators keep to it. |
| One level of raised ground | Accept | 6 | A feature, not a gap (Non-goals). |
| The walls' light shadows by screen pixel | Fix | 7 | The walls' casters shadow the ground's sorting layers only, and units and props read them at their feet as they read the props' (LightingGaps.md's Phase 2). Settled on previews. |
| Props darkening whole under the sun | Accept | 7 | Chosen in LightingGaps.md's Phase 3: by column, a crown reads its own trunk's shadow. |
| Soft light shadows off the art grid | Accept | 7 | No light uses a soft shadow. |
| Restoring our shadow shader rewriting URP's settings, Shadow Caster 2Ds' meshes in the editor and the first dusk's frame | Accept | 7 | Handled by `LightShadowResources` and `ShadowCasterSceneSaving`; the first dusk's 6 to 7 ms frame is URP making its light textures. |
| The recipe inspector's cast over the run as one strip | Fix | 7 | The preview plays the cast over one chosen run frame. |
| A buffered cast replacing the release | Decide | 7 | Show the release frame for its own time before the buffered cast starts, if the cast timeline allows it without changing when hits land; else accept. |
| A cast aimed aside running toward its aim | Accept | 7 | Only split layers could turn the legs (Animation.md's Phase 5). |
| TMP's fallback font changing in the editor | Accept | 7 | Package behaviour; the rule on the static atlas's characters stands. |
| Nothing physical at a world's edge, water, the ocean, lava or the cliff | Accept | 2 | Bodies move only along paths, which go around them; the first move that leaves the paths (a knockback, a dash) brings the colliders, on a layer that stops bodies but not line of sight. Chosen with the user. |
| The walk and the sprint never shown | Accept | 2 | Shown once a slow or a haste reaches them; the PlayMode test covers them. |
| Feet sliding going north or south | Accept | 2 | Accepted in Animation.md's Phase 2. |

## Phases

Each phase ships on its own, keeps the game playable offline and hosted, and comes with tests. A phase whose look can change settles it on previews first. The accepted gaps of a phase are marked accepted in Architecture.md when the phase lands.

### Phase 1: the code within CLAUDE.md's rules

- [x] Inline comments moved to the docs or removed (`IClock.cs`, `SceneDefinition.cs`, `PlayModeBootstrapper`, `SceneBuildValidator`, `SceneDefinitionEditor`, `VContainerDiagnosticsBuildGuard`, the test files).
- [x] `MobConfig`'s obstacle mask from a named layer in its asset, not `1 << 8`; `MobConfig`, `TerrainType2D`, `TerrainMovementProfile2D` and `DualGridRuleTile` created from `TopDownRPG/...`.
- [x] `SceneQuery`'s shared buffer and `UnityClock.Shared` replaced by injected or per-instance state.
- [ ] `SceneDefinitionPathSync` in a file of its own, its `.meta` kept with it.
- [ ] The status dev panel injected (a registration the DevTools scope owns), or the reason it cannot be recorded.
- [ ] Duplicated tuning: one source for `nearestCellSearchRadius`; `MobMotor2D` and `MeleeDamageDealer` no longer serializing what `MobConfig` sets.
- [ ] Unused API removed, test-only API kept `internal` where a test needs it; `StaticSlash.png` removed and its test given a generated sprite; `DualGridRuleTile.DataTile` failing with a clear error instead of throwing.
- [ ] Tests: the touched classes' tests pass unchanged; Architecture.md updated.

#### Phase 1 notes

- Inline comments: `IClock.cs`, `SceneDefinition.cs`, `SceneBuildValidator` and `SceneDefinitionEditor` had none left. The rationale in `PlayModeBootstrapper`'s and `VContainerDiagnosticsBuildGuard`'s went to Architecture.md (Boot sequence, Editor tools); the test files' went into their tests' summaries and assertion messages. Shader and HLSL files keep their comments: HLSL has no XML docs, so those comments are its functions' descriptors.
- `MobConfig`'s obstacle mask: both assets (`Mob_Default`, `Mob_StressTest`) already held the `Obstacles` layer, so dropping the code default changed neither; a test checks every asset keeps it. An empty mask logs an error from the mob's perception and, as before, sees through everything. `MobConfig.Create` defaults the mask to the `Obstacles` layer by name, as the assets set it. The menus: `TopDownRPG/AI/Mob Config`, `TopDownRPG/AI/Navigation/Terrain Type`, `TopDownRPG/AI/Navigation/Terrain Movement Profile`, `TopDownRPG/World/Dual Grid Rule Tile`.
- Static state: `SceneQuery` reads a scene's roots into an array per call (its callers run on loads and builds, never per frame). `UnityClock.Shared` is gone: the Main scope registers a `UnityClock` singleton, and the five components that fell back to it (`PlayerAbilities`, `CharacterVisuals`, `MeleeDamageDealer`, `PlayerController`, `PlayerWeaponController`) log an error from `Start` without an injected clock. Every scope that builds them (Gameplay, world and chunk scopes, the in-process client) already resolved the Main scope's clock, so nothing changes in play; `PlayerAbilities` draws no cast gizmos in edit mode, where it has no clock.

### Phase 2: orders and the save

- [ ] `PointerTargetPicker` filtered by the ability's targets.
- [ ] An attack order closing in until its target is in range and in line of sight.
- [ ] A travelling player protected until its machine fades in, host-authoritative, with a time limit.
- [ ] The equipped weapon saved and restored, the save's format version raised.
- [ ] The colliders, the walk and the sprint, and the feet going north or south marked accepted in Architecture.md.
- [ ] Tests: an ally-only pick over a closer enemy; a bow ordered at a mob behind a wall walking round it before it shoots; a mob at an arrival point not hurting the arriving player until it fades in, on the host and a client; the weapon kept across a save. Architecture.md updated.

### Phase 3: waystones and exits

- [ ] Previews of a waystone, discovered and not, and of an interior's door and a road's exit marker, in the project's generated style; then their recipes and prefabs, and the world writer placing them from the world's entries.
- [ ] Tests: the writer placing a marker at every waystone and exit and moving it with its entry; a discovered waystone's look on the host and a client. Architecture.md updated.

### Phase 4: what each machine shows of the others

- [ ] An arrow's hit carrying where it was shot from.
- [ ] A copy's windup hold ending on its own.
- [ ] A trip releasing the client's chunk holds.
- [ ] A delayed connection for the in-process host and client (Unity Transport's network simulator, or a delay in the test session), since no test delays messages today.
- [ ] The copy's release, the client's contact hold and the remote arrows each decided on a measurement under that delay, then built or accepted.
- [ ] Tests: the in-process host and client under delay: a moving shooter's hit direction alike on both; a copy's windup ending without a contact; a respawn on a client waiting for its chunk. Architecture.md updated.

### Phase 5: worlds and saves

- [ ] A save per session, New Game starting a new one and Continue loading the latest; a client's own place per host session.
- [ ] Terrain edits naming their layer by material, the save's format version raised.
- [ ] The writer pruning a chunk's empty layers; `CreateChunk` listing the chunk's ground layer.
- [ ] A moved place taking the nearest reachable walkable cell.
- [ ] A build stopping on stale coast or glow maps; the brush marking its chunk stale.
- [ ] Persistent ids repaired as a scene saves.
- [ ] The world's search limits on `WorldDefinition`.
- [ ] The Coast region's joins smoothed; the Overworld rewritten and its maps baked.
- [ ] Shared texture arrays in edit mode, if a measurement of the World Editor's whole-world steps calls for them.
- [ ] Tests: two sessions saved side by side; an old save ignored with its warning; a client back on its own after its host is gone; edits kept across a layer reorder; a place on an islet moved to the mainland; a stale map stopping a build. Architecture.md, WorldStreaming.md and WorldGeneration.md updated.

### Phase 6: the terrain and its brush

- [ ] Ground tiles keeping their material by reference.
- [ ] The renderer's checks warning on the layer order.
- [ ] A scratch scene's ground entry pointed at the generator's ground layer.
- [ ] Previews for a flood fill, raised ground and walls across a chunk border.
- [ ] A planks tile per direction.
- [ ] The coast map rebuilt around a change only; the ocean recipe's glint phases removed.
- [ ] The soft variant compiled before the editor switches to it.
- [ ] The walls' corner gap and the bridge's end settled on previews and their cost, then built or accepted.
- [ ] Tests: a renamed material keeping its tile; the order warning; the brush's previews; a planks direction kept; the coast map's rebuild touching only its region and matching a whole rebuild; the `ShaderCompile` tests run if a shader changes. Architecture.md, TerrainBlending.md and TerrainTypes.md updated.

### Phase 7: light and animation

- [ ] Previews of a unit south of a wall with the walls' shadows read at its feet; then the walls' casters and the sprites' shader.
- [ ] The recipe inspector playing a cast over one run frame.
- [ ] The buffered cast's release decided on the cast timeline, then built or accepted.
- [ ] Tests: a unit in a wall's shadow dark whole, its head too; a unit beside it lit; the inspector's preview; the `ShaderCompile` tests run. Architecture.md and Lighting.md updated.

## Testing

- EditMode: each fix's own tests as its phase lists, render tests through the pipeline for what draws differently.
- PlayMode: the in-process host and client for everything another machine sees (Phases 2, 3, 4 and 5).
- `ShaderCompile` after every shader change.

## Open questions

- Whether the waystone and exit markers block movement (a waystone as a prop with a collider) or are drawn on the ground only (Phase 3).
