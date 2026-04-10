using System;
using System.Collections.Generic;

namespace MiniRPG.Core.World.Generators;

/// <summary>
/// BSP 二叉空间分割生成器：
/// 递归将空间二分为叶节点，每个叶节点内生成一个房间，
/// 相邻叶节点之间用走廊连接。
/// 生成更规整、对称的房间布局，适合要塞/矿井。
/// </summary>
public class BSPGenerator : IMapGenerator
{
	public string Id => "bsp";
	public string Name => "BSP 空间分割";

	public void GenerateChunk(ChunkData chunk, int worldSeed)
	{
		var config = GameConfig.Generation.Bsp;
		var cz = chunk.Coord.Cz;

		// 地表附近：3D 体积填充
		if (cz >= -4 && cz <= 4)
		{
			SurfaceGenerator.Generate(chunk, worldSeed);
			if (cz > 0)
				CarveBSP(chunk, worldSeed, config);
			return;
		}

		// 高空：空气
		if (cz < -4)
		{
			chunk.Fill(TerrainRegistry.GetId(Terrains.Air));
			return;
		}

		// 深层地下
		var wallId = GetWallForDepth(cz);
		chunk.Fill(wallId);
		CarveBSP(chunk, worldSeed, config);
	}

	private void CarveBSP(ChunkData chunk, int worldSeed, BspGenerationConfig config)
	{
		var floorId = TerrainRegistry.GetId(Terrains.Floor);
		var seed = HashSeed(worldSeed, chunk.Coord);
		var rng = new Random(seed);

		var root = new BSPNode(1, 1, ChunkData.Size - 2, ChunkData.Size - 2);
		Split(root, rng, 0, config);

		var leaves = new List<BSPNode>();
		CollectLeaves(root, leaves);

		foreach (var leaf in leaves)
		{
			var rw = rng.Next(config.MinRoomSize, leaf.W - 1);
			var rh = rng.Next(config.MinRoomSize, leaf.H - 1);
			var rx = leaf.X + rng.Next(0, leaf.W - rw);
			var ry = leaf.Y + rng.Next(0, leaf.H - rh);
			leaf.RoomX = rx; leaf.RoomY = ry;
			leaf.RoomW = rw; leaf.RoomH = rh;

			for (var dy = 0; dy < rh; dy++)
			for (var dx = 0; dx < rw; dx++)
			{
				var px = rx + dx;
				var py = ry + dy;
				if (px >= 0 && px < ChunkData.Size && py >= 0 && py < ChunkData.Size)
					chunk.SetTerrain(px, py, floorId);
			}
		}

		ConnectSiblings(root, chunk, rng, floorId);
	}

	public void PopulateChunk(ChunkData chunk, int worldSeed)
	{
		if (chunk.Coord.Cz <= 0) return;

		var config = GameConfig.Generation.Bsp;
		var seed = HashSeed(worldSeed, chunk.Coord) ^ unchecked((int)0xB5B5B5B5);
		var rng = new Random(seed);
		var floorId = TerrainRegistry.GetId(Terrains.Floor);
		GeneratorPopulateHelper.PlaceDungeonFixtures(
			chunk,
			rng,
			floorId,
			config.StairDownChancePercent,
			config.StairUpChancePercent,
			allowStairUp: true,
			config.NestChancePercent,
			config.NestSpawnInterval,
			config.NestMaxSpawned);
	}

	private static void Split(BSPNode node, Random rng, int depth, BspGenerationConfig config)
	{
		if (depth >= config.MaxDepth || node.W < config.MinLeafSize * 2 && node.H < config.MinLeafSize * 2) return;

		var splitH = node.W >= node.H ? rng.Next(2) == 0 : true;
		if (node.W < config.MinLeafSize * 2) splitH = true;
		if (node.H < config.MinLeafSize * 2) splitH = false;

		if (splitH)
		{
			if (node.H < config.MinLeafSize * 2) return;
			var split = rng.Next(config.MinLeafSize, node.H - config.MinLeafSize + 1);
			node.Left = new BSPNode(node.X, node.Y, node.W, split);
			node.Right = new BSPNode(node.X, node.Y + split, node.W, node.H - split);
		}
		else
		{
			if (node.W < config.MinLeafSize * 2) return;
			var split = rng.Next(config.MinLeafSize, node.W - config.MinLeafSize + 1);
			node.Left = new BSPNode(node.X, node.Y, split, node.H);
			node.Right = new BSPNode(node.X + split, node.Y, node.W - split, node.H);
		}

		Split(node.Left, rng, depth + 1, config);
		Split(node.Right, rng, depth + 1, config);
	}

	private static void CollectLeaves(BSPNode node, List<BSPNode> leaves)
	{
		if (node.Left == null && node.Right == null) { leaves.Add(node); return; }
		if (node.Left != null) CollectLeaves(node.Left, leaves);
		if (node.Right != null) CollectLeaves(node.Right, leaves);
	}

	private static void ConnectSiblings(BSPNode node, ChunkData chunk, Random rng, ushort floorId)
	{
		if (node.Left == null || node.Right == null) return;

		var (lx, ly) = GetRoomCenter(node.Left);
		var (rx, ry) = GetRoomCenter(node.Right);

		if (rng.Next(2) == 0)
		{
			CarveH(chunk, lx, rx, ly, floorId);
			CarveV(chunk, ly, ry, rx, floorId);
		}
		else
		{
			CarveV(chunk, ly, ry, lx, floorId);
			CarveH(chunk, lx, rx, ry, floorId);
		}

		ConnectSiblings(node.Left, chunk, rng, floorId);
		ConnectSiblings(node.Right, chunk, rng, floorId);
	}

	private static (int X, int Y) GetRoomCenter(BSPNode node)
	{
		if (node.RoomW > 0) return (node.RoomX + node.RoomW / 2, node.RoomY + node.RoomH / 2);

		if (node.Left != null) return GetRoomCenter(node.Left);
		if (node.Right != null) return GetRoomCenter(node.Right);
		return (node.X + node.W / 2, node.Y + node.H / 2);
	}

	private static void CarveH(ChunkData chunk, int x1, int x2, int y, ushort floorId)
	{
		for (var x = Math.Min(x1, x2); x <= Math.Max(x1, x2); x++)
			if (x >= 0 && x < ChunkData.Size && y >= 0 && y < ChunkData.Size)
				chunk.SetTerrain(x, y, floorId);
	}

	private static void CarveV(ChunkData chunk, int y1, int y2, int x, ushort floorId)
	{
		for (var y = Math.Min(y1, y2); y <= Math.Max(y1, y2); y++)
			if (x >= 0 && x < ChunkData.Size && y >= 0 && y < ChunkData.Size)
				chunk.SetTerrain(x, y, floorId);
	}

	private static ushort GetWallForDepth(int z)
	{
		if (z <= 3) return TerrainRegistry.GetId(Terrains.WallSoil);
		if (z <= 8) return TerrainRegistry.GetId(Terrains.WallStone);
		if (z <= 15) return TerrainRegistry.GetId(Terrains.WallGranite);
		return TerrainRegistry.GetId(Terrains.WallObsidian);
	}

	private static int HashSeed(int worldSeed, ChunkCoord c) =>
		unchecked(worldSeed * 73856093 ^ c.Cx * 19349663 ^ c.Cy * 83492791 ^ c.Cz * 41729387);
}

internal class BSPNode
{
	public int X, Y, W, H;
	public BSPNode? Left, Right;
	public int RoomX, RoomY, RoomW, RoomH;

	public BSPNode(int x, int y, int w, int h)
	{
		X = x; Y = y; W = w; H = h;
	}
}
