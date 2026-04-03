using System.Collections.Generic;
using Godot;
namespace MiniRPG.Module;

/// <summary>
/// 游戏日志管理 + 事件日志翻译。
/// 使用 AppendText 增量追加，超过 MaxLines 时用 RemoveParagraph 批量裁剪头部。
/// </summary>
public class LogModule
{
	private const int MaxLines = 200;
	private const int TrimBatch = 50;

	private readonly RichTextLabel _panel;
	private int _lineCount;

	public LogModule(RichTextLabel panel) => _panel = panel;

	public void Add(string msg)
	{
		if (_lineCount > 0)
			_panel.AppendText("\n");
		_panel.AppendText(msg);
		_lineCount++;

		if (_lineCount > MaxLines)
			TrimOldLines();
	}

	public void Clear()
	{
		_panel.Clear();
		_lineCount = 0;
	}

	private void TrimOldLines()
	{
		var toRemove = TrimBatch;
		for (var i = 0; i < toRemove; i++)
			_panel.RemoveParagraph(0);
		_lineCount -= toRemove;
	}

	/// <summary>
	/// 将 GameEvent 翻译为日志行并写入。
	/// 返回 false 表示该事件不需要日志输出（静默消费或由流程模块自行输出）。
	/// </summary>
	public bool DispatchEvent(GameEvent e, GameState state)
	{
		var lines = ToLogLines(e, state);
		if (lines == null) return false;
		foreach (var line in lines) Add(line);
		return true;
	}

	private static List<string>? ToLogLines(GameEvent e, GameState state)
	{
		return e.Type switch
		{
			"hit_wall" => ["撞墙了 🚧"],
			"actor_moved" => null,
			"monster_spawned" => [$"巢穴刷出怪物 👾 ({e.TargetX},{e.TargetY})"],
			"combat_attack" => FormatCombatAttack(e, state),
			"combat_block" => [$"🛡️ {e.TargetActorName}使用了{e.ActionName}！防御+5 (1回合)"],
			"limb_destroyed" => FormatLimbDestroyed(e, state),
			"item_picked_up" => [$"📦 拾取了 {e.ItemName}"],
			"item_dropped" => [$"📦 丢弃了 {e.ItemName}"],
			"drop_failed" => [$"⚠️ 请先卸下 {e.ItemName} 再丢弃"],
			"pickup_failed" => ["物品已经不在了"],
			"dig_success" => [$"{e.ActionName ?? "挖掘"}成功！地形被破坏了 ⛏️"],
			"dig_progress" => [$"{e.ActionName ?? "挖掘"}中... 造成 {e.Damage} 点破坏 ⛏️"],
			"dig_failed" => [$"无法执行：{e.ItemName}"],
			"actor_incapacitated" when e.TargetId != state.PlayerId
				=> [$"😵 {e.TargetActorName}失去了意识！"],
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
			lines.Add($"🩸 {attackerName}攻击了你的{e.LimbName}，造成{e.Damage}点伤害");
		}
		else
		{
			lines.Add($"⚔️ {e.ActionName} → {e.TargetActorName}的{e.LimbName}，造成{e.Damage}点伤害");
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
			return [$"💥 你的{e.LimbName}被摧毁了！"];
		return [$"💥 {e.TargetActorName}的{e.LimbName}被摧毁了！"];
	}
}
