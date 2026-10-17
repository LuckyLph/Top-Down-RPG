# 32 terrain layers

Experiment: raise the number of ground layers one terrain renderer draws from 16 to 32, so the area template can hold every material of the style's order (eighteen today) and an area can list more than sixteen. The work happens on `feature/terrain-32-layers`, branched from `feature/terrain-types`. [TerrainTypes.md](TerrainTypes.md) explains how the cap reached 16 (its Phase 1) and why the template leaves the ocean and lava out (its Phase 9 and Phase 12 notes). [TerrainBlending.md](TerrainBlending.md) describes the renderer, and [Architecture.md](Architecture.md) describes what exists.

Every rule in [Multiplayer.md](Multiplayer.md#rules-for-new-code-starting-now) applies. Ground is scene content that every machine loads the same way and draws locally. The control map, the lookup texture and the renderer's layer indices are never sent, and `AreaTerrain`'s runtime edits name a layer by its index in `AreaTerrain`'s own list. Nothing here touches replication.

## Where we start

- A cell's layers are the bits of one `R16_UNorm` texel of the control map, bit i for layer i ([TerrainControlMap](../Assets/Scripts/World/Terrain/TerrainControlMap.cs): `MaxLayers` 16, with `ushort` cells, preview bits, `GetCell` and `GetShownCell`). The shader reads a texel as `round(r * 65535)` (`TBCell` in [TerrainBlendShape.hlsl](../Assets/Shaders/Terrain/TerrainBlendShape.hlsl)), which needs full float precision.
- Two per-pixel arrays in the shader are sized by the cap: the layers' distances `d[TB_MAX_LAYERS]` and the pair weights `w[TB_MAX_LAYERS + 1]` ([TerrainBlend.hlsl](../Assets/Shaders/Terrain/TerrainBlend.hlsl)). Both are indexed by values known only at run time, so FXC declares them as indexable temporaries rather than plain registers. Unrolled loops clear the whole of each array: `d` once per pixel (`TBDistances`) and `w` once per layer drawn at the pixel (`TBPairWeights`). Every other per-layer loop already stops at the highest layer filled around the pixel (`TBLayersAt`, see TerrainTypes.md's Phase 1 notes).
- The bits of the bridge layers travel as a float in the lookup texture's shared texel (`TerrainBlendLut.BridgeBits`). A float holds a whole number exactly only up to 24 bits. The renderer and `TerrainCoastMap` keep the bits as an `int`.
- `TBNearBridgeEdge` clamps a shift to 31 ([TerrainBlendCoverage.hlsl](../Assets/Shaders/Terrain/TerrainBlendCoverage.hlsl)). That is correct only for layers below 30.
- The debug view gives each layer a pure colour whose channels are each off, at half or full, with at least one full (`TB_DEBUG_COLOURS`). Only nineteen such colours exist, and sixteen are in use. `TerrainBlendProbe.DebugClass` reads those three levels.
- The cap is enforced in four places: `TerrainControlMap`'s constructor, `TerrainBlendChecks.IsUsable`, `AreaGroundSetup.Problem` (used by the writer, Convert Ground and the Scene Generator window), and `AreaGroundSetup.SetUpLayers`, which drops layers past the cap. `TerrainBlendProbe` assumes it (`DebugLayers`).
- The area template holds sixteen layers: every material of the order but the ocean and lava (`AreaTemplateTests.LeftOut`, and the matching entry in Architecture.md's Known gaps). No generator lists more than twelve layers (the Lakeside).
- Each listed layer costs a slice in each of the albedo, cell field and height arrays (3 MiB at 512 px). The lookup texture has 1 + L + L(L + 1) rows of 37 texels of 16 bytes each: 289 rows (167 KiB) at 16 layers, 1089 rows (630 KiB) at 32. TerrainTypes.md's Phase 1 and Phase 11 notes give the measured draw cost at 16.

## Goals

- A renderer draws up to 32 layers, and the control map, the shader, the bridge bits, the debug view, the probe, every check and every message follow.
- Areas of 16 layers or fewer render byte for byte as before and draw no slower. Proof is either compiled code that does not change, or alternating measurements that stay within run-to-run noise.
- The area template holds every material of the style's order, so the ocean and lava are no longer special cases.
- A measured answer, recorded in this file whatever it turns out to be: what the higher cap costs areas that do not use it, what a 32-layer area costs, and how much longer the shader takes to compile.

## Non-goals

- New materials, or changes to the layers any generator or area lists.
- More than 32 layers, which would need two texels or a second texture per cell.
- Support for half-precision or mobile targets beyond what the format choice gives for free.
- Restructuring the per-pixel arrays (see Open questions), unless Phase 1 shows that the cap costs every area and the keyword below is turned down.

## Decisions

- **The branch.** `feature/terrain-32-layers`, from `feature/terrain-types`. If Phase 3's numbers hold, the branch merges back into `feature/terrain-types`. If they do not, this file and its measurements are brought over on their own, and TerrainTypes.md's open question about the template links to it, so the finding survives.
- **The control map: `RGBA32` (`R8G8B8A8_UNorm`).** Proposed here; Phase 2 checks it against Unity's docs. A cell's 32 bits are its texel's four bytes, with red as the lowest byte. They are written through `GetPixelData<uint>` and read as `r | g << 8 | b << 16 | a << 24`, with each channel read as `round(c * 255)`. Every graphics device can sample this format, and 8-bit channels decode exactly even at half precision, so the R16 map's need for full floats goes away. The alternative is `RG32` (`R16G16_UNorm`): two 16-bit halves, each read like today's texel, with the same precision requirement as R16. An integer texture (`R32_UInt`) is ruled out: Shader Graph's texture inputs (`UnityTexture2D`) are float textures, so it would need a texture declared outside the graph. The map takes 4 bytes per cell instead of 2, 13.5 KiB for a 72 x 48 area.
- **Bridge bits in two halves.** The shared texel keeps the low 16 bits in its alpha, as now, and a new shared column holds the high 16 bits. Each half is exact in a float. `BridgeBits` returns a `uint`, and the renderer and `TerrainCoastMap` store it as one.
- **A keyword, `_TERRAIN_WIDE` (decided in Phase 1).** The rule: keep one shader if, with the cap at 32, every area and the 16-layer stress scene draw within run-to-run noise of the baseline and the compile test grows by at most a quarter; otherwise add the keyword. The compile test grew by about a tenth, but with the cap at 32 every area and the stress scene drew 1.8 to 2.7 times slower in crisp and 1.7 to 6.0 times slower in soft at 7680 x 4320 zoom 3 (1.35 to 2.6 times at 1920 x 1080), where runs of the same shader differ by at most 7 %, and the narrower clearing of the arrays did not bring it back (Phase 1's notes). So the shader gets a local keyword, `_TERRAIN_WIDE`, compiled with the cap at 32 and the RGBA32 decode. The renderer enables it only when it lists more than 16 layers, the same way it sets `_TERRAIN_SOFT` and `_TERRAIN_RAISED`. Without the keyword the shader compiles exactly as today, so the compiled code is unchanged, and the control map stays R16 for 16 layers or fewer. The cost of the keyword: twice the variants, and about twice the compile-test time. An area of more than 16 layers runs the wide variant at every pixel, so it pays that variant's cost everywhere, not only where more than 16 layers are painted; Phase 3 measures it.
- **Debug colours: layers 0 to 15 keep theirs.** Layers 16 to 31 get colours whose channels are each off, at a quarter, half, three quarters or full, with at least one channel full and none already in use. Earlier debug captures therefore stay byte for byte, and the probe reads five levels against the brightest channel. Phase 3 picks the table and checks that every two colours stay apart under the areas' light (intensity 0.85).
- **The template holds every material.** Once the cap is 32, the left-out list goes away. A generator still trims an area to its own layers.
- **The stress scene is code.** `TerrainBlendProbe.BuildStressScene(int layers)` builds the measuring scene into the active scene, so every phase and session measures the same thing. Phase 11's test scene came from a script kept in one session's scratchpad. The scene has grass over 72 x 48 cells at the origin, a white Global Light 2D at 0.85, and layers 1 to n - 1 drawing the materials of the style's order after grass in turn, starting over after the last. Each of those layers is painted as four discs of radius 2 to 5 cells, at spots drawn from a `System.Random` seeded with the layer's index. The 32-layer scene is therefore the 16-layer scene with sixteen more layers on top. Planks fall at layer 17, past 16, so the scene exercises the high bridge bits.

## How to measure

The method follows TerrainTypes.md's Phases 1 and 11, plus what later sessions learned:

- **Cost:** `TerrainBlendProbe.MeasureCost` in crisp and soft at 7680 x 4320 zoom 2, 7680 x 4320 zoom 3 and 1920 x 1080 zoom 3, taking the best of three runs of 20 frames and noting the chunks-hidden time. Time old and new alternately in one session, twice each, by swapping the shader files and refreshing. Never compare against numbers from another session. Measure with the editor unfocused; no script may bring Unity to the front.
- **Compile time:** `ShaderData.Pass.CompileVariant` for each Sprite Lit fragment variant (crisp and soft, each with and without `_TERRAIN_RAISED`), with an empty scene open, plus the `ShaderCompile` test. Compare the compiled bytes per variant (the DXBC after Unity's 38-byte header) to show when a change compiles to the same code.
- **Byte for byte:** capture each of the six areas, opened alone, over the control map's bounds at one screen pixel per art pixel: both looks at fixed times of 1.5 s and 2.75 s, plus the debug view, plus `CheckBlobs` in both looks. Capture the stress scene the same way. Keep the hashes in the session's scratchpad; the phase notes record how many files were compared and whether they matched.
- **Rebuild and memory:** `MeasureRebuildAndModeSwitch`, best of five, and the sizes of the arrays and the lookup texture, read back from the built renderer.

## Phases

Tick the boxes as steps land. Record the numbers and any surprises in each phase's notes.

### Phase 1: what the cap costs

Measure before building anything: find out what a cap of 32 costs areas that do not need it, then choose between one shader and the keyword. `TB_MAX_LAYERS` is back at 16 when the phase ends. Only two things stay: the stress scene builder, and the narrower clearing of the arrays if it earns its place.

- [x] `TerrainBlendProbe.BuildStressScene` (see Decisions), with its entry in Architecture.md.
- [x] Baseline at the start of the branch: the six areas and the 16-layer stress scene, measured, captured and compiled as in How to measure.
- [x] The cap at 32 and nothing else changed: the same measurements, alternating with the baseline. Captures must match byte for byte, since nothing reads past an area's layers.
- [x] If that costs time: clear `d` only up to the area's layers, and `w` only up to the layer being drawn, so no per-pixel loop runs to the cap. Check that FXC gives no warnings about uninitialised reads. HLSL's `&&` does not short-circuit, so a read guarded by `m + 1 < layers` still happens. Measure at 16 and at 32, with captures matching byte for byte.
- [x] The compiled code's temp registers and indexable arrays per variant, at 16 and 32 (Unity's compiled-code view), to explain the numbers.
- [x] The decision (one shader or `_TERRAIN_WIDE`) and the numbers, in this phase's notes. The narrower clearing stays at a cap of 16 only if it is no slower, matches byte for byte, and passes the `ShaderCompile` test.

Notes:

- **The stress scene.** `TerrainBlendProbe.BuildStressScene(16)`, built into an empty scene, lists sixteen layers: grass over 3456 cells, then dry grass to the ocean in the style's order, 100 to 253 cells each. Its control map is 74 x 50 cells (the grass and its margin), its arrays 48 MiB (sixteen 512 px slices), its lookup texture 37 x 289 (167 KiB). Its blob check names all sixteen layers, with no unknown colour and no holes; in crisp it lists 452 detached blobs under 200 px, most of them 1 to 3 px pieces of plaza and stone where several discs overlap, and in soft 5. That is the scene's baseline, not a fault to fix. The Editor assembly now references `Unity.RenderPipelines.Universal.2D.Runtime`, for the scene's Global Light 2D.
- **Method.** One session on the RTX 5070 Ti under D3D12, with the editor in the background, alternating the shader as it was (16), the cap at 32, 16, 32, then the narrower clearing at 16 and at 32 with 16 between them, and each of those once more. Each shader was copied in from files kept outside the project and imported with an empty scene open; each area was opened on its own, and every render came from a later editor update than the import or the build. Frames are `MeasureCost`, the best of three runs of 20 frames, at 7680 x 4320 zoom 2 / 7680 x 4320 zoom 3 / 1920 x 1080 zoom 3, in ms. With the chunks hidden a frame takes 0.84 to 0.95 / 0.82 to 0.90 / 0.66 to 0.73 ms in the areas and 0.77 to 0.85 / 0.77 to 0.81 / 0.61 to 0.65 ms in the stress scene, whatever the shader. Six runs whose time with the chunks hidden rose above about 0.9 ms were repeated, and their repeats, within 5 % of the first run, replace them. Runs of the same shader differ by 2 to 3 % at the median and by at most 7 %.
- **The cap at 32 against 16**, the mean of the alternated runs (three at 16, two at 32), ms per frame at the three sizes:

  | | crisp, 16 | crisp, 32 | soft, 16 | soft, 32 | 32 against 16, 8K zoom 3 |
  |---|---|---|---|---|---|
  | the Clearing | 1.56 / 2.35 / 0.99 | 2.85 / 5.35 / 1.47 | 1.71 / 2.63 / 1.09 | 5.62 / 11.50 / 2.56 | x2.3 crisp, x4.4 soft |
  | the Village Forest | 3.50 / 4.90 / 1.02 | 9.02 / 13.15 / 1.48 | 3.76 / 5.20 / 1.08 | 19.64 / 29.32 / 2.56 | x2.7 crisp, x5.6 soft |
  | the Lakeside | 2.75 / 4.84 / 1.10 | 5.15 / 10.30 / 1.54 | 2.93 / 5.21 / 1.15 | 11.34 / 23.88 / 2.81 | x2.1 crisp, x4.6 soft |
  | the Snowfield | 2.02 / 3.38 / 0.93 | 4.64 / 9.25 / 1.49 | 2.08 / 3.46 / 0.95 | 9.45 / 19.99 / 2.48 | x2.7 crisp, x5.8 soft |
  | the Coast | 3.38 / 6.35 / 1.09 | 6.41 / 13.04 / 1.58 | 5.95 / 11.59 / 1.51 | 9.30 / 19.60 / 2.10 | x2.1 crisp, x1.7 soft |
  | the Lava Fields | 2.20 / 3.80 / 1.03 | 5.14 / 10.43 / 1.62 | 2.19 / 3.62 / 0.96 | 10.29 / 21.65 / 2.50 | x2.7 crisp, x6.0 soft |
  | the stress scene (16) | 3.92 / 7.21 / 1.08 | 6.61 / 12.91 / 1.46 | 4.22 / 7.82 / 1.15 | 16.11 / 33.63 / 2.81 | x1.8 crisp, x4.3 soft |

  Every area and the stress scene draw slower at 32, in both looks and at every size, far beyond the noise: 1.35 to 2.6 times at 1080p. The Coast's soft look grows least because it is already slow at 16, 1.8 times its crisp look: it is the only area running the soft variant with raised ground, the variant with the most temps (172).
- **Byte for byte.** The captures of the six areas and the stress scene (both looks at 1.5 s and 2.75 s and the debug view, 42 files) and their blob checks are the same, byte for byte, at 16, at 32, and with the narrower clearing at 16 and at 32. The areas' blob checks are those of TerrainTypes.md's Phase 12.
- **The debug table at 32.** `TB_DEBUG_COLOURS[TB_MAX_LAYERS]` with sixteen colours does not compile at 32 (FXC: initializer does not match type), so the shader measured at 32 declared the table with its sixteen entries and clamped the layer to 15, which leaves the debug code as it was. Phase 3 gives the table 32 entries.
- **Compile time.** `CompileVariant` for Sprite Lit's four fragment variants (crisp, soft, crisp with raised ground, soft with raised ground), D3D, with an empty scene open, all four together: 27.3, 26.7 and 30.1 s at 16, 27.9 and 31.0 s at 32, 29.3 and 28.0 s with the narrower clearing at 16, 30.0 and 27.3 s with it at 32. One variant takes 5 to 10 s whatever the cap, within the noise. The `ShaderCompile` test took 95.3, 91.4 and 93.8 s at 16 (93.5 on average), 98.8 and 106.3 s at 32 (102.5, about a tenth more) and 92.9 s with the narrower clearing at 16.
- **The compiled code**, the DXBC `CompileVariant` returns, disassembled, Sprite Lit's fragment stage, crisp / soft / crisp with raised ground / soft with raised ground. Each indexable temp is declared four components wide and used in x only. Code compiled twice from the same files is the same byte for byte.

  | | bytes | dcl_temps | dcl_indexableTemp | instructions |
  |---|---|---|---|---|
  | 16 | 115822 / 102038 / 138018 / 117198 | 106 / 166 / 111 / 172 | x0 to x6 [16], x7 to x15 [17] | 4328 / 3770 / 5192 / 4340 |
  | 32 | 122734 / 108950 / 144930 / 124110 | 114 / 174 / 119 / 180 | x0 to x6 [32], x7 to x15 [33] | 4616 / 4058 / 5480 / 4628 |
  | narrower clearing, 16 | 115302 / 101518 / 137498 / 116678 | 106 / 166 / 111 / 172 | as at 16 | 4310 / 3752 / 5174 / 4322 |
  | narrower clearing, 32 | 121446 / 107662 / 143642 / 122822 | 114 / 174 / 119 / 180 | as at 32 | 4566 / 4008 / 5430 / 4578 |

- **Why the cap costs every pixel.** `d` and `w` are passed by value, and FXC gives every function they are passed to a copy of its own: seven arrays for `d` (x0 to x6) and nine for `w` (x7 to x15). It reads each array once into registers and writes it, whole, into each copy where the function is inlined: `d`, filled by `TBDistances`, into two copies once per pixel, into one on every step of the loop that finds the opaque start layer, and into three for each layer drawn (for `TBNearBridgeEdge`, `TBVisible` and `TBPairWeights`); `w`, cleared and built by `TBPairWeights` for each layer drawn, into eight (for `TBGetEdge`, `TBLayer` and the functions it calls). Each element of the cap is therefore 18 moves in the code (`d`: its clearing, the read and six copies; `w`: its clearing, the read and eight copies), which makes the whole of the 288 instructions from 16 to 32, and the eight extra temps are the registers that hold the 32 more values on their way. Every copy is as long as the cap, whatever the area lists or paints, so every area pays for it. The soft variants, which already use 166 to 172 temps against 106 to 111 in crisp, slow down most; whether the driver then keeps the arrays in registers or spills them to memory does not show in the DXBC.
- **The narrower clearing** (`d` cleared up to the area's layers and `w` up to the layer drawn, both in loops; FXC gives no warnings at 16 or 32) removes only the two clearing runs, 18 instructions at 16 and 50 at 32, not the copies, which still move the whole declared arrays, and leaves the temps as they were. Against the shader as it was, mean of two runs each:

  | | crisp, narrower 16 | soft, narrower 16 | against 16, 8K zoom 3 | crisp, narrower 32 | soft, narrower 32 | against 32, 8K zoom 3 |
  |---|---|---|---|---|---|---|
  | the Clearing | 1.60 / 2.44 / 1.02 | 1.85 / 2.90 / 1.15 | +4 % crisp, +10 % soft | 2.75 / 5.02 / 1.44 | 6.23 / 12.92 / 2.82 | -6 % crisp, +12 % soft |
  | the Village Forest | 3.62 / 5.02 / 1.04 | 4.28 / 6.03 / 1.12 | +2 % crisp, +16 % soft | 8.73 / 12.75 / 1.51 | 22.73 / 33.44 / 2.93 | -3 % crisp, +14 % soft |
  | the Lakeside | 3.00 / 5.31 / 1.17 | 3.29 / 5.92 / 1.21 | +10 % crisp, +14 % soft | 5.36 / 10.72 / 1.60 | 13.01 / 27.07 / 3.10 | +4 % crisp, +13 % soft |
  | the Snowfield | 2.06 / 3.44 / 0.95 | 2.46 / 4.11 / 1.05 | +2 % crisp, +19 % soft | 4.22 / 8.44 / 1.42 | 10.39 / 21.39 / 2.69 | -9 % crisp, +7 % soft |
  | the Coast | 3.78 / 7.28 / 1.17 | 4.00 / 7.63 / 1.19 | +15 % crisp, -34 % soft | 6.22 / 12.70 / 1.55 | 10.53 / 22.07 / 2.26 | -3 % crisp, +13 % soft |
  | the Lava Fields | 2.29 / 3.71 / 1.03 | 2.61 / 4.61 / 1.14 | -2 % crisp, +27 % soft | 4.70 / 9.46 / 1.59 | 11.41 / 23.39 / 2.75 | -9 % crisp, +8 % soft |
  | the stress scene (16) | 4.29 / 7.84 / 1.14 | 4.67 / 8.66 / 1.20 | +9 % crisp, +11 % soft | 7.50 / 14.54 / 1.60 | 18.91 / 38.86 / 3.18 | +13 % crisp, +16 % soft |

  At 16 it draws 10 to 27 % slower in soft and up to 15 % slower in crisp, the Coast's soft look aside, so it does not stay; at 32 it stays within about 15 % of the plain cap, either way, so it cannot make the cap free. One look gains: the Coast's soft look, 1.8 times its crisp look at 16, comes down close to its crisp look with the narrower clearing while every other look slows down. That variant (soft with raised ground, 172 temps) seems to sit at an edge in the driver's register allocation that small changes in the code move either way (Open questions).
- **Rebuild and memory at 16**, for Phase 3 (`MeasureRebuildAndModeSwitch`, best of five; sizes read from the built renderer; switching the look 5.7 to 6.1 us everywhere). The Coast and the stress scene, the two with an ocean, also build a coast map.

  | | full rebuild | arrays | lookup texture | control map |
  |---|---|---|---|---|
  | the Clearing (5 layers) | 4.9 ms | 3.75 MiB (256 px) | 37 x 36, 20.8 KiB | 50 x 35 |
  | the Village Forest (8) | 6.4 ms | 6.00 MiB (256 px) | 37 x 81, 46.8 KiB | 96 x 65 |
  | the Lakeside (12) | 16.9 ms | 36.00 MiB (512 px) | 37 x 169, 97.7 KiB | 69 x 49 |
  | the Snowfield (6) | 6.0 ms | 18.00 MiB (512 px) | 37 x 49, 28.3 KiB | 65 x 47 |
  | the Coast (7) | 79.1 ms | 21.00 MiB (512 px) | 37 x 64, 37.0 KiB | 67 x 48 |
  | the Lava Fields (4) | 4.8 ms | 12.00 MiB (512 px) | 37 x 25, 14.5 KiB | 68 x 49 |
  | the stress scene (16) | 107.9 ms | 48.00 MiB (512 px) | 37 x 289, 167.1 KiB | 74 x 50 |

- **At the end** `TB_MAX_LAYERS` is 16 and every shader file is as committed; the narrower clearing is not kept. The renderer and shader tests pass (54, the compile test included), and no scene, template or project setting changed.

### Phase 2: a 32-bit control map

The control map, the bridge bits and the decode go to 32 bits while the cap stays at 16, so every change can be checked byte for byte against the areas as they are.

- [ ] The format checked against Unity's docs (sampling support on D3D11, D3D12 and Vulkan, and how `GetPixelData<uint>` lays out an `RGBA32` texel), with the choice recorded in Decisions.
- [ ] `TerrainControlMap`: `uint` cells, preview bits, `GetCell` and `GetShownCell`; `1u << layer`; the texture in the chosen format; `IsFormatSupported` and the renderer's error message updated for it. With the keyword, the map stays R16 for 16 layers or fewer.
- [ ] Shader: `TBCell` decodes the new texel (only in the wide variant, with the keyword).
- [ ] Bridge bits in two halves (Decisions), with the new shared column in `TerrainBlendLut` and `TerrainBlendLut.hlsl` and covered by `TheShadersLayoutDefines_MatchTheConstants`. `TerrainCoastMap` and the renderer take a `uint`.
- [ ] `TBNearBridgeEdge` builds its mask without ever shifting by 32 or more.
- [ ] Tests: every bit of a full cell, the high bytes included, round-trips through the cells, the preview and the texture; the bridge bits of a layer past 16 reach both halves of the lookup texture; the brush, coast map and renderer tests updated from `ushort`.
- [ ] Every area and the 16-layer stress scene render byte for byte as in Phase 1's baseline and draw no slower; the `ShaderCompile` test passes.

### Phase 3: 32 layers

- [ ] `MaxLayers` 32, with `TB_MAX_LAYERS` 32 or the `_TERRAIN_WIDE` keyword, as Phase 1 decided. With the keyword: declared in the graph as local and multi-compile like `_TERRAIN_SOFT`, set in the renderer's `ApplyMode`, and covered by `TerrainBlendShaderTests` (declared by the lit pass, compiled in every combination).
- [ ] Debug view: 16 more colours (see Decisions), plus `TerrainBlendProbe`'s `DebugColours`, `DebugClass` and `LayerNames` for 32.
- [ ] Checks and messages: `TerrainBlendChecks`, `AreaGroundSetup` (writer and Convert Ground) and `AreaGenerator`'s docs allow 32.
- [ ] Tests: a 32-layer renderer builds, draws every layer and previews strokes on the top one (bit 31); the control map and the writer accept 32 layers and refuse 33; the lookup texture has 1089 rows at 32; the debug colours stay distinct.
- [ ] Measured: the 32-layer stress scene against the 16-layer one (cost, rebuild, memory); the six areas byte for byte and no slower; the compile test's time.
- [ ] Go or no-go, with the reasons, in this phase's notes.

### Phase 4: the template, the docs and the merge

- [ ] The template gains the ocean and lava layers through `GroundLayerMigration.Migrate`, given every material of the order. The scene template asset, the area scenes and the project settings stay unchanged (check `git diff`).
- [ ] `AreaTemplateTests` without the left-out list.
- [ ] Docs: Architecture.md (control map, renderer, shader, probe, template, tests, with the Known gaps entry removed), TerrainTypes.md (the "16 layers" decision and the open question about the template), SceneGeneration.md (its mentions of 16), and TerrainBlending.md wherever it names the format.
- [ ] The full EditMode and PlayMode suites and the `ShaderCompile` test pass; the branch merges into `feature/terrain-types`.

## Open questions

- **Arrays of only the layers present.** If the cap costs every area and the keyword is turned down, `d` and `w` could hold only the few layers present at a pixel, as a short list built from `TBLayersAt`'s bits, so per-pixel cost no longer depends on the cap at all. This is a much larger shader change; do it only if needed.
- **Arrays that are not copied.** Phase 1 found the cap's cost in the copies FXC makes of `d` and `w`, a whole declared array for every function they are passed to (seven of `d`, nine of `w`). Keeping both arrays where the functions read them, as static variables of the shader rather than parameters, would leave one of each, so the cost would hardly depend on the cap any more, and the 16-layer shader might draw faster too. It is a smaller change than arrays of only the layers present, and could make one shader at 32 viable after all. It also bears on the Coast's soft look, which draws 1.8 times slower than its crisp look at 16 (Phase 1's notes).
- **A template that grows instead.** A cheaper route to the same goal for the template: the Terrain Brush adds a missing material's layer at its place in the order (`GroundLayerMigration.Migrate` can already do this), so the template never has to hold every material. Worth doing anyway if this experiment is dropped.
