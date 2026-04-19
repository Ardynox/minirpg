using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Data;
using MiniRPG.Core.Genetics;

namespace MiniRPG.Core.Demographics;

/// <summary>
/// 受孕 / 怀孕推进 / 出生的最小可玩闭环。每天调一次（由 <see cref="WorldDemographicsTick"/> 在 day boundary 触发）：
/// 1. 推进所有怀孕角色的 <see cref="Actor.PregnancyTicksRemaining"/> 倒计时（按天减 <c>TurnsPerDay</c>）。
/// 2. 倒计时归零的母亲 → spawn 新生儿，调 <see cref="GeneInheritanceService.Inherit"/> 给孩子分配基因，
///    写入 MotherActorId / FatherActorId / BirthTurn / Sex / Genome。
/// 3. 检查每个适龄 Female 角色，pairing radius 内有适龄异性 → 概率受孕，记 <see cref="Actor.MateActorId"/>。
///
/// 简化范围（Phase 4 不做的事）：妊娠分期 buff、小产、配偶绑定、自然死亡——这些放 Phase 5 / 6。
/// </summary>
public static class ConceptionBirthTick
{
	public static List<GameEvent> OnNewDay(GameState state)
	{
		LifeStageCatalog.EnsureLoaded();
		GeneCatalog.EnsureLoaded();

		var events = new List<GameEvent>();
		var rng = new Random(unchecked(state.WorldSeed * 31 ^ state.Turn));

		AdvancePregnancies(state, rng, events);
		TryConceptions(state, rng, events);

		return events;
	}

	private static void AdvancePregnancies(GameState state, Random rng, List<GameEvent> events)
	{
		var turnsPerDay = MiniRPG.Module.Render.DayNightCycle.TurnsPerDay;
		foreach (var actor in state.Actors.Values.ToList())
		{
			if (actor.PregnancyTicksRemaining is not int remaining)
				continue;

			if (remaining > turnsPerDay)
			{
				actor.PregnancyTicksRemaining = remaining - turnsPerDay;
				continue;
			}

			actor.PregnancyTicksRemaining = null;
			TryBirth(state, actor, rng, events);
		}
	}

	private static void TryBirth(GameState state, Actor mother, Random rng, List<GameEvent> events)
	{
		Actor? father = null;
		if (!string.IsNullOrWhiteSpace(mother.MateActorId)
			&& state.Actors.TryGetValue(mother.MateActorId!, out var f))
			father = f;

		var raceId = mother.Race?.Id ?? "human";
		var childIndex = 0;
		while (true)
		{
			var childId = $"newborn_{mother.Id}_{state.Turn}_{childIndex}";
			if (!state.Actors.ContainsKey(childId))
			{
				var child = SpawnChild(state, mother, father, childId, raceId, rng);
				events.Add(new GameEvent("birth")
				{
					InitiatorId = mother.Id,
					InitiatorActorName = mother.DisplayName,
					TargetId = childId,
					TargetActorName = child.DisplayName,
					TargetX = child.X,
					TargetY = child.Y,
					TargetZ = child.Z,
				});
				return;
			}

			childIndex++;
			if (childIndex > 100)
				return;
		}
	}

	private static Actor SpawnChild(GameState state, Actor mother, Actor? father, string childId, string raceId, Random rng)
	{
		var child = PresetDB.SpawnActor(mother.TemplateId, childId);
		child.X = mother.X;
		child.Y = mother.Y;
		child.Z = mother.Z;
		child.Faction = mother.Faction;
		child.BrainId = "simple";
		child.BirthTurn = state.Turn;
		child.MotherActorId = mother.Id;
		child.FatherActorId = father?.Id;
		child.Sex = rng.Next(2) == 0 ? Sex.Female : Sex.Male;
		child.Genome = GeneInheritanceService.Inherit(rng, mother.Genome, father?.Genome, raceId);
		child.LastResolvedLifeStage = LifeStage.Infant;

		// 切到 human_infant need profile（baby_food + rest，无 hunger/thirst），
		// 让 baby 真正按婴儿需求曲线衰减；EnsureInitialized 重新生成 needs 字典。
		if (child.Race != null)
			child.Race.NeedProfileId = "human_infant";
		MiniRPG.Core.Needs.NeedSystem.EnsureInitialized(child, state.Turn);

		state.Actors[childId] = child;
		return child;
	}

	private static void TryConceptions(GameState state, Random rng, List<GameEvent> events)
	{
		var cfg = LifeStageCatalog.Conception;
		var radius = Math.Max(1, cfg.PairingRadius);
		var perDayChance = Math.Clamp(cfg.ConceptionChancePerDay, 0f, 1f);
		if (perDayChance <= 0f)
			return;

		var gestation = Math.Max(1, cfg.GestationTurns);
		var raceWhitelist = cfg.AutoConceptionRaceIds;

		foreach (var mother in state.Actors.Values.ToList())
		{
			if (mother.Sex != Sex.Female)
				continue;
			if (mother.PregnancyTicksRemaining is not null)
				continue;
			if (!DemographicsService.IsAdultOrOlder(mother, state))
				continue;
			if (string.Equals(mother.Faction, Factions.Hostile, StringComparison.Ordinal))
				continue;
			if (!IsRaceAllowed(raceWhitelist, mother.Race?.Id))
				continue;

			var father = FindEligibleMate(state, mother, radius);
			if (father == null)
				continue;
			if (rng.NextDouble() >= perDayChance)
				continue;

			mother.PregnancyTicksRemaining = gestation;
			mother.MateActorId = father.Id;
			events.Add(new GameEvent("conception")
			{
				InitiatorId = mother.Id,
				InitiatorActorName = mother.DisplayName,
				TargetId = father.Id,
				TargetActorName = father.DisplayName,
				TargetX = mother.X,
				TargetY = mother.Y,
				TargetZ = mother.Z,
			});
		}
	}

	private static bool IsRaceAllowed(List<string>? whitelist, string? raceId)
	{
		if (whitelist == null || whitelist.Count == 0)
			return true; // 兼容老配置：未列白名单 = 不限。
		if (string.IsNullOrWhiteSpace(raceId))
			return false;
		foreach (var allowed in whitelist)
		{
			if (string.Equals(allowed, raceId, StringComparison.Ordinal))
				return true;
		}

		return false;
	}

	private static Actor? FindEligibleMate(GameState state, Actor mother, int radius)
	{
		Actor? best = null;
		var bestDist = int.MaxValue;
		foreach (var a in state.Actors.Values)
		{
			if (string.Equals(a.Id, mother.Id, StringComparison.Ordinal))
				continue;
			if (a.Sex == mother.Sex)
				continue;
			if (a.Z != mother.Z)
				continue;
			if (!string.Equals(a.Race?.Id ?? "", mother.Race?.Id ?? "", StringComparison.Ordinal))
				continue;
			if (!DemographicsService.IsAdultOrOlder(a, state))
				continue;
			if (string.Equals(a.Faction, Factions.Hostile, StringComparison.Ordinal))
				continue;
			var d = Math.Max(Math.Abs(mother.X - a.X), Math.Abs(mother.Y - a.Y));
			if (d > radius)
				continue;
			if (d < bestDist)
			{
				bestDist = d;
				best = a;
			}
		}

		return best;
	}
}
