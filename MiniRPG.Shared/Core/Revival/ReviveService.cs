using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;
using MiniRPG.Core.Health;
using MiniRPG.Core.Needs;

namespace MiniRPG.Core.Revival;

/// <summary>
/// "复活"服务：把一具尸体的原 actor 真正拉回世界——消耗材料、重置 HP/肢体/Vital/MentalBreak、
/// 给被复活者挂长期负面 thought、给施法者+附近 NPC 各挂一条短期 thought，并发出 <c>actor_revived</c>
/// <see cref="GameEvent"/>。
/// </summary>
/// <remarks>
/// 设计意图（《产品愿景》死亡-复活四条验收）：
/// <list type="number">
/// <item>**必有代价**：调用方必须先备好 <see cref="RevivalCostModel.ComputeMaterialCost"/> 列出的材料；
/// <see cref="TryRevive"/> 在执行成功路径时**真**从 reviver inventory 扣掉对应数量；不够直接 fail。</item>
/// <item>**仪式感（旁观反应）**：附近活着的非敌对 NPC 自动获得 "revived_ally" 短期 thought（mood +8 / 60 回合）。</item>
/// <item>**多次复活累加**：被复活者 mood 负 thought 强度由 <see cref="RevivalCostModel.ComputeRevivalScar"/>
/// 按 <see cref="Actor.RevivalCount"/> 递增；累计达 <see cref="RevivalCostModel.IsPermanentlyLost"/>
/// 阈值后直接拒绝复活，让玩家彻底失去这个角色。</item>
/// <item>**失败后果可见**：失败时返回 <see cref="ReviveOutcome.FailureReason"/> + <see cref="ReviveOutcome.Events"/>
/// 至少包含一条 <c>revival_failed</c> GameEvent；上层 GameEventPresentationRouter / LogModule 可据此本地化展示。</item>
/// </list>
///
/// 上层（技能 executor / 设施 worker / TimelineTurnGateway）的责任：
/// <list type="bullet">
/// <item>把 corpse 物品定位到本服务（参数 <c>corpse</c>）。</item>
/// <item>提供"施法者"角色作为材料来源 + thought 受益者。</item>
/// <item>把"是否要回合化"做在外层（见 <see cref="Combat.TimelinePlayerActionType.Revive"/>）；本服务是单次原子调用。</item>
/// </list>
/// </remarks>
public static class ReviveService
{
	/// <summary>复活代价档位 / Method ID。具体数值由 <c>Data/Config/revival_costs.json</c> 驱动。</summary>
	public static class Methods
	{
		public const string Magic = "magic";
		public const string Tech = "tech";
		public const string Divine = "divine";
	}

	/// <summary>失败原因（机器可读 + i18n key 对照基底）。上层可据此映射本地化文案。</summary>
	public static class FailureReasons
	{
		public const string NotACorpse = "not_a_corpse";
		public const string SourceMissing = "source_actor_missing";
		public const string PermanentlyLost = "permanently_lost";
		public const string UnknownMethod = "unknown_method";
		public const string InsufficientMaterials = "insufficient_materials";
		public const string CasterSkillTooLow = "caster_skill_too_low";
		public const string SpellMisfire = "spell_misfire";
	}

	/// <summary>附近"旁观者"半径（同 Z 层切比雪夫距离）。沿用 ActorMemoryModule.CasualtyWitnessRadius 8。</summary>
	public const int BystanderRadius = 8;

	public sealed class ReviveOutcome
	{
		public bool Success { get; init; }
		public string FailureReason { get; init; } = "";
		public Actor? RevivedActor { get; init; }
		public IReadOnlyDictionary<string, int>? ConsumedMaterials { get; init; }
		public List<GameEvent> Events { get; init; } = [];
	}

	/// <summary>根据 corpse 的 metadata 定位原 actor 实体，若被完全清除则返回 null。</summary>
	public static Actor? FindSourceActor(GameState state, Item corpse)
	{
		var metadata = corpse.Corpse;
		if (metadata == null || string.IsNullOrEmpty(metadata.SourceActorId))
			return null;

		return ActorModule.GetById(state, metadata.SourceActorId);
	}

	/// <summary>
	/// 尝试用 <paramref name="reviver"/> 复活 <paramref name="corpse"/> 指向的原角色。
	/// 失败路径全程不修改世界状态；成功路径：扣材料 → 重置 actor → 挂 thought → 发 event。
	/// </summary>
	/// <param name="state">全局状态。</param>
	/// <param name="corpse">尸体物品（必须 <see cref="Item.Corpse"/> != null）。</param>
	/// <param name="reviver">施法者 / 设施操作者，用作材料来源 + 施法者 thought 受益者。</param>
	/// <param name="methodId">见 <see cref="Methods"/>。决定材料配方、失败概率、施法时长。</param>
	/// <param name="currentTurn">当前回合（写到 thought / event 时间戳上）。</param>
	/// <param name="rng">复活掷骰用 RNG（决定是否失败）。null 时用 deterministic seed = state.RngSeed^turn^reviver.Id。</param>
	public static ReviveOutcome TryRevive(
		GameState state,
		Item corpse,
		Actor reviver,
		string methodId,
		int currentTurn,
		Random? rng = null)
	{
		if (corpse.Corpse == null)
			return Failure(state, reviver, methodId, FailureReasons.NotACorpse);

		var source = FindSourceActor(state, corpse);
		if (source == null)
			return Failure(state, reviver, methodId, FailureReasons.SourceMissing);

		// 1. 永久死亡守门：先于任何资源/掷骰检查。让"第 N 次"按确定 fail。
		if (RevivalCostModel.IsPermanentlyLost(source.RevivalCount))
			return Failure(state, reviver, methodId, FailureReasons.PermanentlyLost, sourceActor: source);

		// 2. method 配置存在性。
		var method = RevivalCostModel.GetMethod(methodId);
		if (method == null)
			return Failure(state, reviver, methodId, FailureReasons.UnknownMethod, sourceActor: source);

		// 3. 资源足够性（不足直接拒绝，不扣任何东西）。
		var costs = RevivalCostModel.ComputeMaterialCost(methodId, source.RevivalCount);
		if (!HasMaterials(reviver, costs))
			return Failure(state, reviver, methodId, FailureReasons.InsufficientMaterials, sourceActor: source);

		// 4. 失败概率掷骰（早于材料扣减——失败时材料不退）。
		var actualRng = rng ?? new Random(state.RngSeed ^ currentTurn ^ reviver.Id.GetHashCode());
		var failChance = RevivalCostModel.ComputeFailureChance(methodId, source.RevivalCount);
		if (actualRng.NextDouble() < failChance)
		{
			// "失败"消耗一半材料（向上取整）——这是仪式感的一部分：失败也付代价。
			ConsumeMaterials(reviver, costs, scaleFactor: 0.5f);
			return Failure(state, reviver, methodId, FailureReasons.SpellMisfire, sourceActor: source);
		}

		// 5. 成功路径：扣全额材料 → 重置 actor → 挂 thought → 发 event → 计数 +1。
		ConsumeMaterials(reviver, costs, scaleFactor: 1f);
		ResetActorForRevival(state, source, reviver);

		var (scarOffset, scarDuration) = RevivalCostModel.ComputeRevivalScar(source.RevivalCount);
		NeedSystem.ApplyTemporaryThought(
			source,
			"revived_memory",
			moodOffset: scarOffset,
			durationTurns: scarDuration,
			currentTurn,
			source: $"revival:{methodId}",
			events: null,
			state: state);

		// 施法者本人的"我做到了"——稍长一点，因为是主动行为而非旁观。
		NeedSystem.ApplyTemporaryThought(
			reviver,
			"revived_ally",
			moodOffset: 8f,
			durationTurns: 60,
			currentTurn,
			source: $"revival:{methodId}",
			events: null,
			state: state);

		// 旁观者：所有同层、Chebyshev 半径内、活着、非敌对的非 reviver/source 角色。
		ApplyBystanderThoughts(state, reviver, source, methodId, currentTurn);

		// 计数自增放在最后——上面所有步骤都拿"本次复活前"的 RevivalCount 做计算。
		source.RevivalCount = Math.Max(0, source.RevivalCount) + 1;

		var revivedEvent = new GameEvent("actor_revived")
		{
			EffectType = methodId,
			ActionName = source.Id,
		};
		IdentificationModule.PopulateInitiatorIdentity(revivedEvent, state, reviver);
		IdentificationModule.PopulateTargetIdentity(revivedEvent, state, source);

		return new ReviveOutcome
		{
			Success = true,
			RevivedActor = source,
			ConsumedMaterials = costs,
			Events = [revivedEvent],
		};
	}

	private static ReviveOutcome Failure(
		GameState state,
		Actor reviver,
		string methodId,
		string reason,
		Actor? sourceActor = null)
	{
		var failedEvent = new GameEvent("revival_failed")
		{
			EffectType = methodId,
			ActionName = reason,
		};
		IdentificationModule.PopulateInitiatorIdentity(failedEvent, state, reviver);
		if (sourceActor != null)
			IdentificationModule.PopulateTargetIdentity(failedEvent, state, sourceActor);

		return new ReviveOutcome
		{
			Success = false,
			FailureReason = reason,
			RevivedActor = sourceActor,
			Events = [failedEvent],
		};
	}

	private static bool HasMaterials(Actor reviver, IReadOnlyDictionary<string, int> costs)
	{
		if (costs.Count == 0)
			return true;

		foreach (var (itemId, needed) in costs)
		{
			var have = 0;
			foreach (var item in reviver.Inventory)
			{
				if (string.Equals(item.Id, itemId, StringComparison.Ordinal))
					have += item.SafeStackCount;
				if (have >= needed)
					break;
			}
			if (have < needed)
				return false;
		}
		return true;
	}

	/// <summary>
	/// 从 reviver inventory 扣掉指定材料（按 <paramref name="scaleFactor"/> 折算，用于失败半价）。
	/// 假定调用前已 <see cref="HasMaterials"/> 校验过——这里只做安全扣减，不重新校验。
	/// </summary>
	private static void ConsumeMaterials(
		Actor reviver,
		IReadOnlyDictionary<string, int> costs,
		float scaleFactor)
	{
		if (costs.Count == 0 || scaleFactor <= 0f)
			return;

		foreach (var (itemId, fullCost) in costs)
		{
			var toConsume = Math.Max(1, (int)Math.Ceiling(fullCost * scaleFactor));
			for (var i = reviver.Inventory.Count - 1; i >= 0 && toConsume > 0; i--)
			{
				var item = reviver.Inventory[i];
				if (!string.Equals(item.Id, itemId, StringComparison.Ordinal))
					continue;

				var available = item.SafeStackCount;
				var take = Math.Min(toConsume, available);
				if (take >= available)
				{
					reviver.Inventory.RemoveAt(i);
				}
				else
				{
					item.StackCount = available - take;
				}
				toConsume -= take;
			}
		}
	}

	/// <summary>
	/// 真正"让死者活回来"——
	/// 肢体耐久恢复到 max 的 50%、清掉所有 HealthCondition / MissingLimb、清 vital cache、
	/// 清 mental break、清 pain / blood loss / wetness、把 actor 拉到 reviver 旁边一格。
	/// </summary>
	private static void ResetActorForRevival(GameState state, Actor source, Actor reviver)
	{
		// 1. 肢体修复 + 耐久 50%（不是满血——这是"刚被拉回来"的虚弱）。
		foreach (var limb in source.Limbs)
		{
			var halfMax = Math.Max(1, limb.MaxDurability / 2);
			limb.Durability = Math.Max(limb.Durability, halfMax);
			if (limb.Durability > limb.MaxDurability)
				limb.Durability = limb.MaxDurability;
		}

		// 2. 清健康条件（MissingLimb / Infection / Bleeding / Hypothermia 等所有 entries）。
		source.HealthConditions?.Clear();

		// 3. 清 vital cache + capacity cache，让下次 IsDead/CheckVitalStatus 重新计算。
		source.InvalidateCapacityCache();
		source.SetCachedVitalStatus(null);

		// 4. 清精神状态：mental break / mood 重置到中性 (50f) + pain/bleed 归零。
		source.MentalBreak = null;
		source.PainValue = 0f;
		source.BloodLossValue = 0f;
		source.WetnessValue = 0f;
		if (source.MoodValue < 30f)
			source.MoodValue = 50f;

		// 5. 拉到 reviver 旁边一格（如果不在同位置）：找一个相邻空位；找不到就站在 reviver 头上。
		var (px, py, pz) = FindReviveSpawnSlot(state, reviver);
		source.X = px;
		source.Y = py;
		source.Z = pz;

		// 6. 阵营：如果原本是 Hostile（敌对怪物复活通常是异常路径），强制改 Friendly 让上层有机会处理。
		//    注：这里故意不强行修改 BrainId，避免覆盖玩家通过 PartyModule 拉入队伍后又被驱逐的状态。
		if (string.Equals(source.Faction, Factions.Hostile, StringComparison.Ordinal))
			source.Faction = Factions.Friendly;
	}

	private static (int X, int Y, int Z) FindReviveSpawnSlot(GameState state, Actor reviver)
	{
		// 优先 4 邻居中第一个 walkable + 无 actor 的格子。
		if (state.World != null)
		{
			foreach (var (dx, dy) in GridDirections.Cardinal)
			{
				var nx = reviver.X + dx;
				var ny = reviver.Y + dy;
				if (state.World.IsWalkable(nx, ny, reviver.Z)
					&& ActorModule.GetAllAt(state, nx, ny, reviver.Z).Count == 0)
				{
					return (nx, ny, reviver.Z);
				}
			}
		}
		// 兜底：和 reviver 同格——上层移动子系统会在下一回合解开堆叠。
		return (reviver.X, reviver.Y, reviver.Z);
	}

	private static void ApplyBystanderThoughts(
		GameState state,
		Actor reviver,
		Actor source,
		string methodId,
		int currentTurn)
	{
		foreach (var actor in state.Actors.Values)
		{
			if (actor.Id == reviver.Id || actor.Id == source.Id)
				continue;
			if (actor.Z != reviver.Z)
				continue;
			if (CombatModule.IsDead(actor))
				continue;
			if (FactionRelation.IsHostile(actor.Faction, reviver.Faction))
				continue;

			var dx = Math.Abs(actor.X - reviver.X);
			var dy = Math.Abs(actor.Y - reviver.Y);
			if (Math.Max(dx, dy) > BystanderRadius)
				continue;

			NeedSystem.ApplyTemporaryThought(
				actor,
				"revived_ally",
				moodOffset: 8f,
				durationTurns: 60,
				currentTurn,
				source: $"revival:{methodId}:bystander",
				events: null,
				state: state);
		}
	}
}
