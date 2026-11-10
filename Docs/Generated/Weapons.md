# Weapons

Written by `Tools/TopDownRPG/Design/Write Design Tables` from the project's assets. Do not edit it by hand: change the assets and run the menu item again. The derived columns are defined in [DesignSystem.md](../DesignSystem.md#generated-tables), at attack speed 1 and against a unit's base defence, without statuses.

| Index | Weapon | Hit | Cooldown | Windup, contact, recovery | Cancel rules | Attack range | Delivery | Abilities | Attack period | Damage per second |
|---|---|---|---|---|---|---|---|---|---|---|
| 0 | Sword | 20 Physical | 0.35 s | 0.15, 0.05, 0.15 s | a move cancels the windup; any order ends the recovery | 0.3 | melee, 1.77 x 1.56 area 0.65 ahead | Blank Whirl (1), Blank Lunge (2) | 0.35 s | 57.1 |
| 1 | Greatsword | 48 Physical | 0.9 s | 0.4, 0.06, 0.44 s | a move cancels the windup; any order ends the recovery | 0.5 | melee, 2.6 x 1.8 area 0.85 ahead | Blank Sweep (1), Blank Charge (2) | 0.9 s | 53.3 |
| 2 | Longbow | 24 Physical | 0.8 s | 0.35, 0.07, 0.38 s | a move cancels the windup; any order ends the recovery | 5 | projectile, 18 units/s, range 6.5, radius 0.1, homing | Blank Volley (1), Blank Roll (2) | 0.8 s | 30 |

## Against each mob

A projectile's flight time is not counted.

| Weapon | Mob | Hit dealt | Damage per second | Hits to kill | Time to kill |
|---|---|---|---|---|---|
| Sword | Weasel | 20 Physical | 57.1 | 5 | 1.55 s |
| Greatsword | Weasel | 48 Physical | 53.3 | 3 | 2.2 s |
| Longbow | Weasel | 24 Physical | 30 | 5 | 3.55 s |
