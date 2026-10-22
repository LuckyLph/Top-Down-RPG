# Character animation

Spec for animating characters in movement and combat in the style of League of Legends: attacks with a clear windup, contact and recovery that can be cancelled, attacks that differ by weapon, and movement that changes with speed (walk, run, sprint). The art is generated: a character rig in the editor is baked into sprite sheets. [Architecture.md](Architecture.md) describes what exists today; this file describes the target and the order in which to build it. Tick off the phase checklist as steps land, and move each settled behaviour into Architecture.md as its code arrives.

Every rule in [Multiplayer.md](Multiplayer.md#rules-for-new-code-starting-now) applies: nothing here may assume one player, and each step is checked with a host and 2–4 players. The controls are [PlayerControls.md](PlayerControls.md)'s; this file adds how the orders they give look and when an attack lands.

## Where we start

- The player is a 24 x 32 px sheet with a 3-frame walk in 4 directions, played by a Mecanim blend tree (`IsMoving`, `MoveX/Y`, `LastMoveX/Y`) from `PlayerMotor2D`. The Weasel is 32 x 48 px with a walk and a 0.18 s attack pose from `MobMotor2D`.
- An auto attack has no windup: `PlayerWeaponController.TryAttack` spawns the slash and its hitbox on the frame the order asks for a swing. There is nothing to cancel.
- The slash effect rotates its sprite to the swing's direction and the mob death effect scales the mob by 1.2, which breaks the pixel grid.
- Both sheets predate the rule that all art is generated in the project.

## Goals

- Attacks read clearly: an anticipation pose, a fast swing with a smear on the contact frame, a follow-through and a recovery, plus impact feedback on the target.
- An attack is a gameplay timeline (windup, contact, recovery) that the animation follows, scaled by attack speed. Which phases an order can cancel is data, so both League of Legends' rules and stricter ones can be tried in play.
- Each weapon has its own attacks: a one-handed sword, a two-handed greatsword and a longbow first.
- Movement has three gaits, walk, run and sprint, picked from the speed the character actually moves at.
- Eight directions, never mirrored, so a right-handed character stays right-handed.
- Every frame is generated in the editor by a rig and written as sprite sheets at the game's pixel density.

## Non-goals (for now)

- Equipment that changes the body's look (armour sets). The rig's outfit is one look per character recipe.
- Blending between frames. Pixel art switches frames; gaits switch at matching steps instead.
- Facial animation and speech.
- A sprint key. Speed comes from statuses, terrain and, later, abilities.

## Decisions

| Topic | Decision |
|---|---|
| Art | A character rig generated in the editor: a 3D skeleton whose body parts are drawn as shaded shapes straight onto the pixel grid through the game's 3/4 camera, baked into sprite sheets by an editor recipe tool. No hand-drawn or third-party art. |
| Directions | 8, each rendered from the rig; none is a mirror of another. |
| Size | The body fits 32 x 48 px (about 18 x 46 px, 1.45 cells tall). Frames are 64 x 64 px, or 80 x 80 px for clips whose weapon reaches further (the greatsword's cleave, the bow's release), with the pivot at the feet. |
| Pixel density | 32 px per unit, as the terrain, props and the Pixel Perfect Camera. One pixel the rig draws is one game pixel; sprites import at 32 PPU. Character sprites are never scaled or rotated at runtime. |
| Light | From the top left, (-0.6, 0.8) in the screen plane, as the terrain's. |
| Look | A stylized rogue, for now: a black hood, mantle and cape with cool blue-grey highlights and a red lining, a red scarf mask and sash, silver jagged bangs and two eyes (no brows: a neutral face), a charcoal jerkin, black-brown boots and silver fittings, in flat two-tone hue-shifted shading. Chosen from mockups (variant F2). |
| Cancel rules | Data on each weapon (and later each ability): whether a move order cancels the windup (the attack does not happen and its cooldown is not spent), and whether any order cuts the recovery short. Both are tried in play before one is settled. |
| Gait | Walk, run or sprint from the speed the character moves at, with hysteresis between them; playback rate follows speed so feet do not slide. |
| Runtime | A plain C# layered sprite animator driven by gameplay state, not Mecanim. Gameplay timelines drive the animation; animation events never drive gameplay. |
| Weapons in frames | A weapon is drawn into the same frames as the body, so it passes in front of or behind each part correctly, and only in the clips that use it: its attacks (and later the casts it takes part in). Standing and moving, the character is drawn unarmed. Each weapon bakes its own sheets of its attacks. Decided in Phase 3, after carried weapons did not read at this size from eight directions. |

## The character rig

What the editor tool draws (Phase 1). It was settled on Python mockups (Phase 0) and ported as it was.

### Skeleton and pose

- Space: right, forward and up around the character, its origin between the feet on the ground, in rig units. The baker turns it to each of the 8 facings and projects it through an orthographic camera pitched 30 degrees down, the 3/4 view, at a scale chosen so the standing character is the recipe's target height in pixels.
- Joints: pelvis, neck, head, shoulders, elbows, hands, hips, knees, ankles. Legs and arms are two-bone IK chains: a pose gives foot and hand targets with a pole for the knee or elbow, and the chain bends to reach them. The spine leans forward and twists the shoulders; the hips follow or counter the twist.
- A pose also carries the weapon's state: the blade's direction, the bow's up and aim and how far it is drawn, whether an arrow is nocked, the string's kick after a release, and the cape's flow and phase.

### Drawing

- Parts are chains of spheres (capsules), each pixel of a sphere keeping its depth and surface normal, so nearer parts cover farther ones and shading follows the surface. Smears are triangles.
- Shading is flat: each material has a ramp of four colours (outline, shadow, base, light). A pixel takes the light colour only where its surface faces the light strongly (the top-left rims), the shadow colour where it faces away, and the base colour otherwise.
- Outline: every empty pixel beside the character takes the darkest colour of the nearest part next to it. Inside, a pixel beside a part that is nearer by more than a gap takes its own darkest colour, so an arm in front of the torso is lined.
- The face is placed on the hood by direction: each pixel of the hood's front knows its angle from the face's middle, the hood's opening, the shadow inside its rim, the bangs, the mask and the eyes are regions of those angles, so the face lines up in every direction. The head is tilted toward the camera (20 degrees) so the face is not foreshortened by the view.
- The cape is strips hanging from the shoulders to a hem, black outside and red inside (two layers), flowing back and up with speed and rippling with the stride.

### Motions

- Gaits are one parametric cycle: each foot follows a path (sliding back while planted, then a curve forward through the air), the pelvis bobs (highest mid-stance when walking, lowest when running), the body leans, the arms swing opposite the legs and the shoulders twist with them. Walk, run and sprint differ only in their settings. Every cycle starts with the left foot planted in front, so a switch between gaits keeps its place in the stride.
- Attacks are keyframed poses (time, easing, pelvis, lean, twist, feet, hands, poles, weapon), sampled at the clip's frame times. A frame can carry a smear, the area the blade's outer part swept since an earlier time, and effects: dust where the blade's tip meets the ground, a flash where an arrow leaves the bow.
- A weapon also says how it is carried while moving: the hand that holds it, where, at what angle and how much of the arm's swing it keeps; the gait's legs stay the same. No clip uses it since Phase 3, as weapons only show in their attacks.

### Weapons

- Blade: grip, pommel, crossguard and a tapering blade; one-handed in the right hand, or two-handed with the left hand on the grip below the right.
- Bow: a longbow about as tall as the character, in the left hand, its limbs tapering from the grip to the tips and bending further as it is drawn, the string from the tips to the nock (the right hand while drawing), an arrow while nocked.

## Runtime (Phases 2 to 4)

- `CharacterAnimationSet` (runtime ScriptableObject the baker writes): per clip, the sprites per direction and frame, each frame's duration at natural speed, and markers: the contact frame of an attack (Phase 3), the natural speed of a gait (the distance its feet travel per second at its natural rate, east or west).
- `CharacterAnimator` (plain class): a locomotion layer (idle, walk, run, sprint), an action layer (an attack or cast playing its timeline) above it, and an overlay (hit flash, hit-stop). It returns the sprite to show; it allocates nothing per frame and runs on an injected clock, so EditMode tests drive it.
- A component on the character's visuals applies the sprite and asks the animator each frame, fed by the motor (velocity, facing) and the weapon (attack phases). It replaces the player's and mobs' Mecanim controllers.
- Direction: the nearest of 8 to the facing, kept until the facing is a margin past the boundary, so it does not flicker.
- Locomotion: the gait whose speed band holds the current speed, with hysteresis; playback rate is speed over the gait's natural speed, clamped; a switch keeps the normalised phase.
- Action: each phase of the attack timeline plays its frames stretched to that phase's duration, so the contact frame shows exactly when the hit lands, at any attack speed.

## Attack timeline (Phase 3)

- A weapon's attack is a windup, a contact and a recovery. Their durations come from the weapon and are divided by attack speed. The attack timer (the cooldown) runs from the attack's start, as today.
- The hit resolves at contact (the slash's hitbox, or the arrow leaving), not at the start.
- Cancel rules on the weapon: `windupCancelledByMove` (a new move order during the windup aborts the attack; nothing lands and the cooldown is refunded) and `recoveryCancellable` (any order ends the recovery at once). League of Legends has both on.
- `PlayerOrders` keep asking for swings; the weapon's attack state holds the motor still during the windup and reports when it is busy. Stuns and death cancel a running attack.
- Mobs use the same timeline later (Phase 7): their windup is the telegraph players dodge.

## Networking

Animation is never replicated; every machine derives it from replicated gameplay state.

| What | How |
|---|---|
| Gait and direction | From the replicated move and facing that remote copies already get. The move becomes the actual speed over the base speed, so a slowed or hasted copy picks the same gait. |
| Attack | The owner sends the start (direction, which attack of the chain); other machines play the windup. At contact the owner tells the host, which resolves the hit around where the owner was, as casts do with `CastCompletedRpc`; other machines play the contact. A cancelled windup sends a cancel so copies stop. |
| Casts | As today, plus a cancel so a copy stops a cast that will never complete. |
| Feedback | Hit flash, hit-stop and the camera kick run on each machine from the hits it learns of, and change nothing in gameplay. |
| Late join | A joining player sees ongoing attacks only from their next start; nothing is replayed. |

## Rules

- Animation never decides gameplay: no animation events drive hits, and a dropped frame changes nothing.
- Character sprites are never scaled or rotated at runtime; new directions are baked.
- Every character frame comes from the baker. Change the look or a motion in its recipe and generate again.
- A frame's pivot is the character's feet, so `YPositionSorter` sorts characters by where they stand.
- A weapon only shows in the clips that use it (its attacks, later casts); standing and moving the character is unarmed. Its first and last frames show it at full length from the front, since they are where it appears and goes.

## Phases

Each phase ships on its own, keeps the game playable offline and hosted, and comes with tests.

### Phase 0: mockups
- [x] Python mockups of the rig (scratchpad, not committed): the three gaits, 8 directions, the sword, greatsword and bow attacks, carrying each weapon, the rogue's look and its face, compared on the real terrain at 32 px per unit.

### Phase 1: the baker
- [x] Editor-only recipe assets: the character (body, look, palette, shading, its clips and its sheet's name), motions (a gait cycle or keyed attacks with frame times, smears and effects) and weapon art (a blade or a bow, its colours, how it is carried).
- [x] The rig, the rasteriser and the rogue's look ported from the mockups; generating a recipe returns every clip's frames without touching an asset, the same recipe always giving the same pixels.
- [x] A writer that turns each clip into a sheet under `Assets/Sprites/Characters/<name>/` (a row per direction, a column per frame) at 32 PPU, sliced with the pivot at the feet, keeping slice IDs across regenerations, and an inspector with a preview and Generate.
- [x] The rogue's recipe, its walk, run and sprint, and the sword, greatsword and bow with their attacks, generated.

Phase 1 notes:
- The recipe, motions and weapons are under `Assets/Settings/Characters/`; their values are the mockups', exported as they were. The sheets are under `Assets/Sprites/Characters/Rogue/`: Walk, Run and Sprint unarmed, RunSword, RunGreatsword and RunBow carrying each weapon (64 px frames, feet at (32, 12)), SwordSlash and BowShot (80 px, (40, 22)) and GreatswordCleave (96 px, (48, 28)), whose blade reaches 38 px across and 59 px up from the feet.
- Directions run counterclockwise from east (E, NE, N, NW, W, SW, S, SE), E in a sheet's top row, so a facing's direction is its angle over 45 degrees, rounded.
- The scale (1.19 px per rig unit for the rogue) comes from a plain standing pose facing south, where the mockups measured a walking frame; the difference is under a pixel of height.
- Baking a clip takes about 0.25 s, so the inspector bakes only the clip it previews, and only when something it is made from changed.

### Phase 2: locomotion at runtime
- [x] `CharacterAnimationSet` written by the baker; `CharacterAnimator` with the locomotion layer and directions; the visuals component.
- [x] The player shows the rogue's walk, run and sprint from its speed (slows and statuses included), on every machine; `Player.controller` and the old sheet are retired.

Phase 2 notes:
- Measured on the Phase 1 gaits, from the planted left foot in the baked frames and from the motions' strides: walk 0.52, run 1.35 and sprint 2.57 units per second at their natural rates facing east or west, and half that facing north or south, where the view's pitch shows ground depth at half its length while the world's ground is drawn at full length. The player moves at 5 (3 chilled), so the run would have played at 3.7 times its rate.
- Compared on previews: rates clamped at 1.6 (the feet keep up 43% at 5 units per second), clamped at 2.5 (68%, the legs cycling at about 30 frames a second), longer strides, and a slower game (the player at 2.7). Chosen: longer strides with more time in the air, retuned in the gait motions and generated again. The natural speeds are now walk 1.02, run 4.23 and sprint 5.61; the carried runs (sword, greatsword, bow) share the run's motion and changed with it.
- Bands (`Locomotion_Player`): walk from 0.1, run from 2.4, sprint from 5.6 units per second, each boundary crossed only 8% past it either way; gaits play at 0.5 to 1.5 times their rate. The player runs at 1.18 times the run's rate at 5 and 0.71 chilled, its feet keeping up east and west. Nothing in play reaches the walk or the sprint yet.
- North and south the feet keep up with half the speed at any rate (about four fifths on the diagonals). Accepted: the rate is the same in every direction, as most top-down pixel games do, rather than legs cycling twice as fast going north.
- The idle is a very slight breath on the standing pose: 8 frames at 4 a second (a 2 s loop), the pelvis sinking under a pixel so only a pixel or two at the shoulders and hands shifts now and then, and a faint stir of the cape; at most 52 to 80 pixels change from its first frame. It was toned down after review from a one-pixel bob with a lean and hand sway (up to 250 pixels changing). Starting to move or stopping begins the new clip at its first frame; only switches between gaits keep the phase. The breathing was turned off after Phase 3, to come back later: `Motion_Idle` has one frame, the standing pose (the loop's first frame, unchanged), and setting it back to 8 frames restores the loop.
- The sprites pivot on the feet, so the player's `Visuals` moved from half a unit up to its root. Units now sort by their feet: the Weasel's and the prop bases' sorters take their root as a sorting point, and the death effect sorts where the body did. The slash follows the sprite, so the sword's slash offsets went up half a unit to keep it in place.
- `move` now replicates the actual speed over the base speed, where it was a share of the slowed speed, so a slowed copy picks the owner's gait and rate. The build version went to 1.6.
- Still to check by hand in Multiplayer Play Mode (the in-process tests share one scene): with a host and 2 to 3 clients, each machine's copies run in the right direction when players walk in all eight directions; a chilled player's copies run at the owner's slower rate; copies stand idle facing the owner's way when it stops, is stunned or dies, and run again after a respawn; a late joiner sees a moving player run and a standing one idle at once.

### Phase 3: attack timeline
- [x] Windup, contact and recovery on the weapon, attack speed, cancel rules as data, hits at contact; `PlayerOrders` and the motor follow the attack state.
- [x] The action layer plays the sword's attack stretched to the timeline; the rotated slash sprite is replaced by the baked smear.
- [x] Attack start, contact and cancel over the network; checked with a host and clients.

Phase 3 notes:
- The timeline is data on `PlayerWeapon` (windup, contact and recovery at attack speed 1, the cancel rules) and an `AttackState` on the game clock in the weapon controller runs it; the animator's action layer reads its phases from the same `AttackTimeline`, from the same start time, so the contact frame shows on the update the hit resolves at any attack speed. Attack speed is a multiplier on the weapon controller (1 until statuses scale it) that divides the timeline and the cooldown alike, so attacks keep their shape as they speed up.
- The baked SwordSlash lasts 0.84 s at natural speed: the windup is frames 0 to 3 (500 ms, the 40 ms smear frame 3 included, as the clip's split is by its contact frame), the contact frame 4 (90 ms) and the recovery frames 5 to 7 (250 ms). Each phase keeps its frames' natural proportions when stretched. Compared on previews against the sword's 0.35 s cooldown (`Recordings/AttackTimeline/`): a short windup with a long recovery (100, 50 and 200 ms), an even split (150, 50, 150), the clip's own shape squashed into the cooldown (208, 38, 104) and a recovery past the cooldown (120, 60, 300). Under a windup of about 0.15 s the smear frame 3 lasts under a frame at 60 fps and does not show; the contact frame's own smear carries the swing. Chosen: the even split, 150, 50 and 150 ms, the whole attack as long as the cooldown, so continuous attacks follow each other with no idle between them and each shows its follow-through; a recovery past the cooldown would be cut by every next attack.
- The contact frame is never skipped: an update that jumps past the whole contact still shows it once, as the attack state still resolves the hit on that update.
- The hit is one non-allocating overlap at contact (`AttackService`), in the old slash's hitbox: 1.77 by 1.56 units, centred 0.65 along the attack from the half-unit offsets. Nothing lingers to hit units that walk in later. The rotated slash sprite, its prefab, clip, sprite sheet and editor bootstrap are gone; the swing shows only as the baked smear in the character's frames, which reads much smaller than the old 72 px slash, and facing north the body hides most of it; kept as it is after comparing the two.
- Cancel rules, both on for the sword: a move order or Stop during the windup aborts it (`windupCancelledByMove`); any order during the recovery ends it at once (`recoveryCancellable`), and the next attack may start in a recovery that runs past the cooldown. A move counts when it gives a new destination, so holding the button over the ground cancels too, and re-clicking the unit already attacked does not. Other orders given before the recovery (an attack on another unit, an attack move, a chase toward a target that left range) end it as soon as it begins; the contact always plays out. A stun, a cast, a teleport or death ends an attack in any phase. An attack whose hit never landed gives its cooldown back, whatever ended it.
- The motor holds still through the whole attack, not just the windup (a recovery that cannot be cut short would otherwise play while sliding): the controller sets its speed scale to 0, as a root does, so a move or chase given meanwhile keeps its path and walks once the attack ends.
- The 80 px attack frames line up with the 64 px locomotion frames on the same `Visuals`: in all eight directions the first attack frame's hood is at the idle's height (42 px above the feet) and its boots within a pixel.
- Weapons only show in their attacks (decided in review). Showing the equipped weapon was tried first: the idle, walk and sprint carrying each weapon were baked beside the Phase 1 carried runs, the sword was reposed to hang low at the side (held forward and up it pointed at the camera facing south, leaving only its hilt), and the greatsword and the bow were tried in several poses, the bow after Varus's in League of Legends. One carried pose serves eight directions, and every one tried was edge-on, hidden by the body or foreshortened in some of them; a weapon one or two pixels wide also loses its shape. The carried clips (RunSword, RunGreatsword and RunBow included) were removed, and the player stands and moves unarmed, the weapon appearing on its attack's first windup frame.
- The bow became a longbow, as readable as the blades: half length 19 rig units (it was 9.5), limbs tapering from 1.1 to 0.6 units thick (a limb thickness setting on the weapon art, 0.85 to 0.45 by default as before), a deeper bend at rest (3.5) and drawn (8), longer arrows (18), yew wood with a red grip wrap in place of the dark brown. Its sheet keeps 80 px frames.
- The weapon looks the same through each attack: measured as the visible length of the weapon in every frame, facing south the sword's and the greatsword's first frames showed a stub of under 40% of the blade (pointing at the camera) and the bow's first and last frames under 45% of the bow (tilted toward the camera), the "shortbow" frames. The first and last keys were reposed: the sword low at the right side, tip down and out (as the carry tried in review), the greatsword low and forward, the longbow upright, raised so its lower tip clears the ground. Facing south the sword's first frame now shows 98% of its blade and the bow 80 to 88% in every frame. Some first and last frames now hide the weapon behind the body instead (the sword facing west and north-west, the greatsword north and north-west, the bow north-east), as any one pose must in some direction.
- Networking: the owner sends the start (which attack, the direction and the attack speed), the contact (with where it stood) and an early end, to every other machine. A copy plays the windup and holds its last frame until the contact arrives, then plays the contact and recovery from there; an early end stops it; a contact without a start (a late joiner) is ignored. Only the host resolves hits, around the owner's position, once per contact message; a client's own resolution does nothing without authority. The build version went to 1.7.
- Still to check by hand in Multiplayer Play Mode (the in-process tests share one scene): with a host and 2 to 3 clients attacking the same Weasel, each machine's copies wind up, strike and recover facing the right way in all eight directions, and the Weasel loses each hit's damage once (the popups match on every machine); a client clicking the ground during its windup stops its copies and the Weasel takes nothing; a client stunned or killed mid-windup stops its copies; a client attacking while its copy lags behind under the Network Simulator's bad-connection preset still hits what stood next to the owner; continuous attacks from 3 players read as separate swings; a late joiner sees attacks from their next start and nothing replayed.

### Phase 4: weapons
- [ ] The greatsword and the bow as weapons with their attacks; the bow's arrow as a projectile from its contact.

### Phase 5: casts
- [ ] Cast poses for abilities, and casting while moving (`CastMovement.Continue`) by baking legs and upper body as separate layers.

### Phase 6: feedback
- [ ] Hit flash and hit-stop on the target, a small nudge, impact effects and a camera kick for the local player; pooled.

### Phase 7: mobs
- [ ] A rig for the Weasel, its attack on the timeline with a telegraphed windup.

## Testing

- EditMode: the baker is deterministic (the same recipe gives the same bytes); every clip has 8 rows of its frame count; slices are named, sized and pivoted as specified at 32 PPU; the standing character is the target height; no frame touches its edge; the face shows its eyes from the front and none from behind; a planted foot moves back at its gait's natural speed; the animation set holds every clip, with each attack's contact frame; the animator's gait choice, hysteresis, rate clamping, phase keeping and direction snapping on `ManualClock`; the attack timeline's phases, attack speed and each cancel rule; the action layer's stretched phases, its contact frame showing at contact, and locomotion resuming after it.
- PlayMode: the player's sprite follows its speed and facing; an attack's contact frame shows when its hit lands.
- Manual: Multiplayer Play Mode with 2–4 players attacking the same mob, cancelling windups, and a late join.

## Open questions

- Which cancel rules to keep, after trying both in play.
- Whether casting while moving needs split layers, or a few combined clips suffice.
- Whether equipment should show outside attacks after all. If it should, the baker would draw the weapon as separate layers (behind the body, the body, in front of it) so that bodies and weapons combine freely, instead of carried clips baked per weapon.
