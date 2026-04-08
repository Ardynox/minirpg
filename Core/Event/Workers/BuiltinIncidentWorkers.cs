using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.AI;
using MiniRPG.Core.Data;

namespace MiniRPG.Core.Event.Workers;

/// <summary>
/// 袭击事件：在玩家附近生成一波敌人。
/// 参数：
///   - "templateId": 敌人模板 ID（默认从 def.Params 读取）
///   - "count": 敌人数量（默认根据威胁点数计算）
///   - "radius": 生成半径（默认 8-12）
/// </summary>
public class RaidIncidentWorker : IIncidentWorker
{
	public bool CanFire(GameState state, IncidentDef def)
	{
		// 需要有活着的玩家
		var player = PartyModule.GetActiveActor(state);
		return player != null && player.Limbs.Count > 0;
	}

	public List<GameEvent> Execute(GameState state, IncidentDef def, Dictionary<string, string> runtimeParams)
	{
		var events = new List<GameEvent>();
		var player = PartyModule.GetActiveActor(state);
		if (player == null)
			return events;

		var rng = new Random(state.RngSeed + state.Turn + def.Id.GetHashCode());

		// 确定敌人模板
		var templateId = runtimeParams.TryGetValue("templateId", out var tid) ? tid
			: def.Params.TryGetValue("templateId", out var dtid) ? dtid
			: "goblin";

		// 确定数量
		var count = 1;
		if (runtimeParams.TryGetValue("count", out var countStr) && int.TryParse(countStr, out var c))
			count = c;
		else if (def.ThreatPoints > 0)
			count = Math.Max(1, (int)(def.ThreatPoints / 10f));

		// 确定生成半径
		var minRadius = 8;
		var maxRadius = 12;

		for (var i = 0; i < count; i++)
		{
			var (spawnX, spawnY) = FindSpawnPosition(state, player, rng, minRadius, maxRadius);
			if (spawnX == int.MinValue)
				continue;

			var enemy = PresetDB.SpawnActor(templateId, $"raid_{state.Turn}_{i}");
			if (enemy == null)
				continue;

			enemy.X = spawnX;
			enemy.Y = spawnY;
			enemy.Z = player.Z;
			enemy.Faction = Factions.Hostile;
			enemy.BrainId = "simple";
			state.Actors[enemy.Id] = enemy;

			events.Add(new GameEvent("incident_spawn")
			{
				TargetX = spawnX,
				TargetY = spawnY,
				TargetActorName = enemy.DisplayName,
				InteractionDefId = def.Id,
				InteractionName = def.Name,
			});
		}

		if (events.Count > 0)
		{
			events.Insert(0, new GameEvent("incident_alert")
			{
				InteractionDefId = def.Id,
				InteractionName = def.Name,
			});
		}

		return events;
	}

	private static (int X, int Y) FindSpawnPosition(GameState state, Actor player, Random rng, int minRadius, int maxRadius)
	{
		for (var attempt = 0; attempt < 20; attempt++)
		{
			var angle = rng.NextDouble() * Math.PI * 2;
			var radius = minRadius + rng.Next(maxRadius - minRadius + 1);
			var x = player.X + (int)(Math.Cos(angle) * radius);
			var y = player.Y + (int)(Math.Sin(angle) * radius);

			if (state.World != null && state.World.IsWalkable(x, y, player.Z))
				return (x, y);
		}

		return (int.MinValue, int.MinValue);
	}
}

/// <summary>
/// 流浪者加入事件：生成一个友好 NPC，可以被招募。
/// </summary>
public class WandererJoinIncidentWorker : IIncidentWorker
{
	public bool CanFire(GameState state, IncidentDef def)
	{
		var player = PartyModule.GetActiveActor(state);
		return player != null && PartyModule.Count(state) < state.Party.MaxSize;
	}

	public List<GameEvent> Execute(GameState state, IncidentDef def, Dictionary<string, string> runtimeParams)
	{
		var events = new List<GameEvent>();
		var player = PartyModule.GetActiveActor(state);
		if (player == null)
			return events;

		var rng = new Random(state.RngSeed + state.Turn + def.Id.GetHashCode());

		var templateId = runtimeParams.TryGetValue("templateId", out var tid) ? tid
			: def.Params.TryGetValue("templateId", out var dtid) ? dtid
			: "villager";

		var (spawnX, spawnY) = FindSpawnNearPlayer(state, player, rng, 5, 8);
		if (spawnX == int.MinValue)
			return events;

		var wanderer = PresetDB.SpawnActor(templateId, $"wanderer_{state.Turn}");
		if (wanderer == null)
			return events;

		wanderer.X = spawnX;
		wanderer.Y = spawnY;
		wanderer.Z = player.Z;
		wanderer.Faction = Factions.Friendly;
		wanderer.BrainId = "simple";
		state.Actors[wanderer.Id] = wanderer;

		events.Add(new GameEvent("incident_wanderer")
		{
			TargetX = spawnX,
			TargetY = spawnY,
			TargetActorName = wanderer.DisplayName,
			InteractionDefId = def.Id,
			InteractionName = def.Name,
		});

		return events;
	}

	private static (int X, int Y) FindSpawnNearPlayer(GameState state, Actor player, Random rng, int minRadius, int maxRadius)
	{
		for (var attempt = 0; attempt < 20; attempt++)
		{
			var angle = rng.NextDouble() * Math.PI * 2;
			var radius = minRadius + rng.Next(maxRadius - minRadius + 1);
			var x = player.X + (int)(Math.Cos(angle) * radius);
			var y = player.Y + (int)(Math.Sin(angle) * radius);

			if (state.World != null && state.World.IsWalkable(x, y, player.Z))
				return (x, y);
		}

		return (int.MinValue, int.MinValue);
	}
}

/// <summary>
/// 商队到访事件：生成一个商人 NPC。
/// </summary>
public class TraderVisitIncidentWorker : IIncidentWorker
{
	public bool CanFire(GameState state, IncidentDef def)
	{
		return PartyModule.GetActiveActor(state) != null;
	}

	public List<GameEvent> Execute(GameState state, IncidentDef def, Dictionary<string, string> runtimeParams)
	{
		var events = new List<GameEvent>();
		var player = PartyModule.GetActiveActor(state);
		if (player == null)
			return events;

		var rng = new Random(state.RngSeed + state.Turn + def.Id.GetHashCode());

		var templateId = runtimeParams.TryGetValue("templateId", out var tid) ? tid
			: def.Params.TryGetValue("templateId", out var dtid) ? dtid
			: "trader";

		var (spawnX, spawnY) = FindEdgeSpawn(state, player, rng);
		if (spawnX == int.MinValue)
			return events;

		var trader = PresetDB.SpawnActor(templateId, $"trader_{state.Turn}");
		if (trader == null)
			return events;

		trader.X = spawnX;
		trader.Y = spawnY;
		trader.Z = player.Z;
		trader.Faction = Factions.Friendly;
		trader.BrainId = "simple";
		state.Actors[trader.Id] = trader;

		events.Add(new GameEvent("incident_trader")
		{
			TargetX = spawnX,
			TargetY = spawnY,
			TargetActorName = trader.DisplayName,
			InteractionDefId = def.Id,
			InteractionName = def.Name,
		});

		return events;
	}

	private static (int X, int Y) FindEdgeSpawn(GameState state, Actor player, Random rng)
	{
		var radius = 15;
		for (var attempt = 0; attempt < 20; attempt++)
		{
			var side = rng.Next(4);
			int x, y;
			switch (side)
			{
				case 0: x = player.X - radius; y = player.Y + rng.Next(-radius, radius); break;
				case 1: x = player.X + radius; y = player.Y + rng.Next(-radius, radius); break;
				case 2: x = player.X + rng.Next(-radius, radius); y = player.Y - radius; break;
				default: x = player.X + rng.Next(-radius, radius); y = player.Y + radius; break;
			}

			if (state.World != null && state.World.IsWalkable(x, y, player.Z))
				return (x, y);
		}

		return (int.MinValue, int.MinValue);
	}
}
