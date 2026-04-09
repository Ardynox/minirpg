Procedural voxel tile output
- tile_stone.png
- tile_dirt.png
- tile_grass_top.png
- tile_grass_side.png
- tile_atlas_voxel_basic.png (2x2 atlas order: stone, dirt, grass_top, grass_side)

Regenerate via:
dotnet run --project Tools/MonsterMapAssetGenerator/MonsterMapAssetGenerator.csproj -- generate-voxel-tiles
