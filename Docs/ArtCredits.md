# Art credits

Third-party art in the project, where it comes from and what its licence asks. Add a row here in the same commit that imports new art. Once the game has a credits screen, it must show this list, and a build must ship with it.

## Sources

| Source | Authors | Licence | Files in this project |
|---|---|---|---|
| [Mage City Arcanos](https://opengameart.org/content/mage-city-arcanos) | Hyptosis | CC0 (credit welcome, not required: "Hyptosis", or a link to www.lorestrome.com) | `Assets/Sprites/Props/magecity.png` |
| [LPC Tile Atlas](https://opengameart.org/content/lpc-tile-atlas) | Liberated Pixel Cup artists: Lanea Zimmerman (Sharm), Stephen Challener (Redshrike), Charles Sanchez (CharlesGabriel), Manuel Riecke (MrBeast), Daniel Armstrong (HughSpectrum), Casper Nilsson, Daniel Eddeland, Johann Charlot, Skyler Robert Colladay | CC-BY-SA 3.0 or GPL 3.0; used here under CC-BY-SA 3.0 | `Assets/Sprites/Props/terrain_atlas.png` |
| [2d Lost Garden Zelda style tiles resized to 32x32 with additions](https://opengameart.org/node/11758) | Daniel Cook (2d Circle Graphic Archive), Jetrel (mockups resized to 32x32), Bertram (improvements), Zabin (modifications and additions), Saphy (TMW) (tall grass) | CC-BY 3.0; asks for a link back to OpenGameArt and the submission | `Assets/Sprites/Props/mountain_landscape.png` |
| [2d lost Garden tileset transition to Jetrel's Wood tileset](https://opengameart.org/node/12667) | Jetrel, Daniel Cook, Bertram, Zabin | CC-BY-SA 3.0 | `Assets/Sprites/Props/wood_tileset.png` |
| [RPG Tiles: Cobble stone paths & town objects](https://opengameart.org/content/rpg-tiles-cobble-stone-paths-town-objects) | Zabin, Daneeklu, Jetrel, Hyptosis, Redshrike, Bertram (the page says who did what) | CC-BY-SA 3.0; Redshrike's table and Hyptosis' grass, cliff and water also CC-BY 3.0 | Crops of `PathAndObjects.png`: `Assets/Sprites/Props/props1.png`–`props17.png` |

All of it came over from the earlier project `projet-synthese`, which used it without credits. The four sheets in `Assets/Sprites/Props` are unmodified copies (the LPC atlas matches the original download byte for byte); their sprites are slices, not edited pixels. Characters, mobs, UI art and the grass tileset in `Assets/Tiles/tilesets` (`grass.png`) are not listed yet. The wall, stone path and gravel path tilesets there (`walls.png`, `stone_path.png`, `gravel_path.png`) and the blended terrain's material textures (`Assets/Textures/Terrain`: grass, gravel and stone, written in the editor by the terrain material recipes in `Assets/Settings/Terrain/Recipes` through `TerrainMaterialWriter`) were generated in this project and use no third-party art. The generated grass material replaces `grass.png` wherever the blended terrain draws the ground (the Village Forest so far).

## What the licences ask

- **CC0**: nothing.
- **CC-BY 3.0**: credit the authors, link the licence and the source, and say whether the art was changed.
- **CC-BY-SA 3.0**: the same, and any changed version of that art must be shared under CC-BY-SA 3.0 too. This covers the art only, not the code.

## Left out on purpose

These files from `projet-synthese` were not imported because their licence is restrictive or unknown:
- RPG Maker XP material (`044-Cave02.png` / `Dungeon.png`, `BigA$$Rock.png`, `SmolRock.png`, `tilesheet*.png`, likely `housing.png`), which may only be used in RPG Maker games.
- Sheets and sprites with no known source: `treeThree.png`, `barrel.png`, `signs.png`, the tents, `TowerWhite.png`, `TownSquare1/2.png`, the heraldry banners, `arabicTown.png`, and the animated campfire, brazier, wall torch and chests.
- `desert_1_0_7.png`, which is GPL 3 only.
