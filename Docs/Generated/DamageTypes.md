# Damage types

Written by `Tools/TopDownRPG/Design/Write Design Tables` from the project's assets. Do not edit it by hand: change the assets and run the menu item again. The derived columns are defined in [DesignSystem.md](../DesignSystem.md#generated-tables), at attack speed 1 and against a unit's base defence, without statuses.

| Type | Popup colour | Contrast on Outline |
|---|---|---|
| Physical | `#E64040` | 5.17:1 |
| Fire | `#FF801A` | 8.32:1 |
| Frost | `#73CCFF` | 11.80:1 |
| Lightning | `#FFE64C` | 16.62:1 |
| Poison | `#73D940` | 11.69:1 |
| True | `#F2F2F2` | 18.80:1 |
| Heal | `#4CE666` | 12.76:1 |
| Immune ("Immune") | `#BFBFBF` | 11.45:1 |

## Resolution

| Rule | Value |
|---|---|
| Resistance floor | -100% |
| Resistance cap | 80% |
| Minimum damage | 1 |
| Periodic popup size | Caption |
