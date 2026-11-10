# Abilities

Written by `Tools/TopDownRPG/Design/Write Design Tables` from the project's assets. Do not edit it by hand: change the assets and run the menu item again. The derived columns are defined in [DesignSystem.md](../DesignSystem.md#generated-tables), at attack speed 1 and against a unit's base defence, without statuses.

| Ability | Used by | Targeting | Range | Area | Cast time | Cooldown | Movement | Bufferable | Effect | Damage per cooldown second | Cast clips |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Blank Bolt | Class_Placeholder (Q) | Direction | 6 | a line 0.7 wide | 0.25 s | 2 s | Stop | no | 12 Frost, Chill | 6 | `CastBolt` |
| Blank Charge | Greatsword (2) | Direction | 0 | a line 0 wide | 0.2 s | 6 s | Ability | no | none | - | `CastCharge` |
| Blank Leap | Class_Placeholder (W) | Point | 5 | 0 around the point | instant | 4 s | Stop | yes | none | - | `CastLeap` |
| Blank Lunge | Sword (2) | Direction | 0 | a line 0 wide | 0.2 s | 5 s | Ability | no | none | - | `CastLunge` |
| Blank Mend | Class_Placeholder (E) | Unit (allies) | 4 | the target | 0.5 s | 6 s | Stop | no | heals 25, Fortify | - | `CastMend` |
| Blank Roll | Longbow (2) | Direction | 0 | a line 0 wide | 0.15 s | 5 s | Ability | no | none | - | `CastRoll` |
| Blank Smite | Class_Placeholder (R) | Unit (enemies) | 3 | the target | 1 s | 10 s | Continue | no | 15 Physical, Stun | 1.5 | `CastSmite`, `CastSmiteRun` |
| Blank Sweep | Greatsword (1) | None | - | 2 around the caster | 0.4 s | 4 s | Stop | yes | 15 Physical | 3.8 | `CastSweep` |
| Blank Volley | Longbow (1) | Point | 6 | 1.5 around the point | 0.35 s | 5 s | Stop | no | 12 Physical | 2.4 | `CastVolley` |
| Blank Whirl | Sword (1) | None | - | 1.5 around the caster | 0.3 s | 1.5 s | Continue | yes | 10 Physical | 6.7 | `CastWhirl`, `CastWhirlRun` |

## Display

| Ability | Description |
|---|---|
| Blank Bolt | Fires a bolt of frost along a line. Every enemy in it takes Frost damage and is chilled. |
| Blank Charge | Charge ahead. For now the cast plays without moving you. |
| Blank Leap | Leap to a point nearby. For now the cast plays without moving you. |
| Blank Lunge | Lunge ahead. For now the cast plays without moving you. |
| Blank Mend | Heals an ally and fortifies them, raising their resistances for a while. |
| Blank Roll | Roll out of the way. For now the cast plays without moving you. |
| Blank Smite | A slow strike at one enemy that deals Physical damage and stuns it. You can keep moving while you wind it up. |
| Blank Sweep | Sweeps the greatsword around you, dealing Physical damage to every enemy close by. |
| Blank Volley | Looses a volley of arrows at a point, dealing Physical damage to every enemy around it. |
| Blank Whirl | Spins with the sword, dealing Physical damage to every enemy around you. You can keep moving while you spin. |
