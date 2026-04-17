using System;
using MiniRPG.Core.World.Noise;

namespace MiniRPG.Core.World.Generators;

/// <summary>
/// DF 风格多 Pass 世界生成器：Geology → Biome → Cave → Vegetation → Structure。
/// 使用 Perlin 噪声在世界坐标采样，确保跨 chunk 连续性。
///
/// Z 约定：Z=0 地表基准，Z>0 地下（越深 Z 越大），Z&lt;0 地上（空气/山顶）。
///
/// 地层结构（以 surfaceZ 为参考）：
///   Z &lt; surfaceZ     — 空气层（或水面以下填水）
///   Z == surfaceZ    — 地表面（根据 biome 决定：grass/sand/snow/swamp/mountain）
///   depth 1~3        — 表土层（dirt / wall_soil）
///   depth 4~8        — 沉积岩层（stone），含煤铁铜矿脉
///   depth 9~16       — 花岗岩层（wall_granite），含金矿/水晶矿脉
///   depth 17~25      — 深层花岗岩，偶尔含黑曜石
///   depth 26+        — 基岩层（wall_obsidian）
/// </summary>
public class DwarfFortressGenerator : IMapGenerator
{
	public string Id => "dwarf_fortress";
	public string Name => "Dwarf Fortress World";

	// ── 噪声频率 ──
	private const double HeightScale = 0.018;
	private const double MoistureScale = 0.015;
	private const double TemperatureScale = 0.012;
	private const double CaveScale = 0.045;
	private const double OreScale = 0.08;
	private const double VegetationScale = 0.06;

	// ── 地表高度 ──
	private const int MaxElevation = 6;
	private const int WaterZ = 1;

	// ── 洞穴 ──
	private const double CaveThreshold = 0.38;
	private const int CaveMinDepth = 4;

	// ── 含水层 ──
	private const double AquiferThreshold = 0.42;
	private const int AquiferMinDepth = 3;
	private const int AquiferMaxDepth = 6;

	// ── 基岩 ──
	private const int BedrockDepth = 26;

	public void GenerateChunk(ChunkData chunk, int worldSeed)
	{
		var heightNoise = new PerlinNoise(worldSeed);
		var moistureNoise = new PerlinNoise(worldSeed ^ 0x1234567);
		var temperatureNoise = new PerlinNoise(worldSeed ^ 0x2BCD123);
		var caveNoise = new PerlinNoise(worldSeed ^ 0x3AFEBEE);
		var oreNoise = new PerlinNoise(worldSeed ^ 0x4EAD001);
		var aquiferNoise = new PerlinNoise(worldSeed ^ 0x5EAD002);
		var vegetationNoise = new PerlinNoise(worldSeed ^ 0x6ACE001);

		var cz = chunk.Coord.Cz;

		for (var ly = 0; ly < ChunkData.Size; ly++)
		for (var lx = 0; lx < ChunkData.Size; lx++)
		{
			var wx = chunk.Coord.Cx * ChunkData.Size + lx;
			var wy = chunk.Coord.Cy * ChunkData.Size + ly;

			// ── Pass 1: 地形高度图 ──
			var heightVal = heightNoise.FBM2D(wx * HeightScale, wy * HeightScale, 6, 0.48);
			var surfaceZ = -(int)Math.Round(heightVal * MaxElevation);

			// ── Pass 2: 气候（温度 + 湿度）→ Biome ──
			var moisture = moistureNoise.FBM2D(wx * MoistureScale, wy * MoistureScale, 4, 0.55);
			var temperature = temperatureNoise.FBM2D(wx * TemperatureScale, wy * TemperatureScale, 3, 0.5);

			// ── Pass 3: 3D 洞穴密度 ──
			var caveDensity = caveNoise.FBM3D(wx * CaveScale, wy * CaveScale, cz * CaveScale, 3, 0.5);

			// ── Pass 4: 矿脉密度 ──
			var oreDensity = oreNoise.FBM3D(wx * OreScale, wy * OreScale, cz * OreScale, 2, 0.6);

			// ── Pass 5: 含水层 ──
			var aquiferDensity = aquiferNoise.FBM2D(wx * 0.03, wy * 0.03, 3, 0.5);

			// ── Pass 6: 植被密度 ──
			var vegDensity = vegetationNoise.FBM2D(wx * VegetationScale, wy * VegetationScale, 2, 0.5);

			// ── 合成地形 ──
			var terrainId = ClassifyVoxel(
				cz, surfaceZ,
				moisture, temperature,
				caveDensity, oreDensity, aquiferDensity, vegDensity);

			chunk.SetTerrain(lx, ly, terrainId);
		}
	}

	public void PopulateChunk(ChunkData chunk, int worldSeed)
	{
		var seed = worldSeed
			^ (chunk.Coord.Cx * 19349663)
			^ (chunk.Coord.Cy * 83492791)
			^ (chunk.Coord.Cz * 41729387);
		var rng = new Random(seed);
		var cz = chunk.Coord.Cz;

		var floorId = TerrainRegistry.GetId(Terrains.Floor);
		var rubbleId = TerrainRegistry.GetId(Terrains.Rubble);
		var nestChancePercent = WorldGenerationSettingsRegistry.ScaleNestChancePercent(worldSeed, Id, 3);
		var nestSpawnInterval = WorldGenerationSettingsRegistry.ScaleNestSpawnInterval(worldSeed, Id, 60);
		var nestMaxSpawned = WorldGenerationSettingsRegistry.ScaleNestMaxSpawned(worldSeed, Id, 6);

		// 地下层放置楼梯和怪物巢穴
		if (cz > 0)
		{
			GeneratorPopulateHelper.PlaceDungeonFixtures(
				chunk, rng, floorId,
				stairDownChancePercent: 12,
				stairUpChancePercent: 8,
				allowStairUp: true,
				nestChancePercent: nestChancePercent,
				nestSpawnInterval: nestSpawnInterval,
				nestMaxSpawned: nestMaxSpawned);

			// 废墟碎石中也可能有楼梯
			GeneratorPopulateHelper.PlaceStairs(
				chunk, rng, rubbleId,
				stairDownChancePercent: 5,
				stairUpChancePercent: 3,
				allowStairUp: cz > 1);
		}

		// 地表层 (Z=0 附近) 放置通往地下的入口
		if (cz >= -1 && cz <= 1)
		{
			PlaceSurfaceFeatures(chunk, rng, cz);
		}
	}

	// ══════════════════════════════════════════════════════
	//  体素分类核心
	// ══════════════════════════════════════════════════════

	private static ushort ClassifyVoxel(
		int currentZ, int surfaceZ,
		double moisture, double temperature,
		double caveDensity, double oreDensity,
		double aquiferDensity, double vegDensity)
	{
		// ── 空气层 ──
		if (currentZ < surfaceZ)
		{
			// 水面以下的空气填水
			if (currentZ >= WaterZ)
				return TerrainRegistry.GetId(Terrains.Water);
			return TerrainRegistry.GetId(Terrains.Air);
		}

		// ── 地表面 ──
		if (currentZ == surfaceZ)
			return ClassifySurface(surfaceZ, moisture, temperature, vegDensity);

		// ── 地下 ──
		var depth = currentZ - surfaceZ;
		return ClassifyUnderground(depth, caveDensity, oreDensity, aquiferDensity);
	}

	private static ushort ClassifySurface(int surfaceZ, double moisture, double temperature, double vegDensity)
	{
		// 水面以下 → 水
		if (surfaceZ >= WaterZ)
			return TerrainRegistry.GetId(Terrains.Water);

		// 高山顶 → 山石
		if (surfaceZ <= -MaxElevation + 1)
		{
			if (temperature < -0.2)
				return TerrainRegistry.GetId(Terrains.Snow);
			return TerrainRegistry.GetId(Terrains.Mountain);
		}

		// 近水岸 → 沙滩
		if (surfaceZ >= WaterZ - 1)
		{
			if (moisture > 0.3)
				return TerrainRegistry.GetId(Terrains.Swamp);
			return TerrainRegistry.GetId(Terrains.Sand);
		}

		// ── Biome 分类（温度 × 湿度） ──

		// 寒冷区域
		if (temperature < -0.3)
		{
			if (moisture > 0.15)
				return TerrainRegistry.GetId(Terrains.Snow);
			return TerrainRegistry.GetId(Terrains.Ice);
		}

		// 炎热干燥 → 沙漠
		if (temperature > 0.3 && moisture < -0.15)
			return TerrainRegistry.GetId(Terrains.Sand);

		// 湿热 → 沼泽
		if (temperature > 0.2 && moisture > 0.35)
			return TerrainRegistry.GetId(Terrains.Swamp);

		// 温带湿润 → 森林
		if (moisture > 0.2 && vegDensity > 0.1)
			return TerrainRegistry.GetId(Terrains.Tree);

		// 真菌（深色森林变种）
		if (moisture > 0.2 && vegDensity > -0.1 && temperature < 0)
			return TerrainRegistry.GetId(Terrains.Fungus);

		// 温带干燥 → 砾石
		if (moisture < -0.2)
			return TerrainRegistry.GetId(Terrains.Gravel);

		// 默认 → 草地
		return TerrainRegistry.GetId(Terrains.GrassBlock);
	}

	private static ushort ClassifyUnderground(int depth, double caveDensity, double oreDensity, double aquiferDensity)
	{
		// ── 基岩层 ──
		if (depth >= BedrockDepth)
			return TerrainRegistry.GetId(Terrains.WallObsidian);

		// ── 洞穴挖空 ──
		if (depth >= CaveMinDepth && caveDensity > CaveThreshold)
		{
			// 含水层洞穴 → 水
			if (depth is >= AquiferMinDepth and <= AquiferMaxDepth && aquiferDensity > AquiferThreshold)
				return TerrainRegistry.GetId(Terrains.Water);

			// 深层洞穴偶尔有岩浆
			if (depth > 20 && caveDensity > 0.55)
				return TerrainRegistry.GetId(Terrains.Lava);

			return TerrainRegistry.GetId(Terrains.Floor);
		}

		// ── 表土层（depth 1~3）──
		if (depth <= 3)
		{
			// 含水层 → 水
			if (depth >= AquiferMinDepth && aquiferDensity > AquiferThreshold)
				return TerrainRegistry.GetId(Terrains.Water);

			return depth == 1
				? TerrainRegistry.GetId(Terrains.Dirt)
				: TerrainRegistry.GetId(Terrains.WallSoil);
		}

		// ── 沉积岩层（depth 4~8）——含矿脉 ──
		if (depth <= 8)
		{
			if (oreDensity > 0.55)
				return TerrainRegistry.GetId(Terrains.OreCoal);
			if (oreDensity > 0.50)
				return TerrainRegistry.GetId(Terrains.OreIron);
			if (oreDensity > 0.45)
				return TerrainRegistry.GetId(Terrains.OreCopper);
			return TerrainRegistry.GetId(Terrains.Stone);
		}

		// ── 花岗岩层（depth 9~16）——含稀有矿脉 ──
		if (depth <= 16)
		{
			if (oreDensity > 0.58)
				return TerrainRegistry.GetId(Terrains.OreGold);
			if (oreDensity > 0.52)
				return TerrainRegistry.GetId(Terrains.OreCrystal);
			if (oreDensity > 0.48)
				return TerrainRegistry.GetId(Terrains.OreIron);
			return TerrainRegistry.GetId(Terrains.WallGranite);
		}

		// ── 深层花岗岩（depth 17~25）──
		if (oreDensity > 0.60)
			return TerrainRegistry.GetId(Terrains.CrystalVein);
		if (oreDensity > 0.55)
			return TerrainRegistry.GetId(Terrains.WallIron);

		return TerrainRegistry.GetId(Terrains.WallGranite);
	}

	// ══════════════════════════════════════════════════════
	//  地表设施
	// ══════════════════════════════════════════════════════

	private static void PlaceSurfaceFeatures(ChunkData chunk, Random rng, int cz)
	{
		var grassId = TerrainRegistry.GetId(Terrains.GrassBlock);
		var grassFlatId = TerrainRegistry.GetId(Terrains.Grass);
		var sandId = TerrainRegistry.GetId(Terrains.Sand);
		var gravelId = TerrainRegistry.GetId(Terrains.Gravel);

		var stairDownPlaced = false;
		for (var ly = 0; ly < ChunkData.Size; ly++)
		for (var lx = 0; lx < ChunkData.Size; lx++)
		{
			var tid = chunk.GetTerrainId(lx, ly);
			if (tid != grassId && tid != grassFlatId && tid != sandId && tid != gravelId) continue;
			if (chunk.GetEntities(lx, ly).Count > 0) continue;

			// 稀疏地表入口（通往地下洞穴）
			if (!stairDownPlaced && rng.Next(100) < 3)
			{
				chunk.PushEntity(lx, ly, new CellEntity
				{
					Type = CellEntityType.Fixture,
					Glyph = ">",
					EntityId = Entities.StairDown,
				});
				stairDownPlaced = true;
			}
			// 营火
			else if (rng.Next(1000) < 2)
			{
				chunk.PushEntity(lx, ly, new CellEntity
				{
					Type = CellEntityType.Fixture,
					Glyph = "*",
					EntityId = Entities.Campfire,
				});
			}
		}
	}
}
