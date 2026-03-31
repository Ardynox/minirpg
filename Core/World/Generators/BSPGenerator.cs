using System;
using System.Collections.Generic;
using MiniRPG.Core;

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

	private const int MinLeafSize = 6;
	private const int MinRoomSize = 3;
	private const int MaxDepth = 5;

	public void GenerateChunk(ChunkData chunk, int worldSeed)
	{
		if (chunk.Coord.Cz == 0)
		{
			SurfaceGenerator.Generate(chunk, worldSeed);
			return;
		}

		var wallId = GetWallForDepth(chunk.Coord.Cz);
		var floorId = TerrainRegistry.GetId(Terrains.Floor);
		chunk.Fill(wallId);

		if (chunk.Coord.Cz < 0) { chunk.Fill(floorId); return; }

		var seed = HashSeed(worldSeed, chunk.Coord);
		var rng = new Random(seed);

		var root = new BSPNode(1, 1, ChunkData.Size - 2, ChunkData.Size - 2);
		Split(root, rng, 0);

		var leaves = new List<BSPNode>();
		CollectLeaves(root, leaves);

		foreach (var leaf in leaves)
		{
			var rw = rng.Next(MinRoomSize, leaf.W - 1);
			var rh = rng.Next(MinRoomSize, leaf.H - 1);
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

		var seed = HashSeed(worldSeed, chunk.Coord) ^ unchecked((int)0xB5B5B5B5);
		var rng = new Random(seed);
		var floorId = TerrainRegistry.GetId(Terrains.Floor);
		var downPlaced = false;
		var upPlaced = false;

		for (var ly = 0; ly < ChunkData.Size; ly++)
		for (var lx = 0; lx < ChunkData.Size; lx++)
		{
			if (chunk.GetTerrainId(lx, ly) != floorId) continue;
			if (chunk.GetEntities(lx, ly).Count > 0) continue;

			if (!downPlaced && rng.Next(100) < 2)
			{
				chunk.PushEntity(lx, ly, new CellEntity
					{ Type = CellEntityType.Fixture, Glyph = ">", EntityId = Entities.StairDown });
				downPlaced = true;
			}
			else if (!upPlaced && rng.Next(100) < 2)
			{
				chunk.PushEntity(lx, ly, new CellEntity
					{ Type = CellEntityType.Fixture, Glyph = "<", EntityId = Entities.StairUp });
				upPlaced = true;
			}
			else if (rng.Next(100) < 1)
			{
				chunk.PushEntity(lx, ly, new CellEntity
					{ Type = CellEntityType.Fixture, Glyph = "N", EntityId = Entities.Nest });
				chunk.Nests.Add(new NestData
				{
					X = chunk.Coord.Cx * ChunkData.Size + lx,
					Y = chunk.Coord.Cy * ChunkData.Size + ly,
					SpawnInterval = 6, MaxSpawned = 3,
				});
			}
		}
	}

	private static void Split(BSPNode node, Random rng, int depth)
	{
		if (depth >= MaxDepth || node.W < MinLeafSize * 2 && node.H < MinLeafSize * 2) return;

		var splitH = node.W >= node.H ? rng.Next(2) == 0 : true;
		if (node.W < MinLeafSize * 2) splitH = true;
		if (node.H < MinLeafSize * 2) splitH = false;

		if (splitH)
		{
			if (node.H < MinLeafSize * 2) return;
			var split = rng.Next(MinLeafSize, node.H - MinLeafSize + 1);
			node.Left = new BSPNode(node.X, node.Y, node.W, split);
			node.Right = new BSPNode(node.X, node.Y + split, node.W, node.H - split);
		}
		else
		{
			if (node.W < MinLeafSize * 2) return;
			var split = rng.Next(MinLeafSize, node.W - MinLeafSize + 1);
			node.Left = new BSPNode(node.X, node.Y, split, node.H);
			node.Right = new BSPNode(node.X + split, node.Y, node.W - split, node.H);
		}

		Split(node.Left, rng, depth + 1);
		Split(node.Right, rng, depth + 1);
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
