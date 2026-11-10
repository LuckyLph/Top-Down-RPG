# Design system

Plan for the game's design system, adopted on 2026-11-10. It was proposed on 2026-11-03 and revised after an independent review the same day (see Tools considered). Tick off the phase checklist as steps land, and move each settled behaviour into [Architecture.md](Architecture.md) as its code arrives.

The game's design system has three parts:

- **Language.** The words and the format the specs use, and reference tables generated from the assets.
- **Mechanics.** The rules every piece of gameplay content follows.
- **UI and UX.** The foundations, components and patterns every screen is built from.

Its numbers and rules are backed by tools and tests, so they cannot drift from the game. Architecture.md describes what exists; this file describes the target and the order to build it.

Every rule in [Multiplayer.md](Multiplayer.md#rules-for-new-code-starting-now) applies. Nothing here is game state: the theme, the generated tables and the tests change no one's simulation, and the player's display preferences stay on their own machine.

Two words have one meaning each in this file:

- A **UI px** is one unit of a UI canvas. It is drawn as a whole number of display pixels.
- A **display** is the monitor or window. A **screen** is a UI screen, such as the HUD or the options screen.

## Where we start

The project already has most of the mechanics part, without the name:

- **Content is data.** Weapons, abilities, classes, statuses, combat profiles, mob configs, locomotion settings and the character colours (`CharacterColours`: swatch lists and the choices they colour) are read-only ScriptableObject definitions with `Create` factories for tests. Looks are entries of `CharacterLookCatalog`, and a look's swappable parts (`CharacterParts`) are written by the baker beside its sprites. Mob configs are in `Assets/Settings/AI`; the others are in `Assets/Data`.
- **Networked content has ids.**
  - Weapons, statuses and looks are listed in catalogs (`WeaponCatalog`, `StatusEffectCatalog`, `CharacterLookCatalog`) whose index is their id on the wire. A player's colour and part picks are indexes into `CharacterColours`' and `CharacterParts`' lists.
  - Only `WeaponCatalog` says it is append-only: the adventurer took the rogue's place in the look catalog.
  - Join approval no longer compares builds while the build version stays fixed (Architecture.md's Known gaps), so these ids only hold between machines on the same build.
  - Saves name assets by GUID, and the settings file names looks, swatches and parts by name.
- **Mechanics share building blocks.** They are composed from hits, damage types, statuses, timelines, areas and targeting, specified in [DamageAndStatusEffects.md](DamageAndStatusEffects.md), [PlayerControls.md](PlayerControls.md) and [Animation.md](Animation.md).
- **Specs share a shape, roughly.** Where we start, Goals, Non-goals, Decisions, Phases, Testing and Open questions recur. Some specs add Terms, Behaviour, Architecture or Later. Architecture.md and DamageAndStatusEffects.md draw diagrams in Mermaid.
- **Content tests exist for some kinds**:
  - `StatusEffectTests.Content_*`, `AbilityEffectTests.Content_*`, `DamageResolutionTests.Content_*` and `StatusFeedbackTests.Content_*`;
  - `WeaponCatalogTests`, `CharacterPrefabTests`, `CharacterLookTests`, `PropPrefabTests` and `WorldValidationTests`;
  - the input content tests;
  - since PlayerCharacter.md and KnownGaps.md: `AdventurerRecipeTests`, `CharacterColoursTests`, `CharacterPaletteTests`, `CharacterOutfitsTests`, `WorldMarkersTests` and `OptionsMenuControllerTests` read project assets too.
  Some of them pin asset values (the sword's 20, Blank Bolt's 12 Frost, the resistance cap's 80).
- **Asset values are restated by hand.** Architecture.md restates each weapon's damage and timings, the `CombatSettings` values, each status's effect, each blank ability's numbers and the Weasel's health. Every tuning change therefore needs a doc edit.

The UI has no system yet. The table below is what it looked like on 2026-11-03, checked again on 2026-11-10 after KnownGaps.md and PlayerCharacter.md merged.

| Area | Today |
|---|---|
| Scaling | Three setups across the canvases:<br>• The HUD scales with the display from 1920 x 1080.<br>• The main and in-game menus scale from 640 x 360.<br>• The damage popups and the transition fader keep a constant pixel size.<br>Every canvas scaler keeps 100 reference pixels per unit, while every sprite under `Assets/Sprites` imports at 32. |
| Text | **Sizes:** ten font sizes, from 11 to 40.<br>**HUD:** 28 of its 35 texts are 12 or 14 px at 1080p, and the six ability names auto-size down to 8 px. All of these are under the minimum in the Xbox Accessibility Guidelines (XAG) for PC: a body height (ascender to descender) of 18 px.<br>**Menus:** text is 27 to 120 px at 1080p. The key-binding rows auto-size down to 9 UI px, the notice is 12 and the section headers 13.<br>**Popups:** damage popups are 32 px on any display, so they shrink at 4K. |
| Font | TextMesh Pro's LiberationSans SDF everywhere (SIL OFL 1.1). Its atlas padding allows an outline of only about 0.4 UI px. Fonts licensed for use in a game are exempt from the art rule. |
| Colour | **In view fields:** raw colours in `AbilitySlotView` (frames, failure flash), `MoveMarker` (move, attack move) and `PlayerHudView` (weapon icon tints).<br>**In component data:** most colours are serialized on Image and TMP components, button colour blocks and SpriteRenderers (the hover ring, the Weasel's bar).<br>**On gameplay assets:** damage-type, heal and immune colours on `CombatSettings`; a colour on each status definition; the waystone popup's colour on `GameplaySettings`.<br>Three reds mean danger and two mean health. Nothing checks contrast: the Physical popup red is 3.1:1 against black, and the join field's placeholder about 2.9:1. |
| UI art | Most of the HUD's frames and fills are tinted copies of `Assets/Sprites/UI/Square.png`. The health fill is `HealthBarFill.png`, and the weapon slot has a ring and a mask of its own and shows the weapon's generated icon. The menus are plain rectangles. The world health bar is a smooth rounded sprite (`Assets/Textures/UI/RoundedBar.png`). There is no button or panel art. |
| Components | The key-binding row, section header, damage popup and move marker are prefabs. Buttons, panels and confirmations are built in place in each menu. There are no tooltips: a status shows two letters, and its description is in the data but shown nowhere.<br>**The options screen** reuses the key-binding row for its Look row, eight part rows and fifteen colour rows, which name a swatch without showing it. With the binding rows and section headers that makes 37 rows in a 240 UI px box with no scrolling, where they need about 800; how they look squeezed is unchecked. The screen is still titled "Controls". |
| Colour alone | **Markers:** the move and attack-move markers differ only by colour. The hover ring stays under the hovered enemy and comes with the attack cursor.<br>**Statuses:** on the HUD a status pairs its colour with a two-letter abbreviation; no status has an icon yet. Over mobs, the world status icons and the status auras show colour alone.<br>**Popups:** they are coloured by damage type. Heal has its "+", Immune its text, and periodic ticks are smaller.<br>**Waystones** show their discovery by sprite and light, not colour alone. |
| Preferences | `settings.json` holds the bindings, the player id, the look and the colour and part picks (format version 1). There are no display or accessibility options. |

## Goals

- Every design number can be read without opening an asset, and no doc restates one by hand.
- Tests check every kind of content.
- Every screen is built from shared foundations (scale, type, colour) and shared components, so one change reaches every screen.
- Tests enforce an accessibility baseline: text size, contrast, never colour alone, flashing, reachable controls.
- Text is readable by default on any common display, and players can make the UI larger, reduce flashing and turn off screen motion.

## Non-goals (for now)

- **A design tool or a token file outside Unity.** The theme asset is the source of every UI value. If a design tool such as Penpot joins later, a JSON export in the W3C design-token format can be generated from that asset.
- **UI Toolkit.** uGUI stays: Unity 6's manual still recommends it for runtime UI. The menus may move to UI Toolkit at an editor upgrade.
- **A combat or economy simulator.** Combat has no random rolls (crits, dodge and application chances are non-goals in DamageAndStatusEffects.md), so its numbers follow from formulas.
- **Under Later:** runtime theme switching, a high-contrast theme, text scaling to 200%, and status icon art.
- **Localisation.** Player-facing text is in code constants and data for now. Layouts leave room for longer strings.
- **Controller glyphs.** Gameplay is mouse and keyboard only (KeyBindings.md).
- **Audio.** The project has none yet. The feedback pattern gains a sound channel when it does.
- **Debug tools:** gizmos and the IMGUI dev panels.

## Principles

This is a draft drawn from the existing specs, to confirm (Open questions). A spec's decisions should follow from one of these.

1. **Co-op first.** The host decides, and every machine shows the same outcomes, late joiners included. One player is just the smallest party.
2. **Readable at a glance.**
   - Pixel art is read one art pixel at a time.
   - Every attack telegraphs before it lands, and every order and cast is answered on the frame it is given.
   - Text meets its size and contrast minimums by default.
   - Information never depends on colour alone.
3. **Direct control.** Click to move, with orders that cancel and buffer like a MOBA's. What the player sees is what the game will do.
4. **Content is data.** Mechanics are assets composed from a small set of building blocks. A new idea first extends a block in its spec, then becomes content.

## Decisions

| Topic | Decision |
|---|---|
| Source of truth | Assets hold the current numbers. Specs hold the intent, the rules and the reasons for chosen values. `Docs/Generated` holds the current numbers in readable form. |
| Generated tables | Written by an editor tool from the assets and the real formulas (`DamageMath`), formatted with the invariant culture. An EditMode test fails when a committed table differs from what the tool would write. |
| Content checks | One table-driven EditMode fixture covers every content kind. It reads serialized values, not the getters that clamp them. |
| Balance | Derived columns in the generated tables (damage per second, hits and time to kill) instead of a simulator, until combat has random rolls or interacting procs. |
| UI framework | uGUI with TextMesh Pro. |
| UI scale | A UI px is drawn as a whole number of display pixels: the UI zoom. See Foundations for how it is chosen. |
| Text | Five sizes. The smallest, `Caption`, is 7 UI px, a 19.5 px body at 1080p. Nothing is smaller, auto-sizing included. |
| Font | A project font asset generated from `LiberationSans.ttf`, with enough atlas padding for a 1 UI px outline. The theme holds it. A pixel font is an open question. |
| Colour | UI code and prefabs name colour roles, never raw colours. Damage-type, status and waystone colours stay on their gameplay assets but follow the same contrast and never-colour-alone rules. |
| Theme | **Asset:** a read-only `UiTheme` in Gameplay holds the colour roles, the text sizes and the font.<br>**Applying it:** themed components reference the asset and set their own instance once when they wake. Nothing reads the theme per frame, and no tool writes it into prefabs or scenes.<br>**Edit mode:** prefabs show their serialized values in the editor. |
| UI art | Generated in the editor from a recipe, like every other sprite: 9-sliced frames, bars and outlines in greyscale, tinted by role. |
| Accessibility baseline | The Game Accessibility Guidelines' basic items listed under Accessibility, plus XAG 101's text size and XAG 102's contrast. We link and paraphrase them rather than copy their text. |
| Preferences | UI size (Phase 3); reduce flashing and reduce motion (Phase 6). They are local, saved in `settings.json`, and never replicated. |
| Diagrams | Mermaid. Use `stateDiagram-v2` for state machines and lifecycles, `sequenceDiagram` for host and client messages, and `flowchart` for flows and scope graphs. Timelines are tables. |

## Language

### Vocabulary

These are the words every spec uses with one meaning. A new building block adds a row here when its spec lands.

| Term | Meaning | Spec |
|---|---|---|
| Unit | Anything with a `DamageReceiver` and `Health`: a player or a mob. | DamageAndStatusEffects.md |
| Faction | A unit's side (`Players`, `Mobs`). Harm crosses factions and help stays within one. | DamageAndStatusEffects.md |
| Hit | An amount, a damage type and the statuses it applies. Weapons, mob attacks and abilities all deal hits. | DamageAndStatusEffects.md |
| Damage type | `Physical`, `Fire`, `Frost`, `Lightning`, `Poison`, `True`. Append-only. | DamageAndStatusEffects.md |
| Defence | A unit's resistances (percent per type, between the floor and the cap), damage immunities and status immunities, from its combat profile and its statuses. | DamageAndStatusEffects.md |
| Status | A timed buff or debuff with tags, stacking (`Refresh`, `AddStack`, `Independent`), an optional periodic effect, modifiers and controls. | DamageAndStatusEffects.md |
| Control | `Stun`, `Root` or `Silence`: what a status stops a unit doing. | DamageAndStatusEffects.md |
| Timeline | An action's phases: an attack's windup, contact and recovery; a cast's gather, release and follow-through. Gameplay runs the timeline and the animation follows it. | Animation.md |
| Attack | A weapon's or a mob's repeated hit on a timeline, with a cooldown from its start and cancel rules. It is delivered in a melee area or as a projectile. | Animation.md, PlayerControls.md |
| Ability | A slotted action with targeting (`None`, `Direction`, `Point`, `Unit`), range, cast time, cooldown, a movement rule and an effect (a hit, a heal, an area). | PlayerControls.md |
| Area | Where an effect searches for units: a circle around the caster or a point, a capsule along a direction, or a box in front of an attacker. | PlayerControls.md, Animation.md |
| Order | What the player asks a unit to do: move, attack move, stop, cast. Orders buffer and cancel by the rules of the current timeline. | PlayerControls.md |
| Authority, mirror | The host (or the offline game) decides. Clients mirror what it decided and show it. | Multiplayer.md |
| Definition, catalog | A read-only content asset, and the append-only list of a networked kind whose index is the wire id. | Mechanics, below |

### Writing a spec

- **Sections**, in order: Where we start, Terms, Goals, Non-goals (for now), Decisions, Behaviour, Architecture, Networking, Removals and migrations, Phases (with Later), Testing, Open questions. Leave out the ones that would be empty.
- **Decisions** says what is decided now. A changed decision says what it replaced and in which phase.
- **Numbers.** A spec states targets and rules ("a Weasel dies in three to five sword hits"). A decision may record the value chosen and what it was compared with. Current values are in `Docs/Generated`.
- **Diagrams** follow the Decisions table above.
- **`Docs/Generated`** is written only by its tool. Never edit it by hand.

### Generated tables

`Tools/TopDownRPG/Design/Write Design Tables` writes one Markdown file per kind into `Docs/Generated/`. Rows are sorted by catalog index, or by name for kinds without a catalog, so the diffs stay small.

| File | Rows | Columns |
|---|---|---|
| `DamageTypes.md` | Each damage type, plus the heal and immune rows | Popup colour (hex) and its contrast against the popup outline; the resistance floor, cap and minimum damage from `CombatSettings` |
| `Weapons.md` | `WeaponCatalog` | Index, damage and type, cooldown, windup, contact and recovery, cancel rules, attack range, delivery (area, or projectile speed, range and homing), abilities. Derived: attack period, damage per second, and hits and time to kill each mob prefab |
| `Abilities.md` | Every `AbilityDefinition` | Used by (class slot or weapon), targeting and filter, range, cast time, cooldown, movement, bufferable, effect (damage and type, heal, statuses, area), cast clips. Derived: damage per cooldown second |
| `StatusEffects.md` | `StatusEffectCatalog` | Index, kind, tags, abbreviation, colour, description, duration, stacking and cap, periodic amount and interval, modifiers, controls, immunities. Derived: the raw periodic total at one stack and at the cap |
| `Units.md` | The player and mob prefabs | Health, combat profile (resistances and immunities), attack (damage, type, interval, timeline), move speeds. Derived: the time one mob takes to kill a player |
| `UiTheme.md` | Each colour role and text size | Value; for sizes, the body height in px at 1080p; the contrast of every role pair that a themed text uses |

The derived columns, at attack speed 1:

- **Attack period.** The next attack may start once the cooldown is over and the current attack has reached a point where it can be cut short. That point is the end of the contact when orders may cut the recovery short, and the end of the timeline otherwise. So the period is the cooldown, or that time if it is longer. For every weapon and the Weasel today, the period is the cooldown.
- **Hits to kill.** The target's health divided by the hit as `DamageMath.Resolve` deals it against the target's profile, rounded up.
- **Time to kill.** (hits − 1) x period + windup, because a hit lands at contact, at the end of the windup. Projectile flight time is not included. Attacks start on the first frame after they are ready, so real times run up to one frame per hit longer.
- **Damage per second.** The resolved hit divided by the period.
- **Periodic total.** floor(duration / interval) ticks of amount x stacks, before the target's defence. A status ticks one interval after it lands and on the instant it expires.

The test's failure message names the menu item, and says to regenerate after a merge, because two branches that both tune will conflict in these files.

## Mechanics

### Content rules

1. **Assets.** Every kind of content is a ScriptableObject definition, read-only at runtime, with a `Create(...)` factory for tests.
   - Definitions live under `Assets/Data/<Kind>`.
   - Mob configs stay in `Assets/Settings/AI` until they move to `Assets/Data` with their `.meta`.
   - What a tool writes (a look's parts and animation sets) stays beside the sprites it writes.
   - Looks are entries of `CharacterLookCatalog`.
2. **Ids.** A kind that crosses the wire has a list whose index is the id: a catalog, or a definition's own list (the colour choices and swatches, the parts' options). Entries are appended, never reordered or replaced. The ids only hold between machines on the same build, since join approval does not compare builds while the version stays fixed. Saves name assets by GUID, and the settings file names choices by name.
3. **Shared values.** Values shared across a system (floors, caps, layers, popup colours) live in that system's one settings asset (`CombatSettings`, `HitFeedbackSettings`), not on each definition.
4. **Building blocks.** A definition only combines building blocks. It holds no game logic of its own (derived helpers such as `PlayerWeapon.HitCentre` are fine). A new block is added in its spec first. Then it gets a vocabulary row, a `Create` parameter, tests and a column in its generated table.
5. **Display text.** Every definition or option players see (a swatch, a colour choice, a part) has a display name unique within its kind or list. Every status has an abbreviation unique among statuses and a description, because the HUD and its tooltips show them.

### Adding content

1. Create the asset in its kind's folder with the inspector. Append it to its catalog if the kind has one.
2. Run Write Design Tables and read the diff in `Docs/Generated`: the derived columns show what the new numbers mean.
3. Run `ContentTests` and the kind's own test class.
4. A spec states the intent ("Chill slows enough to kite a Weasel") if the content has one. The numbers stay in the asset.

### Tuning

Unity Search's table view shows a kind's assets side by side with editable property columns. It is a spreadsheet over the assets themselves, so there is no second copy, and it exports to CSV or JSON.

Phase 2 adds a saved search per kind (weapons, abilities, statuses, mob configs) under `Assets/Editor/Design/Searches`. A saved search keeps its table layout. Edits in the table skip the inspector, `[Min]`, `[Range]` and `OnValidate`, so `ContentTests` is what catches an out-of-range value.

## UI and UX

### Foundations

**Scale.** Every UI canvas carries a `UiPixelScale`. It sets the canvas scaler to a constant pixel size, with the scale factor `UiScale` gives and 32 reference pixels per unit, so one sprite pixel is one UI px.

`UiScale` picks the UI zoom in three steps:

1. **Smallest readable zoom.** It takes the smallest whole zoom at which `Caption` reaches a body height of 18 px per 1080 display lines. In LiberationSans that is the display height divided by 390, rounded up.
2. **Fit.** It lowers the zoom until the layout's 480 x 270 UI px still fit the display.
3. **Size step.** It adds the player's UI size step, where the result still fits.

| Display | Zoom | UI px | Large |
|---|---|---|---|
| 1280 x 720 | x2 | 640 x 360 | does not fit |
| 1366 x 768, 1024 x 768 | x2 | 683 x 384, 512 x 384 | does not fit |
| 1536 x 864 | x3 | 512 x 288 | does not fit |
| 1440 x 900, 1600 x 900 | x3 | 480 x 300, 533 x 300 | does not fit |
| 1680 x 1050 | x3 | 560 x 350 | does not fit |
| 1920 x 1080 | x3 | 640 x 360 | x4 (480 x 270) |
| 1920 x 1010 (a window on a 1080p display) | x3 | 640 x 337 | does not fit |
| 1920 x 1200 | x4 | 480 x 300 | does not fit |
| 2560 x 1440 | x4 | 640 x 360 | x5 (512 x 288) |
| 2560 x 1600 | x5 | 512 x 320 | does not fit |
| 3840 x 2160 | x6 | 640 x 360 | x7 and x8 |

At 720p, 1080p, 1440p and 4K this equals the Pixel Perfect Camera's zoom, so a UI px is an art pixel. Elsewhere the UI is one step larger than the world's pixels, which keeps text readable where the camera rounds down. Layouts anchor to the canvas's edges and must fit 480 x 270 UI px.

**Type.** These are TMP font sizes in UI px. The body heights are for LiberationSans (0.93 of the size) at 1080p.

| Size | UI px | Body at 1080p | Used for |
|---|---|---|---|
| `Caption` | 7 | 19.5 px | Key labels, cooldown seconds, stack counts, periodic popups |
| `Body` | 9 | 25 px | Menu rows, buttons, notices, ability names |
| `Heading` | 11 | 31 px | Section headers, damage popups |
| `Title` | 14 | 39 px | Screen titles |
| `Display` | 20 | 56 px | The game's name on the main menu |

Further rules:

- **Auto-size** is allowed only with its minimum at `Caption` or above.
- **Damage popups** use `Heading`, so periodic ticks at 0.75 of it (8.25) stay above `Caption`.
- **Text style:** sentence case, no italics or all-caps below `Title`, left-aligned rows (the Game Accessibility Guidelines' "simple, clear text formatting").

**Colour roles.** Each role starts from the colour most used for it today. Where a role merges several of today's colours, or a colour fails its contrast pair, Phase 4 settles the value on previews.

| Role | Starts from | Replaces |
|---|---|---|
| `Text` | White | Light text on dark panels |
| `TextOnControl` | (0.1, 0.1, 0.12) | The dark button labels and the HUD's dark labels |
| `TextMuted`, `TextDisabled` | The menus' greys | Secondary and disabled text |
| `Placeholder` | Retuned to 4.5:1 | The join field's placeholder (about 2.9:1 today) |
| `Accent` | (1, 0.85, 0.4) | Section headers, the notice, key labels, the selected control |
| `Surface`, `SurfaceEmpty` | The filled slot frame (0.12, 0.12, 0.16, 0.85); the empty one at 0.35 | HUD frames |
| `SurfaceRaised` | The more used of the two menu box colours | Menu boxes |
| `Scrim`, `ScrimModal` | Black at 0.6; black at 0.85 | The in-game menu's dimmer; the confirmation and listening panels |
| `Shade` | Black at 0.65 | Cooldown and elapsed fills, the weapon icon's mask |
| `ControlNormal`, `ControlHighlighted`, `ControlPressed`, `ControlDisabled` | Today's button colour block | Button states (`Selected` uses `Accent`) |
| `Danger` | The hover ring (1, 0.3, 0.25) | The attack-move marker and the failure flash |
| `Positive` | The move marker | |
| `Health`, `HealthTrack` | The HUD's fill and background | The world bar's fill and frame |
| `IconMissing` | White at 0.25 | The missing weapon icon |
| `Outline` | Black | The outline of text drawn over the world |

The Physical popup red (3.1:1 against the outline) is retuned on the same previews. It stays on `CombatSettings` with the other damage-type colours.

**Art.** `UiSkinRecipe` is an editor recipe in the manner of the prop and terrain recipes.
- It writes greyscale 9-sliced sprites into `Assets/Sprites/UI/Skin`, which the pixel art postprocessor imports at 32 PPU: a panel with a 1 UI px border and cut corners, a raised panel, a slot frame, a bar frame and fill, and marker shapes.
- Images and SpriteRenderers tint them by role.
- They replace `Square.png`.
- The look is settled on previews first, as for any art.
- Paddings and gaps are multiples of 2 UI px. Spacing becomes theme sizes only if the components show a need.

### Components

UI is built from these prefabs in `Assets/Prefabs/UI/Components` (Phase 5), with prefab variants for real variation. A component prefab carries its text size and font; a change to the Button prefab reaches every button.

| Component | Today | Variants |
|---|---|---|
| Button | Built in place in each menu | Primary, Secondary, Danger |
| Panel | Plain rectangles | Surface, Raised, Scrim, Modal scrim |
| Dialog | The in-game menu's and the options screen's confirmations | One, with a question, a confirm label and Cancel |
| Section header | `OptionsSectionHeader` | |
| Row | `KeyBindingRow`, which also serves the Look, part and colour rows | Binding, Choice (Look, parts, UI size), Swatch (colours, the swatch shown beside its name), Toggle (reduce flashing, reduce motion) |
| Scroll list | None: the options rows overflow their box | One, keeping the selected row in view |
| Notice line | In the options screen, and the main menu's status line | |
| Slot | `AbilitySlotView` in the HUD | |
| Status icon | `StatusIconView` in the HUD | |
| Tooltip | None | Shows a status's or an ability's name and description while the pointer is over its icon or slot, or it is selected. The status row then blocks clicks, as the ability bar does. |
| Bar | The HUD's health fill | Screen (HUD), World (`WorldHealthBar` takes the same roles) |
| Popup text | `DamagePopup` | |
| Marker | `MoveMarker`, the hover ring | Move, Attack move, Hover, each a different shape |

The themed components:

- **`ThemedText`:** a TMP text's colour role, background role and size. It sets the font, size, colour and outline on its own instance when it wakes.
- **`ThemedGraphic`:** an Image's or a SpriteRenderer's role.
- **`ThemedSelectable`:** a Selectable's colour block, set from the control roles.

Each references the `UiTheme` asset directly, so pooled prefabs that are never injected (the move marker) work too. Views that pick colours at runtime (the failure flash, the marker kinds, the missing weapon tint) also reference the theme, and read their roles' colours once when they wake.

### Patterns

- **Feedback.** Every order and cast is answered on the frame it is given: a marker, a cast clip, or a failure flash on its slot. Every hit shows a popup and a flash on every machine that learns of it.
- **Menus.**
  - Escape or gamepad East closes the top layer through `MenuStack`.
  - An action that ends the game for other players, or loses data, asks first: the host's Leave game and Quit online, and Reset all (KeyBindings.md).
  - Changes apply and save at once, with no Apply step.
- **Focus.**
  - Every control can be reached by mouse, keyboard and gamepad, through `UISelection` only.
  - A screen selects its first control when it opens.
  - Closing a screen selects the control that opened it.
- **Messages.**
  - Each screen has one place for messages: the main menu's status line, the options screen's notice.
  - In game, short notices float over the local player as popups (Waystone discovered).
  - A failure says what happened and, when the player can act on it, what to do.
- **World-space UI.**
  - Bars and status icons sit on the `Overlay` layer, unlit.
  - Markers stay on the `Terrain` layer, under the units they mark.
  - Text drawn over the world has an `Outline` of at least 1 UI px.
  - World-space UI is sized in world units and follows the camera's zoom, not the UI size step.
- **Co-op.**
  - The HUD shows only the local player.
  - Other players' state will show above their units in the world. Players have no world bar yet (Later).
- **Never colour alone.** A colour that carries meaning comes with a shape, an icon, a label or a size.
  - **Phase 5:** the move, attack-move and hover markers get different shapes, and HUD statuses get tooltips.
  - **Known gaps:** world status icons and auras stay colour-only until statuses have icon art (Later), and a popup's damage type stays colour-only (Open questions).

### Accessibility

| Rule | Threshold | Enforced by |
|---|---|---|
| Text size | A body height (ascender to descender) of at least 18 px per 1080 display lines, by default (XAG 101's PC minimum): `Caption` at the chosen zoom | `UiScale`'s zoom rule; `UiThemeTests` (from the font's metrics); `UiContentTests` (sizes and auto-size minimums) |
| Contrast | **Text:** 4.5:1 under `Title`, 3:1 for `Title` and above (XAG 102's large text is a 36 px body at 1080p).<br>**Shapes that carry meaning:** 4.5:1 when small, 3:1 when large.<br>**Others:** 3:1 for disabled text; 4.5:1 for placeholders.<br>Text over the world is measured against its outline. | `UiThemeTests`: every pair of text role and background role that a `ThemedText` uses, and the damage-type, heal, immune and waystone colours against `Outline` |
| Never colour alone | Essential information also has a shape, icon, label or size (Game Accessibility Guidelines, basic) | Patterns above; `ContentTests`: every status has a unique abbreviation and a description |
| Flashing | Nothing flashes more than three times a second over more than a quarter of a 10° field, about 2.8% of a 1080p display (WCAG 2.3.1; Game Accessibility Guidelines, basic) | **Hit flash:** one unit flashes, about 0.5% of the display.<br>**Light flicker:** steps 8 times a second by at most 15% of a light's strength, likely under WCAG's 10% luminance change; measured on the Phase 6 previews.<br>**Reduce flashing:** also stills it. |
| Controls | At least 12 UI px in each direction and 2 UI px apart. Our number; the guidelines ask "large and well spaced" without one. | `UiContentTests` on every Selectable in the menu prefabs |
| Reachable UI | Menus work with keyboard and gamepad alone | `UiContentTests`: no menu Selectable has navigation off |
| Remapping | Every gameplay control can be rebound | KeyBindings.md, done |
| Settings remembered | Every preference is saved | `settings.json`, done |
| Larger UI | One larger step where the display fits it. This is not XAG's text scaling to 200%, which is Later. | `UiScale` |

### Preferences

There are new rows in a Display section of the options screen. Each is saved at once, in `settings.json` through `UserSettings`, and never replicated.

| Preference | Phase | Values | Effect |
|---|---|---|---|
| UI size | 3 | Normal; Large and Larger where the display fits them | Adds 0, 1 or 2 to the UI zoom. Screen-space UI and popups grow; world-space UI does not. |
| Reduce flashing | 6 | Off, On | `HitFeedbackPresenter`'s flashes (a hit's, an immunity's and a tick's) at a third of their strength, a new `HitFeedbackSettings` field (the asset is never edited at runtime). Light props, flame pixels and flame frames stop flickering, through `PropLighting`'s flicker override, which only the lighting dev panel sets today. |
| Reduce motion | 6 | Off, On | No camera kick on hits. Popups fade in place instead of rising. |

## Architecture

### Core (`Assets/Scripts/Core/UI`)

- `UiScaleSettings`: a ScriptableObject (`Assets/Data/UI/UiScale.asset`) assigned on `MainLifetimeScope`. It holds the reference size (640 x 360), the layout's minimum (480 x 270) and the display lines per zoom step (390).
- `UiScale`:
  - `ZoomFor(width, height, step)`: plain and static, so EditMode can test it.
  - A Main-scope entry point (`ITickable`) that compares the display size and the size step from `UserSettings` each tick (two integer compares, no allocation).
  - It exposes `Factor` and `StepsThatFit`, and raises `Changed`.
- `UiPixelScale`: on each UI canvas.
  - At start and on `Changed` it sets its scaler to a constant pixel size with `Factor` and 32 reference pixels per unit.
  - The canvases are listed in their scopes' auto-inject objects, which are empty today: `PlayerHudCanvas`, `DamagePopupCanvas` and `GameMenuCanvas` in the Gameplay scope, and `MainMenuCanvas` in the Menu scope.

### Gameplay (`Assets/Scripts/UI`)

- `UiTheme`: a ScriptableObject (`Assets/Data/UI/UiTheme.asset`).
  - It holds a colour per `UiColour` role, UI px per `UiTextSize` (both enums append-only), and the font asset.
  - It has getters only and a `Create(...)` factory for tests.
  - It lives in Gameplay because Core does not reference TextMesh Pro.
- `ThemedText`, `ThemedGraphic`, `ThemedSelectable`: described under Components. They change only their own instance and have no `Update`.
- `AbilitySlotView`, `MoveMarker` and `PlayerHudView` swap their colour fields for roles and a theme reference.

### Editor (`Assets/Editor/Design`, `Assets/Editor/UI`)

- `DesignTables`: `Build()` returns each file's name and Markdown, built from the assets with `DamageMath`; `Write()` writes them to `Docs/Generated`. Menu item: `Tools/TopDownRPG/Design/Write Design Tables`.
- `UiSkinRecipe` and its writer: Generate writes the skin sprites.

### Font

A project font asset in `Assets/Fonts`, generated from TMP's `LiberationSans.ttf` with the padding a 1 UI px outline at `Caption` needs. `LiberationSans SDF` stays TMP's default and is no longer used by the UI.

### Settings

`UserSettingsData` gains `uiSizeStep` (Phase 3), `reduceFlashing` and `reduceMotion` (Phase 6). A file from before reads them as their defaults, so the format version stays 1.

## Networking

- Nothing in this file is replicated.
- The theme, the UI zoom and the preferences change only how this machine shows things. The host's simulation never reads them.
- Reduce flashing and reduce motion act only on local visuals: `HitFeedbackPresenter`'s flash, the light flicker, the camera kick, the popups.
- Catalog ids and save GUIDs are unchanged.

## Removals and migrations

- **View colour fields.** The colour fields of `AbilitySlotView`, `MoveMarker` and `PlayerHudView` are removed, and their values move to the theme's roles. The serialized data is dropped on purpose.
- **Canvases** move to `UiPixelScale`: `PlayerHudCanvas` (1920 x 1080), `GameMenuCanvas` and `MainMenuCanvas` (640 x 360), and `DamagePopupCanvas` (constant pixel size).
  - In Phase 3 the HUD is converted mechanically: sizes and positions divided by three, fonts moved to the nearest type size at or above `Caption`.
  - It is redesigned once, from the components, in Phase 5.
  - The fader keeps its own canvas (`TransitionCanvas`). It has no text or art to scale.
- **The options screen** is titled "Options" and its rows move into a scroll list (Phase 3).
- **Scenes.** `Main.unity`, `MainMenu.unity` and `Gameplay.unity` are edited through the editor, and the diff is checked for save churn before committing.
- **`Square.png`** is removed with its `.meta` once nothing references it.
- **Architecture.md:**
  - The asset values it restates (the weapons' numbers, the status table, the blank abilities' table) become links to `Docs/Generated` in Phase 1.
  - The UI, configs, editor tools, settings and tests sections change with each phase, and so do the scopes' auto-inject lists.
- **Tests that pin asset values.** They become relations or move into the generated tables (Open questions).
- **KeyBindings.md.** The options screen gains its Display section (Phases 3 and 6).

## Phases

Each phase ships on its own, keeps the game playable offline and hosted, and comes with tests. A phase whose look can change settles it on previews first, signed off before its tests and docs. An open question is settled with the user at the start of the phase named for it. Each phase updates Architecture.md and Roadmap.md as it lands.

### Phase 1: generated design tables

- [ ] The principles confirmed or rewritten with the user (Open questions).
- [ ] `DesignTables` in `Assets/Editor/Design`: `Build()` and `Write()`, and the menu item `Tools/TopDownRPG/Design/Write Design Tables`. Markdown in the invariant culture, rows sorted by catalog index or by name.
- [ ] `DamageTypes.md`, `Weapons.md`, `Abilities.md`, `StatusEffects.md` and `Units.md` written into `Docs/Generated`, with the derived columns resolved through `DamageMath`.
- [ ] Architecture.md's restated values (the weapons' numbers, the `CombatSettings` values, the status table, the blank abilities' table, the Weasel's health) replaced with links to the tables. The plans keep theirs, as the record of what each phase chose.
- [ ] Tests: `DesignTablesTests`:
  - the committed files equal `Build()` after normalising line endings, and a mismatch names the menu item;
  - the derived columns on definitions from `Create`: a recovery longer than the cooldown setting the period, the time to kill, a periodic total with its tick on expiry.

### Phase 2: content checks and tuning views

- [ ] Balance targets settled with the user (Open questions), and with them what becomes of the tests that pin asset values.
- [ ] `ContentTests`: one case per kind (weapons, abilities, classes, statuses, combat profiles, mob configs, looks, character colours), every definition found under `Assets/Data` and `Assets/Settings/AI`, checked on its serialized values:
  - listed exactly once in its catalog;
  - required references set;
  - ranges valid (cooldowns, contacts, durations and tick intervals above 0; at least one max stack);
  - display names unique within the kind, swatch and choice names within their lists;
  - status abbreviations unique and descriptions present;
  - colours opaque.
- [ ] The tests that pin asset values turned into relations or target checks, as the balance targets settle.
- [ ] Saved Unity Search table views under `Assets/Editor/Design/Searches`: weapons, abilities, statuses, mob configs.
- [ ] Tests: `ContentTests` passes on the project's assets, and each rule fails on a definition from `Create` that breaks it.

### Phase 3: one UI scale, the UI size setting and the options screen

- [ ] Control size settled with the user on the previews (Open questions).
- [ ] `UiScaleSettings` (`Assets/Data/UI/UiScale.asset`, on `MainLifetimeScope`), `UiScale` and `UiPixelScale` on `PlayerHudCanvas`, `DamagePopupCanvas`, `GameMenuCanvas` and `MainMenuCanvas`, each injected through its scope's auto-inject list.
- [ ] The HUD converted to UI px: sizes and positions divided by three. Every text on the HUD, the menus and the popups moved to the nearest type size at or above `Caption`, none auto-sizing below it.
- [ ] The options screen titled "Options", its rows in a scroll list that keeps the selected row in view, and a Display section with the UI size row.
- [ ] `uiSizeStep` in `settings.json`; KeyBindings.md's options screen updated.
- [ ] Previews of the HUD, the menus and the options screen at the displays in the Scale table and at each size step that fits, signed off before the tests and docs. They are captured with the canvases drawn through a camera, because overlay canvases do not render into a texture.
- [ ] Tests:
  - `UiScaleTests`;
  - settings: a file without `uiSizeStep` reads Normal, and changing it writes the file;
  - `OptionsMenuControllerTests` and `OptionsMenuPlayModeTests`: the UI size row, and the list scrolling the selected row into view;
  - PlayMode: changing the UI size sends nothing.

### Phase 4: the theme

- [ ] The font settled with the user on previews (Open questions).
- [ ] The project font asset in `Assets/Fonts`, with the atlas padding a 1 UI px outline at `Caption` needs and the static atlas's character set (Architecture.md's Known gaps: TMP's fallback font).
- [ ] `UiTheme` (`Assets/Data/UI/UiTheme.asset`) with the `UiColour` roles, the `UiTextSize` scale and the font. The merged roles, the Physical popup red and the placeholder settled on previews.
- [ ] `ThemedText`, `ThemedGraphic` and `ThemedSelectable` on every UI text, image, sprite and control: the move marker, the hover ring, the world bar and the world status icons included. `AbilitySlotView`, `MoveMarker` and `PlayerHudView` keep roles instead of colours.
- [ ] Damage popups, the waystone popup and other text over the world outlined in `Outline`.
- [ ] `UiTheme.md` added to `DesignTables`.
- [ ] Tests: `UiThemeTests`, `UiContentTests`, `DesignTablesTests` with `UiTheme.md`, and the views' tests on a theme from `Create`.

### Phase 5: skin and components

- [ ] Damage types on popups settled with the user on previews (Open questions).
- [ ] `UiSkinRecipe` and its writer, the skin settled on previews: a panel, a raised panel, a slot frame, a bar frame and fill, and the marker shapes, in `Assets/Sprites/UI/Skin`.
- [ ] The component prefabs and variants in `Assets/Prefabs/UI/Components` (Components table), the scroll list, the swatch row and the tooltip included.
- [ ] The HUD, the main menu, the in-game menu and the options screen rebuilt from them. `Square.png` removed with its `.meta`.
- [ ] Distinct marker shapes for a move, an attack move and the hover ring.
- [ ] Tooltips for statuses and abilities.
- [ ] Tests:
  - `UiContentTests` on the rebuilt prefabs;
  - the tooltip shows a status's or an ability's name and description on hover and on selection, and the status row blocks clicks while it shows;
  - the marker kinds have different sprites.

### Phase 6: reduce flashing and reduce motion

- [ ] `reduceFlashing` and `reduceMotion` in `settings.json`, and their rows in the Display section.
- [ ] Reduce flashing: `HitFeedbackPresenter`'s flashes scaled by the new `HitFeedbackSettings` field; light props, flame pixels and flame frames stilled through `PropLighting`'s flicker override.
- [ ] Reduce motion: no camera kick on hits, and popups that fade in place.
- [ ] The flicker measured on previews against WCAG's flash threshold, and the result written under Accessibility.
- [ ] Tests:
  - settings: a file without the fields reads them off;
  - the presenter's flash amounts and camera kick with each preference on and off;
  - PlayMode in Testlands, with the in-process host and client: changing the preferences sends nothing, and the host's HP and statuses, and the client copy's hit reports, are the same with reduce flashing on and off.

### Later

- A high-contrast theme, with the themed components reapplying on a theme change.
- Text scaling to 200% (XAG), which needs layouts that reflow.
- Status icon art, for the HUD and the world status icons.
- Colour-vision options for damage-type and status colours.
- Co-op identity:
  - player identity colours and markers (not the colours a player picks for its character), which need a player slot the host assigns so every machine agrees;
  - players' world bars and status icons.
- World-space UI and the attack cursor sized by the UI zoom. Today the cursor is a fixed 32 px hardware cursor.
- A component gallery scene, if the component set grows past what the screens themselves show.
- Shared durations, once the first menu animates.
- Screen reader roles.
- Accessibility features listed on the store page.
- A combat simulator over the real services, once there are random rolls.
- Dialogue in Ink.
- The menus in UI Toolkit, at an editor upgrade.
- A W3C token export, if a design tool joins.

## Testing

- **EditMode, `DesignTablesTests`.** `Build()` equals the committed files after normalising line endings. On a mismatch, the message names the menu item and says to regenerate after a merge.
- **EditMode, `ContentTests`.** One case per content kind, reading the project's assets and never writing them. It checks serialized values, not the clamping getters:
  - catalog membership and uniqueness;
  - required references;
  - ranges (cooldowns, contacts, durations and tick intervals above 0; at least one max stack);
  - unique display names;
  - unique status abbreviations and present descriptions;
  - opaque colours.
- **EditMode, `UiScaleTests`.** `ZoomFor` at every display in the Scale table, windowed sizes included, for each size step, and which steps fit. `UiScaleSettings`' reference size equals the Main scene's Pixel Perfect Camera's.
- **EditMode, `UiThemeTests`**, on a theme from `Create`, then on the project's theme:
  - the contrast ratio formula against known pairs;
  - every text-and-background role pair a `ThemedText` uses, against its threshold by size;
  - the gameplay colours against `Outline`;
  - `Caption`'s body height, from the font asset's glyph metrics, at least 18 px per 1080 display lines at the zoom `UiScale` picks, which ties the 390 to the font.
- **EditMode, `UiContentTests`**, on the project's UI prefabs and menu scenes (only read):
  - every text has a `ThemedText`;
  - sizes and auto-size minimums are at `Caption` or above;
  - no italic or all-caps text below `Title`;
  - every UI canvas has a `UiPixelScale`;
  - every Selectable has a `ThemedSelectable`, is at least 12 UI px each way, and has navigation on.
- **EditMode, settings.** A file without the new fields reads their defaults; changing one writes the file.
- **PlayMode.** In Testlands, with the in-process host and client:
  - changing the preferences sends nothing (the UI size from Phase 3, the others from Phase 6);
  - the host's HP and statuses, and the client copy's hit reports, are the same with reduce flashing on and off.
- **Manual.** The Phase 3 and 4 previews at each display in the Scale table, and the Phase 5 skin previews.
- **Before merging into `main`:** the full EditMode and PlayMode suites.

## Open questions

Each is settled with the user at the start of the phase named.

- **Principles** (Phase 1). Confirm or rewrite them. They are drawn from the specs, not stated anywhere yet.
- **Balance targets** (Phase 2). Should specs carry numeric targets ("three to five sword hits per Weasel") that a test checks against the derived columns? If so, the tests that pin asset values today (`DamageResolutionTests`, `AbilityEffectTests`) become those checks instead of restating the assets.
- **Control size** (Phase 3). Is 12 UI px (36 px at 1080p) the right minimum for buttons, rows and slots?
- **Font** (Phase 4). Keep LiberationSans (smooth text over pixel art), or move to a pixel font under OFL, which would make body heights exact and its outline drawn in? Decide on the previews.
- **Damage types on popups** (Phase 5). Should popups show the hit's type, or its `Resisted` and `Weakness` flags (shown nowhere today), with a cue other than colour?

## Tools considered

This is a survey of tools and guidelines from 2026-11-01, checked by a second review.

That review corrected the survey on several points:
- Combat has no random rolls, so a simulator is not needed yet.
- Content tests already exist for some kinds.
- Saves already name assets by GUID.
- The UI font is a third-party font, allowed by the font exception.

| Tool | Verdict | Why |
|---|---|---|
| Mermaid | Adopt (already used) | GitHub renders it; it is text in git |
| Unity Search tables | Adopt (built in) | A spreadsheet view and CSV or JSON export over the assets, with no second copy |
| XAG, Game Accessibility Guidelines, WCAG 2.3.1, AbleGamers' player-experience patterns | Adopt as the rulebook | Numbers precise enough to test. Linked and paraphrased, not copied |
| OpenRA, Wesnoth | Borrow the idea | Reference docs generated from the code and data, and validated: `DesignTables` and `ContentTests` |
| sinanata's UI Toolkit design system, Unity's App UI | Borrow the idea | A theme asset applied to components, and a component gallery. Not the code, which targets UI Toolkit |
| ModiBuff | Borrow ideas for statuses later | Charges, revertible effects, every-N-stacks triggers, dispels. MPL-2.0, so ideas only |
| SimulationCraft | Borrow the idea when there are random rolls | A simulator over the real code, not a second model |
| Ink (or Yarn Spinner) | Later, for dialogue | Plain C# story state that serializes, so the host can run it |
| icepool or dyce, BPTK-Py, PySD, SimPy | Later | Probabilities and economies, once there are rolls and an economy |
| Graph Toolkit (built into Unity 6.4) | Later, if abilities branch | Editor-only; it needs our own runtime model |
| Penpot and the W3C token format | Later, if a design tool joins | An export from `UiTheme` |
| Unity Atoms, ScriptableObject-Architecture | Avoid | They write gameplay state into assets, and that state is global and cannot be per player |
| Unity ports of Unreal's Gameplay Ability System | Avoid | None replicates over Netcode for GameObjects: the best known is archived, EX-GAS needs paid Odin, and UnityStarter's networking targets Mirror |
| Luban, Charon, spreadsheet pipelines | Avoid | A second home for tunables beside the catalogs |
| Figma and Penpot layout bridges | Avoid | Early or unmaintained, and they import art from outside the project |
| Machinations, game description languages | Avoid | Public free diagrams, non-commercial or non-OSI licences, and no real-time action |
