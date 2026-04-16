using System.Collections.Generic;
using MiniRPG.Core.Data;
using MiniRPG.Core.Needs;

namespace MiniRPG.Core.Revival;

/// <summary>
/// 数据层"复活"服务：把尸体回指向原 actor，并为上层（技能 / 设施 / 事件）提供统一触发点。
/// </summary>
/// <remarks>
/// 设计意图（见 <c>Docs/产品愿景.md</c> 死亡与复活段）：
/// 死亡不是终局——玩家可以通过科技或魔法手段让死者回来，但要付代价。
///
/// 当前仅提供最小骨架：
/// <list type="bullet">
/// <item>靠 <see cref="ItemCorpseMetadata.SourceActorId"/> 定位原 actor。</item>
/// <item>发一个 <c>actor_revived</c> <see cref="GameEvent"/>，让表现层 / 日志 / Storyteller 都能听到。</item>
/// <item>给复活者与被复活者各留一条 thought（短期 + 长期），挂在 Memory 系统已有的 <see cref="NeedSystem.ApplyTemporaryThought"/> 上。</item>
/// </list>
///
/// 故意不做（下一批次）：
/// <list type="bullet">
/// <item>真正重置 actor 的 HP / 限肢 / Vital status——这涉及 <c>HealthSystem</c> 内部 API，要单独做一次 Health 批次。</item>
/// <item>把 actor 传送到复活者旁边 / 从 party 重新加入 / 消耗 corpse 物品——留给上层触发器（技能 executor、facility worker）承担。</item>
/// <item>不同 method 的代价配置（材料、成功率、失败副作用）。</item>
/// </list>
/// 在这三件里任何一件接入时，只要调 <see cref="TryRevive"/>，然后处理它返回的 <see cref="ReviveOutcome"/> 即可。
/// </remarks>
public static class ReviveService
{
	/// <summary>复活代价档位。具体数值/材料需求由后续的 JSON 配置驱动。</summary>
	public static class Methods
	{
		public const string Magic = "magic";
		public const string Tech = "tech";
		public const string Divine = "divine";
	}

	public sealed class ReviveOutcome
	{
		public bool Success { get; init; }
		public string FailureReason { get; init; } = "";
		public Actor? RevivedActor { get; init; }
		public List<GameEvent> Events { get; } = [];
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
	/// 以 <paramref name="reviver"/> 的身份尝试复活 <paramref name="corpse"/> 指向的原角色。
	/// 仅做：定位 source、发 <c>actor_revived</c> 事件、挂 thought。不替代 Health 内部的重置。
	/// </summary>
	public static ReviveOutcome TryRevive(
		GameState state,
		Item corpse,
		Actor reviver,
		string methodId,
		int currentTurn)
	{
		var outcome = new ReviveOutcome();

		if (corpse.Corpse == null)
			return new ReviveOutcome { FailureReason = "not_a_corpse" };

		var source = FindSourceActor(state, corpse);
		if (source == null)
			return new ReviveOutcome { FailureReason = "source_actor_missing" };

		NeedSystem.ApplyTemporaryThought(
			source,
			"revived_memory",
			moodOffset: -8f,
			durationTurns: 720,
			currentTurn,
			source: $"revival:{methodId}",
			events: null,
			state: state);

		NeedSystem.ApplyTemporaryThought(
			reviver,
			"revived_ally",
			moodOffset: +4f,
			durationTurns: 240,
			currentTurn,
			source: $"revival:{methodId}",
			events: null,
			state: state);

		var revivedEvent = new GameEvent("actor_revived")
		{
			EffectType = methodId,
			ActionName = source.Id,
		};
		IdentificationModule.PopulateInitiatorIdentity(revivedEvent, state, reviver);
		IdentificationModule.PopulateTargetIdentity(revivedEvent, state, source);
		outcome.Events.Add(revivedEvent);

		return new ReviveOutcome
		{
			Success = true,
			RevivedActor = source,
			// 注意：Events 是 init-only 集合，这里通过新 outcome 继承即可。
		}.WithEvents(outcome.Events);
	}

	private static ReviveOutcome WithEvents(this ReviveOutcome outcome, IEnumerable<GameEvent> events)
	{
		outcome.Events.AddRange(events);
		return outcome;
	}
}
