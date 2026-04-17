# Grass Surface Cover TODO

This note captures the next implementation slice for the isometric 2.5D idea:
keep soil/block geometry as world truth, and render grass distribution as a
separate surface/detail layer.

## Working Decision

- Base block remains the gameplay truth: `dirt`, `stone`, `sand`, `water`.
- Grass should not be encoded only as another full cube material when the goal
  is patchy top-surface distribution.
- Prefer a layered model:
  - `BaseBlock`: collision, hardness, dig/build truth.
  - `SurfaceCover`: grass, snow, ash, moss, sand cover.
  - `DetailFoliage`: tall grass / flowers / sparse decorative sprites.

## Why This Fits The Current Repo

- World truth already lives in `ChunkData.TerrainIds`.
- Weather accumulation already proves the "base truth + surface overlay" model:
  `SnowDepth`, `SandDepth`, `Wetness`, `IceDepth`.
- Rendering already has a single composition entry in
  `Module/Render/IsometricVoxelRenderer.cs`, so a new overlay pass can stay
  local to the renderer first.

## MVP TODO

- [ ] Confirm the first target scope:
  renderer-only fake grass, or real persisted grass cover data.
- [ ] Keep current terrain gameplay semantics unchanged for the first slice.
  Do not replace existing `grass_block` usage in generators yet.
- [ ] Add a lightweight per-cell surface-cover representation modeled after
  weather accumulation.
  Suggested first shape:
  - `byte[] GrassCover`
  - value meaning: `0 = none`, `1..255 = density/intensity`
- [ ] Save/load the new cover array beside existing chunk surface arrays.
- [ ] Generate grass cover only on exposed surface cells that are valid for
  grass growth.
  First-pass heuristic:
  - base terrain is `dirt` or `grass_block`
  - not underwater
  - not blocked by solid top cover
  - density sampled from stable world noise
- [ ] Add a top-face grass overlay pass in
  `Module/Render/IsometricVoxelRenderer.cs`.
  Rendering rule:
  - keep soil on side faces
  - blend/overlay grass only on the top face
  - support a few deterministic variants from world position seed
- [ ] Keep tall grass out of core terrain truth.
  If needed, add a later sparse detail pass rather than turning each blade into
  an entity.
- [ ] Add debug visibility/tuning hooks.
  Minimum useful hooks:
  - show grass density in editor/debug readout
  - force grass overlay on/off
  - tune density threshold / variant count

## Sequencing

1. Data scaffold in `ChunkData`, save snapshot, save module.
2. Generator writes deterministic cover density.
3. Renderer draws top-face grass overlay only.
4. Debug toggle and quick visual validation.
5. Decide whether tall grass should become a separate decorative pass.

## Non-Goals For The First Slice

- Full biome rewrite.
- Replacing all existing `grass_block` / `grass` terrain IDs.
- Making foliage a dense entity layer.
- Solving every surface effect under one abstraction in the same commit.

## Acceptance Check

- Side faces still look like soil/earth instead of green cubes.
- Grass distribution looks patchy instead of tile-repeated.
- Existing movement, digging, hardness, and terrain logic stay stable.
- Save/load remains compatible for worlds without grass cover data.
