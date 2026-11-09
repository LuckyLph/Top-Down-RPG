# The player character

Plan for reworking the player's character: a base character the player makes their own, in place of the hooded rogue. It covers the character's look, stylized gaits, idle animations, then its attacks and casts, and three kinds of customization. [Animation.md](Animation.md) describes the rig, the baker and the runtime. This plan adds a body plan and the customization on top of them, and changes how the motions look. [Architecture.md](Architecture.md) describes what exists today. Tick off the phase checklist as steps land, and move each settled behaviour into Architecture.md as its code arrives.

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
| Gaits | The adventurer's own walk, run and sprint, styled on top of the stride (Phase 2): a bob big enough to read at this size with its top and bottom on frames, a bounce in the run and the sprint, the head a beat and the hair's tips two beats behind the bob, a torso that stretches and squashes with it, hands that rise at both ends of their swing like a pendulum, and longer strides, so the steps come slower. The rogue's gaits stay as they were. |
| Idle | A still frame, the standing pose, as before. A breathing loop was turned down in favour of idle animations that play once after the player has stood still for a while, as champions' do in League of Legends: looking around, a stretch, a scratch of the head, a tapped foot and a yawn (Phase 3). |
| Casts | One animation each, restyled like the attacks, with the head and the hair late and the torso stretching and squashing (Phase 4). The weapon casts were reworked so they read as more than an attack: the whirl and the sweep spin a whole turn, the lunge thrusts from a coil, the charge leaps and slams the greatsword down, the volley shoots into the sky, and the roll tumbles unarmed. |
| Attacks | Two attacks a weapon for now, which flow into each other: each ends in the pose the other starts from, so attacking again carries on from where the last attack stopped (Phase 4). After equipping a weapon its first attack plays, then each attack is followed by one of the others at random, never by itself, so two alternate. Three a weapon, picked that way, was the first ask; two came first so they could flow. A weapon can list more. |

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

- [x] Gait experiments on previews, settled with the user: the idle, walk, run and sprint on the new body, with new motion features if the stride's settings are not enough (squash and stretch, the head and hair a beat behind the body).
- [x] The settled gaits baked, with their natural speeds and speed bands kept free of foot slide.

Phase 2 notes:

- **How it was settled.** On previews baked in memory, each variant playing at the rate it would play in the game (`Recordings/RogueRework/phase2/`): the run first, as the player runs at its base speed, then the walk and the sprint with the run's choices, then the idle.
  - Run, round 1: today's run beside a bigger bob with a bounce, the head and hair behind it, squash and stretch, a waddle (the hips over the planted foot and the spine tilting toward it), cartoon arms, and longer strides. Round 2 started from the longer strides and tried them without squash, with more time in the air, longer still, with pendulum arms and with more lean. Chosen: the longer strides with the pendulum arms, a wider swing and less twist.
  - Walk: the run's choices on the walk, beside a bounce against a smooth bob, a bigger bob, a waddle, today's shorter stride and no arm arc. Chosen: the smooth bob.
  - Sprint: the run's choices on the sprint, beside more lean, a longer stride, more time in the air and no arm arc. Chosen: the longer stride.
  - Idle: a breath with the head and hair behind it, in 8 and 4 frames, deeper, and with a sway, beside the old breath. None was kept: the idle stays still, and idle animations that play after a while standing still replace a loop (Phase 3).
- **Why the bob moved onto frames.** With 8 frames a cycle, a bob twice a cycle gets four samples a step, so a change in its shape barely showed until its top and bottom fell on frames and it grew about twice as big.
- **What the stride gained.** `GaitMotion`'s Style settings, all off by default, so the rogue's sheets did not change by a byte:
  - `bounce`: the bob's shape, from a smooth wave to a bounce, round at its top and sharp at its bottom;
  - `bobOffset`: how much later than mid-stance the bob turns, so its top and bottom land on frames;
  - `headFollow` and `headLag`, `hairFollow` and `hairLag`: the head, and the tips of the hair's locks, following the bob late (`CharacterPose.HeadLift`, which `CharacterRig` adds to the head, and `HairLift`, which `AdventurerPainter` adds to each lock's tip by its length);
  - `stretch`: the torso's length grows at the bob's top and shrinks at its bottom (`CharacterPose.Stretch`);
  - `armArc`: the hands rise at both ends of their swing.

  A sway of the hips over the planted foot and a tilt of the spine toward it were tried and dropped with the waddle.
- **The adventurer's gaits.** `Motion_AdventurerWalk`, `Motion_AdventurerRun` and `Motion_AdventurerSprint`, copies of the rogue's with the chosen settings, and `Motion_AdventurerCastSmiteRun` and `Motion_AdventurerCastWhirlRun` over the new run. The rogue's motions and sheets are untouched. In rig units for 14-unit legs and 10.4-unit arms:

  | Gait | Stride (front, back) | Bob | Style | Arms |
  |---|---|---|---|---|
  | Walk | 6.5, -6.5 (was 5.5, -5.5) | 1.2, smooth, a frame earlier | head 1 at 1/8 of the cycle, hair 1.5 at 0.2, stretch 0.1 | swing 5.5, arc 1.5, twist 4 |
  | Run | 8, -10 (was 6.2, -7.8) | 2.4, a bounce, 0.025 later | the same, stretch 0.15 | swing 7, arc 2, twist 6 |
  | Sprint | 9.5, -12 (was 6.5, -8) | 2.6, a bounce, 0.035 later | the same, stretch 0.15 | swing 8.5, arc 2, twist 7 |

  The swing curves rose with the strides. The walk plays at 16 frames a second at its natural speed instead of 12: a gait plays at the speed over its natural speed, so the frames a second it shows at a given speed depend on its stride alone, and the higher rate only raises its natural speed so its band fits.
- **Speeds.** Natural speeds walk 1.11, run 3.76 and sprint 5.76 units per second (0.71, 2.92 and 3.88 before). At the player's 5 the run plays at 1.33 times its rate, where it played at 1.71. `Locomotion_Player`: run from 1.9 (was 1.7), sprint from 5.6, rates 0.45 to 1.9 (were 0.5 to 1.8). With the 8% hysteresis the walk plays up to 2.05 at 1.84 times its rate, the run down to 1.75 at 0.47 and up to 6.05 at 1.61, and the sprint down to 5.15 at 0.89, so no change of gait slides the feet. They slide only walking under 0.5 units per second, a slow of 90%, under the walk's slowest rate.
- **Checked by eye.** The baked walk, run and sprint sheets are the chosen previews to the pixel.
- **Tests.**
  - `AdventurerRecipeTests` adds: the gaits are the adventurer's own and the casts over the run use its run, while the rogue's gaits stay plain; each gait's bob has its top and bottom on frames; the head follows the bob late, the torso stretches at its top and squashes at its bottom, and the hair's tips move; the hands rise at both ends of their swing, and not in the rogue's.
  - `CharacterPrefabTests` adds: on either side of each boundary between the player's gaits, the gait the hysteresis keeps plays unclamped.

### Phase 3: idle animations

- [x] The idle animations, settled with the user on previews: looking around, a stretch, a scratch of the head, a tapped foot and a yawn, each starting and ending on the idle's frame; the face's expressions they need (the chin tipped back, the eyes shut, the mouth open).
- [x] Baked into the adventurer's set, and played after the player has stood still for a while: one at a time, picked so the same one does not play twice in a row, again after a further wait, and cut at once by moving, an attack, a cast or a hit.
- [x] Shown on every machine: each machine plays them on its own copies from the standing it already sees, picked the same everywhere, with nothing new on the wire.

Phase 3 notes:

- **Previews.** In `Recordings/RogueRework/phase2/ia1` and `ia2`, keyed on the adventurer's body (each a `KeyedMotion` starting and ending on the idle's pose): looking over one shoulder then the other (1.65 s), gathering the hands then stretching the arms up and back with a lean (1.36 s; the arms do not reach over this head, so its top reads as the hands behind it), scratching the side of the head (1.2 s), the hands on the hips tapping a foot three times with a glance aside (1.62 s), and a yawn: a breath in, the chin up with the eyes shut and the mouth wide, then a hand patting the mouth as it closes (1.47 s). All five liked.
- **The face's expressions.** `CharacterPose` and `MotionKey` gained `HeadTilt` (the face tipped back past the recipe's tilt, in degrees), `EyesClosed` (above 0.5 each eye is a line, its lowest row, without its glint) and `MouthOpen` (rows the mouth opens below its line, the rows under the lip in the brows' dark brown). `AdventurerPainter` draws them; all are 0 by default, so no sheet changed.
- **What is baked.** `Motion_IdleLookAround`, `Motion_IdleStretch`, `Motion_IdleScratch`, `Motion_IdleTapFoot` and `Motion_IdleYawn`, keyed motions posed on `Recipe_Adventurer` with `idleAnimation` set, so they have no contact frame and their clips are flagged `IsIdleAnimation` in the set. `Recipe_Adventurer` holds them as five more clips (64 px frames on the feet at (32, 12), after the 19 it had). Their sheets are the liked previews to the pixel, and each first frame is the idle's.
- **When they play.** `CharacterAnimator` gained `CharacterIdleAnimations`: after 8 seconds standing still with no action or hold showing, one plays once at its frames' durations in the direction faced, then the character stands; while it keeps standing, the next starts 10 to 16 seconds after one ends, never the same one twice in a row (`Locomotion_Player`'s Idle animations). Moving, an attack, a cast or a hold (the hit-stop of a hit taken) cuts one at once and starts the wait over; the hold shows the character standing. A dead player is back on its feet at a waystone after 3 seconds, before the first wait is over, and a stun lasts 1.5 seconds, so neither shows one. The Weasel's and the Bandit's sets have none, so they never play one.
- **On every machine.** Each machine plays them on its own copy of a player, from the standing it already sees, and picks them with a `SystemRandom` seeded with the player's network id (`CharacterVisuals.SeedIdleAnimations`, called by `PlayerNetworkSync` on spawn everywhere). Machines that saw the same stops pick the same animations and gaps; a copy that saw a stop the owner did not (a late joiner) may pick differently, which only changes which animation shows. Nothing new goes on the wire.
- **Tests.**
  - `CharacterAnimatorTests` adds: an idle animation after the wait, from its first frame at its natural pace, then standing; the next after its gap, never the one before; moving, an action or a hold cutting one and starting the wait over; animators seeded alike picking alike, none without a random source or idle animations, and no allocation; visuals seeded alike showing the same ones.
  - `AdventurerRecipeTests` adds: the five idle animations posed on the adventurer without a contact frame, starting and ending on the idle's pose, their first frames the idle's to the pixel in every direction; the yawn shutting the eyes and opening the mouth. The rogue's keyed motions test leaves the idle animations out.
  - `CharacterPrefabTests` adds: the player's set holds the five, complete and unarmed in 64 px frames on the feet, and its settings wait before the first and longer between the next.
  - `NetworkPlayersPlayModeTests` adds: with both sides waiting half a second, the host's standing player and the client's copy of it play the same first two idle animations, and moving cuts them on both.

### Phase 4: attacks and casts

- [x] The sword, greatsword and bow attacks restyled to match the gaits on the new body, settled with the user on previews: two attacks a weapon that flow into each other, their contact frames kept on the timeline.
- [x] A weapon's attacks played in turn, never the same twice in a row, the owner naming the attack it made to the other machines; a look without a weapon's other attacks plays its first.
- [x] The casts restyled to match, settled with the user on previews, each looked at in every direction.

Phase 4 notes:

- **How the attacks were settled.** On previews baked in memory, each attack at its weapon's timeline in the game, chained back to back from the idle (`Recordings/RogueRework/phase4/`).
  - Sword, round 1: today's slash beside its keys with the head and hair late and the torso stretching, a slower windup holding at its top (the anticipation), a bigger swing and a bigger one still. No single slash was picked: the user asked for several attacks a weapon instead, three picked at random but never the same twice in a row.
  - Sword, round 2: a slash (the held windup), a backhand and a chop. Then two that flow came first: the slash and the backhand.
  - Sword, round 3: the backhand redone, each attack ending in the pose the other starts from. Approved.
  - Greatsword: an overhead cleave and a low sweep back across. Bow: today's upright shot ending on the nock, and a canted shot from it, crouched with a lean and the bow tilted. The canted shot's string first ran through the body, the draw hand pulled back to the chest; the hand now draws to the cheek, the bow tilted about 35 degrees where it was 60. Approved.
- **What the keyed motion gained.** `KeyedMotion`'s Style settings, off by default, so the rogue's sheets did not change by a byte. `headFollow` and `headLag`, and `hairFollow` and `hairLag`, have the head and the hair's tips follow the head's own rise and fall late: the head's height a lag earlier in the motion, against now. A key's `stretch` grows or shrinks the torso. Every attack of the adventurer has the head follow at 1 with a lag of 0.06 of the motion, and the hair at 1.5 with a lag of 0.1.
- **The attacks.** Copies of the rogue's motions with new keys, still posed on `Recipe_Rogue` and fitted to the adventurer:

  | Weapon | First attack | Second attack | Frames, contact |
  |---|---|---|---|
  | Sword | `SwordSlash`: from a guard at the right shoulder, a windup that holds at its top (320 of its 430 natural ms), a wide forehand cut ending low on the left | `SwordBackhand`: from low on the left, back across and up, ending at the right shoulder's guard | 8, frame 4 |
  | Greatsword | `GreatswordCleave`: from the blade over the right shoulder, raised high, cut down and across to the left, ending low with the knees bent | `GreatswordSweep`: from low on the left, swept round at waist height, ending over the right shoulder | 9, frame 5 |
  | Longbow | `BowShot`: upright, drawn and loosed, ending with an arrow nocked, the bow held before the chest | `BowCantedShot`: from the nock, crouched with a lean and the bow tilted, drawn to the cheek and loosed, ending standing | 9, frame 5 |

  The weapons' timelines did not change: the sword 0.15, 0.05 and 0.15 s, the greatsword 0.40, 0.06 and 0.44, the longbow 0.35, 0.07 and 0.38. Each phase's frames still stretch to it in their natural proportions.
- **How they flow.** Each attack's last key is the pose the other's first key holds, with two small differences: the backhand ends leaning 2 degrees further forward than the slash starts, and the sweep ends with its left foot 1.3 rig units from where the cleave starts it. In the sheets, in every direction, an attack's last frame puts the head's top and the boots within two pixels of the other's first frame. An attack's first frame from standing still is within a pixel of the idle's at the head and two at the boots, the greatsword's two and three at the head, as they start with the knees bent.
- **How they play.** The owner picks: after equipping a weapon, its first attack, then one of the others than the last with the injected `IRandom` (`MainLifetimeScope`'s), never the same twice in a row, so with two they alternate. A weapon names its attacks' clips, its first (`attackClip`, as before) and the others (`otherAttackClips`). Every attack of a weapon lands alike: the same area or projectile, damage and timeline.
- **On every machine.** The attack's index goes in the start and the contact messages, which carried the byte already (always 0 before), so nothing new goes on the wire. A copy plays that attack's clip, and nothing for an index past the weapon's attacks. A look whose set lacks one of a weapon's other attacks plays the first attack's clip instead: the Bandit, drawn in Aseprite with one attack a weapon. Only the first attack is a clip every look must hold (`CharacterClipNeeds`).
- **Checked by eye.** The baked attack sheets are the approved previews to the pixel.
- **Tests.**
  - `AdventurerRecipeTests` adds: the adventurer's attacks are its own, two a weapon, each ending with the pelvis, the shoulders' twist, the hands and the weapon where the other starts, the head following late and the torso stretching or squashing on some frame, while the rogue's stay plain. The motions fitted from the rogue's body are 16 now.
  - `CharacterPrefabTests`: every attack of the three weapons lines up with the idle at the feet, and each attack's last frame lines up with the other's first.
  - `WeaponCatalogTests`: every weapon has its two attacks in the set, each showing its contact frame at contact under attack speed.
  - `CharacterAnimatorTests` adds: a weapon's three attacks start with the first, never repeat, are picked at random among the others and start over on equipping; two alternate without a random source; a copy plays the attack the owner made, or the first where its set lacks it, and nothing for an attack past the weapon's.
  - `NetworkWeaponsPlayModeTests`: a client's second attack plays the second attack's clip on the host's copy.
- **How the casts were settled.** On previews at each ability's in-game timing (`Recordings/RogueRework/phase4/casts1`, `casts2` and `charge3`).
  - Round 1 put each cast as it was beside three steps: the attacks' style (c1), a hold at the gather's top with a settle after the release (c2), and 40% bigger poses (c3). The user picked c1 for all of them and asked for each to be looked at on its own.
  - Two problems came back. The bow went through the player again: the volley's draw hand sat behind the head and its upper limb crossed over it, and the roll swung the bow flat through the body. The sword's and the greatsword's casts were underwhelming: the whirl looked like an attack.
  - Round 2 reworked each cast, checked frame by frame in all eight directions. "Much better"; the charge needed more.
  - The charge, round 3: a rush that drives the greatsword forward and cuts across, beside a leap that slams it down. The leap was picked.
- **The casts.** Copies of the rogue's motions, still posed on `Recipe_Rogue`, all with the attacks' style. The rogue's casts are untouched.

  | Cast | What it does |
  |---|---|
  | `CastBolt` | Winds the glowing hand back above the shoulder, the other hand pointing ahead, then throws it forward with a step. |
  | `CastLeap` | From a crouch, jumps clear of the ground with the arms raised and the legs tucked, then lands in a crouch and stands. |
  | `CastMend` | Opens the arms wide at the chest with the chin up, then brings the hands together forward. |
  | `CastSmite` | Raises the glowing hand up beside the head, where it pulses through the channel, then slams down into a crouch, the burst on the ground. |
  | `CastWhirl` | Crouches and winds the sword back, then spins a whole turn with the blade held out at the waist, its smear a ring. |
  | `CastLunge` | Coils with the weight back, then thrusts in a deep lunge, the blade straight ahead and the back leg and arm stretched behind. |
  | `CastSweep` | Lifts the greatsword overhead, drops into a deep coil, then spins a whole turn low with the tip near the ground, dust behind it. |
  | `CastCharge` | Raises the greatsword overhead, crouches and jumps, then lands in a deep crouch bringing the blade down to the ground, with dust. |
  | `CastVolley` | The bow's upright shot tilted back about 25 degrees, the chin up, shooting into the sky. |
  | `CastRoll` | Dives into a tuck and comes back up, unarmed. |

  `CastSmiteRun` and `CastWhirlRun` draw the new smite and whirl over the adventurer's run; the whirl spins over the running legs.
- **The bow.** At 38 rig units the bow is as long as the character, so held near the body it crosses it somewhere. The volley keeps the approved upright shot's hold, tilted back with the archer, so the bow sits where it sits in that shot in every direction (across the face from the front, as there). The roll draws no weapon: the character already moves unarmed, and the bow held at the side crossed the body in the side views however it was turned.
- **What the rig gained.** A key's `spin` turns the whole character, weapon included, from the way it faces (`CharacterPose.Spin`). The baker draws a frame facing its direction plus its spin, and turns each smear sample by its own spin against the frame's, so a spin's smear is a ring; the light stays where it is. A cast over the run takes the cast's spin and adds the cast's stretch and head's and hair's lift to the run's. All are 0 by default, so the rogue's and the Weasel's sheets did not change by a byte.
- **Checked by eye.** The baked cast sheets are the approved previews to the pixel.
- **Tests.** `AdventurerRecipeTests` adds:
  - the ten casts are the adventurer's own and styled, the whirl and the sweep spinning a whole turn and no other cast spinning, the casts over the run drawing its own smite and whirl, the roll unarmed, the rogue's casts plain;
  - a spin turns the whole character: the lunge spun a quarter turn draws facing east what it draws facing north;
  - a cast over the run takes the cast's spin and adds its style to the run's.

### Phase 5: colours

- [ ] A player's colours chosen per material ramp (skin, hair, tunic, shirt, trousers, boots, belt, eyes and more), shown on every machine, from one bake.

### Phase 6: swappable parts

- [ ] Parts per slot (hair, headwear, top, cape, legs, boots) a player chooses from, shown on every machine, without a bake per combination.

### Phase 7: gear that shows

- [ ] Equipment drawn over the body, and weapons carried outside attacks (Animation.md's Open questions).

## Testing

- EditMode: the recipe's pixels are repeatable and the standing height is right. The face, the locks and the outfit's options draw as set (Phase 0). Every look holds every clip the player plays (Phase 1). The gaits' style shows where it should and the speed bands keep the feet (Phase 2). The idle animations start after the wait, never twice in a row, and stop on anything else (Phase 3). A weapon's attacks flow into each other and never play twice in a row; the casts are the adventurer's own, and a spin turns the whole character (Phase 4). Customization: a player's choice resolves to the right colours and parts (Phases 5 to 7).
- PlayMode: the idle animations play on the other machines' copies of a standing player (Phase 3). The attack the owner made plays on the other machines' copies (Phase 4). A player's look, colours and parts reach the other machines through the in-process host and client, a late joiner's included (Phases 5 to 7).

## Open questions

- How colours are applied at runtime. One way is a sheet of material and tone indices read through a palette in the lit sprite shader. The other is a bake per palette, which costs memory for every combination.
- How parts combine. One way is a layer per slot drawn over the body, as the lantern's layer was, carrying the depth test's answer. The other is a bake per combination of parts. Either way, how a slot's parts are made: from the recipe's settings, or drawn in Aseprite in 8 directions.
- Whether the 8 directions stay a requirement once parts are drawn by hand. Stardew Valley and Necesse, named as references, draw 4.
