# Grass And Water Surface Layer TODO

This note captures the next implementation slice for the isometric 2.5D idea:
keep block/liquid interior as world truth, then render grass distribution and
water-surface variation as separate surface/detail layers.

## Working Decision

- Base block remains the gameplay truth: `dirt`, `stone`, `sand`, `water`.
- Grass should not be encoded only as another full cube material when the goal
  is patchy top-surface distribution.
- Water should also keep a stable interior truth while surface appearance is
  allowed to vary by state and context.
- Prefer a layered model:
  - `BaseBlock`: collision, hardness, dig/build truth.
  - `SurfaceCover`: grass, snow, ash, moss, sand cover.
  - `LiquidSurface`: calm water, ripple, foam, shore break, swamp film, ice
    sheen.
  - `DetailFoliage`: tall grass / flowers / sparse decorative sprites.

## Why This Fits The Current Repo

- World truth already lives in `ChunkData.TerrainIds`.
- Weather accumulation already proves the "base truth + surface overlay" model:
  `SnowDepth`, `SandDepth`, `Wetness`, `IceDepth`.
- `WeatherSurfaceState` already distinguishes `BaseTerrain` from the effective
  visual result, which is close to the direction we want for water surfaces.
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
- [ ] Add a lightweight per-cell water-surface representation that keeps water
  interior truth separate from appearance.
  Suggested first shape:
  - `byte[] WaterSurfaceKind`
  - `byte[] WaterSurfaceStrength`
  - kind examples: `calm`, `ripple`, `flow`, `foam`, `shore`, `swamp`
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
- [ ] Add a water-surface pass in `Module/Render/IsometricVoxelRenderer.cs`.
  Rendering rule:
  - keep the interior/liquid body represented by base `water`
  - vary only the top surface appearance
  - derive calm/ripple/foam/shore variants from depth, neighbors, weather,
    and deterministic world position seed
- [ ] Keep shoreline logic cheap in the first slice.
  First-pass heuristic:
  - open water interior -> calm/ripple
  - water next to land edge -> shore/foam
  - storm/rain -> stronger ripple
  - swamp biome -> darker film variant
- [ ] Keep tall grass out of core terrain truth.
  If needed, add a later sparse detail pass rather than turning each blade into
  an entity.
- [ ] Add debug visibility/tuning hooks.
  Minimum useful hooks:
  - show grass density in editor/debug readout
  - show water surface kind/strength in editor/debug readout
  - force grass overlay on/off
  - force water-surface overlay on/off
  - tune density threshold / variant count

## Sequencing

1. Data scaffold in `ChunkData`, save snapshot, save module.
2. Generator or surface sampler writes deterministic grass cover density.
3. Renderer draws top-face grass overlay only.
4. Add water-surface state sampling and top-surface water pass.
5. Debug toggle and quick visual validation.
6. Decide whether tall grass should become a separate decorative pass.

## Non-Goals For The First Slice

- Full biome rewrite.
- Replacing all existing `grass_block` / `grass` terrain IDs.
- Replacing base `water` terrain truth with many gameplay-facing water IDs.
- Making foliage a dense entity layer.
- Solving every surface effect under one abstraction in the same commit.

## Acceptance Check

- Side faces still look like soil/earth instead of green cubes.
- Grass distribution looks patchy instead of tile-repeated.
- Water reads as "same body, different surface mood" instead of different full
  cube materials.
- Shorelines and storms visibly affect the water top surface without changing
  movement/collision truth.
- Existing movement, digging, hardness, and terrain logic stay stable.
- Save/load remains compatible for worlds without grass cover data.
