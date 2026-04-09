using System;
using System.Linq;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;
using MiniRPG.Core.Debug;
using MiniRPG.Core.Facility;
using MiniRPG.Module;
using MiniRPG.Module.Panel;

namespace MiniRPG;

internal sealed class DebugPanelController : DebugPanelModule.IHost
{
	private readonly GameState _state;
	private readonly GameSessionModule _session;
	private readonly LogModule _log;
	private readonly PanelManager _panels;
	private readonly Func<DebugPanelModule.IHost, Action, DebugPanelModule> _createPanel;
	private readonly Action _markUiDirty;
	private readonly Action _flushMap;
	private readonly Func<TimelinePlayerAction, TimelineStepResult> _submitPlayerActionWithResult;
	private readonly Func<bool> _sessionStarted;
	private readonly Func<bool> _menuInMenu;
	private readonly Func<bool> _enabled;
	private readonly Func<(int ActiveSpriteCount, int DrawCommandCount, double FrameTimeAvgMs)> _renderPerfSnapshot;

	private DebugPanelModule? _panel;

	public DebugPanelController(
		GameState state,
		GameSessionModule session,
		LogModule log,
		PanelManager panels,
		Func<DebugPanelModule.IHost, Action, DebugPanelModule> createPanel,
		Action markUiDirty,
		Action flushMap,
		Func<TimelinePlayerAction, TimelineStepResult> submitPlayerActionWithResult,
		Func<bool> sessionStarted,
		Func<bool> menuInMenu,
		Func<bool> enabled,
		Func<(int ActiveSpriteCount, int DrawCommandCount, double FrameTimeAvgMs)> renderPerfSnapshot)
	{
		_state = state;
		_session = session;
		_log = log;
		_panels = panels;
		_createPanel = createPanel;
		_markUiDirty = markUiDirty;
		_flushMap = flushMap;
		_submitPlayerActionWithResult = submitPlayerActionWithResult;
		_sessionStarted = sessionStarted;
		_menuInMenu = menuInMenu;
		_enabled = enabled;
		_renderPerfSnapshot = renderPerfSnapshot;
	}

	public void Toggle()
	{
		if (!_enabled() || !_sessionStarted() || _menuInMenu())
			return;

		var panel = EnsurePanel();
		if (panel.Visible)
		{
			Close();
			return;
		}

		panel.Open();
		_panels.PushFocus(panel);
	}

	public void Close()
	{
		if (_panel == null || !_panel.Visible)
			return;

		_panel.Close();
		_panels.OnPanelClosed(_panel);
	}

	public void MarkDirty()
	{
		if (_panel?.Visible == true)
			_panel.Dirty = true;
	}

	public void FlushIfDirty()
	{
		if (_panel?.Visible == true && _panel.Dirty)
			_panel.FlushIfDirty();
	}

	public bool HandleCommand(string cmd)
	{
		if (string.IsNullOrWhiteSpace(cmd))
			return false;

		ApplyResult(ExecuteCommand(cmd));
		return true;
	}

	GameState DebugPanelModule.IHost.State => _state;
	DebugModule.Result DebugPanelModule.IHost.ExecuteSpawnChest() => ApplyResult(DebugModule.SpawnChestAtPlayer(_state));
	DebugModule.Result DebugPanelModule.IHost.ExecuteAddGold(int amount) => ApplyResult(DebugModule.AddGold(_state, amount));
	DebugModule.Result DebugPanelModule.IHost.ExecuteHeal() => ApplyResult(DebugModule.HealPlayer(_state));
	DebugModule.Result DebugPanelModule.IHost.ExecuteToggleGodMode() => ApplyResult(DebugModule.ToggleGodMode(_state));
	DebugModule.Result DebugPanelModule.IHost.ExecuteMoveDownFloor() => ApplyResult(DebugModule.MoveDownFloor(_state, _session));
	DebugModule.Result DebugPanelModule.IHost.ExecuteSpawnDialogTestNpcs() => ApplyResult(DebugModule.SpawnDialogTestNpcsNearPlayer(_state));
	DebugModule.Result DebugPanelModule.IHost.ExecuteSpawnActor(string templateId) => ApplyResult(DebugModule.SpawnActorAtPlayerFacing(_state, templateId));
	DebugModule.Result DebugPanelModule.IHost.ExecuteQueryWeatherStatus() => ApplyResult(DebugModule.QueryWeatherStatus(_state));
	DebugModule.Result DebugPanelModule.IHost.ExecuteLockWeather(WeatherType type, WeatherIntensity intensity) => ApplyResult(DebugModule.LockWeather(_state, type, intensity));
	DebugModule.Result DebugPanelModule.IHost.ExecuteUnlockWeather() => ApplyResult(DebugModule.UnlockWeather(_state));
	DebugModule.Result DebugPanelModule.IHost.ExecuteStepWeather(int turns) => ApplyResult(DebugModule.StepWeather(_state, turns));
	DebugModule.Result DebugPanelModule.IHost.ExecuteClearWeatherAccumulation() => ApplyResult(DebugModule.ClearWeatherAccumulation(_state));
	DebugModule.Result DebugPanelModule.IHost.ExecuteQueryFacilityStatus() => ApplyResult(DebugModule.QueryFacilityStatus(_state));
	DebugModule.Result DebugPanelModule.IHost.ExecutePlaceFacility(string facilityId, string? directionId) => ApplyResult(DebugModule.PlaceFacility(_state, facilityId, directionId));
	DebugModule.Result DebugPanelModule.IHost.ExecuteFacilityDeliver() => ApplyResult(ExecuteFacilityDeliverDebugAction());
	DebugModule.Result DebugPanelModule.IHost.ExecuteFacilityBuild() => ApplyResult(ExecuteFacilityBuildDebugAction());
	DebugModule.Result DebugPanelModule.IHost.ExecuteExportPreset(string scenarioId) => ApplyResult(DebugModule.ExportPreset(_session, scenarioId));
	DebugModule.Result DebugPanelModule.IHost.ExecuteQueryRenderPerfStatus() => BuildRenderPerfStatus();

	private DebugPanelModule EnsurePanel() => _panel ??= _createPanel(this, Close);

	private DebugModule.Result ExecuteCommand(string cmd)
	{
		var parts = cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (parts.Length >= 2 && string.Equals(parts[0], "/facility", StringComparison.OrdinalIgnoreCase))
		{
			switch (parts[1].ToLowerInvariant())
			{
				case "deliver":
					return ExecuteFacilityDeliverDebugAction();
				case "build":
					return ExecuteFacilityBuildDebugAction();
			}
		}

		return DebugModule.HandleCommand(cmd, _state, _session);
	}

	private DebugModule.Result ApplyResult(DebugModule.Result result)
	{
		foreach (var message in result.Logs)
			_log.Add(message);

		if (result.NeedsUiRefresh)
			_markUiDirty();

		MarkDirty();
		if (result.NeedsFlush)
			_flushMap();

		return result;
	}

	private DebugModule.Result BuildRenderPerfStatus()
	{
		var (activeSpriteCount, drawCommandCount, frameTimeAvgMs) = _renderPerfSnapshot();
		return new DebugModule.Result
		{
			Logs =
			[
				$"[perf] active_sprite_count={activeSpriteCount}",
				$"[perf] draw_command_count={drawCommandCount}",
				$"[perf] frame_time_avg_ms={frameTimeAvgMs:F2}",
			],
		};
	}

	private DebugModule.Result ExecuteFacilityDeliverDebugAction()
	{
		var player = ActorModule.GetPlayer(_state);
		if (player == null)
			return new DebugModule.Result { Logs = ["[debug] no player"] };

		if (!FacilityConstructionModule.TryFindNearbyFacility(
			_state,
			player,
			static facility => facility.Stage is FacilityStage.Blueprint or FacilityStage.DeliverMaterials,
			includeCurrentCell: false,
			out var facility)
			|| facility == null)
		{
			return new DebugModule.Result { Logs = ["[debug] no adjacent facility is waiting for materials"] };
		}

		var missing = FacilityConstructionModule.GetMissingConstructionMaterials(facility);
		if (missing.Count == 0)
		{
			return new DebugModule.Result { Logs = ["[debug] adjacent facility is already ready for construction"] };
		}

		var hasAnyMatchingMaterial = missing.Any(requirement =>
			InventoryModule.CountMatching(player, item =>
				!item.Equipped
				&& string.Equals(item.Id, requirement.ItemId, StringComparison.Ordinal)) > 0);
		if (!hasAnyMatchingMaterial)
		{
			return new DebugModule.Result { Logs = ["[debug] no matching construction materials in inventory"] };
		}

		var result = _submitPlayerActionWithResult(TimelinePlayerAction.FacilityDeliver(facility.Id));
		if (!result.ActionConsumed)
			return new DebugModule.Result { Logs = ["[debug] facility delivery failed"] };

		return new DebugModule.Result
		{
			Logs = ["[debug] facility delivery queued"],
			NeedsFlush = true,
			NeedsUiRefresh = true,
		};
	}

	private DebugModule.Result ExecuteFacilityBuildDebugAction()
	{
		var player = ActorModule.GetPlayer(_state);
		if (player == null)
			return new DebugModule.Result { Logs = ["[debug] no player"] };

		if (!FacilityConstructionModule.TryFindNearbyFacility(
			_state,
			player,
			static facility => facility.Stage == FacilityStage.Construct,
			includeCurrentCell: false,
			out var facility)
			|| facility == null)
		{
			return new DebugModule.Result { Logs = ["[debug] no adjacent facility is ready for construction"] };
		}

		var result = _submitPlayerActionWithResult(TimelinePlayerAction.FacilityConstruct(facility.Id));
		if (!result.ActionConsumed)
			return new DebugModule.Result { Logs = ["[debug] facility construction failed"] };

		return new DebugModule.Result
		{
			Logs = ["[debug] facility construction queued"],
			NeedsFlush = true,
			NeedsUiRefresh = true,
		};
	}

}
