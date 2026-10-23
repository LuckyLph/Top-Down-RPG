# Hand-drawn characters from Aseprite

Plan for characters drawn by hand in Aseprite and played by the same runtime as the baked ones. [Animation.md](Animation.md) describes the character rig, the baker and the runtime; this file adds a second way to make a character's clips and changes nothing about how they play. [Architecture.md](Architecture.md) describes what exists today. Tick off the phase checklist as steps land, and move each settled behaviour into Architecture.md as its code arrives.

Every rule in [Multiplayer.md](Multiplayer.md#rules-for-new-code-starting-now) applies. Animation is never replicated, so nothing here goes over the network; every machine needs the same imported assets, as it needs the same baked ones.

## Where we start

- Every character frame comes from the baker ([Animation.md](Animation.md)'s Rules), and CLAUDE.md allows only art generated in the project.
- The baker and the runtime meet at one contract, the [CharacterAnimationSet](../Assets/Scripts/Characters/CharacterAnimationSet.cs): a list of [CharacterAnimationClip](../Assets/Scripts/Characters/CharacterAnimationClip.cs)s, each with
  - a name (what weapons and abilities ask for: `SwordSlash`, `GreatswordCleave`, `BowShot`, `Cast<Ability>`) and a gait (`Idle`, `Walk`, `Run`, `Sprint`, or none for an action);
  - its sprites, 8 directions times its frames (times its gait frames for a cast drawn over a gait), at 32 px per unit with the pivot at the feet;
  - each frame's duration at natural speed;
  - an attack's contact frame or a cast's release frame, and a gait's natural speed.
- `CharacterAnimator`, the attack and cast timelines, the hit-stop, the flash, mobs and networking read only that contract. They do not know where a sprite came from.
- The 2D Aseprite Importer (`com.unity.2d.aseprite` 4.0.1) is installed, and no `.aseprite` file is in the project. The importer turns a file's frames into sprites at a pixels-per-unit and pivot of our choosing, keeps each frame's own duration, reads tags (forward only) and cel user data, and hands an import's parsed file to post-import handlers (`AsepriteImporter.asepriteFile`, `OnPostAsepriteImport`); `AsepriteReader.ReadFile` parses a file on its own. It does not import Aseprite slices.

## Goals

- A character drawn in Aseprite plays exactly as a baked one: gaits by speed, attacks on their timeline with the contact frame at contact, casts with the release on completion, hit-stop, flash, every weapon and ability it has clips for, on every machine.
- Drawing stays in Aseprite: frame timings, the contact and release frames and the direction of each clip are set there, and saving the file updates the game.
- Mistakes are caught on import with a message naming the file, tag and frame: a missing direction, directions of different lengths or timings, a missing contact frame, a clip a weapon or ability needs and the character lacks.
- Baked and hand-drawn characters live side by side; a character is one or the other.

## Non-goals (for now)

- Fewer than 8 directions, or mirroring one side to make the other (see Open questions).
- Weapons or gear as separate sprites layered at runtime. A weapon stays drawn into the clips that use it, as baked characters do.
- Casts drawn over a gait by hand (8 directions of every cast frame over every run frame). A hand-drawn character's casts play standing; the animator already handles a cast without a moving clip.
- Effects and projectiles from Aseprite (impacts, the death puff, the arrow). They come in Phase 3 or later.
- Changing a hand-drawn clip from Unity. The `.aseprite` file is the source.

## Decisions

| Topic | Decision |
|---|---|
| Contract | Unchanged. An imported character produces a `CharacterAnimationSet` like the baker's; no runtime type learns about Aseprite. |
| Files | One `.aseprite` file per clip, under `Assets/Art/Characters/<Character>/`, named `<Character>_<Clip>.aseprite` (`Bandit_Run.aseprite`), so each clip keeps its own canvas size (attacks reach further than gaits, as the baked 64, 80 and 96 px frames do). |
| Directions | Eight tags per file, named by `CharacterDirections`' names: `E`, `NE`, `N`, `NW`, `W`, `SW`, `S`, `SE`, each covering that direction's frames. Every tag holds the same number of frames with the same durations. |
| Timing | Each frame's own duration in Aseprite is its natural duration. The runtime stretches the phases as it does for baked clips. |
| Markers | The contact or release frame is marked with cel user data `contact` on any cel of that frame, in every direction (the importer's own `event:Name` syntax stays free for other uses). A gait's natural speed is set on the import recipe, not in the file. |
| Pivot | The feet stand on one pixel of the canvas, the same in every frame of the file. The importer runs in Animated Sprite mode with Canvas pivot space and a custom pivot at that pixel, so every sprite's pivot is the feet. The recipe names the pixel. |
| Import settings | Enforced for every file under `Assets/Art/Characters/` by an asset postprocessor: Animated Sprite mode, Merge Frame layer import, 32 px per unit, point filtering, no compression, Canvas pivot space with the recipe's feet pixel, no model prefab, no animation clips (Mecanim is not used). Hidden layers are not imported, so guide layers can stay in the file. |
| Recipe | An editor-only `AsepriteCharacterRecipe` per character, beside the baker's recipes: the character's name, its clips (file, gait, natural speed for a gait), its feet pixel, and where its set is written (`Assets/Sprites/Characters/<Character>/<Character>_AnimationSet.asset`, as the baker's). Generate builds the set from the imported sprites; the same files always give the same set. |
| Rules | CLAUDE.md's art rule becomes: art is generated in the project or drawn by hand in Aseprite for it; nothing third-party. Animation.md's rule that every frame comes from the baker becomes: every frame comes from the baker or from an Aseprite file imported through its recipe. Changed in Phase 1. |

## How it works

1. An artist saves `Bandit_Run.aseprite`. Unity's Aseprite importer reimports it with the enforced settings: one sprite per frame, its pivot at the feet pixel, 32 px per unit.
2. The recipe's Generate (and, from Phase 2, a postprocessor after any of its files reimports) reads each file's parsed data: its tags, each frame's duration, the cels' user data.
3. For each clip it orders the sprites direction by direction in `CharacterDirections` order and frame by frame, takes the first direction's durations, finds the frame marked `contact`, and checks the rest (Validation below). A clip that fails is reported and left out of the set; the others are written.
4. It writes the `CharacterAnimationSet` through the same path as the baker, keeping the asset's GUID, and writes nothing when the set is unchanged.
5. The character's prefab points its `CharacterVisuals` at that set and at its `CharacterLocomotionSettings`, as the rogue's and the Weasel's do.

Validation, per clip:

- all 8 direction tags present, none overlapping another, none in another direction than forward;
- every tag the same number of frames, and every frame the same duration in every direction;
- a gait has no marker; an action (an attack or a cast) has exactly one marked frame, the same in every direction;
- no frame empty, and every frame's opaque pixels within its canvas;
- the clip names the character needs are present: its weapons' attack clips and its abilities' cast clips, for the player; a mob config's attack clip, for a mob (`CharacterPrefabTests` checks the same for the baked characters).

## Phases

Each phase ships on its own, keeps the game playable offline and hosted, and comes with tests.

### Phase 0: spike

- [ ] A test helper that writes small `.aseprite` files from the published file format (frames with durations, one layer, raw cels, tags, cel user data), so tests and the spike need no hand-drawn art. It writes the rogue's baked `Run` and `SwordSlash` as two files, in the conventions above.
- [ ] Imported with the enforced settings: the sprites' sizes, pivots and pixels match the baked ones; frame durations, tags and the `contact` user data read back through `AsepriteImporter.asepriteFile`.
- [ ] Sprite references survive a reimport after a frame is added, a tag is renamed, a layer is added and the canvas is resized, or the spike finds what breaks them. This decides whether the set can point at the importer's sprites or must copy them into sheets of its own through `CharacterSheetWriter`.
- [ ] Import time and memory for a whole character (19 files of 8 directions), measured and recorded in the notes.

### Phase 1: the import recipe

- [ ] `AsepriteCharacterRecipe` with its inspector (clip list, feet pixel, Generate, a preview playing a clip in each direction) and the postprocessor enforcing the import settings.
- [ ] Generate writes the `CharacterAnimationSet` with every validation above, each problem naming file, tag and frame; an unchanged set is not rewritten.
- [ ] Round trip: the rogue's baked clips written as `.aseprite` files by the Phase 0 helper and imported give a set equal to the baked one, clip by clip (names, gaits, frame counts, durations, contact frames, natural speeds, the pixels and pivot of every sprite), the casts over the run excepted.
- [ ] CLAUDE.md's art rule and Animation.md's Rules updated; Architecture.md's Editor tools and Character animation sections describe the recipe.

### Phase 2: a hand-drawn character in play

- [ ] A character drawn by hand plays in game from its imported set: a player prefab variant (or a mob) with its visuals, locomotion settings and the clips its weapons and abilities need, missing ones listed by Generate.
- [ ] Saving a `.aseprite` file regenerates the sets that use it, once per save, without touching other sets.
- [ ] PlayMode: the hand-drawn character walks and runs at its speeds, its contact frame shows on the update its hit lands, its casts release on completion, and its hit-stop and flash work as the rogue's.
- [ ] Checked in Multiplayer Play Mode with a host and a client, one baked and one hand-drawn: each sees the other's gaits, attacks and casts.

### Phase 3: effects and projectiles

- [ ] Impacts (one variant per damage type, 8 directions), the death puff and the arrow (32 directions) from Aseprite files, through recipes building the same `EffectSprites` and `DirectionalSprites` the effect and weapon sprite writers build.

### Later

- Fewer directions, if hand-drawing 8 proves too costly (Open questions).
- Watching for a file that changed outside Unity while the editor was closed (Unity reimports it on startup; the postprocessor covers that once Phase 2 lands).

## Testing

- EditMode: the `.aseprite` writer helper reads back through `AsepriteReader` as written; each validation rule fails on a file built to break it, with its message; the round trip against the baked rogue; regenerating an unchanged character writes nothing; sprite references survive the reimports Phase 0 settles on; the enforced import settings are applied to a file dropped under `Assets/Art/Characters/`.
- PlayMode: a character built from an imported set (from the helper's files, so no hand-drawn art is needed in tests) runs the animator tests that matter for the contract: gaits by speed, the contact frame at contact, the release at completion, the hold.
- Manual: a real hand-drawn character in play and in Multiplayer Play Mode (Phase 2).

## Open questions

- How many directions to draw by hand. Eight keeps the runtime unchanged; four or five with mirroring halves the drawing but needs a direction count and mirror map on each clip, `flipX` on the visuals, and a decision on right-handedness (a mirrored character holds its weapon in the other hand).
- Whether a hand-drawn character should ever cast while running with its legs moving. That needs either the casts drawn over the run by hand or split upper and lower body layers, both large.
- Whether the set keeps pointing at the importer's sprites or copies them into sheets of its own (Phase 0).
- Whether a gait's natural speed could be measured from the file (a marked planted foot moving across frames) instead of set on the recipe.
