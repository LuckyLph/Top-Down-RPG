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

Settled with the user on 2026-11-10 (Phase 1): the four drawn from the existing specs, and a fifth from CLAUDE.md's art rule. A spec's decisions should follow from one of these.

1. **Co-op first.** The host decides, and every machine shows the same outcomes, late joiners included. One player is just the smallest party.
2. **Readable at a glance.**
   - Pixel art is read one art pixel at a time.
   - Every attack telegraphs before it lands, and every order and cast is answered on the frame it is given.
   - Text meets its size and contrast minimums by default.
   - Information never depends on colour alone.
3. **Direct control.** Click to move, with orders that cancel and buffer like a MOBA's. What the player sees is what the game will do.
4. **Content is data.** Mechanics are assets composed from a small set of building blocks. A new idea first extends a block in its spec, then becomes content.
5. **Made in the project.** Every sprite, effect and UI piece comes from a recipe in the editor or is drawn in Aseprite for the game. Fonts licensed for use in a game are the one exception, for now.

## Decisions

| Topic | Decision |
|---|---|
| Source of truth | Assets hold the current numbers. Specs hold the intent, the rules and the reasons for chosen values. `Docs/Generated` holds the current numbers in readable form. |
| Generated tables | Written by an editor tool from the assets and the real formulas (`DamageMath`), formatted with the invariant culture. An EditMode test fails when a committed table differs from what the tool would write. |
| Content checks | One table-driven EditMode fixture covers every content kind. It reads serialized values, not the getters that clamp them. |
| Balance | Derived columns in the generated tables (damage per second, hits and time to kill) instead of a simulator, until combat has random rolls or interacting procs. No test pins an asset's value: the committed tables record every value. A spec's numeric target is a range, checked against the derived columns by a case in `BalanceTargetsTests`, which the first target creates (settled in Phase 2). |
| UI framework | uGUI with TextMesh Pro. |
| UI scale | A UI px is drawn as a whole number of display pixels: the UI zoom. See Foundations for how it is chosen. |
| Text | Five sizes. The smallest, `Caption`, is Galmuri7 at 8 UI px, a 24 px body at 1080p. Nothing is smaller, and nothing auto-sizes. Changed in Phase 4 from LiberationSans at 7 UI px (19.5 px). |
| Font | Galmuri, a pixel font (SIL OFL 1.1), in four sizes: a static SDF font asset of each in `Assets/Fonts`, with the atlas padding a 1 UI px outline needs and an outlined material preset. The theme holds a font per text size. Chosen in Phase 4 over LiberationSans, which TMP keeps as its default. |
| Colour | UI code and prefabs name colour roles, never raw colours. Damage-type and status colours stay on their gameplay assets but follow the same contrast and never-colour-alone rules (the waystone's popup colour went with its popup in UiRevamp.md's Phase 3). |
| Theme | **Asset:** a read-only `UiTheme` in Gameplay holds the colour roles, the text sizes and the font.<br>**Applying it:** themed components reference the asset and set their own instance once when they wake. Nothing reads the theme per frame, and no tool writes it into prefabs or scenes.<br>**Edit mode:** prefabs show their serialized values in the editor; Phase 4's conversion set them to the theme's once. |
| UI art | Generated in the editor from a recipe, like every other sprite: 9-sliced frames, bars and outlines in greyscale, tinted by role (`UiSkinRecipe`, Phase 5). |
| Damage types on popups | A popup shows its hit's damage type by colour alone, and shows neither the type nor the `Resisted` and `Weakness` flags otherwise. Settled in Phase 5 on previews of a type icon and of arrows for the flags: left as it was. |
| Accessibility baseline | The Game Accessibility Guidelines' basic items listed under Accessibility, plus XAG 101's text size and XAG 102's contrast. We link and paraphrase them rather than copy their text. |
| Preferences | UI size (Phase 3); reduce flashing and reduce motion (Phase 6). They are local, saved in `settings.json`, and never replicated. Reduce motion covers the camera kick and the popups' rise; a hit's nudge and hit stop stay, as neither moves the screen (settled with the user in Phase 6). |
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
- **Numbers.** A spec states targets and rules ("a Weasel dies in three to five sword hits"). A numeric target is a range, and gets a case in `BalanceTargetsTests` that checks it against the derived columns (`DesignMath`). A decision may record the value chosen and what it was compared with. Current values are in `Docs/Generated`, and no test restates them.
- **Diagrams** follow the Decisions table above.
- **`Docs/Generated`** is written only by its tool. Never edit it by hand.

### Generated tables

`Tools/TopDownRPG/Design/Write Design Tables` writes one Markdown file per kind into `Docs/Generated/`. Rows are sorted by catalog index, or by name for kinds without a catalog, so the diffs stay small.

| File | Rows | Columns |
|---|---|---|
| `DamageTypes.md` | Each damage type, plus the heal and immune rows | Popup colour (hex) and its contrast on the theme's `Outline`; the resistance floor, cap, minimum damage and periodic popup size from `CombatSettings` |
| `Weapons.md` | `WeaponCatalog` | Index, damage and type, cooldown, windup, contact and recovery, cancel rules, attack range, delivery (area, or projectile speed, range and homing), abilities. Derived: attack period, damage per second, and hits and time to kill each mob prefab |
| `Abilities.md` | Every `AbilityDefinition` | Used by (class slot or weapon), targeting and filter, range, cast time, cooldown, movement, bufferable, effect (damage and type, heal, statuses, area), cast clips. Derived: damage per cooldown second. Then each ability's description (Phase 5) |
| `StatusEffects.md` | `StatusEffectCatalog` | Index, kind, tags, abbreviation, colour, description, duration, stacking and cap, periodic amount and interval, modifiers, controls, immunities. Derived: the raw periodic total at one stack and at the cap |
| `Units.md` | The player and mob prefabs | Health, combat profile (resistances and immunities), attack (damage, type, interval, timeline), move speeds. Derived: the time one mob takes to kill a player |
| `UiTheme.md` | Each colour role and text size, then each pair of roles a themed text uses | A role's colour and opacity; a size's font, UI px and body height at 1080p; a pair's smallest size, contrast and threshold (`UiThemeUsage`) |

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
5. **Display text.** Every definition or option players see (a swatch, a colour choice, a part, and since UiRevamp.md's Phase 3 a world and each of its entries) has a display name unique within its kind or list: a world's among worlds, an entry's among its world's entries. A world's name takes no article, since its notice reads "Entering the" and the name. Display names are written by hand, so the world generator keeps an entry's when it rewrites the entry. Every status has an abbreviation unique among statuses and a description, and every ability a description, because the HUD and its tooltips show them.

### Adding content

1. Create the asset in its kind's folder with the inspector. Append it to its catalog if the kind has one.
2. Run Write Design Tables and read the diff in `Docs/Generated`: the derived columns show what the new numbers mean.
3. Run `ContentTests` and the kind's own test class.
4. A spec states the intent ("Chill slows enough to kite a Weasel") if the content has one. The numbers stay in the asset.

### Tuning

Unity Search's table view shows a kind's assets side by side with editable property columns. It is a spreadsheet over the assets themselves, so there is no second copy, and it exports to CSV or JSON.

`Tools/TopDownRPG/Design/Tune/` (Phase 2) has a menu item per kind (weapons, abilities, statuses, mob configs) that opens the Search window's table view over every asset of the kind, a column per serialized field after the asset's name. The columns are read from the assets each time, so they follow the fields. Unity 6.4 keeps saved search assets internal, so none are committed; a view saved from the window keeps its layout. Edits in the table skip the inspector, `[Min]`, `[Range]` and `OnValidate`, so `ContentTests` is what catches an out-of-range value.

## UI and UX

### Foundations

**Scale.** Every UI canvas carries a `UiPixelScale`. It sets the canvas scaler to a constant pixel size, with the scale factor `UiScale` gives and 32 reference pixels per unit, so one sprite pixel is one UI px.

`UiScale` picks the UI zoom in three steps:

1. **Readable zoom.** The display height divided by 390, rounded up. That is the Pixel Perfect Camera's zoom at 720p, 1080p, 1440p and 4K, and at every zoom it gives `Caption` has a body height of 18 px per 1080 display lines or more: Galmuri7's 8 UI px body needs only the display height divided by 480. Until Phase 4 the 390 was LiberationSans' own minimum.
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

**Screen regions.** In game, each part of the screen has one owner, so nothing overlaps at 480 x 270 (UiRevamp.md's Decisions, written here in its Phase 2):

| Region | Where (UI px) | Holds |
|---|---|---|
| Bottom centre | 378 wide, from the bottom edge up to 68 | The command bar: the ability row 8 above the bottom edge, the health bar and the message line over it, a status group at each side |
| Top centre | 240 wide and 24 tall, 8 below the top edge | The notice line (since UiRevamp.md's Phase 3): a panel sized to its notice, 15 tall for one line, the whole band for two |
| Left edge | 104 wide and 82 tall, 8 from the left and top edges | The party frames, three of 26 with 2 between (Phase 5) |
| Top right | 104 wide and 82 tall, 8 from the right and top edges | Kept free for a place name and a minimap |

Tooltips float over the regions, above the command bar so its health stays in view. Online, menus opened in game stay clear of the command bar and the notice band. `HudLayoutTests` checks that the HUD keeps to its region at 480 x 270 and at every display and size step of the Scale table, and that the regions never overlap.

**Type.** The UI's font is Galmuri, a pixel font, drawn at one UI px per font pixel: each size is a Galmuri size at its em in UI px. The body height (the top of a d to the bottom of a p) is from the fonts' glyphs, at 1080p (zoom 3). [UiTheme.md](Generated/UiTheme.md) has the current values.

| Size | Font | UI px | Body at 1080p | Used for |
|---|---|---|---|---|
| `Caption` | Galmuri7 | 8 | 24 px | The HUD's key labels, ability names, status letters and stacks, periodic popups |
| `Body` | Galmuri7 | 8 | 24 px | Menu rows, buttons, notices, the HP and cooldown seconds |
| `Heading` | Galmuri9 | 10 | 30 px | Section headers, damage popups, the main menu's buttons and status line |
| `Title` | Galmuri11 | 12 | 39 px | Screen titles |
| `Display` | Galmuri14 | 15 | 51 px | The game's name on the main menu |

`Caption` and `Body` are the same today, since Galmuri has nothing smaller than Galmuri7; they stay two sizes, so the HUD's labels and the menus' rows can part later with one change to the theme.

Further rules:

- **No auto-size:** a pixel font is crisp only at its own size.
- **Whole UI px:** `ThemedText` moves each glyph onto the whole UI px of its canvas whenever its text is built, and popups move in whole UI px; text is crisp wherever its rect lands.
- **Smaller, on whole display pixels:** since UiRevamp.md's Phase 3 a text may draw smaller than the zoom through `PixelTextScale`: each font pixel covers the whole number of display pixels nearest a target share of the zoom's (three quarters for the health bar's numbers: 2 at x3, 3 at x4, 5 at x6, unchanged at x2), and `ThemedText` snaps such a text to whole display pixels. Any other scale leaves the pixel grid.
- **Damage popups** use `Heading`, and periodic ticks `Caption` (`CombatSettings.PeriodicPopupSize`), a smaller font rather than a scale, which would leave the pixel grid.
- **Text style:** sentence case, no bold (the fonts have none, and TMP's faux bold blurs the pixels), no italics or all-caps below `Title`, left-aligned rows (the Game Accessibility Guidelines' "simple, clear text formatting").

**Colour roles.** Settled on Phase 4's previews. Each role started from the colour most used for it; where a role merged several colours, or a colour failed its contrast pair, the previews chose. [UiTheme.md](Generated/UiTheme.md) has the values and the contrast of every pair in use.

| Role | Value | Replaced |
|---|---|---|
| `Text` | White | Light text on dark panels, and the rows' off-white |
| `TextOnControl` | (0.1, 0.1, 0.12) | The dark button labels |
| `TextMuted`, `TextDisabled` | Light and mid greys: `TextMuted` for the tooltips' descriptions (Phase 5); `TextDisabled` unused yet | Secondary and disabled text |
| `Placeholder` | `#595959`, 4.9:1 on the field | The join field's placeholder (about 2.9:1, and italic) |
| `Accent` | (1, 0.85, 0.4) | Section headers, the notices and the main menu's status line, the HUD's key labels (a paler yellow), the selected control; since UiRevamp.md's Phase 1 the frame of the slot whose cast runs and the brackets of a queued cast |
| `Surface`, `SurfaceEmpty` | The filled slot frame (0.12, 0.12, 0.16) at 0.85; at 0.75 when empty, where it was 0.35, so a key label reads on bright ground | HUD frames, and the health panel's grey |
| `SurfaceRaised` | (0.08, 0.09, 0.12) | Menu boxes, full-screen panels, and the dialogs' lighter box |
| `Scrim`, `ScrimModal` | Black at 0.6; black at 0.85 | The in-game menu's dimmer; the confirmation and listening panels |
| `Shade` | Black at 0.65 | Cooldown and elapsed fills (0.55), the weapon icon's mask (0.42), the scroll list's track |
| `ControlNormal`, `ControlHighlighted`, `ControlPressed` | Today's button colour block | Button states (`Selected` uses `Accent`) |
| `ControlDisabled` | An opaque grey, `#6E6E73`, where it was a translucent tint, so a label keeps 3.4:1 | A disabled button; since UiRevamp.md's Phase 1 the hatch of an ability slot crowd control blocks |
| `ControlSecondary` | `#9E9E96`, dimmer than `ControlNormal`, 6.4:1 under `TextOnControl` | A secondary button's face (new in Phase 5); since UiRevamp.md's Phase 3 the toggle row's box frame, which a black `Border` would lose on the dark panel |
| `Danger` | `#FF4D40` | The hover marker, the attack-move marker and the failure flash (at 0.6 opacity), three reds; a danger button's face since Phase 5 |
| `Positive` | The move marker's green | |
| `Health`, `HealthTrack` | The HUD's fill and track; the track opaque since UiRevamp.md's Phase 1, so the fill reads at 3.68:1 on it, where it was 2.37:1 at 85%. Since Phase 2 `Health` is the mobs' (the world bar's) and the track also colours the health bar's ticks | The world bar's brighter fill and its frame |
| `HealthPlayer` | `#4CC25A`, settled on UiRevamp.md's Phase 2 previews of three greens; 8.04:1 on `HealthTrack` | New: players' health, green, where mobs' is red; the badge and the bar's shape still tell them apart |
| `HealthLost` | `#D6D6D6`, 12.67:1 on `HealthTrack` | New (Phase 2): the part of a health bar just lost, under its fill for 0.4 s |
| `Icon` | White | Icons and art shown in their own colours (new) |
| `IconMissing` | White at 0.25 | The missing weapon icon |
| `Border` | Black since UiRevamp.md's Phase 3, settled on its previews; `#9A9EB3`, a grey-blue, from Phase 5's previews until then (white before) | Every frame: panels, dialogs, slots, status icons, the health bar, the notice line and the tooltip (new) |
| `Outline` | Black | The outline of text drawn over the world, and of the HUD's status letters and stacks |

The Physical popup red, 3.11:1 against the outline, became `#E64040` (5.17:1). It stays on `CombatSettings` with the other damage-type colours.

**Art.** `UiSkinRecipe` is an editor recipe in the manner of the prop and terrain recipes (Phase 5).
- It writes greyscale sprites into `Assets/Sprites/UI/Skin`, which the pixel art postprocessor imports at 32 PPU, so one sprite pixel is one UI px, or one art pixel in the world:
  - 9-sliced, their corners square since UiRevamp.md's Phase 3 (cut by 1 px along a diagonal before, which left a 1 px frame meeting itself only at a diagonal and a square fill showing past it, so frames looked chipped; the recipe's corner cut can still cut them): a panel, the 1 px frame around it, a raised panel whose face sits on a 2 px darker lip, a slot frame with a dark line inside it, a bar's frame (the track its fill sits in) and its fill;
  - shown whole: the three marker shapes, a 5 px pip and, since Phase 6, an 8 x 5 check mark, a toggle row's mark;
  - since UiRevamp.md's Phase 1: a slot's corner brackets (an L of 5 px arms in each corner, 9-sliced so they keep to any slot's corners), shown 1 UI px outside a slot whose cast is queued, and a hatch, a 4 px tile of one diagonal, tiled over a slot crowd control blocks;
  - since Phase 2: a health bar's tick, a 1 x 3 px line down from the top of its fill every 10 HP, and a debuff's notch, a 5 px right triangle in its icon's top right corner.
- Images and SpriteRenderers tint them by role. A fill is white, so it shows its role's colour exactly and the contrast measured on a role is what shows; only edges are shaded.
- They replaced `Square.png`, the HUD's other smooth sprites and the world bar's `RoundedBar.png`.
- Paddings and gaps are multiples of 2 UI px. The components showed no need for spacing in the theme.

### Components

UI is built from these prefabs in `Assets/Prefabs/UI/Components` (Phase 5), with prefab variants for real variation. A component prefab carries its themed components, text size and font; a change to the Button prefab reaches every button.

| Component | Prefabs | Used by |
|---|---|---|
| Button | `Button` (Primary: raised, `ControlNormal`), its variants `ButtonSecondary` (flat, `ControlSecondary`), `ButtonDanger` (raised, `Danger`) and `ButtonLarge` (Primary with `Heading` text, 160 x 20) | Primary: the in-game menu, Back, a row's value. Secondary: Cancel, a row's Reset, Reset all. Danger: a dialog's confirm. Large: the main menu, whose buttons are `Heading` since Phase 3 |
| Panel | `Panel` (`Surface`, framed in `Border`), its variants `PanelRaised` (`SurfaceRaised`) and `Scrim` (full screen, no sprite or frame), and `ScrimModal`, a variant of `Scrim` | Menu boxes and full-screen panels are raised; the in-game menu's dimmer is a scrim; the listening panel a modal scrim; the HUD's notice line sits on a `Panel` (UiRevamp.md's Phase 3) |
| Dialog | `Dialog`, a variant of `ScrimModal`: a raised box with its question, a danger confirm and a secondary Cancel | The in-game menu's and the options screen's confirmations |
| Section header | `SectionHeader` (was `OptionsSectionHeader`) | The options screen |
| Row | `Row` (a label, a value button and Reset; was `KeyBindingRow`) and its variants `RowSwatch` (a 12 UI px chip showing the swatch before the value) and `RowToggle` (Phase 6: a 12 UI px box there, `Shade` framed in `ControlSecondary` (in `Border` until UiRevamp.md's Phase 3), holding the skin's check mark in `Accent` while on; its button reads On or Off and flips it, its Reset turns it off) | Binding and choice rows share `Row`, since they look the same; colour rows are swatch rows; the Display section's on-off preferences are toggle rows |
| Scroll list | `ScrollList`: a `ScrollRect`, its viewport, its rows and scrollbar | The options screen |
| Notice line | `NoticeLine` (`Accent`, centred; wrapping since UiRevamp.md's Phase 3) and its variant `MessageLine` (UiRevamp.md's Phase 1: `Text` outlined in `Outline`, for text over the world) | The options screen's notice, and the main menu's status line (at `Heading`; a long status wraps in its column until Phase 9 rebuilds the menu); the message line above the ability bar, where a failed press says why; since Phase 3 the HUD's notice line at the top centre: a `NoticeLine` on a `Panel` sized to its text (`NoticeView`), up to 240 UI px and two lines, 4 UI px of padding at its sides and 2 above and below (less for two lines, which take the band's 24), read against the panel's `Surface`, ending in an ellipsis past two lines |
| Slot | `Slot` (frame, icon, slot frame) and its variant `SlotAbility` (`AbilitySlotView`: name, cooldown fill and seconds, key, failure flash, tooltip; the key in its top left corner and the seconds in its top right since UiRevamp.md's Phase 3; since UiRevamp.md's Phase 1 its states: the slot frame in `Accent` while its cast runs, the skin's corner brackets in `Accent` just outside it while its cast is queued, and the skin's hatch in `ControlDisabled` under its name while crowd control blocks it) | The weapon slot is a `Slot` with a `WeaponSlotView` and a tooltip (Phase 1), in the ability row since Phase 2, after the gap and before its two abilities; the six ability slots are `SlotAbility` |
| Status icon | `StatusIcon` (`StatusIconView`, its frame taking the click, and a tooltip) and its variant `StatusIconDebuff` (UiRevamp.md's Phase 2: its frame in `Danger` and the skin's notch in `Danger` in its top right corner). The last icon of a full group shows "+n" on `Surface` for the statuses that do not fit, its tooltip naming them | The command bar's status groups: buffs left of the abilities, debuffs right of them, so both their side and their shape carry their kind |
| Tooltip | `Tooltip` (`HudTooltip`: a raised panel with a title in `Accent`, since UiRevamp.md's Phase 1 a line of numbers in `Text`, and a description in `TextMuted`, 150 UI px wide; an empty line hides) | Shows a status's or an ability's name and description while the pointer is over its icon or slot, or it is selected; an ability's numbers are its cooldown, cast time and range, and the weapon slot's tooltip gives the weapon's damage, type and range. It shows below a target with no room above it, and above the command bar for the command bar's slots and icons (Phase 2), so the health bar stays in view. The status groups block clicks, as the ability bar does |
| Bar | `Bar` (screen, `HealthBarView`: a frame, a fill, a frame in `Border` and the HP text, "72 / 100" since UiRevamp.md's Phase 1; the fill names its track, so its contrast is measured. Since UiRevamp.md's Phase 3 its text draws at three quarters of the zoom, on whole display pixels (`PixelTextScale`). Since Phase 2 the fill is `HealthPlayer`, the part just lost shows under it in `HealthLost`, a tick marked every 10 HP from the top until UiRevamp.md's Phase 3, which set the spacing to 0, none, the text is outlined in `Outline`, and the frame turns `Danger` and pulses at 30% or less) and `WorldBar` (world: `WorldHealthBar` and `WorldStatusIcons`, a 22 x 4 art px bar with its 20 x 2 fill and four 5 px pips above it, scaled to 4.16 px until UiRevamp.md's Phase 4), and its variant `WorldBarPlayer` (UiRevamp.md's Phase 4: its fill in `HealthPlayer`, and under it a cast bar, `WorldCastBar`, 22 x 3 with a 20 x 1 fill in `Accent`, sharing the bar's bottom border and hidden until a cast) | The command bar's health bar, 242 x 13 (12 until UiRevamp.md's Phase 3, when its text could not centre); the Weasel's bar; every player's bar, 46 art px above its feet |
| Popup text | `PopupText` (was `DamagePopup`) | Damage, heal and immune popups (the waystone notice moved to the notice line in UiRevamp.md's Phase 3) |
| Marker | `Marker` and its variants `MarkerMove` (four chevrons pointing in, `Positive`), `MarkerAttackMove` (a variant of `MarkerMove`: the ring crossed by four ticks, `Danger`) and `MarkerHover` (four brackets on the ring's diagonals, `Danger`); `MarkerHover`'s own variant `MarkerHoverAlly` (UiRevamp.md's Phase 4: the same brackets in `Positive`) | The three kinds are each a different shape, so colour is never the only cue. The ally hover marker under another player shares the enemy's shape, by the user's choice; the attack cursor, shown over enemies only, tells them apart |

The themed components:

- **`ThemedText`:** a TMP text's colour role, background role and size. It sets the font, size, colour and outline (the size's outlined material preset when the background is `Outline`) on its own instance when it wakes, and snaps its glyphs to whole UI px each time its text is built. Views that change a text's size call `SetSize` and `ResetSize`.
- **`ThemedGraphic`:** an Image's or a SpriteRenderer's role, and for a bar's fill the track it is read against (UiRevamp.md's Phase 1).
- **`ThemedSelectable`:** a Selectable's colour block, set from the control roles: its own normal role (`ControlNormal`, `ControlSecondary` or `Danger`, since Phase 5) and the shared highlighted, pressed, selected and disabled ones.

Each references the `UiTheme` asset directly, so pooled prefabs that are never injected (the markers) work too. Views that pick colours at runtime (the failure flash, the marker kinds, the missing weapon tint) also reference the theme, and read their roles' colours once when they wake. A control's target graphic stays white, its colour block holding the roles.

### Patterns

- **Feedback.** Every order and cast is answered on the frame it is given: a marker, a cast clip, or a failure flash on its slot. Every hit shows a flash on every machine that learns of it, and a popup there; an ally's popup (a hit, tick, heal or block involving another player but not the local one) shows as the local player's Allies' damage numbers preference asks (UiRevamp.md's Phase 3).
  - **A failure is answered in words too** (UiRevamp.md's Phase 1): beside the flash, the message line above the ability bar says why for 1.2 s ("On cooldown", "Already casting", "No target", "Stunned", "Silenced", "Rooted"), from one table keyed by the outcome. An empty slot, death, and a queued cast that expired or was dropped say nothing.
  - **A slot shows its state** before it is pressed: framed in `Accent` while its cast runs, bracketed while a cast waits in the buffer, hatched while crowd control blocks it (every slot stunned or silenced, the slots whose cast moves the caster rooted). Each state is a shape, not only a colour.
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
  - In game, notices queue on the notice line at the top centre, each for 3 s, a repeat restarting it: "Waystone discovered", "Entering the Overworld", and while the local player is dead "You fell. Respawning in 3", then "Respawning..." (UiRevamp.md's Phase 3; players joining, leaving and going down from its Phase 5). Popups are left to hits and heals. Each machine raises its own notices from what it already knows.
  - A failure says what happened and, when the player can act on it, what to do.
- **World-space UI.**
  - Bars and status icons sit on the `Overlay` layer, unlit.
  - Markers stay on the `Terrain` layer, under the units they mark.
  - Text drawn over the world has an `Outline` of at least 1 UI px.
  - World-space UI is sized in world units and follows the camera's zoom, not the UI size step.
- **Co-op.**
  - The HUD shows only the local player.
  - Every player's state shows above its unit, on every machine, the local player's own included (UiRevamp.md's Phase 4): a green world bar, its debuff pips, and a cast bar under it while it casts. An ally under the pointer gets the ally hover marker; the local player never does. Party frames come with UiRevamp.md's Phase 5.
- **Never colour alone.** A colour that carries meaning comes with a shape, an icon, a label or a size.
  - **Since Phase 5:** the move, attack-move and hover markers are different shapes, and the HUD's statuses and abilities have tooltips.
  - **Since UiRevamp.md's Phase 2:** buffs and debuffs sit on different sides of the abilities and debuffs carry a notch; a low health bar's frame says so beside the numbers.
  - **Since UiRevamp.md's Phase 4:** the ally hover marker is the enemy's shape in `Positive`; over an enemy the attack cursor shows too.
  - **Known gaps:** world status icons and auras stay colour-only until statuses have icon art (Later). A popup's damage type stays colour-only, and its `Resisted` and `Weakness` flags unshown, as settled in Phase 5 (Decisions): a hit's weapon or ability tells its type.

### Accessibility

| Rule | Threshold | Enforced by |
|---|---|---|
| Text size | A body height (ascender to descender) of at least 18 px per 1080 display lines, by default (XAG 101's PC minimum): `Caption` at the chosen zoom. One exception, settled in UiRevamp.md's Phase 3: the health bar's numbers, drawn smaller (`PixelTextScale`), have 14 px digits at 1080p, since the bar's fill shows the health too | `UiScale`'s zoom rule; `UiThemeTests` (from the theme font's glyphs); `UiContentTests` (every text themed, none auto-sizing) |
| Contrast | **Text:** 4.5:1 under `Title`, 3:1 for `Title` and above (XAG 102's large text is a 36 px body at 1080p).<br>**Shapes that carry meaning:** 4.5:1 when small, 3:1 when large.<br>**Others:** 3:1 for disabled text; 4.5:1 for placeholders.<br>Text over the world is measured against its outline. | `UiThemeTests` through `UiThemeUsage`: every pair of text role and background role that a `ThemedText` uses (a translucent background over white and over black, the lower ratio; a control in its normal, highlighted, selected and disabled states), and the damage-type, heal and immune colours against `Outline`; since UiRevamp.md's Phase 1, every bar's fill on its track at 3:1 (`UiThemeUsage.MeasureFills`) |
| Never colour alone | Essential information also has a shape, icon, label or size (Game Accessibility Guidelines, basic) | Patterns above; `ContentTests`: every status has a unique abbreviation and a description, and every ability a description; `UiContentTests`: the marker kinds draw different sprites |
| Flashing | Nothing flashes more than three times a second over more than a quarter of a 10° field, about 2.8% of a 1080p display (WCAG 2.3.1; Game Accessibility Guidelines, basic) | **Hit flash:** one unit flashes: a Weasel covers 0.27% of a 1080p display, its luminance rising by 0.96 under a full hit flash at night.<br>**Light flicker:** measured on Phase 6's previews at night (Phase 6 notes): only the flames' own pixels change by 10% or more in a step, 450 to 729 px (0.022 to 0.035% of 1080p) for a torch, a brazier or a campfire, and 1,566 px (0.076%) for all three in one view, about 37 times under the threshold; their light pools change by under 2%.<br>**Reduce flashing:** every flash at a third (a hit at night rises by 0.32, a tick's wash by 0.044, under 10%), and the flicker still: nothing in those views changes by 5%. `HitFeedbackTests` and `PropLightingTests` check both. |
| Controls | At least 12 UI px in each direction and 2 UI px apart. Our number; the guidelines ask "large and well spaced" without one. | `UiContentTests` on every Selectable in the UI prefabs and menu scenes, the scroll list's scrollbar excepted (it shows the list's place; the list scrolls with the selection and the wheel) |
| Reachable UI | Menus work with keyboard and gamepad alone | `UiContentTests`: no Selectable but that scrollbar has navigation off |
| Remapping | Every gameplay control can be rebound | KeyBindings.md, done |
| Settings remembered | Every preference is saved | `settings.json`, done |
| Larger UI | Up to two larger steps (Large, Larger) where the display fits them. This is not XAG's text scaling to 200%, which is Later. | `UiScale` |

### Preferences

There are new rows in a Display section of the options screen. Each is saved at once, in `settings.json` through `UserSettings`, and never replicated.

| Preference | Phase | Values | Effect |
|---|---|---|---|
| UI size | 3 | Normal; Large and Larger where the display fits them | Adds 0, 1 or 2 to the UI zoom. Screen-space UI and popups grow; world-space UI does not. |
| Reduce flashing | 6 | Off, On | `HitFeedbackPresenter`'s flashes (a hit's, an immunity's and a tick's) at a third of their strength, `HitFeedbackSettings.ReducedFlashShare` (the asset is never edited at runtime). Light props, flame pixels and flame frames stop flickering, as the lighting dev panel's flicker override stills them; the override stays the panel's, and the flicker stands still while either asks (Phase 6 notes). |
| Reduce motion | 6 | Off, On | No camera kick on hits. Popups fade in place instead of rising. |
| Allies' damage numbers | UiRevamp.md's Phase 3 | Shown, Smaller, Hidden | How the popups of hits, ticks, heals and blocks that involve another player but not the local one show: like the others (the default), at `Caption` (`CombatSettings.AllyPopupSize`, the ticks' size, since Galmuri has nothing smaller), or not at all. The local player's own and those no player is part of always show. |

## Architecture

### Core (`Assets/Scripts/Core/UI`)

- `UiScaleSettings`: a ScriptableObject (`Assets/Data/UI/UiScale.asset`) assigned on `MainLifetimeScope`. It holds the layout's minimum (480 x 270), the display lines per zoom step (390) and the most size steps (2). `UiScaleTests` checks the zoom against the Main scene's Pixel Perfect Camera instead of keeping its reference size here.
- `UiScale`:
  - `ReadableZoom`, `FittingZoom`, `StepsFor` and `ZoomFor(width, height, step, settings)`: plain and static, so EditMode can test them.
  - A Main-scope entry point (`ITickable`) that compares the display size and the size step from `UserSettings` each tick (three integer compares, no allocation).
  - It exposes `Factor` and `StepsThatFit`, and raises `Changed` when either changes.
- `UiPixelScale`: on each UI canvas.
  - Once injected, and on `Changed`, it sets its scaler to a constant pixel size with `Factor` and 32 reference pixels per unit.
  - The canvases are listed in their scopes' auto-inject objects: `PlayerHudCanvas`, `DamagePopupCanvas` and `GameMenuCanvas` in the Gameplay scope, and `MainMenuCanvas` in the Menu scope.

### Gameplay (`Assets/Scripts/UI`)

- `UiTheme`: a ScriptableObject (`Assets/Data/UI/UiTheme.asset`).
  - It holds a colour per `UiColour` role and, per `UiTextSize`, a font asset, its size in whole UI px and its outlined material (both enums append-only).
  - It has getters only and a `Create(...)` factory for tests.
  - It lives in Gameplay because Core does not reference TextMesh Pro.
- `ThemedText`, `ThemedGraphic`, `ThemedSelectable`: described under Components. They change only their own instance and have no `Update`.
- `AbilitySlotView`, `MoveMarker` and `PlayerHudView` swap their colour fields for roles and a theme reference. Each marker prefab names its own role (Phase 5). UiRevamp.md's Phase 1 moved the weapon icon and its theme from `PlayerHudView` to `WeaponSlotView`.
- `HudTooltip`, `TooltipTrigger` and `ITooltipSource` (Phase 5): a trigger on a slot or status icon shows the HUD's tooltip for the view on its object (`AbilitySlotView`, `StatusIconView`, and since UiRevamp.md's Phase 1 `WeaponSlotView`) while the pointer is over it or it is selected, and follows its changes; the numbers come from `TooltipDetails`. Local, never replicated.
- `FloatingDamageText` takes a text size per popup, through its `ThemedText`, and places itself on whole UI px.
- The display preferences (Phase 6), each read from `UserSettings` on this machine only: `HitFeedbackPresenter` shows every flash at `HitFeedbackSettings.ReducedFlashShare` of its strength with Reduce flashing on, and kicks no camera with Reduce motion on; `PropLighting` stills the flicker with Reduce flashing on, as the panel's `FlickerOverride` does, without writing it; `DamagePopupLayer` (injected through an `[Inject]` method) has a popup spawned with Reduce motion on fade in place. The options screen's two toggle rows come from `RowToggle` (`OptionsMenuView.AddToggleRow`, `KeyBindingRowView.SetToggle`).

### Editor (`Assets/Editor/Design`, `Assets/Editor/UI`)

- `DesignTables`: `Build()` returns each file's name and Markdown, built from the assets with `DamageMath`; `Write()` writes them to `Docs/Generated`. Menu item: `Tools/TopDownRPG/Design/Write Design Tables`.
- `DesignSearches`: the tuning views, a menu item per kind under `Tools/TopDownRPG/Design/Tune/` (`ViewState(kind)`, `Table(context, kind)`).
- `UiFonts`: `Tools/TopDownRPG/UI/Write Fonts` writes the font assets and their outlined presets (Phase 4).
- `UiThemeUsage`: the themed texts of the prefabs and menu scenes, and the contrast of each pair of roles they use, for `DesignTables` and `UiThemeTests` (Phase 4); since UiRevamp.md's Phase 1 also each bar's fill on its track.
- `UiSkinRecipe`, `UiSkinWriter` and the recipe's inspector (Phase 5): `Assets/Settings/UI/Recipe_UiSkin.asset`. `Generate(piece)` draws a piece without touching an asset; Generate writes every piece and sets its 9-slice border. Phase 6 appended `Check`, the toggle row's mark, and UiRevamp.md's Phase 1 `SlotCorners` and `Hatch`.

### Font

Galmuri by Lee Minseo, under the SIL Open Font License 1.1, which allows it in a game, modified and shipped, with its licence (`Assets/Fonts/Galmuri - OFL.txt`); it reserves no font name, so the subsets keep it. `Assets/Fonts` holds Latin subsets of Galmuri 2.40.4's 7, 9, 11 and 14 sizes (printable ASCII, Latin-1 for key names on other layouts, dashes, curly quotes, the bullet, the ellipsis, and a zero-width space added for TMP's empty input field), made with fontTools, and `UiFonts` writes from each a static SDF font asset: 6 atlas pixels per font pixel, 12 pixels of padding (a 1 UI px outline needs 6 and the edge a little more), every character of the subset, and an outlined preset. `LiberationSans SDF` stays TMP's default and is no longer used by the UI.

### Settings

`UserSettingsData` gains `uiSizeStep` (Phase 3), `reduceFlashing` and `reduceMotion` (Phase 6), and `allyPopups` (UiRevamp.md's Phase 3). A file from before reads them as their defaults, so the format version stays 1.

## Networking

- Nothing in this file is replicated.
- The theme, the UI zoom and the preferences change only how this machine shows things. The host's simulation never reads them.
- Reduce flashing and reduce motion act only on local visuals: `HitFeedbackPresenter`'s flash, the light flicker, the camera kick, the popups.
- Catalog ids and save GUIDs are unchanged.

## Removals and migrations

- **View colour fields.** The colour fields of `AbilitySlotView`, `MoveMarker` and `PlayerHudView` are removed, and their values move to the theme's roles. The serialized data is dropped on purpose.
- **`CombatSettings.periodicPopupScale`** became `periodicPopupSize`, a text size (Phase 4). The old value is dropped.
- **The weapon slot's transparent image**, which drew nothing, was removed in Phase 4.
- **Canvases** move to `UiPixelScale`: `PlayerHudCanvas` (1920 x 1080), `GameMenuCanvas` and `MainMenuCanvas` (640 x 360), and `DamagePopupCanvas` (constant pixel size).
  - In Phase 3 the HUD was converted to UI px, with its slots grown so `Caption` text fits (Phase 3 notes).
  - It was redesigned once, from the components, in Phase 5.
  - The fader keeps its own canvas (`TransitionCanvas`). It has no text or art to scale.
- **The options screen** is titled "Options" and its rows move into a scroll list (Phase 3).
- **Scenes.** `Main.unity`, `MainMenu.unity` and `Gameplay.unity` are edited through the editor, and the diff is checked for save churn before committing.
- **`Square.png`** was removed with its `.meta` in Phase 5, with the HUD's `HealthBarFill.png`, `WeaponSlotRing.png` and `WeaponSlotMask.png`, the world bar's `RoundedBar.png` (and its folder, `Assets/Textures/UI`) and `MoveMarker.prefab`, once nothing referenced them. `OptionsSectionHeader`, `KeyBindingRow` and `DamagePopup` moved into the components as `SectionHeader`, `Row` and `PopupText`, keeping their GUIDs.
- **Architecture.md:**
  - The asset values it restates (the weapons' numbers, the status table, the blank abilities' table) become links to `Docs/Generated` in Phase 1.
  - The UI, configs, editor tools, settings and tests sections change with each phase, and so do the scopes' auto-inject lists.
- **Tests that pin asset values.** Phase 2 turned them into checks of what each asset is (its damage type, statuses, targeting, faction), leaving its numbers to the generated tables.
- **KeyBindings.md.** The options screen gains its Display section (Phases 3 and 6).

## Phases

Each phase ships on its own, keeps the game playable offline and hosted, and comes with tests. A phase whose look can change settles it on previews first, signed off before its tests and docs. An open question is settled with the user at the start of the phase named for it. Each phase updates Architecture.md and Roadmap.md as it lands.

### Phase 1: generated design tables

- [x] The principles confirmed or rewritten with the user (Open questions).
- [x] `DesignTables` in `Assets/Editor/Design`: `Build()` and `Write()`, and the menu item `Tools/TopDownRPG/Design/Write Design Tables`. Markdown in the invariant culture, rows sorted by catalog index or by name.
- [x] `DamageTypes.md`, `Weapons.md`, `Abilities.md`, `StatusEffects.md` and `Units.md` written into `Docs/Generated`, with the derived columns resolved through `DamageMath`.
- [x] Architecture.md's restated values (the weapons' numbers, the `CombatSettings` values, the status table, the blank abilities' table, the Weasel's health) replaced with links to the tables. The plans keep theirs, as the record of what each phase chose.
- [x] Tests: `DesignTablesTests`:
  - the committed files equal `Build()` after normalising line endings, and a mismatch names the menu item;
  - the derived columns on definitions from `Create`: a recovery longer than the cooldown setting the period, the time to kill, a periodic total with its tick on expiry.

#### Phase 1 notes

- **Principles.** Settled with the user on 2026-11-10: the four as drafted, and a fifth, Made in the project, from CLAUDE.md's art rule.
- **What it is.** `DesignTables` builds the five files from `DesignContent` (the project's combat settings and status catalog, the weapon catalog, every ability and class under `Assets/Data`, and a `DesignUnit` per unit prefab in `Assets/Prefabs/Player` and `Assets/Prefabs/Mobs`); `DesignMath` holds the derived columns. Tests build their content from `Create`.
- **Choices.**
  - The tables show the values the game uses, through the definitions' getters, which clamp. Phase 2's `ContentTests` reads the serialized values instead.
  - The contrast column is measured on black, the colour `Outline` starts from, until Phase 4's theme names it.
  - A mob's attack period counts its whole timeline, since its recovery always plays out (`MeleeDamageDealer` starts its attacks with the recovery not cancellable).
  - Slots are named by their default keys (Q, W, E, R for a class, 1 and 2 for a weapon), and classes by their asset name, since `PlayerClass` has had no display name since KnownGaps.md's Phase 1.
  - A status's abbreviation is the first two letters of its display name, so Phase 2's unique abbreviations come down to names that differ in their first two letters.
- **What the tables show today.** Every weapon's period and the Weasel's is its cooldown. A Sword kills a Weasel in 5 hits (1.55 s), a Greatsword in 3 (2.2 s) and a Longbow in 5 (3.55 s, plus the arrows' flight); a Weasel kills a player in 10 (7.2 s). The Physical popup red, at 3.11:1 on black, is the only popup colour under 4.5:1.
- **Tests.** `DesignTablesTests` (9 tests): the committed tables against `Build()`, with no other file in the folder, its message naming the menu item (a table edited by hand fails it); the attack period with and without a recovery orders may end; hits and time to kill; the periodic tick count against the ticks `StatusEffectSet` deals; known contrast ratios; tables built from `Create` content.

### Phase 2: content checks and tuning views

- [x] Balance targets settled with the user (Open questions), and with them what becomes of the tests that pin asset values.
- [x] `ContentTests`: one case per kind (weapons, abilities, classes, statuses, combat profiles, mob configs, looks, character colours), every definition found under `Assets/Data` and `Assets/Settings/AI`, checked on its serialized values:
  - listed exactly once in its catalog;
  - required references set;
  - ranges valid (cooldowns, contacts, durations and tick intervals above 0; at least one max stack);
  - display names unique within the kind, swatch and choice names within their lists;
  - status abbreviations unique and descriptions present;
  - colours opaque.
- [x] The tests that pin asset values turned into relations or target checks, as the balance targets settle.
- [x] Unity Search table views for weapons, abilities, statuses and mob configs: menu items rather than saved searches under `Assets/Editor/Design/Searches` (Phase 2 notes).
- [x] Tests: `ContentTests` passes on the project's assets, and each rule fails on a definition from `Create` that breaks it.

#### Phase 2 notes

- **Balance targets.** Settled with the user on 2026-11-10: a spec that states a numeric target, as a range, gets a check of it against the derived columns, in a `BalanceTargetsTests` the first target creates; none is set yet. No test pins an asset's value: the committed tables record every value, and `DesignTablesTests` fails until a change is written into them, so every tuning change shows in a diff. The pinned tests now check what each asset is, not its numbers: `DamageResolutionTests`' units test (now `Content_UnitsFightForTheirFactions_WithPhysicalHits`: the factions and the physical hits, no longer the health of 100, the Sword's 20 and the Weasel's 10) and its combat settings test (the scene's reference only), and `AbilityEffectTests`' content test (Bolt's Frost and Chill along a line, Whirl around the caster, Smite's stun, Mend's heal and Fortify, without their amounts, range and radius).
- **The checks.** `ContentRules` (in the test assembly) holds one check per kind, on serialized values, each problem a line naming the asset and the field; a field it reads that no longer exists throws, so a rename fails loudly. `ContentTests` runs it on nine kinds: the eight planned, and the combat settings, whose resolution rules and popup colours are content values too. Each kind must find at least one asset, so a moved folder cannot leave a case with nothing to read. The project's content passed every rule on the first run; nothing needed fixing.
- **What the rules cover.** Weapons: an id, a name, a hit that is not negative, a cooldown and contact above 0, an icon and an attack clip, and an area or a projectile with its visual. Abilities: a cooldown above 0, and a range above 0 when aimed at a point or a unit (the blanks aimed along a direction keep 0 until abilities move the caster). Classes: four slots. Statuses: a description, an opaque colour, a duration above 0, at least one stack, a periodic effect with an amount and interval above 0. Profiles: a faction, resistances within ±100. Mob configs: speeds, the attack's timings, an area, a clip and an obstacle mask. Looks: a name, a set and locomotion settings. Character colours: unique list, swatch and choice names, opaque ramps, each choice's list and own swatch existing. Catalogs: no empty entry, each definition once, every definition listed. Abilities have no catalog (the wire names them by slot and weapon), and classes no display name.
- **The tuning views.** `DesignSearches` builds a Search window state in the table view (`SearchViewState`, `SearchTable`, `SearchColumn.Enumerate`), shown with `SearchService.ShowWindow`, all public in Unity 6.4; saved searches would need `SearchQueryAsset`, which is internal there, so they are menu items under `Tools/TopDownRPG/Design/Tune/` instead. The query is `t=<Type>`: `t:PlayerWeapon` also finds the player prefab, through its `PlayerWeaponController`.
- **Left as they are.** `StatusEffectTests`' catalog check and `CharacterColoursTests`' project test overlap `ContentTests` in part; they also check behaviour (Unstoppable clearing a stun) and the adventurer's palette, so they stay.
- **Tests.** `ContentTests` (17 cases): the nine kinds on the project, and the rules reporting a weapon, an ability, a status (and two sharing an abbreviation), a profile, a mob config, the combat settings, a catalog, a class, a look catalog and character colours that break them. A class keeps four slots through `OnValidate`, so its case sets three by reflection. `DesignSearchesTests` (4 cases): each view is a table over every asset of its kind, the name first, a column for its tuned fields and none for the script.

### Phase 3: one UI scale, the UI size setting and the options screen

- [x] Control size settled with the user on the previews (Open questions).
- [x] `UiScaleSettings` (`Assets/Data/UI/UiScale.asset`, on `MainLifetimeScope`), `UiScale` and `UiPixelScale` on `PlayerHudCanvas`, `DamagePopupCanvas`, `GameMenuCanvas` and `MainMenuCanvas`, each injected through its scope's auto-inject list.
- [x] The HUD converted to UI px: sizes and positions divided by three. Every text on the HUD, the menus and the popups moved to the nearest type size at or above `Caption`, none auto-sizing below it.
- [x] The options screen titled "Options", its rows in a scroll list that keeps the selected row in view, and a Display section with the UI size row.
- [x] `uiSizeStep` in `settings.json`; KeyBindings.md's options screen updated.
- [x] Previews of the HUD, the menus and the options screen at the displays in the Scale table and at each size step that fits, signed off before the tests and docs. They are captured with the canvases drawn through a camera, because overlay canvases do not render into a texture.
- [x] Tests:
  - `UiScaleTests`;
  - settings: a file without `uiSizeStep` reads Normal, and changing it writes the file;
  - `OptionsMenuControllerTests` and `OptionsMenuPlayModeTests`: the UI size row, and the list scrolling the selected row into view;
  - PlayMode: changing the UI size sends nothing.

#### Phase 3 notes

- **Settled with the user on 2026-11-10**, on previews of every display in the Scale table: the look as previewed; the main menu's buttons at `Heading` rather than `Body` (option B; at `Body` the title screen came out half its old size); and the control minimum kept at 12 UI px, every control now being 16 UI px tall or more.
- **The previews.** Written to the gitignored `Recordings/DesignSystem/Phase3` (before, after, the main menu's two options, and side-by-side sheets at 1080p) by a temporary tool, since removed: the HUD and in-game menu prefabs and copies of the main menu's and the popups' canvases in an empty scene, each canvas switched to the camera's space and rendered through a render request into a texture of the display's size. The before set showed the options screen's 37 rows squeezed into a third of their height.
- **The HUD.** Not divided by three throughout: its slots grew so `Caption` text fits them. A 140 x 22 health panel and a 34 UI px weapon slot 10 UI px from the corners; 24 UI px ability slots 2 apart, the key at the top right and the name at the bottom (they overlapped once the text grew), with a 6 UI px gap before the weapon's; 14 UI px status icons, the letters at the top left and the stacks at the bottom right. It comes out a little larger than before at 1080p (slots 72 px, were 56), and its smallest text grew from 12 to 21 px.
- **The text sizes, by role.** `Caption`: the HUD's ability names, keys, status letters and stacks. `Body`: the HP and the cooldown seconds, menu rows and buttons, notices, prompts and confirmations, and the binding buttons auto-sizing from `Caption`. `Heading`: section headers, damage popups and the main menu's buttons. `Title`: "Menu" and "Options". `Display`: the game's name.
- **The menus.** The in-game menu's box is 160 x 128 UI px and the confirmations 240 x 64. The main menu's buttons are a 160 x 188 column with the title just above it. The options screen's list is 346 UI px wide between the title and the footer, rows 16 tall, with a 4 UI px scrollbar shown when they overflow.
- **The UI size row** sits in a Display section between the Character section and the controls. On a display that fits no larger size its button keeps Normal. Opening the screen selects its first row, the Look row, rather than the first binding, so the list opens at its top.
- **Tests.** `UiScaleTests` (the Scale table, the project's settings against the camera, `Caption`'s body height from the font's glyphs, which this phase's 390 relies on, so it moved here from Phase 4's `UiThemeTests`, the zoom following the step, a canvas following the zoom); `ScrollIntoViewTests`; `UserSettingsTests`, `OptionsMenuControllerTests` and `OptionsMenuPlayModeTests` for the step, the row, the 39 rows and the first row selected; `UiScalePlayModeTests`. Nothing counts the bytes a session sends, so the PlayMode test checks what a client would see instead: with the host on Large, the host's three Gameplay canvases at zoom 4, and the client still connected, at Normal, its copy of the host's player unchanged.
- **Left for later phases.** The join field's placeholder is still italic (Phase 4's `UiContentTests`), and the binding buttons still auto-size their text.

### Phase 4: the theme

- [x] The font settled with the user on previews (Open questions).
- [x] The project font assets in `Assets/Fonts`, with the atlas padding a 1 UI px outline at `Caption` needs and the static atlas's character set (Architecture.md's Known gaps: TMP's fallback font).
- [x] `UiTheme` (`Assets/Data/UI/UiTheme.asset`) with the `UiColour` roles, the `UiTextSize` scale and the fonts. The merged roles, the Physical popup red and the placeholder settled on previews.
- [x] `ThemedText`, `ThemedGraphic` and `ThemedSelectable` on every UI text, image, sprite and control: the move marker, the hover ring, the world bar and the world status icons included. `AbilitySlotView`, `MoveMarker` and `PlayerHudView` keep roles instead of colours.
- [x] Damage popups, the waystone popup and other text over the world outlined in `Outline`.
- [x] `UiTheme.md` added to `DesignTables`, and `DamageTypes.md`'s contrast measured against `Outline`.
- [x] Tests: `UiThemeTests`, `UiContentTests`, `DesignTablesTests` with `UiTheme.md`, and the views' tests on a theme from `Create`.

#### Phase 4 notes

- **Settled with the user on 2026-11-10**, on previews over the Overworld's Clearing at 1080p and 720p:
  - the font: Galmuri, a pixel font, over LiberationSans;
  - the theme as previewed: the merged roles, `Icon` and `Border`, the placeholder, `SurfaceEmpty` at 0.75, an opaque `ControlDisabled`, the HUD grown to fit the pixel font, the status letters outlined, the main menu's status line in `Accent` and its buttons in sentence case;
  - the Physical red: `#E64040`, of `#E64040`, `#F04A4A` and `#FF5C5C`.
- **The previews.** Written to the gitignored `Recordings/DesignSystem/Phase4` (`font/`, `theme/`, `final/`) by a temporary tool, since removed, as in Phase 3: the HUD, menu and options prefabs and a copy of the main menu's canvas switched to the camera's space and rendered through a render request into a texture, over a render of the Clearing at the display's zoom. Five candidate pixel fonts were set side by side first (`font_candidates_x3.png`); Galmuri was the clearest in mixed case and the only one with a family of sizes near the type scale.
- **The font.** Galmuri7, 9, 11 and 14 at 8, 10, 12 and 15 UI px. Its smallest size is a step larger than LiberationSans' `Caption` (a cap height of 7 UI px, where it was 4.8), so `Caption` and `Body` are both Galmuri7 and every size grew a little; "Blank Bolt" no longer fit the HUD's 24 UI px ability slots. The fonts are SDF rather than bitmap so the outline can come from a material. Centred text in a rect of odd width lands on half a UI px and blurred on the first preview, which `ThemedText`'s snapping fixes.
- **The outline.** TMP's outline is centred on the glyph's edge, so it ate into the pixel font's one-pixel strokes. Dilating the face by the outline's width keeps it whole and puts the outline outside. Measured on renders at 8 display pixels a UI px, the outline reaches 2 x width x padding atlas pixels outside the glyph: a width of 0.25 at 12 pixels of padding and 6 atlas pixels per UI px is 1 UI px. Its corners are rounded, as an SDF outline's are.
- **The HUD.** Ability slots grew from 24 to 32 UI px (the bar 210 wide), the name's two lines 10 UI px apart under the key; status icons from 14 to 20, 4 above the slots. The status letters and stacks became white, outlined: dark text failed on Root's and Vulnerable's colours (3.1 and 3.7:1) and on the part of an icon its elapsed fill darkens. The weapon slot's transparent image went. The cooldown seconds still sit over an icon-less ability's name, as before; Phase 5 redesigns the slots.
- **Bugs found on the way.** The main menu's status line was dark text on the dark background (about 1.05:1), so it could not be seen; it is `Accent` now. Its buttons read New Game, Host Game and Join Game, against the sentence case rule.
- **The conversion.** A temporary editor script added the themed components to the UI prefabs, the Weasel's world bar and icons, the move marker, the main menu's canvas and the hover ring, and set the saved fonts, sizes and colours to the theme's, so the editor shows what play shows. Nested prefab instances were converted in their own prefabs. The scenes' prefab instances keep only their root overrides, and `git diff` showed no save churn.
- **Contrast.** `UiThemeUsage` reads the pairs from the themed texts themselves rather than a list in the theme. Translucent backgrounds count over white and over black; the HUD's key labels are measured on `SurfaceEmpty`, the worse of the two frames they sit on. A placeholder is not measured on a disabled control: only Continue is ever disabled. Every pair passes; the lowest are the placeholder on `ControlNormal` (4.91:1), Text on `Health` (5.01:1) and a disabled label (3.41:1 against 3:1).
- **Left for Phase 5.** The skin and the components, the marker shapes, tooltips; `TextMuted` and `TextDisabled` have values but no text uses them yet; `Border` only colours the weapon ring.
- **Tests.** `UiThemeTests` (15), `UiContentTests` (6), `DesignTablesTests` (the theme's table from `Create` content, the popups against a white `Outline`), and the views' tests on a theme from `TestThemes`: the slot's frames and flash, the attack-move marker, the weapon icon's tints, the popups' sizes, fonts and outlines, and popups on whole UI px. `UiScaleTests`' Caption check moved to `UiThemeTests`, on the theme's font.

### Phase 5: skin and components

- [x] Damage types on popups settled with the user on previews (Open questions).
- [x] `UiSkinRecipe` and its writer, the skin settled on previews: a panel, a raised panel, a slot frame, a bar frame and fill, and the marker shapes, in `Assets/Sprites/UI/Skin`.
- [x] The component prefabs and variants in `Assets/Prefabs/UI/Components` (Components table), the scroll list, the swatch row and the tooltip included.
- [x] The HUD, the main menu, the in-game menu and the options screen rebuilt from them. `Square.png` removed with its `.meta`.
- [x] Distinct marker shapes for a move, an attack move and the hover ring.
- [x] Tooltips for statuses and abilities, with a description on every ability.
- [x] Tests:
  - `UiContentTests` on the rebuilt prefabs;
  - the tooltip shows a status's or an ability's name and description on hover and on selection, and the status row blocks clicks while it shows;
  - the marker kinds have different sprites;
  - the skin writer's sprites;
  - `ContentTests` and `DesignTablesTests` for the abilities' descriptions.

#### Phase 5 notes

- **Settled with the user on 2026-11-10:**
  - **Damage types on popups:** left as they are. Four options were previewed over the Clearing: today's colour only; a 7 UI px generated type icon before the number; the `Resisted` and `Weakness` flags as arrows after it (↓ and ↑ from Galmuri, since its ▼▲ outweighed the digits and its small ▾▴ are missing from Galmuri7); and both.
  - **The skin**, on mocks of the HUD, the menus and the world:
    - 1 px cut corners;
    - `Border` a grey-blue `#9A9EB3`, of white, that and white at 40%;
    - the world bar on the skin's 22 x 4 bar, of that, 24 x 5 and the smooth bar;
    - the markers 31 x 15 with 5 px arms, of that and 29 x 15 with 4;
    - raised Primary and Danger buttons with a 2 px lip, and a flat, dimmer Secondary button on a new role, `ControlSecondary`;
    - the cooldown seconds in a slot's top left corner, a square weapon slot, and swatch chips.
  - **The rebuilt screens and the tooltips** as previewed, and the ten blank abilities' descriptions as drafted.
- **The previews.** Written to the gitignored `Recordings/DesignSystem/Phase5` (`popups/`, `mock/`, `final/`) by a temporary tool, since removed, as in Phase 4:
  - the canvases drawn through a camera over a render of the Clearing;
  - the Weasel, the player and the markers placed in the untitled scene beside the chunk before the render;
  - popups placed by hand on whole UI px;
  - the arrows drawn from dynamic font assets made from Galmuri's full fonts, never written.
- **The skin.** `Recipe_UiSkin.asset` writes ten sprites. The panel and its frame are 8 px with 3 px borders, and the raised panel 8 x 10, its lip at 0.6 grey. The slot frame has a black line at 40% inside it. The bar frame is 5 px with 2 px borders, so it slices down to the world bar's 4 px. The bar fill is 3 px of square white, so a filled image keeps its edge wherever it stops. The three markers share a 43 x 23 canvas, their 2 px lines outlined in black at 50%, and the pip is 5 px. The world status icons are pips now.
- **The components.** Twenty-five prefabs, listed in the Components table, and the screens are instances of them:
  - the HUD: a `Bar`, a `Slot` for the weapon, six `SlotAbility`, eight `StatusIcon` and one `Tooltip`, drawn last;
  - the in-game menu: a `Scrim` holding a `PanelRaised` box of `Button`s and a `Dialog`;
  - the options screen: a full-screen `PanelRaised`, a `ScrollList`, a `NoticeLine`, a secondary Reset all and a primary Back, a `ScrimModal` listening panel and a `Dialog`; its rows come from `Row` and `RowSwatch`, the colour rows' chips showing the picked swatch's first ramp's middle tone;
  - the main menu: a `PanelRaised` background, `ButtonLarge`s and a `NoticeLine` at `Heading`; the join field keeps its own control with the panel's sprite;
  - the Gameplay scene: a `MarkerHover`, and the two marker prefabs, each in a pool of its own;
  - the Weasel: a `WorldBar`.
- **The conversion.** A temporary editor script, since removed, built the components and rebuilt the screens, in their own prefabs and scenes. It kept every view's references, the buttons' clicks and navigation, and the old prefabs' GUIDs where they moved. The scenes' diffs hold only the rebuilt objects.
- **Code.** `ThemedSelectable` names its normal role. `MoveMarker` reads its own role, and `PointerFeedbackLayer` keeps a pool per marker prefab. `AbilityDefinition` has a description, which `ContentRules` requires and `Abilities.md` lists. `KeyBindingRowView` has an optional swatch chip and `OptionsMenuView` a swatch row prefab. `PlayerHudView` exposes its health fill, text and weapon icon to tests. Selecting a slot or an icon shows its tooltip, but nothing selects one yet: the HUD has no navigation.
- **Found on the way.** `WorldHealthBarTests` still asked for a bright red fill on the Weasel's bar, which Phase 4's theme had made `Health`; it now checks the bar's roles and skin.
- **Left as they are.** `TextDisabled` has no text yet. The world status icons and auras stay colour-only until statuses have icon art (Later). The main menu's game name is at `Title`, although the type scale names `Display` for it; the rebuild left its text alone.
- **Tests.**
  - `UiSkinWriterTests` (4): every piece greyscale and sliced around its corners, the cut corners, the frame's width and the raised panel's lip, the markers' different shapes on one canvas, and the project's sprites being what its recipe draws, imported at 32 PPU with their borders.
  - `TooltipTests` (7): a slot on hover and a status icon on selection showing their name and description, an empty slot showing none, the tooltip following its source and hiding with it, kept inside its canvas and above its target, a shown status icon taking the click and a hidden one not, and the HUD wiring every slot and icon to its one tooltip.
  - `UiContentTests` (8): every image draws the skin or nothing, and the marker kinds' sprites and roles, with the Gameplay scene using them.
  - The rest: `ContentTests` and `DesignTablesTests` on the abilities' descriptions; `StatusFeedbackTests` on the icons' frames taking the click; `PlayerFeedbackTests` on a marker prefab and pool per kind; `WorldHealthBarTests` on the skinned `WorldBar`; `MobPlayModeBehaviorTests` reading the HUD through its view.

### Phase 6: reduce flashing and reduce motion

- [x] The toggle row, `RowToggle`, a variant of `Row` with the skin's new check mark, settled on previews.
- [x] `reduceFlashing` and `reduceMotion` in `settings.json`, and their rows in the Display section.
- [x] Reduce flashing: `HitFeedbackPresenter`'s flashes scaled by the new `HitFeedbackSettings` field; light props, flame pixels and flame frames stilled as `PropLighting`'s flicker override stills them.
- [x] Reduce motion: no camera kick on hits, and popups that fade in place.
- [x] The flicker measured on previews against WCAG's flash threshold, and the result written under Accessibility.
- [x] Tests:
  - settings: a file without the fields reads them off, and changing either writes the file;
  - the presenter's flash amounts and camera kick with each preference on and off;
  - popups rising, or fading in place, by the preference;
  - the flicker stilled by the preference, the panel's override left alone;
  - the toggle row in `UiContentTests`, its check mark in `UiSkinWriterTests`, and its two rows in `OptionsMenuControllerTests`;
  - PlayMode in Testlands, with the in-process host and client: changing the preferences sends nothing, and the host's HP and statuses, and the client copy's hit reports, are the same with reduce flashing on and off.

#### Phase 6 notes

- **Settled with the user on 2026-11-10:**
  - **The toggle row's mark:** a check mark, a new skin piece, over a filled square. The square sat in the swatch chips' column and read as a yellow swatch (`rows/compare_marks_zoom.png`).
  - **What Reduce motion covers:** the camera kick and the popups' rise, as the spec named. A hit's nudge (the struck sprite 2 px then 1 px away) and hit stop (the 80 ms hold) stay, since neither moves the screen (Decisions).
  - **The looks and the measurement as previewed:** the toggle rows, every flash at a third, popups fading in place, and the flicker measured below.
- **The previews.** Written to the gitignored `Recordings/DesignSystem/Phase6` (`rows/`, `flicker/`, `flash/`, `popups/`) by a temporary tool, since removed:
  - the options screen through the real controller on a camera's canvas, as in Phases 4 and 5, its themed components woken by reflection and its rows refreshed after them;
  - the Overworld's chunk (1, 1) beside an untitled scene whose Global Light 2D took the night key's sky (0.88), the editor's night preview (`NightPreview`) setting the shader globals, light props and Weasels placed south of the waystone's plaza, and a real `PropLighting` over a `DayClock` on a `ManualClock`, ticked one flicker step at a time, each Light 2D's mesh built through its internal `UpdateMesh` before each render;
  - popups placed on the canvas from the world camera's projection and laid over the world's render.
- **The toggle row.** `RowToggle`, a variant of `Row` made by an editor script, has a 12 UI px column before the value, where the swatch row has its chip. The column holds a box (the panel sprite in `Shade`, a frame in `Border`) and the skin's `Check`, 8 x 5 px in 2 px strokes, in `Accent`, shown only while on (`KeyBindingRowView.SetToggle`). The controller writes On or Off on the button, which flips the preference; Reset turns it off, so the Display section's three rows keep the screen's columns.
- **The settings.** `reduceFlashing` and `reduceMotion` in `UserSettingsData` read as off from a file without them, so the format version stays 1. `UserSettings` has a getter, a setter that writes at once and a change event for each, modelled on `UiSizeStep`.
- **Reduce flashing.**
  - `HitFeedbackSettings.ReducedFlashShare`, a third, scales every flash the presenter shows, read at each hit.
  - **Who owns the flicker override:** the lighting dev panel keeps `PropLighting.FlickerOverride`, and the preference never writes it. `PropLighting` reads `UserSettings.ReduceFlashing` at each update, a bool and no allocation. The flicker stands still while the override is off or the preference is on (`Flickers`), and every light is written at the update either changes. So the panel's `V` still stills the flicker for a developer whose preference is off, and turning the preference off never restarts a flicker the panel stilled, nor the reverse.
  - Stilled, the step is 0: the lights burn at their steady strength, `_GlowFlicker.y` is 0 (no pixel flickers) and every flame shows its first frame, as the override does since Lighting.md's Phase 6. No shader changed, so the `ShaderCompile` category had nothing new to check.
- **Reduce motion.** The presenter skips the camera kick while it is on. `DamagePopupLayer` takes `UserSettings` through an `[Inject]` method (a component the Gameplay scope registers) and tells each popup whether it rises as it spawns, so a popup shown before the change still finishes its rise. In place, a popup fades where it starts, 1.1 units above a Weasel's feet, under its bar; quick hits stack there instead of spreading upward, which the previews showed.
- **The measurement.** By WCAG 2.3.1, a flash is a pair of opposite changes of 0.1 or more in relative luminance (sRGB linearised, 0.2126 R + 0.7152 G + 0.0722 B), the darker state under 0.8. Content fails when such flashes come more than three times a second over more than a quarter of a 10° field, 2.8% of a 1080p display (about 58,000 px). Each view was rendered at 1920 x 1080 at zoom 3 (20 x 11.25 units) for 25 flicker steps, 3 s; the image changes only at a step, 8 a second. Per pixel, the tool took the change between steps, the largest area changing by 0.1 in one step, and the pixels with more than three flashes in any 8 steps:

  | View | Reduce flashing | Largest change | Changing by 0.1 or more in one step | More than 3 flashes a second |
  |---|---|---|---|---|
  | Campfire | off | 0.76 | 729 px, 0.035% | 36 px |
  | Torch | off | 0.75 | 450 px, 0.022% | 9 px |
  | Brazier | off | 0.71 | 558 px, 0.027% | 36 px |
  | All three | off | 0.76 | 1,566 px, 0.076% | 144 px |
  | Each, and all three | on | 0.033 to 0.037 | none (none by 0.05 either) | none |

  - Only the flames' own pixels change by 0.1: their frames and their flickering colours. The light pools, stepping by 10 to 15% of a dim light, change by under 0.02, and a lit tree by 0.02 to 0.05. With Reduce flashing on, what still changes is a plant swaying.
  - The worst view is about 37 times under the threshold, and a view shows four prop lights at most (Lighting.md's Phase 6). At 720p, 1440p and 4K the zoom follows the display height, so the flames cover the same share.
  - **The hit flash**, measured on a Weasel at night: it covers 0.27% of a 1080p display (5,616 px). Its mean luminance rises by 0.96 under a full hit flash and by 0.32 at a third, by 0.48 and 0.16 under an immune flash, and by 0.13 and 0.044 under a Fire tick's wash, which at a third no longer counts as a flash.
- **Tests.**
  - `UserSettingsTests`: the preferences off by default and in an older file, each change written at once and raised once.
  - `HitFeedbackTests`: every flash at the reduced share, the rest of a hit kept; no camera kick with Reduce motion on, the rest kept.
  - `CombatComponentTests`: a popup spawned while Reduce motion is on fading in place, one shown before still rising.
  - `PropLightingTests`: the preference stilling the flicker without writing the override, and the flicker moving again only once neither asks.
  - `UiContentTests`: the toggle row a variant of the row, its box, mark and roles, its contrast, and every options screen using it. `UiSkinWriterTests`: the check mark pixel by pixel.
  - `OptionsMenuControllerTests` and `OptionsMenuPlayModeTests`: the two rows (41 in all).
  - `NetworkHitFeedbackPlayModeTests`: the same chilling Frost hit three times, with no machine reducing flashing and motion, then the host, then the client. The flash shows at the reduced share only on the machine reducing it, the client's camera kicks unless the client reduces motion, neither machine's settings follow the other's, and the Weasel's HP lost and statuses and the client's report are the same each time.

### Later

- A high-contrast theme, with the themed components reapplying on a theme change.
- Text scaling to 200% (XAG), which needs layouts that reflow.
- Status icon art, for the HUD and the world status icons.
- Colour-vision options for damage-type and status colours.
- Co-op identity: player identity colours and markers (not the colours a player picks for its character), which need a player slot the host assigns so every machine agrees (UiRevamp.md's Phase 5). Players' world bars and status icons came with UiRevamp.md's Phase 4.
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
- **EditMode, `DesignSearchesTests`.** Each tuning view is a table over every asset of its kind, its name first and a column per tuned field.
- **EditMode, `ContentTests`.** One case per content kind, reading the project's assets and never writing them. It checks serialized values, not the clamping getters:
  - catalog membership and uniqueness;
  - required references;
  - ranges (cooldowns, contacts, durations and tick intervals above 0; at least one max stack);
  - unique display names;
  - unique status abbreviations, and present status and ability descriptions;
  - opaque colours.
- **EditMode, `UiScaleTests`.** `ZoomFor` at every display in the Scale table, windowed sizes included, for each size step, and which steps fit. The zoom equals the Main scene's Pixel Perfect Camera's at 720p, 1080p, 1440p and 4K.
- **EditMode, `ScrollIntoViewTests`.** A selected row scrolls into view by the least.
- **EditMode, `UiThemeTests`**, on a theme from `Create`, then on the project's theme:
  - the theme's getters, and a missing role or size;
  - the contrast of a translucent background, and the thresholds by size;
  - the themed components: font, size, colour and outline set, the size changed and reset, glyphs snapped to whole UI px, a graphic's colour, a control's colour block;
  - every role and size held once; each font static, holding the UI's characters, on the pixel grid; each outlined preset 1 UI px outside a whole face;
  - every text-and-background role pair a `ThemedText` uses, against its threshold by size;
  - the gameplay colours against `Outline`;
  - `Caption`'s body height in the theme's font at every display of the Scale table.
- **EditMode, `UiContentTests`**, on the project's UI prefabs, the mobs' world UI and the menu scenes (only read):
  - every text has a `ThemedText` on the project's theme, does not auto-size and is not bold;
  - no italic or all-caps text below `Title`;
  - every image has a `ThemedGraphic`, or is a themed control's target, and so has every world UI sprite;
  - every UI canvas has a `UiPixelScale`;
  - every Selectable has a `ThemedSelectable` and, the scroll list's scrollbar excepted, is at least 12 UI px each way with navigation on;
  - every image draws a skin sprite or none, and the marker kinds draw different sprites (Phase 5);
  - the toggle row shows its state as a check mark in a framed box, and every options screen makes its toggle rows from it (Phase 6).
- **EditMode, settings.** A file without the new fields reads their defaults; changing one writes the file.
- **EditMode, the preferences (Phase 6).** `HitFeedbackTests`: every flash at the reduced share with Reduce flashing on, no camera kick with Reduce motion on, the rest of a hit kept. `PropLightingTests`: the flicker stilled by the preference, the panel's override untouched. `CombatComponentTests`: popups fading in place. `OptionsMenuControllerTests`: the two toggle rows.
- **PlayMode.** In Testlands, with the in-process host and client:
  - changing the preferences sends nothing (the UI size from Phase 3, checked by what the client shows; the others from Phase 6, checked by each machine's flash and kick and by settings that never follow the other machine's);
  - the host's HP and statuses, and the client copy's hit reports, are the same with reduce flashing on and off.
- **EditMode, `UiSkinWriterTests`.** Every skin piece greyscale and sliced around its corners, the panels' square corners (cut at a recipe's cut) and the raised panel's lip, the markers different shapes on one canvas, the check mark's two arms, and the project's sprites what its recipe draws.
- **EditMode, `TooltipTests`.** A slot's or a status icon's tooltip on hover and on selection, following its source and inside its canvas; a shown status icon taking the click; the HUD's one tooltip wired to every slot and icon.
- **Manual.** The Phase 3 and 4 previews at each display in the Scale table, the Phase 5 skin previews, and Phase 6's toggle rows, flashes, popups and flicker measurement.
- **Before merging into `main`:** the full EditMode and PlayMode suites.

## Open questions

None are left. Each was settled with the user at the start of the phase named: the principles in Phase 1, the balance targets in Phase 2, the control size in Phase 3 (12 UI px kept), the font in Phase 4 (Galmuri, a pixel font) and the damage types on popups in Phase 5 (left as they are; Decisions). UiRevamp.md's Phase 3 settled other players' popups as a preference (Preferences: Allies' damage numbers).

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
