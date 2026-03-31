using System.Collections.Generic;
using MiniRPG.Core;

namespace MiniRPG.Module;

/// <summary>
/// 纯函数：将 GameEvent 翻译为人类可读的日志文本。
/// 不持有任何状态，不触发任何流程——只做 "事件 → 字符串" 的映射。
/// 流程触发类事件（combat_bump、interaction 等）不在此处理，由调用方路由到对应 UI Module。
/// </summary>
public static class EventLogModule
{
	/// <summary>
	/// 将单个事件翻译为日志行。
	/// 返回 null 表示该事件不需要日志输出（静默消费或由流程模块自行输出）。
	/// 返回多行时用 List 表示（如攻击事件附带肢体状态）。
	/// </summary>
	public static List<string>? ToLogLines(GameEvent e, GameState state)
	{
		return e.Type switch
		{
			"hit_wall" => ["\u649e\u5899\u4e86 \ud83d\udea7"],
			"actor_moved" => null,
			"monster_spawned" => [$"\u5de2\u7a74\u5237\u51fa\u602a\u7269 \ud83d\udc7e ({e.TargetX},{e.TargetY})"],
			"combat_attack" => FormatCombatAttack(e, state),
			"combat_block" => [$"\ud83d\udee1\ufe0f {e.TargetActorName}\u4f7f\u7528\u4e86{e.ActionName}\uff01\u9632\u5fa1+5 (1\u56de\u5408)"],
			"limb_destroyed" => FormatLimbDestroyed(e, state),
			"item_picked_up" => [$"\ud83d\udce6 \u62fe\u53d6\u4e86 {e.ItemName}"],
			"item_dropped" => [$"\ud83d\udce6 \u4e22\u5f03\u4e86 {e.ItemName}"],
			"drop_failed" => [$"\u26a0\ufe0f \u8bf7\u5148\u5378\u4e0b {e.ItemName} \u518d\u4e22\u5f03"],
			"pickup_failed" => ["\u7269\u54c1\u5df2\u7ecf\u4e0d\u5728\u4e86"],
			"dig_success" => [$"{e.ActionName ?? "\u6316\u6398"}\u6210\u529f\uff01\u5730\u5f62\u88ab\u7834\u574f\u4e86 \u26cf\ufe0f"],
			"dig_progress" => [$"{e.ActionName ?? "\u6316\u6398"}\u4e2d... \u9020\u6210 {e.Damage} \u70b9\u7834\u574f \u26cf\ufe0f"],
			"dig_failed" => [$"\u65e0\u6cd5\u6267\u884c\uff1a{e.ItemName}"],
			"actor_incapacitated" when e.TargetId != state.PlayerId
				=> [$"\ud83d\ude35 {e.TargetActorName}\u5931\u53bb\u4e86\u610f\u8bc6\uff01"],
			_ => null,
		};
	}

	private static List<string> FormatCombatAttack(GameEvent e, GameState state)
	{
		var lines = new List<string>();

		if (e.TargetId == state.PlayerId)
		{
			var attackerName = e.InitiatorId != null
				? ActorModule.GetById(state, e.InitiatorId)?.DisplayName ?? "???"
				: "???";
			lines.Add($"\ud83e\ude78 {attackerName}\u653b\u51fb\u4e86\u4f60\u7684{e.LimbName}\uff0c\u9020\u6210{e.Damage}\u70b9\u4f24\u5bb3");
		}
		else
		{
			lines.Add($"\u2694\ufe0f {e.ActionName} \u2192 {e.TargetActorName}\u7684{e.LimbName}\uff0c\u9020\u6210{e.Damage}\u70b9\u4f24\u5bb3");
		}

		var hitTarget = e.TargetId != null ? ActorModule.GetById(state, e.TargetId) : null;
		if (hitTarget != null)
		{
			var hl = hitTarget.Limbs.Find(l => l.Name == e.LimbName);
			if (hl != null)
				lines.Add($"   {e.LimbName} ({hl.Durability}/{hl.MaxDurability})");
		}

		return lines;
	}

	private static List<string> FormatLimbDestroyed(GameEvent e, GameState state)
	{
		if (e.TargetId == state.PlayerId)
			return [$"\ud83d\udca5 \u4f60\u7684{e.LimbName}\u88ab\u6467\u6bc1\u4e86\uff01"];
		return [$"\ud83d\udca5 {e.TargetActorName}\u7684{e.LimbName}\u88ab\u6467\u6bc1\u4e86\uff01"];
	}
}
