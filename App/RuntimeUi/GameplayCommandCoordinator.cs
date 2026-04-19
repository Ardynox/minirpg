using System;
using System.Collections.Generic;
using System.Text;

namespace MiniRPG;

internal sealed class GameplayCommandCoordinator
{
	private readonly GameState _state;
	private readonly InputModule _input;
	private readonly LogModule _log;
	private readonly Func<bool> _isMultiplayerSession;
	private readonly Action<ClientCommand> _submitClientCommand;
	private readonly Action<TimelinePlayerAction> _submitPlayerAction;
	private readonly Action<List<GameEvent>> _dispatch;
	private readonly Action _flushMap;
	private readonly Action _invalidateGround;
	private readonly Action _refreshGround;
	private readonly Action _doGroundInteractSelected;

	public GameplayCommandCoordinator(
		GameState state,
		InputModule input,
		LogModule log,
		Func<bool> isMultiplayerSession,
		Action<ClientCommand> submitClientCommand,
		Action<TimelinePlayerAction> submitPlayerAction,
		Action<List<GameEvent>> dispatch,
		Action flushMap,
		Action invalidateGround,
		Action refreshGround,
		Action doGroundInteractSelected)
	{
		_state = state;
		_input = input;
		_log = log;
		_isMultiplayerSession = isMultiplayerSession;
		_submitClientCommand = submitClientCommand;
		_submitPlayerAction = submitPlayerAction;
		_dispatch = dispatch;
		_flushMap = flushMap;
		_invalidateGround = invalidateGround;
		_refreshGround = refreshGround;
		_doGroundInteractSelected = doGroundInteractSelected;
	}

	/// <summary>
	/// 提交一次玩家移动。返回值告诉调用方"实际走的是哪条提交路径"：
	/// true = 走了客户端预测（多人 + 非地表自由移动 + 预测器接受）；
	/// false = 退化到 TimelineAction（单机 / 多人开了地表自由移动 / 预测器拒绝）。
	/// AutoNavigationCoordinator 等需要按提交路径区分对账策略的调用方靠这个返回值定 in-flight 类型。
	/// </summary>
	public bool DoMove(int dx, int dy, Func<int, int, bool> trySubmitPredictedMove)
	{
		if (!_state.RuntimeSurfaceFreeMove && trySubmitPredictedMove(dx, dy))
			return true;

		_submitPlayerAction(TimelinePlayerAction.Move(dx, dy));
		return false;
	}

	public void StartDig()
	{
		var player = ActiveActorAccess.GetActive(_state);
		if (player == null)
			return;

		var cellSkills = SkillQuery.GetCellSkills(player);
		if (cellSkills.Count == 0)
		{
			_log.Add(LocalizationService.T("dig.no_skills"));
			return;
		}

		var hasTargets = false;
		foreach (var skill in cellSkills)
		{
			if (InteractionModule.GetBreakableNeighbors(_state, player, skill).Count > 0)
			{
				hasTargets = true;
				break;
			}
		}

		if (!hasTargets)
		{
			_log.Add(LocalizationService.T("dig.no_targets"));
			return;
		}

		_log.Add(LocalizationService.T("dig.choose_direction"));
		_input.EnterDirectionMode("dig");
	}

	public void HandleDigDirection(string dir)
	{
		var player = ActiveActorAccess.GetActive(_state);
		if (player == null)
			return;

		var (dx, dy, dz) = dir switch
		{
			"n" => (0, -1, 0),
			"s" => (0, 1, 0),
			"w" => (-1, 0, 0),
			"e" => (1, 0, 0),
			"u" or "up" => (0, 0, -1),
			"d" or "down" => (0, 0, 1),
			_ => (0, 0, 0),
		};
		if (dx == 0 && dy == 0 && dz == 0)
			return;

		var tx = player.X + dx;
		var ty = player.Y + dy;
		var tz = player.Z + dz;

		if (_state.World == null)
			return;
		var terrain = _state.World.GetTerrain(tx, ty, tz);
		var hardness = _state.World.GetHardness(tx, ty, tz);

		if (!terrain.Solid || hardness == 0)
		{
			_log.Add(LocalizationService.T("dig.invalid_target"));
			return;
		}

		var cellSkills = SkillQuery.GetCellSkills(player);
		InteractionDef? bestSkill = null;
		foreach (var skill in cellSkills)
		{
			if (MiniRPG.Core.World.DigModule.CanApply(skill, terrain, hardness))
			{
				if (bestSkill == null || skill.TerrainMaterial.Length > 0)
					bestSkill = skill;
			}
		}

		if (bestSkill == null)
		{
			_log.Add(LocalizationService.T("dig.cannot_break", ("terrain", GameLocalizer.LocalizeTerrainName(terrain.StringId))));
			return;
		}

		_submitPlayerAction(TimelinePlayerAction.Dig(dx, dy, bestSkill.Id));
	}

	public void DoInteract()
	{
		var player = ActiveActorAccess.GetActive(_state);
		if (player == null)
			return;

		var targets = InteractionModule
			.GetAvailableTargets(_state, player)
			.FindAll(target => GetNonCombatInteractions(player, target).Count > 0);

		if (targets.Count > 0)
		{
			if (targets.Count == 1)
			{
				ShowInteractionsFor(player, targets[0]);
				return;
			}

			var options = new List<(string Name, Action Execute)>();
			foreach (var t in targets)
			{
				var target = t;
				options.Add((IdentificationModule.GetActorDisplayName(_state, target), () => ShowInteractionsFor(player, target)));
			}

			var sb = new StringBuilder(LocalizationService.T("ui.interaction.choose_target"));
			for (var i = 0; i < options.Count; i++)
				sb.Append($"  [{i + 1}] {options[i].Name}");
			_log.Add(sb.ToString());

			_input.EnterSelection(n =>
			{
				if (n < 1 || n > options.Count)
				{
					_log.Add(LocalizationService.T("ui.selection.invalid"));
					return;
				}
				options[n - 1].Execute();
			}, () => _log.Add(LocalizationService.T("ui.selection.canceled")));
			return;
		}

		var groundItems = (_state.World?.PeekGroundItems(_state.PlayerX, _state.PlayerY, _state.PlayerZ) ?? []);
		if (groundItems.Count > 0)
		{
			_refreshGround();
			_doGroundInteractSelected();
			return;
		}

		_log.Add(LocalizationService.T("ui.interaction.none_nearby"));
	}

	private void ShowInteractionsFor(Actor player, Actor target)
	{
		var interactions = GetNonCombatInteractions(player, target);
		if (interactions.Count == 0)
		{
			_log.Add(LocalizationService.T("ui.interaction.none_nearby"));
			return;
		}

		var options = new List<(string Name, Action Execute)>();
		foreach (var def in interactions)
		{
			var d = def;
			options.Add((d.Name, () =>
			{
				var command = new InteractClientCommand
				{
					ActorId = player.Id,
					TargetActorId = target.Id,
					InteractionDefId = d.Id,
				};
				_submitClientCommand(command);
				_flushMap();
			}));
		}
		options.Add((LocalizationService.T("ui.interaction.nothing"), () => _log.Add(LocalizationService.T("ui.interaction.walk_away"))));

		if (options.Count == 1)
		{
			options[0].Execute();
			return;
		}

		var sb = new StringBuilder($"{IdentificationModule.GetActorDisplayName(_state, target)}：");
		for (var i = 0; i < options.Count; i++)
			sb.Append($"  [{i + 1}] {options[i].Name}");
		_log.Add(sb.ToString());

		_input.EnterSelection(n =>
		{
			if (n < 1 || n > options.Count)
			{
				_log.Add(LocalizationService.T("ui.selection.invalid"));
				return;
			}
			options[n - 1].Execute();
		}, () => _log.Add(LocalizationService.T("ui.selection.canceled")));
	}

	private static List<InteractionDef> GetNonCombatInteractions(Actor player, Actor target) =>
		InteractionModule
			.GetInteractions(player, target, InteractionDefs.All)
			.FindAll(def => !string.Equals(def.Category, "combat", StringComparison.Ordinal));
}
