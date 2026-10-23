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
| Timing | Each frame's own duration in Aseprite is its natural duration, in whole milliseconds as the format stores it. The runtime stretches the phases as it does for baked clips. |
| Markers | The contact or release frame is marked with cel user data `contact` on any cel of that frame, in every direction (the importer's own `event:Name` syntax stays free for other uses). A gait's natural speed is set on the import recipe, not in the file. |
| Pivot | The feet stand on one pixel of the canvas, the same in every frame of the file. The recipe names it for each clip, from the canvas's bottom left as the baker's pivots are ((32, 12) on a 64 px canvas, (40, 22) on an 80 px one), and every slice of the clip's sheet pivots on it. A canvas resized so the feet move needs the recipe changed with it (Open questions). |
| Sheets | Generate copies each clip's frames out of its file into a sheet of its own, `<Character>_<Clip>.png` in the baker's layout (a row per direction, east on top, a column per frame), sliced through `CharacterSheetWriter` into `<Character>_<Clip>_<Direction>_<Frame>` with the pivot on the feet. The set points at those slices, never at the importer's sprites. Decided in Phase 0: the importer ties a sprite to its place among the file's non-empty frames and never renames it, so adding, removing or emptying a frame silently moves references onto other drawings or loses them for good, and its pivot is a share of the canvas; slices named by direction and frame keep their IDs through any edit, and the rogue's frames copied this way give sheets byte for byte the baked ones. |
| Import settings | Enforced for every file under `Assets/Art/Characters/` by an asset postprocessor, so the importer's own output stays light: Animated Sprite mode, Merge Frame layer import, hidden layers left out, 32 px per unit, point filtering, no compression, no model prefab, no animation clips (Mecanim is not used). Its texture and sprites only preview the file in the Project window; nothing references them, so no pivot is set on it. |
| Recipe | An editor-only `AsepriteCharacterRecipe` per character, beside the baker's recipes: the character's name, the prefab drawn with the set (whose weapons, abilities or mob attack name the clips it needs), and its clips (file, gait, whether drawn with a weapon, natural speed for a gait, feet pixel), the clip's name taken from its file's. Its sheets and set are written under `Assets/Sprites/Characters/<Character>/` (the set `<Character>_AnimationSet.asset`), as the baker's. Generate reads the files with `AsepriteReader` and writes the sheets and the set; the same files always give the same sheets and set. |
| Rules | CLAUDE.md's art rule becomes: art is generated in the project or drawn by hand in Aseprite for it; nothing third-party. Animation.md's rule that every frame comes from the baker becomes: every frame comes from the baker or from an Aseprite file imported through its recipe. Changed in Phase 1. |

## How it works

1. An artist saves `Bandit_Run.aseprite`. Unity's Aseprite importer reimports it with the enforced settings; its output only previews the file.
2. The recipe's Generate (and, from Phase 2, a postprocessor after any of its files reimports) reads each file with `AsepriteReader.ReadFile`: its tags, each frame's duration, the cels of its visible layers and their user data. The importer's own parsed file (`AsepriteImporter.asepriteFile`) is only set while its import runs.
3. For each clip it takes the frames direction by direction in `CharacterDirections` order and frame by frame, draws each frame's visible layers onto a canvas (normal blending at the layer's and the cel's opacity, a linked cel from the frame it links to), takes the first direction's durations, finds the frame marked `contact`, and checks the rest (Validation below). A clip that fails is reported and left out of the set; the others are written.
4. It writes each clip's sheet through `CharacterSheetWriter` as the baker does (slices named by direction and frame with their pivot on the feet, existing slices keeping their IDs, a sheet whose bytes did not change neither rewritten nor reimported), then the `CharacterAnimationSet` through the same path as the baker, keeping the asset's GUID, and writes nothing when the set is unchanged.
5. The character's prefab points its `CharacterVisuals` at that set and at its `CharacterLocomotionSettings`, as the rogue's and the Weasel's do.

Validation, per clip:

- all 8 direction tags present, none overlapping another, none in another direction than forward;
- every tag the same number of frames, and every frame the same duration in every direction;
- a gait has no marker; an action (an attack or a cast) has exactly one marked frame, the same in every direction;
- no frame empty, and every frame's opaque pixels within its canvas;
- every drawn layer blending normally, the one blend mode Generate draws, and every group around one blending normally at full opacity; no tilemap layer showing (hidden layers, layers in a hidden group and reference layers are left out);
- the clip's feet pixel inside its canvas;
- the clip names the character needs are present: its weapons' attack clips and its abilities' cast clips, for the player; a mob config's attack clip, for a mob (`CharacterPrefabTests` checks the same for the baked characters).

## Phases

Each phase ships on its own, keeps the game playable offline and hosted, and comes with tests.

### Phase 0: spike

- [x] A test helper that writes small `.aseprite` files from the published file format (frames with durations, layers, compressed cels, tags, cel user data), so tests and the spike need no hand-drawn art. It writes the rogue's baked `Run` and `SwordSlash` as two files, in the conventions above.
- [x] Imported with the enforced settings: the sprites' sizes, pivots and pixels match the baked ones; frame durations, tags and the `contact` user data read back through `AsepriteImporter.asepriteFile`.
- [x] Sprite references survive a reimport after a frame is added, a tag is renamed, a layer is added and the canvas is resized, or the spike finds what breaks them. This decides whether the set can point at the importer's sprites or must copy them into sheets of its own through `CharacterSheetWriter`.
- [x] Import time and memory for a whole character (19 files of 8 directions), measured and recorded in the notes.

Phase 0 notes (the spike's code was not kept, only the helper, [AsepriteFileWriter](../Assets/Tests/Editor/AsepriteFileWriter.cs), and its tests; the files were imported into a scratch folder under `Assets/Dev`, since removed; editor times are debug code with the editor in the background):

- **The helper** writes the format as published: the 128-byte header (32 bits per pixel, the layers' opacity marked valid, square pixels, a 16 x 16 grid), then each frame's header and chunks: the layers and the tags in the first frame, and each frame's cels in layer order, each followed by its user data. Its cels are compressed with zlib, as Aseprite saves them; the plan said raw cels, which the importer also reads but Aseprite only writes in very old files. `FromSheet` writes a baked sheet in the conventions above, and the importer's `AsepriteReader` reads every value and pixel back as written (`AsepriteFileWriterTests`).
- **What the importer needs: nothing the plan doesn't write.** No palette (it looks one up only for indexed colour), no colour profile, no sprite or tag user data, and it logged nothing on import. Points to keep in mind:
  - A frame's duration is a whole number of milliseconds (a 16-bit field), so baked durations round: the walk's 83.3 ms to 83, the run's 76.9 ms to 77, the sprint's 66.7 ms to 67. The walk's cycle comes out 0.4% short and the sprint's 0.5% long, under a pixel of foot slide a stride. The header's speed is ignored.
  - A layer's opacity is applied whether or not the header marks it valid, so an opaque layer must hold 255, as Aseprite always writes.
  - Strings are read a byte to a character, so a name past ASCII comes out garbled; the direction tags and `contact` are ASCII.
  - Cel user data only becomes an animation event when it starts with `event:`, so `contact` stays inert.
  - Imports run in the editor's own process (the project imports in process). `asepriteFile` is only set while an import runs: read in `OnPostAsepriteImport` it held every tag, duration and marker as written, and afterwards `AssetImporter.GetAtPath(path)` gives an importer whose `asepriteFile` is null. Generate reads the files itself with `AsepriteReader.ReadFile`, 122 ms for all 19 of the rogue's.
  - The importer's Background import preference is off in 4.0.1, though its manual says on: a file saved while the editor is in the background imports when the editor next gets focus.
- **Imported with the plan's settings** (set on each importer after a first import with its defaults, which also make a prefab, an animator controller and a clip per tag): Run and SwordSlash, then all 19 clips, gave one sprite per frame at 32 px per unit, point filtered, RGBA32 without mipmaps. Every sprite's pivot and pixels match the baked frame's: placed on its pivot, each shows the baked frame pixel for pixel. No baked pixel is semi-transparent, so the importer's normal blend, which can lose a step on such a pixel, never came into play. The sizes do not match: the importer trims each sprite to its drawn pixels (Run's are 25 to 28 by 39 to 43 px, not 64 x 64), moves its pivot with it and packs a file's sprites into one texture (256 x 512 for Run, against the baked 512 x 512). Durations, tags and markers came back through `asepriteFile` as written.
- **References across reimports.** Run's 64 sprites were held by direction and frame in a `CharacterAnimationSet` asset, then the file was rewritten with one change, reimported, and put back:

  | Change | References | What they show |
  |---|---|---|
  | Tag E renamed East | all 64, same file IDs | the same frames |
  | Hidden layer added | all 64 | the same frames |
  | Visible layer added, drawn on every frame | all 64 | the same frames with the layer |
  | Frame added at the end of E | all 64, same file IDs and names | the 56 after it show the frame before theirs; the new frame is a 65th sprite |
  | Canvas grown from 64 to 80 px around the drawing | all 64 | every frame 5 px too high: the pivot is a share of the canvas (0.1875 of 80 is 15 px, the feet are at 20); right again once the pivot is set for the new canvas |
  | Last frame removed | 63, the last one missing | still missing after the frame is put back, which comes back as a new sprite |
  | Frame 3 of E emptied | 63, the last one missing | references 3 to 62 show the frame after theirs |

  The importer keeps a sprite's ID, and so its file ID, by its place among the file's non-empty frames rather than by frame, and names it once, when the ID first appears: with frame 3 empty, the sprite still named `Frame_3` showed frame 4. A set pointing at the importer's sprites would have to be rebuilt after every import from the importer's private cell list, since the names cannot be trusted; anything else holding one of its sprites (a prefab's renderer) would silently show another frame, a removed frame would break references for good, and the postprocessor would have to read each file's canvas size to set the pivot.
- **Decision: copy into sheets** (Decisions: Sheets). Copied straight out of the files (one layer, so nothing to blend), the rogue's 19 clips, the two casts over the run included, gave PNG sheets byte for byte the baked ones. Reading, drawing and encoding all 19 took 0.6 to 0.9 s; writing them with their import and slicing took 9.1 s the first time and 0.66 s when nothing had changed. Slices named by direction and frame keep their IDs through any edit, as the baker's do. The import settings stay enforced to keep the importer's unused output light; its pivot settings no longer matter.
- **Time for a whole character**: the rogue's 19 clips are 2048 frames in 1.17 MB of `.aseprite` files, against 1.45 MB of baked PNGs. Writing the files took 0.48 s (baking the clips in memory 7.9 s).

  | Import | 17 clips of 1 to 9 frames a direction, each | 2 casts over the run, 512 frames each | All 19 |
  |---|---|---|---|
  | First, with the importer's defaults (prefab, controller, a clip per tag) | 72 to 115 ms (Run and SwordSlash, imported before, 47 and 52 ms) | 570 and 696 ms | 2.8 s |
  | Again on applying the plan's settings | 53 to 126 ms | 366 and 617 ms | 2.7 s |
  | Forced, with the plan's settings | 41 to 65 ms (4 to 26 ms of it inside the importer) | 297 and 518 ms | 1.7 s one by one, 1.4 s as one folder |

  A hand-drawn character has no casts over the run: its 17 files reimport in 0.9 s, and saving one costs about 50 ms of import before Generate.
- **Memory for a whole character.** Importing all 19 files and unloading them left the editor's private memory where it was (7,863 MB before, 7,862 MB after). Loading every sprite and texture raised it by 26 MB: the importer's 19 packed textures hold 6.6 million pixels, 25 MiB of RGBA32 (the profiler reports twice that in the editor, a CPU and a GPU copy). The baked sheets keep every frame whole and hold 11.5 million pixels, 44 MiB, for the same frames: 23 MiB against the importer's 13 MiB without the casts over the run. Copying into sheets gives up that saving (Open questions).

### Phase 1: the import recipe

- [x] `AsepriteCharacterRecipe` with its inspector (clip list with each clip's feet pixel, Generate, a preview playing a clip in each direction) and the postprocessor enforcing the import settings.
- [x] Generate reads the files with `AsepriteReader`, draws each frame's visible layers, and writes each clip's sheet and the `CharacterAnimationSet` through `CharacterSheetWriter` (its sheet and set writing shared by the baker and the recipe), with every validation above, each problem naming file, tag and frame; an unchanged sheet or set is not rewritten.
- [x] Round trip: the rogue's baked clips written as `.aseprite` files by the Phase 0 helper give sheets byte for byte the baked ones and a set equal to the baked one, clip by clip (names, gaits, frame counts, durations to the millisecond, contact frames, natural speeds, the rect and pivot of every sprite), the casts over the run excepted.
- [x] Regenerating after a frame is added, a tag renamed, a layer added and the canvas resized keeps the sprite ID of every slice still drawn.
- [x] CLAUDE.md's art rule and Animation.md's Rules updated; Architecture.md's Editor tools and Character animation sections describe the recipe.

Phase 1 notes:

- **What it is.** `AsepriteCharacterRecipe` (the name, the prefab, the clips), `AsepriteClipReader` (one file read, drawn into a sheet in the baker's layout and checked), `AsepriteCharacterWriter` (Generate, returning the problems and the paths it wrote), `CharacterClipNeeds` (the clips a prefab plays), `AsepriteImportSettings` (the postprocessor) and the recipe's inspector, all under `Assets/Editor/Characters` (Architecture.md's Editor tools). `CharacterSheetWriter` now shares `WriteSheet`, `SheetSprites` and `WriteSet` with it; regenerating the rogue and the Weasel through them changed no file. No recipe asset exists yet: the first hand-drawn character, and with it `Assets/Art/Characters`, comes in Phase 2.
- **A clip's name is its file's**: `<character>_<clip>.aseprite` under `Assets/Art/Characters`, checked by `CanGenerate`, so a clip cannot be named apart from its file. Clips also say whether they are drawn with a weapon, which the set's locomotion lookup reads (an armed gait is never picked) and the baked set has.
- **Messages count frames from 1**, as Aseprite shows them, both within the tag and in the file (`Bandit_Run.aseprite, tag SW, frame 2 (Aseprite frame 12): 60 ms, but 100 ms in tag E.`); the slices count from 0 as the baker's (`Bandit_Run_SW_1`). Every problem of a file is listed at once, but for a missing or doubled direction tag or directions of different lengths, after which the rest cannot be told.
- **Drawing.** Layers draw in their order with Aseprite's own normal blend in its integer arithmetic (`rgba_blender_normal`, pixman's `MUL_UN8` for the cel's opacity times the layer's), checked against values worked out by hand. Hidden layers, layers in a hidden group and reference layers are left out; a linked cel draws, and carries the user data of, the cel it links to (Aseprite writes that user data only after the cel linked to). A cel's z-index is not read, since the importer's parser keeps it internal: a cel moved in front with a z-index draws in its layer's place. A group must blend normally at full opacity, since Aseprite draws any other group on its own first.
- **A file that is not an Aseprite file** is reported from its header (its magic number), before the importer's parser would log an error.
- **The enforced settings land in the file's `.meta`** on its first import (the importer saves its own settings), so its inspector shows them; a file elsewhere keeps the importer's defaults.
- **Writing a sheet that lost frames** made Unity warn that the old slices lay past the new texture's edge, since the shared writer imported the new pixels before trimming the slices (the baker would have met it on a motion losing frames). A sheet already imported now gets its slices first and imports once.
- **Round trip.** The rogue's 17 clips (not the two casts over the run) written as files from the committed sheets and generated give PNGs byte for byte the baked ones and a set equal clip by clip, the durations to the millisecond; the natural speeds come from the recipe, set to the baked ones. Generating them takes about 9 s the first time (mostly importing the 17 sheets) and writes nothing the second.
- **Edits.** Regenerating the run after a frame is added in every direction, the directions are moved within the file, a layer is added and the canvas is grown 8 px on every side keeps every slice still drawn under the same sprite ID, and the set points at them; moving the directions within the file gives the baked sheet again, since the sheet is laid out by tag name. A tag merely renamed fails validation (its direction goes missing), so the clip is left out and its sheet stays as it was.
- **Tests**: `AsepriteCharacterTests`, 23 tests in about 14 s, most of it the round trip.

### Phase 2: a hand-drawn character in play

- [ ] A character drawn by hand plays in game from its imported set: a player prefab variant (or a mob) with its visuals, locomotion settings and the clips its weapons and abilities need, missing ones listed by Generate.
- [ ] Saving a `.aseprite` file regenerates the sheets and sets that use it, once per save, without touching other sets.
- [ ] PlayMode: the hand-drawn character walks and runs at its speeds, its contact frame shows on the update its hit lands, its casts release on completion, and its hit-stop and flash work as the rogue's.
- [ ] Checked in Multiplayer Play Mode with a host and a client, one baked and one hand-drawn: each sees the other's gaits, attacks and casts.

### Phase 3: effects and projectiles

- [ ] Impacts (one variant per damage type, 8 directions), the death puff and the arrow (32 directions) from Aseprite files, through recipes building the same `EffectSprites` and `DirectionalSprites` the effect and weapon sprite writers build.

### Later

- Fewer directions, if hand-drawing 8 proves too costly (Open questions).
- Watching for a file that changed outside Unity while the editor was closed (Unity reimports it on startup; the postprocessor covers that once Phase 2 lands).

## Testing

- EditMode: the `.aseprite` writer helper reads back through `AsepriteReader` as written (Phase 0); each validation rule fails on a file built to break it, with its message; the round trip against the baked rogue, sheet bytes included; regenerating an unchanged character writes nothing; slice IDs survive a frame added, a tag renamed, a layer added and the canvas resized; the enforced import settings are applied to a file dropped under `Assets/Art/Characters/`.
- PlayMode: a character built from an imported set (from the helper's files, so no hand-drawn art is needed in tests) runs the animator tests that matter for the contract: gaits by speed, the contact frame at contact, the release at completion, the hold.
- Manual: a real hand-drawn character in play and in Multiplayer Play Mode (Phase 2).

## Open questions

- How many directions to draw by hand. Eight keeps the runtime unchanged; four or five with mirroring halves the drawing but needs a direction count and mirror map on each clip, `flipX` on the visuals, and a decision on right-handedness (a mirrored character holds its weapon in the other hand).
- Whether a hand-drawn character should ever cast while running with its legs moving. That needs either the casts drawn over the run by hand or split upper and lower body layers, both large.
- Whether the feet should be marked in the file (one pixel on a hidden layer named `Feet`, which Aseprite moves with the drawing when the canvas is resized) rather than named on the recipe, where a resize that moves the feet leaves the character floating or sunk until the recipe follows (a canvas grown by 8 px on every side moves the feet 8 px right and 8 px up).
- Whether character sheets should be packed tighter: the importer's trimmed, packed textures held the rogue's frames in 57% of the pixels of the whole-frame sheets (25 against 44 MiB). A Sprite Atlas packing the slices by their tight meshes would do it for baked and hand-drawn characters alike.
- Whether a gait's natural speed could be measured from the file (a marked planted foot moving across frames) instead of set on the recipe.
