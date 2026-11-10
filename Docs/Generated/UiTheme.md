# UI theme

Written by `Tools/TopDownRPG/Design/Write Design Tables` from the project's assets. Do not edit it by hand: change the assets and run the menu item again. The derived columns are defined in [DesignSystem.md](../DesignSystem.md#generated-tables), at attack speed 1 and against a unit's base defence, without statuses.

## Colour roles

| Role | Colour | Opacity |
|---|---|---|
| Text | `#FFFFFF` | 100% |
| TextOnControl | `#1A1A1F` | 100% |
| TextMuted | `#BFBFC7` | 100% |
| TextDisabled | `#80808A` | 100% |
| Placeholder | `#595959` | 100% |
| Accent | `#FFD966` | 100% |
| Surface | `#1F1F29` | 85% |
| SurfaceEmpty | `#1F1F29` | 75% |
| SurfaceRaised | `#14171F` | 100% |
| Scrim | `#000000` | 60% |
| ScrimModal | `#000000` | 85% |
| Shade | `#000000` | 65% |
| ControlNormal | `#D9D9CC` | 100% |
| ControlHighlighted | `#FFFFFF` | 100% |
| ControlPressed | `#B2B2A6` | 100% |
| ControlDisabled | `#6E6E73` | 100% |
| Danger | `#FF4D40` | 100% |
| Positive | `#73FF8C` | 100% |
| Health | `#D92121` | 100% |
| HealthTrack | `#141414` | 85% |
| Icon | `#FFFFFF` | 100% |
| IconMissing | `#FFFFFF` | 25% |
| Border | `#9A9EB3` | 100% |
| Outline | `#000000` | 100% |
| ControlSecondary | `#9E9E96` | 100% |

## Text sizes

| Size | Font | UI px | Body at 1080p |
|---|---|---|---|
| Caption | Galmuri7 SDF | 8 | 24 px |
| Body | Galmuri7 SDF | 8 | 24 px |
| Heading | Galmuri9 SDF | 10 | 30 px |
| Title | Galmuri11 SDF | 12 | 39 px |
| Display | Galmuri14 SDF | 15 | 51 px |

## Text on backgrounds

Each pair of text role and background role a themed text uses, at the smallest size it is used at. A translucent background counts over white and over black, the lower ratio; a control in each state it is read in.

| Text | Background | Smallest size | Contrast | Needs |
|---|---|---|---|---|
| Text | Surface | Caption | 10.16:1 | 4.50:1 |
| Text | SurfaceRaised | Body | 17.91:1 | 4.50:1 |
| Text | Health | Body | 5.01:1 | 4.50:1 |
| Text | Outline | Caption | 21.00:1 | 4.50:1 |
| TextOnControl | ControlNormal | Body | 12.17:1 | 4.50:1 |
| TextOnControl | ControlHighlighted | Body | 17.33:1 | 4.50:1 |
| TextOnControl | Accent (selected) | Body | 12.68:1 | 4.50:1 |
| TextOnControl | ControlDisabled | Body | 3.41:1 | 3.00:1 |
| TextOnControl | Danger | Body | 5.27:1 | 4.50:1 |
| TextOnControl | ControlSecondary | Body | 6.42:1 | 4.50:1 |
| TextMuted | SurfaceRaised | Body | 9.80:1 | 4.50:1 |
| Placeholder | ControlNormal | Heading | 4.91:1 | 4.50:1 |
| Placeholder | ControlHighlighted | Heading | 7.00:1 | 4.50:1 |
| Placeholder | Accent (selected) | Heading | 5.12:1 | 4.50:1 |
| Accent | SurfaceEmpty | Caption | 5.24:1 | 4.50:1 |
| Accent | SurfaceRaised | Body | 13.11:1 | 4.50:1 |
