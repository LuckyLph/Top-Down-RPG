# Key bindings and options menu

Spec for remappable gameplay controls, an options menu to change them (reachable from the main menu and in game), and saving them on the local machine. [Architecture.md](Architecture.md) describes what exists today; this file describes the target and the order in which to build it. Tick off the phase checklist as steps land, and move each settled behaviour into Architecture.md as its code arrives.

Every rule in [Multiplayer.md](Multiplayer.md#rules-for-new-code-starting-now) applies: nothing here may assume one player, and each step is checked with a host and 2–4 players. Bindings are a per-machine preference, not game state, so they are the one kind of persistent data that every machine writes, clients included (see Saving).

## Where we start

- The `Player` map of [InputSystem_Actions.inputactions](../Assets/InputSystem_Actions.inputactions) holds the controls from [PlayerControls.md](PlayerControls.md#controls): `MoveClick` (right mouse button), `Point` (pointer position), `Stop` (X) and `Ability1`…`Ability6` (Q W E R A S), all in the `Keyboard&Mouse` group. It also still holds the template's unused actions (Look, Interact, Crouch, Jump, Previous, Next, Sprint).
- `PlayerInputService` (Main scope) wraps that map on the inspector's `InputActionAsset`, the same asset the Main scene's `EventSystem` uses for the `UI` map. `GameplayInputGate` enables the map only in game and outside transitions.
- Key labels are hard-coded: `AbilitySlots.KeyName(slot)` gives Q W E R A S to the ability bar and to the editor gizmos in `PlayerAbilities` and `PlayerController`.
- There is no options screen and no in-game menu. Escape does nothing in game; the only way back to the main menu is a lost connection.
- `GameSave` writes `save.json` through `ISaveStore`/`FileSaveStore`, and only where `IGameAuthority.IsAuthoritative`.

## Goals

- Every gameplay control the player presses can be bound to another key or mouse button from an options menu.
- The options menu opens from the main menu and from an in-game menu on Escape.
- Changes take effect at once, survive restarts, and can be reset per control or all at once.
- Labels shown in game (the ability bar) always match the current bindings and the keyboard layout.

## Non-goals (for now)

- Gamepad gameplay bindings (gameplay stays mouse and keyboard, see [PlayerControls.md](PlayerControls.md#non-goals-for-this-rework)). The gamepad keeps working to navigate the menus.
- Modifier combinations (Shift+Q), double taps, holds as separate bindings.
- A second binding per action.
- Rebinding the `UI` map, the pointer, or the menu key.
- Other options (audio, video, cast modes). The settings file and the options screen leave room for them.

## Decisions

| Topic | Decision |
|---|---|
| Rebindable actions | `MoveClick`, `Stop`, `Ability1`…`Ability6`. `Point` (the pointer) and the new `Menu` action are fixed. |
| Bindings per action | One, in the `Keyboard&Mouse` group. |
| Allowed inputs | Any keyboard key except Escape, and the mouse buttons (left, right, middle, back, forward). Not the pointer, its delta, the scroll wheel, or other devices. |
| Reserved keys | Escape opens and closes menus and cancels a rebind, so it can never be bound. |
| Conflicts | Swap. Binding a key that another rebindable action uses gives that action the key this one had, and the screen says so ("Stop moved to Q"). No action is ever left unbound. |
| Physical keys | Bindings are stored as physical key positions (the Input System default: `<Keyboard>/q` is the key where Q sits on a US layout). Labels come from the active layout, so on AZERTY the default for `Ability1` reads "A". Rebinding records the key the player pressed, whatever its label. |
| When changes apply | At once, and saved at once. There is no Apply or Cancel step. |
| Reset | Per action (back to the asset's default) and "Reset all" behind a confirmation. |
| Saving | A separate `settings.json` next to `save.json`, written on every machine. Starting a new game or deleting the save never touches it. |
| In-game menu | Escape opens it: Resume, Options, Leave game, Quit. In single player (no network session) it pauses the game. In an online session, hosting alone included, it pauses nothing: no new orders are made and the current order runs on, the same rule as during transitions. |
| Pausing | Game time stops (`Time.timeScale` 0) through the same mechanism as the transition freeze, so the clock, cooldowns, statuses, physics, mob AI and animations all stop together. Menus and the fader run on unscaled time. |
| Unused template actions | Removed from the `Player` map now, so the conflict rules only ever deal with real controls. |

## Controls

| Action | Label in the menu | Section | Default | Rebindable |
|---|---|---|---|---|
| `MoveClick` | Move / attack | Movement | Right mouse button | Yes |
| `Stop` | Stop | Movement | X | Yes |
| `Ability1`…`Ability4` | Class ability 1…4 | Class abilities | Q W E R | Yes |
| `Ability5`, `Ability6` | Weapon ability 1, 2 | Weapon abilities | A S | Yes |
| `Point` | – | – | Pointer position | No |
| `Menu` | – | – | Escape, gamepad Start | No |

Short labels for the ability bar: the layout's key name for keys ("Q", "Space", "F1"), and "LMB", "RMB", "MMB", "M4", "M5" for mouse buttons.

## Behaviour

### Options screen

- One screen with a Controls section for now, built from data: a row per rebindable action, under its section header, showing the label, the current binding and a reset button. The footer has Reset all and Back.
- Selecting a binding (click, Enter, or gamepad South) starts listening on the next frame, so the click that opened it is never captured. A modal panel says "Press a key or mouse button for Class ability 1. Esc to cancel."
- While listening, the panel blocks pointer clicks on the rest of the screen and UI navigation and submit are suspended, so the key pressed only rebinds. Escape cancels. The press that ends listening (including Escape) is not also read as Back or Submit.
- A refused input (Escape is the only one) cancels. A key that is already this action's binding ends listening with no change.
- On a swap, both rows update and a one-line notice under the list names the action that moved.
- Back (the Back button, Escape, or gamepad East) returns to wherever the screen was opened from, and selects the button that opened it.

### Main menu

- An Options button between Join Game and Quit. It hides the main buttons and shows the options screen; Back shows them again. It is interactable while a join is connecting, but not during a transition.

### In-game menu

- Escape (or gamepad Start) in game opens it; Escape, gamepad East or Resume closes it. Options opens the options screen on top, and Back returns to the in-game menu.
- Opening it blocks gameplay input through `GameplayInputGate`, so the `Player` map is disabled for as long as it is open, which also lets rebinding run (the Input System only rebinds disabled actions). Pointer feedback (hover ring, attack cursor) goes away because gameplay input reads as neutral.
- In single player (`NetworkSession.IsActive` false) it pauses the game while it is open, the options screen included, and resumes when it closes, whichever way it closes. Opening it from a host session that has no clients yet does not pause, because a client may join at any moment.
- Online it pauses nothing. The local player can be hit, die and respawn behind it.
- Leave game returns to the main menu through `GameFlow.ShowMainMenuAsync()`; `NetworkSessionLifecycle` already shuts the session down when a transition ends in the menu. For the host, a confirmation first says that leaving ends the game for everyone; clients then go back to their menu through the existing lost-connection path. A client leaving affects only itself.
- Quit closes the application (same as the main menu's Quit), with the same confirmation for the host.
- It stays open across area changes (the fader draws over it) and closes with the Gameplay scene when a session ends or restarts. It does not open during a transition.

### Saving

- `settings.json` holds a format version and the binding overrides as written by `InputActionAsset.SaveBindingOverridesAsJson()`, which keys each override by action and binding id. Renaming an action keeps its override; an override for a binding that no longer exists is ignored.
- It is loaded once at boot, before any menu shows, and written after every change, through the same atomic `FileSaveStore` as the save.
- A missing file means defaults. A malformed file, or one with an older format version, is ignored with a warning and replaced on the next change. A file written by a newer build is ignored with a warning and never overwritten: changes made in this session apply but are not saved.
- After loading, if two rebindable actions end up on the same input (possible only when a later build changes a default), the overrides involved are dropped with a warning and the file is rewritten.
- Multiplayer Play Mode virtual players share `persistentDataPath`, so they share `settings.json`: each reads it at boot and the last change wins on disk, while each keeps its own bindings in memory for the session.

### Labels

- The ability bar shows each slot's current short label and updates when a binding changes or the keyboard layout changes.
- Editor gizmos label abilities by slot number (1–6) instead of a key, so `AbilitySlots.KeyName` goes away.

## Architecture

Binding logic is input plumbing, so it lives in Core next to `PlayerInputService`, as plain C# with the swap and validation rules testable in EditMode. The screens are UI, so they live in Gameplay, and since the Main scope (Core) cannot hold Gameplay components, the options screen is one prefab placed under both the MainMenu and the Gameplay canvases.

### Settings (Core)

- `UserSettingsData`: serialized form (`JsonUtility`): `version` (`CurrentVersion` 1) and `inputBindingOverrides` (the Input System's JSON as a string). `TryParse` returns false on malformed JSON.
- `UserSettings`: reads the data once through its own `ISaveStore` and writes it on every change. `InputBindingOverrides`, and `SetInputBindingOverrides`, which writes the file. Later options add fields here.
- Main scope: a second `ISaveStore` (a `FileSaveStore` at `Application.persistentDataPath/settings.json`) registered under the `UserSettings.FileName` key, which `UserSettings` asks for, so the unkeyed registration for `save.json` is untouched and tests can swap either store.

### Bindings (Core, `Assets/Scripts/Core/Input`)

- `RuntimeInputActions`: Main-scope singleton holding this session's copy of the project input asset (`Instantiate` keeps action ids). `PlayerInputService`, `InputBindings` and the `EventSystem`'s UI module all use the copy, so overrides never touch the project asset (CLAUDE.md treats ScriptableObject assets as immutable at runtime), and `InputActionReference`s in assets resolve to the copy by id.
- `InputBindings`: Main-scope singleton and entry point over the session's copy.
  - On initialize: loads the overrides from `UserSettings`, applies them, and resolves duplicates.
  - `IsRebindable(action)`, `GetShortLabel(action)`, `GetLabel(action)` (for the options screen: the layout's full key name).
  - `TryAssign(action, controlPath, out InputAction swappedWith)`: the rule in one place. Refuses non-rebindable actions and disallowed paths, swaps on conflict, applies the override (or removes it when the path is the default), saves and raises `BindingsChanged`.
  - `StartRebind(action)`: wraps `PerformInteractiveRebinding` with the allowed paths (`WithControlsHavingToMatchPath` for `<Keyboard>` and the mouse buttons), Escape as the cancel control, the `Keyboard&Mouse` binding group, and an `OnApplyBinding` that routes the result through `TryAssign`. Returns a handle that reports completion or cancellation and disposes the operation. Logs an error and does nothing if the action is enabled.
  - `Reset(action, out swappedWith)` assigns the default through `TryAssign`, so a default another action took is swapped back; `ResetAll()` removes every override. Both save and raise `BindingsChanged`.
  - Raises `BindingsChanged` also when a keyboard's layout changes (`InputSystem.onDeviceChange`, `ConfigurationChanged`), so labels follow it.
  - On dispose: cancels a running rebind. The copy is destroyed with the Main scope.
- `GameplayInputGate` gains `Block(object owner)` and `Unblock(object owner)`. The `Player` map is enabled when in game, not transitioning, and nothing holds a block.
- `IMenuInput` (implemented by `PlayerInputService`): `BackPressedThisFrame` from the `UI` map's `Cancel` and `MenuPressedThisFrame` from the `Menu` action. The menu key is not a player command, so it stays off `IPlayerInput`. The `Menu` action lives in its own `Menus` map, enabled whenever the game is in a session, because the `Player` map is disabled while the in-game menu is open.
- `MenuStack` (Main scope) is the only reader of `IMenuInput`: open menus push themselves as `IMenuLayer`s, and each tick at most one press is handled, by the top layer (Back or the Menu key) or, with nothing open, as a request to open the in-game menu. Escape is both keys and still counts once, so no menu keeps its own frame bookkeeping; a rebind consumes its own Escape.
- `RebindableActionList` ScriptableObject (`Assets/Data/Input/RebindableActions.asset`): ordered entries of an `InputActionReference`, a name and a section. It is assigned on `MainLifetimeScope` and given to `InputBindings`, which exposes each action's name and section, so the options screen builds its rows from `InputBindings` alone.

### UI (Gameplay, `Assets/Scripts/UI`)

| Type | Role |
|---|---|
| `OptionsMenuView` | Passive view on the `OptionsMenu` prefab: rows container, footer buttons, listening panel, notice line, reset confirmation. |
| `KeyBindingRowView` | One row: label, binding button, reset button. Instantiated from a row prefab when the screen is built (once, not per open). |
| `OptionsMenuController` | Plain entry point next to the view. Builds rows from `InputBindings`, starts and cancels rebinds through it, refreshes rows on `BindingsChanged`, shows swap notices, handles Back and Reset all. `Open(onClosed)`, `Close()`. |
| `GameMenuController` | In-game menu on a `GameMenu` prefab under the Gameplay HUD: opens on `MenuPressedThisFrame`, closes on Back; holds a `GameplayInputGate` block while open, and a `TimeFreeze` hold too when no network session is active; releases both when it closes or is destroyed; Resume, Options, Leave game, Quit with the host confirmation. Ignores input during transitions and while the options screen is listening. |
| `MainMenuController` | Gains the Options button handler. |
| `AbilityBarPresenter` | Sets slot labels from `IInputBindingLabels` (implemented by `InputBindings`) at start and on `BindingsChanged`, instead of `AbilitySlots.KeyName`. |

Scopes: `MenuLifetimeScope` and `GameplayLifetimeScope` each register their scene's `OptionsMenuView` (component in hierarchy) and an `OptionsMenuController` entry point; `GameplayLifetimeScope` also registers `GameMenuController`.

### Pause (Core, Main scope)

- `TimeFreeze`: Main-scope singleton that owns `Time.timeScale`. `Hold(object owner)` and `Release(object owner)`; the scale is 0 while anything holds it and 1 otherwise, and 1 again on dispose. Releasing an owner that holds nothing does nothing, so a menu can release unconditionally.
- `TransitionTimeFreeze` becomes a holder of `TimeFreeze` from `TransitionStarted` to `TransitionFinished` instead of setting the time scale itself. Leaving the game while paused therefore stays frozen through the transition and runs again in the menu, because the menu's hold goes with the Gameplay scene.
- Everything that should keep running while paused already uses unscaled time (`ScreenFader`) or does not depend on time (uGUI, the Input System in dynamic update, rebinding). New menu animations must use unscaled time.

### Networking

Nothing is replicated. Bindings only change which device control drives the local `IPlayerInput`, and `PlayerCommand` was already device-independent. A remote player's copy never reads input, so another player's bindings cannot matter. The in-game menu blocks only the local player's input, and pauses only when there is no session, so it never stops a host's simulation or a client's view of it.

## Removals and migrations

- The unused template actions in the `Player` map (Look, Interact, Crouch, Jump, Previous, Next, Sprint) and their bindings.
- `AbilitySlots.KeyName` and its uses in the ability bar and the gizmos.
- `PlayerInputServiceTests`: the "no other Player action on those keys" check becomes "the `Player` map holds only `MoveClick`, `Point`, `Stop` and `Ability1`…`Ability6`, and the `Menus` map only `Menu`".
- [Multiplayer.md](Multiplayer.md#rules-for-new-code-starting-now): the persistence rule covers game state; per-machine preferences go through `UserSettings` on every machine.
- Architecture.md: the Input, UI, Save data, scopes, configs, events and tests sections change with each phase.

## Phases

### Phase 1: settings file and binding service

- [x] `UserSettingsData`, `UserSettings` and `settings.json` in the Main scope.
- [x] `InputBindings` (load, assign with swap, reset, labels, `BindingsChanged`, dispose) and `RebindableActionList` with its asset.
- [x] Unused template actions removed; `Menus` map with the `Menu` action (Escape, gamepad Start).
- [x] Ability bar labels from the bindings; gizmos by slot number; `AbilitySlots.KeyName` removed.

### Phase 2: options screen from the main menu

- [x] `OptionsMenu` and row prefabs, `OptionsMenuController`, `StartRebind`, `IMenuInput.BackPressedThisFrame`, listening panel, swap notice, reset and reset all.
- [x] Options button on the main menu; mouse, keyboard and gamepad navigation.

### Phase 3: in-game menu

- [x] `IMenuInput.MenuPressedThisFrame` and enabling the `Menus` map, `GameplayInputGate` blocks, `TimeFreeze` with `TransitionTimeFreeze` moved onto it.
- [x] `GameMenu` prefab and `GameMenuController`: Resume, Options, Leave game, Quit, host confirmations, pause in single player.
- [ ] Checked in Multiplayer Play Mode: a client and the host each rebind and play; the menu pauses no one, the host included when it is alone; a client leaving and the host leaving.

### Review follow-ups

- [x] A settings file from a newer build is never overwritten; a shared default input is reported once; HUD labels by slot; one `UISelection`.
- [x] `HoldSet` shared by `TimeFreeze` and `GameplayInputGate`; `ISessionStatus` exposes `IsActive`.
- [x] `MenuStack` routes Back and the Menu key once per frame instead of per-menu frame counters.
- [x] `RuntimeInputActions`: rebinding works on a session copy of the input asset.

### Later

- A second binding per action, and modifier combinations.
- A gamepad gameplay scheme with its own bindings.
- Audio and video options, and cast modes (see [PlayerControls.md](PlayerControls.md#later)), as more fields in `settings.json` and more sections on the options screen.

## Testing

- EditMode, `UserSettings` with a fake `ISaveStore`: missing file gives defaults, round trip, malformed and other-version files ignored with a warning, writes on change.
- EditMode, `InputBindings` on a copy of the project asset: load applies overrides; `TryAssign` applies, swaps on conflict (both actions reported), refuses Escape, the pointer, scroll and gamepad paths and non-rebindable actions, and removes the override when assigning the default; reset one and all; duplicates after load dropped with a warning; `BindingsChanged` raised once per change; short labels for keys and mouse buttons; dispose removes every override; rebinding an enabled action logs an error.
- EditMode, interactive rebinding through `InputTestFixture` (`Unity.InputSystem.TestFramework` is referenced by the test assembly; the package already builds it, so no `testables` entry is needed): a key press binds, Escape cancels, a mouse click binds, the pointer moving does not.
- EditMode, content: `RebindableActions.asset` matches the map; the project asset's default bindings have no duplicates among rebindable actions; the `Player` and `Menus` maps hold only the expected actions.
- EditMode, pause: `TimeFreeze` is 0 while any owner holds it and 1 after the last release or on dispose; a transition during a pause keeps it at 0 until both end; `GameMenuController` holds it only with no active session, releases it on close and on destroy.
- EditMode, gate and menus: the `Player` map stays disabled while a block is held and comes back when the last one is released; `GameMenuController` on a `MenuStack` with a fake `IMenuInput` opens, closes, never handles two levels in one frame, and ignores input during transitions; `AbilityBarPresenter` labels follow `BindingsChanged`.
- Manual: rebind every action from the main menu and in game, restart and check they stuck; AZERTY labels; in single player the menu freezes mobs, cooldowns and statuses and Leave game from a pause reaches the menu; Multiplayer Play Mode with 2 and 4 players as in Phase 3.

## Open questions

- Whether "Leave game" should keep the session alive for clients when the host leaves (host migration) is out of scope; today the session ends for everyone.
