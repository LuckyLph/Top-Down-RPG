# Top-Down-RPG

## What This Is

Top-Down-RPG is a solo, local Unity project for building the reusable tools and gameplay systems of an original top-down action RPG. It takes high-level inspiration from the accessible world-building of *Necesse*, the story and town-facing elements of *Stardew Valley*, and the readable action combat of *RuneScape: Dragonwilds*, while developing its own mechanics, art direction, and content.

The immediate product is an iterative clearing-to-town vertical slice: the player begins with equipment and abilities, learns through combat along a linear path, and reaches a starting town that introduces the foundations of quests, crafting, and character progression. The project is deliberately a proving ground for systems and feel before it becomes a full game.

## Core Value

Make fast, satisfying, highly readable top-down combat whose visual effects push sprite-based presentation beyond basic sprite rendering.

## Requirements

### Validated

- ✓ Player movement, directional facing, input actions, and a data-driven melee attack exist in the current Unity project.
- ✓ Enemy perception, navigation, finite-state behavior, melee damage, health, death, and damage feedback are implemented as reusable gameplay components.
- ✓ Tilemap navigation, prefabs, ScriptableObject gameplay configuration, camera follow, HUD updates, and Unity EditMode/PlayMode test foundations are in place.
- ✓ The project uses Unity 6000.4.2f1 with URP, the Unity Input System, and a local Unity MCP workflow for editor-assisted iteration.

### Active

- [ ] Build reusable game-development tools and system seams that make combat, content, and visual experiments fast to create, inspect, and revise.
- [ ] Evolve combat into a fast action system with weapon and ability combinations, responsive control, and clearly telegraphed enemy behavior.
- [ ] Establish a visual-effects toolkit using particles, lighting, and shaders to make top-down sprite combat expressive without sacrificing gameplay readability.
- [ ] Create a playable clearing-to-town journey that teaches the combat loop through play and provides a coherent arrival at the starting town.
- [ ] Expose minimal but extensible town-system foundations for quests, crafting, and character progression after the player reaches town.

### Out of Scope

- A complete commercial game, extensive content production, or a finished campaign — the current milestone proves foundations and the core loop first.
- Multiplayer, online services, backend infrastructure, or release/distribution work — this is a local single-player project for the developer's own experimentation.
- Copying the mechanics, characters, art, or story of referenced games — references inform desired feel and broad genre expectations only.
- Treating subjective combat feel or visual quality as arbitrary numeric targets — these are evaluated through frequent playable review and iteration.

## Context

The current codebase already supplies a Unity component-oriented foundation: player input and weapon attacks, tilemap navigation, mob state machines, health/damage/death feedback, HUD, prefabs, and authored ScriptableObject data. The next work should improve and compose those pieces rather than discard them.

The intended player journey starts in a clearing with starting equipment and abilities. A linear combat path teaches the player through fast, readable encounters before reaching a starting town, where questing, crafting, leveling, and later story development can be introduced. Combat should combine quick action with disciplined positioning: attacks and enemy intent need to be legible enough for players to make meaningful decisions, while weapons and abilities remain expressive and responsive.

This is currently for the developer alone, running locally. Success is a continuously playable vertical slice that the developer judges to look good and play well; experimentation, visual inspection, and iteration are more useful than premature production metrics.

## Constraints

- **Technology**: Continue with Unity 6000.4.2f1, URP, C#, the Input System, 2D Tilemaps, and the existing test assemblies — they are the established project platform.
- **Architecture**: Prefer runtime discovery, ownership-based lookups, and local helper components for scene relationships. Reserve serialized fields for configuration, asset references, and intentional overrides.
- **Visuals**: Effects must reinforce player understanding of attacks, enemy telegraphs, damage, and space; spectacle must not obscure gameplay decisions.
- **Scope**: Build a small, revisable vertical slice and reusable system/tooling foundations before expanding world scale, content breadth, or narrative depth.
- **Environment**: Develop and run locally on Windows. Unity MCP remains a loopback, project-scoped editor tool, not a runtime dependency.

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Build foundations before a full game | A compact vertical slice lets systems, tools, combat, and visuals be tested and revised cheaply. | — Pending |
| Use a clearing-to-town journey as the first slice | It links combat teaching to an eventual quest/crafting/progression hub in one coherent path. | — Pending |
| Prioritize fast, readable action combat | Responsive movement and ability use need clear enemy behavior and attack telegraphs to support meaningful positioning. | — Pending |
| Make particles, lighting, and shaders part of gameplay presentation | Visual experimentation is a core project goal, provided effects preserve combat readability. | — Pending |
| Treat external games as feel references only | The project needs original mechanics, world, story, and presentation. | — Pending |

## Evolution

This document evolves at phase transitions and milestone boundaries.

**After each phase transition** (via `$gsd-transition`):
1. Requirements invalidated? Move to Out of Scope with reason.
2. Requirements validated? Move to Validated with phase reference.
3. New requirements emerged? Add to Active.
4. Decisions to log? Add to Key Decisions.
5. Is “What This Is” still accurate? Update if it has drifted.

**After each milestone** (via `$gsd-complete-milestone`):
1. Review all sections.
2. Check that the core value is still the right priority.
3. Audit Out of Scope items and their reasons.
4. Update Context with the current playable state and lessons from iteration.

---
*Last updated: 2026-07-19 after initialization*
