# Status effects

Written by `Tools/TopDownRPG/Design/Write Design Tables` from the project's assets. Do not edit it by hand: change the assets and run the menu item again. The derived columns are defined in [DesignSystem.md](../DesignSystem.md#generated-tables), at attack speed 1 and against a unit's base defence, without statuses.

Modifiers are per stack. A periodic total is before the target's defence, at one stack and at the cap.

| Index | Status | Kind | Tags | Duration | Stacking | Periodic | Periodic total | Modifiers | Controls | Grants |
|---|---|---|---|---|---|---|---|---|---|---|
| 0 | Burn | Debuff | Burn | 4 s | stacks to 5 | 3 Fire every 1 s | 12 (60 at 5) | none | none | none |
| 1 | Poison | Debuff | Poison | 6 s | independent, up to 3 | 2 Poison every 1 s | 12 (36 at 3) | none | none | none |
| 2 | Regeneration | Buff | none | 5 s | refresh | heals 4 every 1 s | 20 | none | none | none |
| 3 | Fortify | Buff | none | 8 s | refresh | none | - | +30% Physical resistance, +30% Fire resistance, +30% Frost resistance, +30% Lightning resistance, +30% Poison resistance | none | none |
| 4 | Vulnerable | Debuff | none | 5 s | refresh | none | - | damage taken x1.2 | none | none |
| 5 | Empower | Buff | none | 6 s | refresh | none | - | damage dealt x1.25 | none | none |
| 6 | Invulnerable | Buff | none | 3 s | refresh | none | - | none | none | immune to All damage |
| 7 | Chill | Debuff | Slow, Chill | 3 s | refresh | none | - | move x0.6 | none | none |
| 8 | Stun | Debuff | Stun | 1.5 s | refresh | none | - | none | Stun | none |
| 9 | Root | Debuff | Root | 2 s | refresh | none | - | none | Root | none |
| 10 | Silence | Debuff | Silence | 3 s | refresh | none | - | none | Silence | none |
| 11 | Unstoppable | Buff | none | 4 s | refresh | none | - | none | none | immune to Stun, Root, Slow statuses |

## Display

| Index | Status | Abbreviation | Colour | Description |
|---|---|---|---|---|
| 0 | Burn | Bu | `#FF731A` | Fire damage every second. Stacks up to 5; each stack adds damage and resets the duration. |
| 1 | Poison | Po | `#73D940` | Poison damage every second. Up to 3 separate doses, each with its own timer. |
| 2 | Regeneration | Re | `#4CE68C` | Heals every second. |
| 3 | Fortify | Fo | `#B2BFE6` | +30% resistance to every damage type except True. |
| 4 | Vulnerable | Vu | `#D94C99` | Takes 20% more damage. |
| 5 | Empower | Em | `#FFCC40` | Deals 25% more damage. |
| 6 | Invulnerable | In | `#FFF7B2` | Immune to all damage. |
| 7 | Chill | Ch | `#73CCFF` | Moves 40% slower. |
| 8 | Stun | St | `#FFEB33` | Cannot move, attack or cast. |
| 9 | Root | Ro | `#996B33` | Cannot move. Can still attack and cast in place. |
| 10 | Silence | Si | `#9E80F2` | Cannot cast abilities. |
| 11 | Unstoppable | Un | `#FF9933` | Immune to stuns, roots and slows; removes active ones. |
