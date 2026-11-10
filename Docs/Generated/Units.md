# Units

Written by `Tools/TopDownRPG/Design/Write Design Tables` from the project's assets. Do not edit it by hand: change the assets and run the menu item again. The derived columns are defined in [DesignSystem.md](../DesignSystem.md#generated-tables), at attack speed 1 and against a unit's base defence, without statuses.

| Unit | Faction | Health | Resistances | Immunities | Move speed | Attack |
|---|---|---|---|---|---|---|
| Player | Players | 100 | none | none | 5 units/s | the weapon it holds ([Weapons.md](Weapons.md)), starting with the Sword |
| Weasel | Mobs | 100 | none | none | 2 units/s | 10 Physical; cooldown 0.75 s; 0.45, 0.05, 0.25 s; area 1 x 0.75; period 0.75 s |

## Each mob against each player

| Mob | Player | Hit dealt | Damage per second | Hits to kill | Time to kill |
|---|---|---|---|---|---|
| Weasel | Player | 10 Physical | 13.3 | 10 | 7.2 s |
