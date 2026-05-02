# Data Layout

`Data/` root keeps authored content presets and resource mappings that define gameplay data and rendering registration.

Files that stay in `Data/` root:
- Content presets such as `actors.json`, `items.json`, `terrains.json`, and `Conversations/*.conversation.json`
- Resource mapping files such as `tile_mapping.json` and `entity_render.json`
- Any schema-shaped data that is part of the game's authored content library

`Data/Config/` is only for runtime tuning data that designers or programmers are expected to adjust frequently.

Files that belong in `Data/Config/`:
- Player and AI vision tuning
- Runtime world loading and simulation knobs
- Auto-test and benchmark knobs
- Map generator numeric parameters

Files that do not belong in `Data/Config/`:
- Saves or caches
- Content preset libraries
- Rendering registries or tileset mappings
- Structural constants such as chunk size, schema versions, or UI grid dimensions

Config files are loaded once during startup. Edit JSON and restart the game to apply changes.
