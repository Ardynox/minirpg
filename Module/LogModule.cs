using System;
using System.Collections.Generic;
using Godot;
using MiniRPG.Core.Config;
using MiniRPG.Core.World;
using MiniRPG.Module.Panel;

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
	private readonly List<string> _lines = [];
	private int _lineCount;

	public int LineCount => _lineCount;
	public IReadOnlyList<string> Lines => _lines;
	public string? LastLine => _lines.Count == 0 ? null : _lines[^1];

	public LogModule(RichTextLabel panel) => _panel = panel;

	public void Add(string msg)
	{
		if (_lineCount > 0)
			_panel.AppendText("\n");
		_panel.AppendText(msg);
		_lines.Add(msg);
		_lineCount++;

		if (_lineCount > MaxLines)
			TrimOldLines();
	}

	public void Clear()
	{
		_panel.Clear();
		_lines.Clear();
		_lineCount = 0;
	}

	private void TrimOldLines()
	{
		var toRemove = TrimBatch;
		for (var i = 0; i < toRemove; i++)
			_panel.RemoveParagraph(0);
		if (_lines.Count <= toRemove)
			_lines.Clear();
		else
			_lines.RemoveRange(0, toRemove);
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

	private static string C(string hex, string text) => $"[color={hex}]{text}[/color]";

	private static List<string>? ToLogLines(GameEvent e, GameState state)
	{
		return e.Type switch
		{
			"hit_wall" => [C(UIColors.HexDim, LocalizationService.T("log.hit_wall"))],
			"actor_moved" => null,
			"monster_spawned" => [C(UIColors.HexWarning, LocalizationService.T("log.monster_spawned", ("x", e.TargetX), ("y", e.TargetY)))],
			"combat_attack" => FormatCombatAttack(e, state),
			"combat_block" => [C(UIColors.HexCombat, LocalizationService.T("log.combat_block", ("target", e.TargetActorName), ("action", e.ActionName)))],
			"awareness_state_changed" => FormatAwarenessStateChanged(e, state),
			"skill_cast_failed" => [C(UIColors.HexWarning, FormatSkillCastFailed(e))],
			"limb_destroyed" => FormatLimbDestroyed(e, state),
			"reload_complete" => [C(UIColors.HexUtility, FormatReloadComplete(e))],
			"reload_failed" => [C(UIColors.HexWarning, FormatReloadFailed(e))],
			"empty_magazine" => [C(UIColors.HexWarning, FormatEmptyMagazine(e))],
			"item_picked_up" => [C(UIColors.HexUtility, LocalizationService.T("log.item_picked_up", ("item", e.ItemName)))],
			"item_dropped" => [C(UIColors.HexUtility, LocalizationService.T("log.item_dropped", ("item", e.ItemName)))],
			"drop_failed" => [C(UIColors.HexDim, LocalizationService.T("log.drop_failed", ("item", e.ItemName)))],
			"pickup_failed" => [C(UIColors.HexDim, LocalizationService.T("log.pickup_failed"))],
			"block_place_failed" => [C(UIColors.HexWarning, FormatBlockPlaceFailed(e))],
			"dig_success" => [C(UIColors.HexSocial, LocalizationService.T("log.dig_success", ("action", e.ActionName ?? LocalizationService.T("action.dig")), ("damage", e.Damage)))],
			"dig_progress" => [C(UIColors.HexDim, LocalizationService.T("log.dig_progress", ("action", e.ActionName ?? LocalizationService.T("action.dig")), ("damage", e.Damage)))],
			"dig_failed" => [C(UIColors.HexWarning, LocalizationService.T("log.dig_failed", ("item", e.ItemName)))],
			"weather_changed" => [C(UIColors.HexUtility, FormatWeatherChanged(e))],
			"weather_lightning_strike" => FormatWeatherLightningStrike(e, state),
			"fire_started" => FormatFireCellEvent(e, state, "log.fire.started", "Fire started at ({x},{y})"),
			"fire_spread" => FormatFireCellEvent(e, state, "log.fire.spread", "Fire spread to ({x},{y})"),
			"actor_ignited" => FormatActorIgnited(e, state),
			"fire_extinguished" => FormatFireExtinguished(e, state),
			"item_burned" => FormatItemFireEvent(e, state, "log.fire.item_burned", "{item} burned for {damage} durability"),
			"item_destroyed_by_fire" => FormatItemFireEvent(e, state, "log.fire.item_destroyed", "{item} was destroyed by fire"),
			"fixture_burned_down" => FormatFixtureBurnedDown(e, state),
			"actor_incapacitated" when e.TargetId != state.PlayerId
				=> [C(UIColors.HexCombat, LocalizationService.T("log.actor_incapacitated.other", ("target", e.TargetActorName)))],
			"food_consumed" when e.TargetId == state.PlayerId
				=> [C(UIColors.HexSocial, LocalizationService.T("log.need.food_consumed", ("item", e.ItemName)))],
			"rest_started" when e.TargetId == state.PlayerId
				=> [C(UIColors.HexSocial, LocalizationService.T("log.need.rest_started"))],
			"rest_completed" when e.TargetId == state.PlayerId
				=> [C(UIColors.HexSuccess, LocalizationService.T("log.need.rest_completed"))],
			"need_stage_changed" when e.TargetId == state.PlayerId
				=> [C(UIColors.HexWarning, LocalizationService.T("log.need.stage_changed", ("need", LocalizeNeed(e.EffectType)), ("stage", LocalizeThought(e.ActionName))))],
			"thought_applied" when e.TargetId == state.PlayerId
				=> [C(UIColors.HexDim, LocalizationService.T("log.need.thought_applied", ("thought", LocalizeThought(e.ActionName)), ("value", e.ItemName)))],
			"bleeding_started" when e.TargetId == state.PlayerId
				=> [C(UIColors.HexWarning, LocalizationService.TOrFallback("log.health.bleeding_started", "You start bleeding."))],
			"treatment_applied" when e.TargetId == state.PlayerId
				=> [C(UIColors.HexSuccess, LocalizationService.TOrFallback("log.health.treatment_applied", "Treatment applied to {condition}.", ("condition", LocalizeCondition(e.ActionName))))],
			"infection_started" when e.TargetId == state.PlayerId
				=> [C(UIColors.HexWarning, LocalizationService.TOrFallback("log.health.infection_started", "An infection has started."))],
			"infection_worsened" when e.TargetId == state.PlayerId
				=> [C(UIColors.HexWarning, LocalizationService.TOrFallback("log.health.infection_worsened", "The infection is getting worse."))],
			"scar_gained" when e.TargetId == state.PlayerId
				=> [C(UIColors.HexDim, LocalizationService.TOrFallback("log.health.scar_gained", "You gained a scar."))],
			"death_blood_loss"
				=> [C(UIColors.HexCombat, LocalizationService.TOrFallback("log.health.death_blood_loss", "{target} died from blood loss.", ("target", e.TargetActorName ?? "Someone")))],
			"death_infection"
				=> [C(UIColors.HexCombat, LocalizationService.TOrFallback("log.health.death_infection", "{target} died from infection.", ("target", e.TargetActorName ?? "Someone")))],
			"corpse_spawned" => [C(UIColors.HexDim, FormatCorpseSpawned(e))],
			"corpse_stripped" => [C(UIColors.HexDim, FormatCorpseStripped(e))],
			"corpse_butchered" => [C(UIColors.HexDim, FormatCorpseButchered(e))],
			"corpse_harvested" => [C(UIColors.HexDim, FormatCorpseHarvested(e))],
			"surgery_installed" => [C(UIColors.HexSuccess, FormatSurgeryInstalled(e))],
			"live_harvested" => [C(UIColors.HexWarning, FormatLiveHarvested(e))],
			"operate_failed" => [C(UIColors.HexWarning, FormatOperateFailed(e))],
			"campfire_lit" when e.InitiatorId == state.PlayerId
				=> [C(UIColors.HexEquipped, LocalizationService.TOrFallback("log.heat.campfire_lit", "You light a campfire."))],
			_ => null,
		};
	}

	private static string FormatReloadComplete(GameEvent e) =>
		LocalizationService.TOrFallback(
			"log.reload_complete",
			Localize("{item}装填了{amount}发。", "{item} reloaded {amount} rounds."),
			("item", e.ItemName ?? Localize("武器", "weapon")),
			("amount", e.Damage));

	private static string FormatReloadFailed(GameEvent e) =>
		LocalizationService.TOrFallback(
			"log.reload_failed",
			Localize("无法为{item}装填。", "Could not reload {item}."),
			("item", e.ItemName ?? Localize("武器", "weapon")));

	private static string FormatEmptyMagazine(GameEvent e) =>
		LocalizationService.TOrFallback(
			"log.empty_magazine",
			Localize("{item}已经空仓。", "{item} is empty."),
			("item", e.ItemName ?? Localize("武器", "weapon")));

	private static string FormatCorpseSpawned(GameEvent e) =>
		LocalizationService.TOrFallback(
			"log.corpse_spawned",
			Localize("{target}倒下，留下了{item}。", "{target} died and left behind {item}."),
			("target", e.TargetActorName ?? Localize("某个目标", "someone")),
			("item", e.ItemName ?? Localize("尸体", "a corpse")));

	private static string FormatCorpseStripped(GameEvent e) =>
		LocalizationService.TOrFallback(
			"log.corpse_stripped",
			Localize("已剥取{item}上的装备。", "{item} was stripped."),
			("item", e.ItemName ?? Localize("尸体", "corpse")));

	private static string FormatCorpseButchered(GameEvent e) =>
		LocalizationService.TOrFallback(
			"log.corpse_butchered",
			Localize("已肢解{item}。", "{item} was butchered."),
			("item", e.ItemName ?? Localize("尸体", "corpse")));

	private static string FormatCorpseHarvested(GameEvent e) =>
		LocalizationService.TOrFallback(
			"log.corpse_harvested",
			Localize("从{corpse}摘取了{item}（{limb}）。", "Harvested {item} ({limb}) from {corpse}."),
			("corpse", e.TargetActorName ?? Localize("尸体", "corpse")),
			("item", e.ItemName ?? (e.LimbName ?? Localize("器官", "part"))),
			("limb", e.LimbName ?? Localize("部位", "part")));

	private static string FormatSurgeryInstalled(GameEvent e) =>
		LocalizationService.TOrFallback(
			"log.surgery_installed",
			Localize("为{target}安装了{item}（{limb}）。", "Installed {item} on {target} ({limb})."),
			("target", e.TargetActorName ?? Localize("目标", "target")),
			("item", e.ItemName ?? Localize("植入物", "implant")),
			("limb", e.LimbName ?? Localize("部位", "part")));

	private static string FormatLiveHarvested(GameEvent e) =>
		LocalizationService.TOrFallback(
			"log.live_harvested",
			Localize("从{target}摘取了{item}（{limb}）。", "Harvested {item} ({limb}) from {target}."),
			("target", e.TargetActorName ?? Localize("目标", "target")),
			("item", e.ItemName ?? Localize("器官", "part")),
			("limb", e.LimbName ?? Localize("部位", "part")));

	private static string FormatOperateFailed(GameEvent e) =>
		LocalizationService.TOrFallback(
			"log.operate_failed",
			Localize("{action}失败：{target}（{reason}）。", "{action} failed on {target} ({reason})."),
			("action", e.ActionName ?? Localize("手术", "Operation")),
			("target", e.TargetActorName ?? e.ItemName ?? Localize("目标", "target")),
			("reason", LocalizeOperateFailureReason(e.FailureReason)));

	private static string FormatBlockPlaceFailed(GameEvent e) => e.FailureReason switch
	{
		"world_uninitialized" => LocalizationService.T("log.block_place_failed.world_uninitialized"),
		"occupied" => LocalizationService.T("log.block_place_failed.occupied"),
		"target_not_empty" => LocalizationService.T("log.block_place_failed.target_not_empty"),
		"unknown_terrain" => LocalizationService.T(
			"log.block_place_failed.unknown_terrain",
			("terrain", e.ItemName ?? Localize("未知方块", "unknown block"))),
		"missing_support" => LocalizationService.T("log.block_place_failed.missing_support"),
		_ => string.IsNullOrWhiteSpace(e.ItemName)
			? Localize("放置失败。", "Place failed.")
			: e.ItemName!,
	};

	private static List<string> FormatCombatAttack(GameEvent e, GameState state)
	{
		var lines = new List<string>();

		if (e.TargetId == state.PlayerId)
		{
			var attackerName = string.IsNullOrWhiteSpace(e.InitiatorActorName)
				? "???"
				: e.InitiatorActorName!;
			lines.Add(C(UIColors.HexCombat, LocalizationService.T("log.combat_attack.player",
				("attacker", attackerName),
				("limb", e.LimbName),
				("damage", e.Damage))));
		}
		else
		{
			lines.Add(C(UIColors.HexCombat, LocalizationService.T("log.combat_attack.other",
				("action", e.ActionName),
				("target", e.TargetActorName),
				("limb", e.LimbName),
				("damage", e.Damage))));
		}

		var hitTarget = e.TargetId != null ? ActorModule.GetById(state, e.TargetId) : null;
		if (hitTarget != null)
		{
			var hl = hitTarget.Limbs.Find(l => l.Name == e.LimbName);
			if (hl != null)
			{
				lines.Add(C(UIColors.HexDim, LocalizationService.T("log.combat_attack.limb_status",
					("limb", e.LimbName),
					("current", hl.Durability),
					("max", hl.MaxDurability))));
			}
		}

		return lines;
	}

	private static List<string> FormatLimbDestroyed(GameEvent e, GameState state)
	{
		if (e.TargetId == state.PlayerId)
			return [C(UIColors.HexWarning, LocalizationService.T("log.limb_destroyed.player", ("limb", e.LimbName)))];
		return [C(UIColors.HexCombat, LocalizationService.T("log.limb_destroyed.other", ("target", e.TargetActorName), ("limb", e.LimbName)))];
	}

	private static string FormatWeatherChanged(GameEvent e)
	{
		var weather = GameLocalizer.LocalizeWeatherName(e.WeatherTypeId);
		var intensity = GameLocalizer.LocalizeWeatherIntensity(e.WeatherIntensityId);
		return LocalizationService.TOrFallback("log.weather_changed", "Weather changed to {weather} ({intensity})", ("weather", weather), ("intensity", intensity));
	}

	private static List<string>? FormatWeatherLightningStrike(GameEvent e, GameState state)
	{
		if (!IsEventVisibleToPlayer(e, state))
			return null;

		return
		[
			C(UIColors.HexWarning, LocalizationService.TOrFallback(
				"log.weather_lightning_strike",
				"Lightning strikes {target}'s {limb} for {damage}",
				("target", e.TargetActorName ?? "target"),
				("limb", e.LimbName ?? "body"),
				("damage", e.Damage))),
			];
	}

	private static List<string>? FormatFireCellEvent(GameEvent e, GameState state, string key, string fallback)
	{
		if (!IsEventVisibleToPlayer(e, state))
			return null;

		return
		[
			C(UIColors.HexWarning, LocalizationService.TOrFallback(
				key,
				fallback,
				("x", e.TargetX),
				("y", e.TargetY),
				("damage", e.Damage))),
		];
	}

	private static List<string>? FormatActorIgnited(GameEvent e, GameState state)
	{
		if (!IsEventVisibleToPlayer(e, state))
			return null;

		return
		[
			C(UIColors.HexWarning, LocalizationService.TOrFallback(
				"log.fire.actor_ignited",
				"{target} caught fire",
				("target", e.TargetActorName ?? "Someone"))),
		];
	}

	private static List<string>? FormatFireExtinguished(GameEvent e, GameState state)
	{
		if (!IsEventVisibleToPlayer(e, state))
			return null;

		if (string.Equals(e.ActionName, "self", StringComparison.Ordinal) && e.TargetId == state.PlayerId)
		{
			return
			[
				C(UIColors.HexSuccess, LocalizationService.TOrFallback(
					"log.fire.self_extinguished",
					"You beat out the flames on yourself")),
			];
		}

		if (string.Equals(e.ActionName, "actor", StringComparison.Ordinal))
		{
			return
			[
				C(UIColors.HexDim, LocalizationService.TOrFallback(
					"log.fire.actor_extinguished",
					"{target} is no longer on fire",
					("target", e.TargetActorName ?? "Someone"))),
			];
		}

		return
		[
			C(UIColors.HexDim, LocalizationService.TOrFallback(
				"log.fire.extinguished",
				"The fire at ({x},{y}) was extinguished",
				("x", e.TargetX),
				("y", e.TargetY))),
		];
	}

	private static List<string>? FormatItemFireEvent(GameEvent e, GameState state, string key, string fallback)
	{
		if (!IsEventVisibleToPlayer(e, state))
			return null;

		return
		[
			C(UIColors.HexWarning, LocalizationService.TOrFallback(
				key,
				fallback,
				("item", e.ItemName ?? LocalizationService.T("ui.common.unknown")),
				("damage", e.Damage))),
		];
	}

	private static List<string>? FormatFixtureBurnedDown(GameEvent e, GameState state)
	{
		if (!IsEventVisibleToPlayer(e, state))
			return null;

		return
		[
			C(UIColors.HexWarning, LocalizationService.TOrFallback(
				"log.fire.fixture_burned_down",
				"{fixture} burned down",
				("fixture", GameLocalizer.LocalizeFixtureName(e.ActionName ?? string.Empty)))),
		];
	}

	private static List<string>? FormatAwarenessStateChanged(GameEvent e, GameState state)
	{
		if (string.Equals(e.EffectType, "idle", StringComparison.Ordinal))
			return null;

		var enemyName = string.IsNullOrWhiteSpace(e.InitiatorActorName)
			? "???"
			: e.InitiatorActorName!;
		var (key, hex) = e.EffectType switch
		{
			"suspicious" => ("log.awareness.suspicious", UIColors.HexEquipped),
			"alerted" => ("log.awareness.alerted", UIColors.HexCombat),
			"searching" => ("log.awareness.searching", UIColors.HexUtility),
			_ => (string.Empty, UIColors.HexDim),
		};
		return string.IsNullOrEmpty(key)
			? null
			: [C(hex, LocalizationService.T(key, ("enemy", enemyName)))];
	}

	private static string FormatSkillCastFailed(GameEvent e)
	{
		var skillName = !string.IsNullOrWhiteSpace(e.ActionName)
			? e.ActionName!
			: !string.IsNullOrWhiteSpace(e.SkillId)
				? e.SkillId!
				: LocalizationService.T("log.skill_cast_failed.unknown_skill_name");
		var reason = string.IsNullOrWhiteSpace(e.FailureReason) ? "unknown" : e.FailureReason!;
		var key = $"log.skill_cast_failed.{reason}";
		return LocalizationService.T(
			key,
			("skill", skillName),
			("remaining", e.CooldownRemaining),
			("target", e.TargetActorName ?? string.Empty));
	}

	private static string LocalizeNeed(string? needId) =>
		string.IsNullOrWhiteSpace(needId) ? LocalizationService.T("ui.common.none") : NeedCatalog.GetNeedDisplayName(needId);

	private static string LocalizeThought(string? thoughtId) =>
		string.IsNullOrWhiteSpace(thoughtId) ? LocalizationService.T("ui.common.none") : NeedCatalog.GetThoughtDisplayName(thoughtId);

	private static string LocalizeCondition(string? conditionId) =>
		string.IsNullOrWhiteSpace(conditionId) ? LocalizationService.T("ui.common.none") : HealthCatalog.GetConditionDisplayName(conditionId);

	private static string LocalizeOperateFailureReason(string? reason) => reason switch
	{
		"missing_target" => Localize("缺少目标", "missing target"),
		"out_of_range" => Localize("距离过远", "out of range"),
		"invalid_target" => Localize("目标无效", "invalid target"),
		_ => string.IsNullOrWhiteSpace(reason) ? Localize("未知原因", "unknown reason") : reason!,
	};

	private static string Localize(string zhHans, string en) =>
		string.Equals(LocalizationService.CurrentLocale, "en", StringComparison.Ordinal)
			? en
			: zhHans;

	private static bool IsEventVisibleToPlayer(GameEvent e, GameState state)
	{
		if (e.TargetId == state.PlayerId || e.InitiatorId == state.PlayerId)
			return true;

		if (state.World == null || state.PlayerZ != e.TargetZ)
			return false;

		return MiniRPG.Core.World.VisibilityUtil.HasLineOfSight(
			state.World,
			state.PlayerX,
			state.PlayerY,
			e.TargetX,
			e.TargetY,
			state.PlayerZ);
	}
}
