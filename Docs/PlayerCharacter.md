# The player character

Plan for reworking the player's character: a base character the player makes their own, in place of the hooded rogue. It covers the character's look, stylized gaits, then its attacks and casts, and three kinds of customization. [Animation.md](Animation.md) describes the rig, the baker and the runtime. This plan adds a body plan and the customization on top of them, and changes how the motions look. [Architecture.md](Architecture.md) describes what exists today. Tick off the phase checklist as steps land, and move each settled behaviour into Architecture.md as its code arrives.

Every rule in [Multiplayer.md](Multiplayer.md#rules-for-new-code-starting-now) applies. Each player chooses its look on its own machine, and every other machine shows it, a late joiner's included, as a look's catalog index is sent today.

## Where we start

- The player was the rogue (`RogueRecipe`, `RoguePainter`) until Phase 1. It is hooded, masked and caped in a dark palette, 46 px tall, about 3.3 heads counting the hood. The Bandit, drawn in Aseprite, is the second look of the `CharacterLookCatalog`.
- A look is a whole baked `CharacterAnimationSet`. Nothing combines parts or recolours a look at runtime.
- The gaits are one parametric stride (`GaitMotion`), drawn by re-rendering the 3D rig every frame.
- The player carried a lantern (Lighting.md's Phase 9) until this plan's first step removed it.

## Goals

- **A base character, not a rogue.** The player decides who they are. The face, hair and outfit are theirs to choose.
- **One shared body.** No build, height or body type options.
- **Three kinds of customization, planned together from the start.**
  - Colours: one bake, recoloured.
  - Swappable parts: hair, headwear, top, cape, legs and boots.
  - Gear that shows: equipment drawn over the body, and weapons carried outside attacks.

  Looks are edited in the editor for now.
- **Stylized gaits.** Smooth gaits that feel drawn, not "a robot trying to walk like a human".
- **The player only.** The Weasel and the Bandit stay as they are.
- **Every machine shows each player's look,** a late joiner's included.

## Non-goals (for now)

- An in-game character creator.
- Builds, heights or body types.
- Mobs or other characters on the new body plan.
- A light the player carries.

## Decisions

| Topic | Decision |
|---|---|
| Body plan | The adventurer: `AdventurerRecipe` and `AdventurerPainter`, beside the rogue's and the Weasel's. It draws a bare head of hair with its locks, ears and a face laid out by angle, a tunic with a skirt below a buckled belt, sleeves, hands, trousers and boots. |
| Proportions | 40 px tall, about 2.5 heads. In rig units: thigh 5, shin 5, torso 8.5, neck to head 5.6, upper arm 4.4 and forearm 4.0; the head's radius stays the rogue's 6.6. Picked as B from mockups drawn in Aseprite (A at 33 px, B at 40, C at 43), then confirmed on the baker. |
| Rendering | The baker as it is, re-rendering the rig every frame. Two other approaches were tried and rejected: a 4-frame pose-to-pose walk drawn in Aseprite ("looks worse than before"), and the upper body snapped to whole pixels so rigid parts never redraw ("doesn't look better"). |
| Hair | Locks over a ball of hair. Each `HairLock` is a tapering capsule from a root on the head, placed by angle, to a tip placed in the face's frame, so a hairstyle turns with the head. A hairstyle is a list of locks, which makes it a part slot. The default is tousled: three tufts on top, a fringe swept across the forehead, a tuft behind and two side locks. |
| Face | The eyes are 2 x 2 px. The top pixel nearest the light is a soft blue-grey glint (196, 204, 214). The brows are 3 px, 2 px above the eyes, dark brown (58, 32, 18), their outer end a pixel higher (confident). The fringe sits 4 degrees higher so they show. The mouth is a 2 px line; the head has ears and no blush. A feature turned away from the camera is not drawn, so none lands on the head's rim. |
| Outfit | The default: long beige shirt sleeves, a blue tunic with a short skirt, a brown belt with a brass buckle, brown trousers, mid-height boots and bare hands. The recipe's options are sleeves (long, short over bare forearms, or layered), gloves, a satchel's strap and pouch, the skirt's length and the boots' height. All are kept as settings for the parts and colours to come. |
| Materials | `Skin` and `Hair` are shaded and outlined; the rogue's face materials go unlined, since they sit inside its hood. `Brow`, `Mouth`, `Blush` and `EyeGlint` are flat and unlined. |
| Lantern | Removed from the player before anything else (Lighting.md's Phase 9 note). The baker's and Aseprite's lantern support stays for a character that carries one later. |
| Looks | The adventurer is the first look of the `CharacterLookCatalog`, the default; the Bandit stays second. The rogue is retired from the looks: its recipe, motions and sheets stay as the baker's test bed. |
| Clips and frames | The rogue's 19 clips on the new body, in the rogue's frames (64, 80 and 96 px), which hold the smaller body with room. Every look keeps the same frames and feet. |
| Attacks and casts on a new body | A keyed motion names the body its keys were posed on and fits each pose to the body it is baked on (Animation.md's Motions): the legs' and arms' lengths scale the pelvis, feet and hands. |
| Gaits | The walk stays today's on the new proportions. That was preferred over bouncier, springier and swaggering settings of the same stride, whose differences barely showed at this size. Stylizing it is Phase 2. |

## Phases

Each phase ships on its own, keeps the game playable offline and hosted, and comes with tests.

### Phase 0: the base look

- [x] The lantern removed from the player: its key, light, replicated state and sheets.
- [x] The adventurer's body plan, with the face, hair and outfit settings.
- [x] `Recipe_Adventurer.asset` with the settled look.
- [x] `AdventurerRecipeTests`.

Phase 0 notes:

- **How it was settled.** On previews baked in memory from the recipe with settings overwritten, written to the gitignored `Recordings/RogueRework/` (round 1 the Aseprite mockups, round 2 the gait settings and the snapping, then the face, eyes, brows and mouth, and the outfits). Picked in order: proportions B, today's walk on them, the tousled hair with the bright style's 2 x 2 eyes, the soft glint, the confident brows with a 2 px mouth, and the first outfit as the default.
- **What it is.** `AdventurerRecipe` holds the outfit options (boots' size and height, skirt length, `AdventurerSleeves`, gloves, strap), the head (hair size, face tilt, the locks, ears) and the face (its ellipse, fringe, eyes' place, height, width and glint, brows' gap, width and tilt, mouth and its curve, blush), plus its colour ramps. `AdventurerPainter` draws them. The recipe holds no clips yet; Phase 1 gives it the player's.
- **Tests.** `AdventurerRecipeTests` covers:
  - the same pixels from a new baker;
  - the target height;
  - the face's features from the front, one eye in profile and none from behind;
  - the ears;
  - the locks growing the hair;
  - each outfit option drawing as set.

### Phase 1: the new body in play

- [x] The adventurer's clips: every clip the player plays (the gaits, the three weapons' attacks and every cast), from the rogue's motions on the new body, in frames that hold it.
- [x] The adventurer as the player's default look in the `CharacterLookCatalog`, the rogue retired and the Bandit kept.
- [x] Tests: every look holds every clip the player plays (`CharacterPrefabTests`); the adventurer's sheets are what its recipe bakes.

Phase 1 notes:

- **Clips.** `Recipe_Adventurer` holds the rogue's 19 clips (the gaits, the three attacks, the casts and the two casts over the run) with the same motions, weapons, frames and feet: 64 px at (32, 12), 80 px at (40, 22) and 96 px at (48, 28). No drawn pixel comes within 5 px of its frame's edge (the bow's roll is the nearest; the gaits keep 6). Its sheets and set are in `Assets/Sprites/Characters/Adventurer/`.
- **Fitting the attacks and casts.** They are keyed in the rogue's rig units, so on the adventurer's shorter legs the pelvis stood too high and the feet floated 2 to 3 px above the idle's in every attack. `KeyedMotion` gained `posedOn`, the body its keys were posed on (the rogue's recipe, on all 13 of the rogue's keyed motions; none on the Weasel's dagger slash, keyed on the Weasel), and `FitToBody`: the pelvis's height over the ankles and each foot's place from its hip are scaled by the legs' length, and each hand keeps its place from its shoulder, scaled by the arms' length. A body of the same sizes keeps every pose as it is, so the rogue's sheets did not change by a byte. Now the attacks' first frames put the boots within a pixel of the idle's, and the head's top within a pixel (two for the greatsword, which bends its knees).
- **Speeds.** The shorter legs take shorter strides: the natural speeds are walk 0.71, run 2.92 and sprint 3.88 units per second (the rogue's 1.02, 4.23 and 5.61). At the player's 5 the run plays at 1.71 times its rate (the rogue's 1.18), and chilled at 1.03. `Locomotion_Player` became the adventurer's: run from 1.7 (the rogue's 2.4 scaled by the speeds' ratio), sprint from 5.6 as before, and rates up to 1.8, so the run at the base speed keeps its feet. The Bandit, with the rogue's speeds, has `Locomotion_Bandit`, the old settings. Phase 2 restyles the strides and retunes these.
- **The catalog.** Adventurer, then Bandit: the rogue's entry became the adventurer's, so the Bandit keeps its index. A look's index is its id on the wire, safe since join approval refuses other builds; a local settings file naming the rogue shows the adventurer.
- **The player prefab.** It draws the adventurer's set. Hit impacts show 0.62 above the feet, the middle of the 40 px body (0.7 for the rogue), and damage popups 1.56 above them (1.75), the same gap over the head. The prop inspector draws the adventurer beside props for scale.
- **Checked by eye.** The attacks and casts on the new body were looked at sheet by sheet before and after the fitting (`Recordings/RogueRework/phase1/`).
- **Tests.**
  - `AdventurerRecipeTests` adds: every clip a row per direction and within its frames; the rogue's keyed motions fitting the adventurer (the pelvis and feet in the same share of the legs, the hands as far from the shoulders in arm lengths, nothing changed on a copy of the rogue); the sheets and set what the recipe bakes. The last two checks are shared with `CharacterRecipeTests` through `CharacterSheetAssert`.
  - `CharacterPrefabTests`: the catalog's first look is the adventurer, the prefab's own, and the rogue is in none; the attacks line up with the idle as above.
  - `WeaponCatalogTests`, `OptionsMenuControllerTests`, `NetworkLooksPlayModeTests` and the PlayMode animation tests read the adventurer where they read the rogue.

### Phase 2: stylized gaits

- [ ] Gait experiments on previews, settled with the user: the idle, walk, run and sprint on the new body, with new motion features if the stride's settings are not enough (squash and stretch, the head and hair a beat behind the body).
- [ ] The settled gaits baked, with their natural speeds and speed bands kept free of foot slide.

### Phase 3: attacks and casts

- [ ] The sword, greatsword and bow attacks and the casts, restyled to match the gaits on the new body, their contact and release frames kept on the timeline.

### Phase 4: colours

- [ ] A player's colours chosen per material ramp (skin, hair, tunic, shirt, trousers, boots, belt, eyes and more), shown on every machine, from one bake.

### Phase 5: swappable parts

- [ ] Parts per slot (hair, headwear, top, cape, legs, boots) a player chooses from, shown on every machine, without a bake per combination.

### Phase 6: gear that shows

- [ ] Equipment drawn over the body, and weapons carried outside attacks (Animation.md's Open questions).

## Testing

- EditMode: the recipe's pixels are repeatable and the standing height is right. The face, the locks and the outfit's options draw as set (Phase 0). Every look holds every clip the player plays (Phase 1). Customization: a player's choice resolves to the right colours and parts (Phases 4 to 6).
- PlayMode: a player's look, colours and parts reach the other machines through the in-process host and client, a late joiner's included (Phases 4 to 6).

## Open questions

- How colours are applied at runtime. One way is a sheet of material and tone indices read through a palette in the lit sprite shader. The other is a bake per palette, which costs memory for every combination.
- How parts combine. One way is a layer per slot drawn over the body, as the lantern's layer was, carrying the depth test's answer. The other is a bake per combination of parts. Either way, how a slot's parts are made: from the recipe's settings, or drawn in Aseprite in 8 directions.
- Whether the 8 directions stay a requirement once parts are drawn by hand. Stardew Valley and Necesse, named as references, draw 4.
