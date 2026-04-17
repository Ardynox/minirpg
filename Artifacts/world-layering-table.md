# World Layering Table

This note turns the "base truth + surface appearance" idea into a reusable
design table for the current isometric 2.5D world.

## Core Rule

- `Truth/Base`: owns gameplay truth such as collision, movement, hardness,
  dig/build result, liquid body, heat hazard, save truth.
- `Surface`: owns top-surface appearance and shallow state that should not
  explode terrain IDs.
- `Detail`: owns sparse decorative content that can often be culled or toggled.
- `FX`: owns short-lived or fully derived motion/effects that usually should
  not become saved world truth.

## Layering Table

| Domain | Truth/Base | Surface | Detail | FX | First Save Rule |
| --- | --- | --- | --- | --- | --- |
| Soil ground | `dirt` / `grass_block` / `sand` / `gravel` as pathing and hardness truth | grass cover, mud, snow, ash, wet gloss | grass clumps, flowers, pebbles | wind sway, footprints, dust puffs | save `Surface` if it affects growth, burning, trampling |
| Water | `water` as liquid body truth | calm, ripple, flow, shore foam, swamp film, ice sheen | reeds, lily pads, floating debris | rain rings, splashes, wave shimmer | base water must save; surface can start derived |
| Stone | `stone` / `wall_stone` / `mountain` as hardness and blocking truth | moss, dampness, soot, ore exposure, cracks | loose rubble, moss tufts | dripping, spark hits, glow pulse | save only if surface state is gameplay-relevant |
| Sand | `sand` as traversal and dig truth | dry ripple, wet sand, crust, wind-streak | shells, drift clutter, dune grass | drifting sand, edge crumble | prefer derived surface first |
| Swamp / marsh | `swamp` as move-cost and terrain truth | algae film, oily sheen, shallow scum, wet gloss | reeds, rot clusters, bugs | bubbles, gnats, ooze ripple | save surface only if it changes harvesting or danger |
| Farmland | `farmland` / tilled soil as farming truth | dry, wet, seeded, sprout, stubble, frost cover | crop rows, weeds, sticks | watering splash, pollination motes | save surface because growth depends on it |
| Road / path | dirt road / stone road as traversal truth | dust, mud, puddles, snow cover, wear | roadside weeds, stones, trash | wheel splash, dust kick, dripping | prefer derived surface unless wear becomes gameplay |
| Snow / ice zone | base terrain stays dirt/stone/water underneath | snow depth, compacted snow, slush, ice crust | snow piles, icicles | snowfall, melt drip, crunch spray | existing weather-style accumulation is the model |
| Burnt ground | base terrain remains original unless truly transformed | scorch, ash, ember residue, cooled crust | charcoal chunks | smoke wisps, ember pop, heat haze | save if fire aftermath changes spreading or farming |
| Lava | `lava` as hazard/liquid truth | crust, bright seam, cooling skin | obsidian fragments | heat shimmer, burst, sparks | save base lava; crust can be mixed derived/saved |
| Roof / wall top | roof/wall material keeps structure truth | snow, moss, rain gloss, leaf litter | vines, roof clutter | drip lines, flutter | mostly derived unless decay is gameplay |

## Practical Classification Rules

- Put a state into `Truth/Base` when AI, movement, combat, building, digging,
  or save compatibility depend on it directly.
- Put a state into `Surface` when it mainly changes the top face look and may
  optionally influence a small number of systems.
- Put a state into `Detail` when it is sparse, decorative, and cheap to ignore
  at distance.
- Put a state into `FX` when it is temporary, weather-driven, impact-driven, or
  easy to rebuild from other data.

## Useful Reuse Pattern

- One base terrain can support many surface moods.
- One surface system can be shared across many base terrains.
- Detail and FX should read from base/surface state, not replace them.

Example:

- `water` + `shore` surface + `reeds` detail + `rain rings` FX
- `dirt` + `grass cover` surface + `flowers` detail + `wind sway` FX
- `stone` + `wet moss` surface + `rubble` detail + `drip` FX

## Repo-Oriented Mapping

- Current base truth lives mainly in `ChunkData.TerrainIds`.
- Existing weather accumulation already behaves like a surface layer:
  `SnowDepth`, `SandDepth`, `Wetness`, `IceDepth`.
- `IsometricVoxelRenderer` is the current safest place to prototype visual
  surface passes before widening gameplay rules.

## Suggested Implementation Priority

1. Grass-on-soil surface pass.
2. Water top-surface pass.
3. Reuse the same pattern for wet stone / muddy road / ash cover.
4. Add sparse detail layers only after the surface read is convincing.

## Guardrails

- Do not create a new terrain ID for every visual combination.
- Do not move decorative state into dense entity lists unless interaction truly
  needs it.
- Do not let side faces inherit top-surface material blindly.
- Do not require save data for effects that can be rebuilt cheaply from world
  truth, weather, neighbors, and stable seeds.
