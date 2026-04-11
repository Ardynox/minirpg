using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Core.Weather;
using MiniRPG.Core.World;
using MiniRPG.Module;

namespace MiniRPG;

public partial class Main : IAutoTestHost
{
	GameSessionModule IAutoTestHost.Session => _session;
	FogOfWarTracker IAutoTestHost.FogTracker => _fogTracker;
	DebugConfig IAutoTestHost.AutoTestConfig => _autoTestRuntimeConfig;
	bool IAutoTestHost.ResourcesReady => ResourcesReady;

	Task IAutoTestHost.StartDefaultNewGameAsync()
	{
		DoStartNewGame(PlayerCreationOptions.CreateDefault());
		return Task.CompletedTask;
	}

	Task<SaveLoadStatus> IAutoTestHost.LoadPresetScenarioAsync(string scenarioId) =>
		Task.FromResult(LoadPresetScenarioForAutoTest(scenarioId));

	void IAutoTestHost.EnterGame() => DoEnterGame();
	void IAutoTestHost.ExecuteCommand(string command) => OnCommand(command);
	void IAutoTestHost.FlushMap() => FlushMap();
	void IAutoTestHost.Dispatch(List<GameEvent> events) => Dispatch(events);
	AutoTestRuntimeSnapshot IAutoTestHost.CaptureSnapshot() => CaptureAutoTestSnapshot();
	Task IAutoTestHost.WaitStepAsync(float? delaySeconds) => WaitForAutoTestStepAsync(delaySeconds);

	private SaveLoadStatus LoadPresetScenarioForAutoTest(string scenarioId)
	{
		PrepareSessionTransition(clearLogs: true);
		var status = _session.LoadPresetScenario(scenarioId);
		if (status != SaveLoadStatus.Success)
			return status;

		FinalizeSessionPanels(openSkillBar: true);
		SyncTimelineAutoAdvanceState();
		return status;
	}

	private async Task WaitForAutoTestStepAsync(float? delaySeconds)
	{
		var delay = delaySeconds ?? _autoTestRuntimeConfig.AutoTestStepDelay;
		var tree = GetTree();
		if (delay > 0f)
			await tree.ToSignal(tree.CreateTimer(delay), SceneTreeTimer.SignalName.Timeout);
		else
			await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
	}

	private AutoTestRuntimeSnapshot CaptureAutoTestSnapshot()
	{
		var player = ActorModule.GetPlayer(_state);
		var groundItemCount = _state.World == null
			? 0
			: MapModule.PeekGroundItems(_state, _state.PlayerX, _state.PlayerY).Count;
		var viewport = IsInsideTree() ? GetViewport() : null;
		var viewportSize = viewport?.GetVisibleRect().Size ?? Vector2.Zero;
		var mapViewportSize = _mapRender?.MapViewportSize ?? Vector2I.Zero;
		var weatherExposed = _state.World != null
			&& player != null
			&& _state.World.IsWeatherExposed(player.X, player.Y, player.Z);
		var weatherSample = _state.World != null && player != null
			? WeatherRules.GetLocalWeather(_state, player.X, player.Y, player.Z)
			: default(WeatherSample?);
		var displayServerName = GetCurrentDisplayServerName();

		return new AutoTestRuntimeSnapshot
		{
			GameStarted = _session?.GameStarted ?? false,
			ResourcesReady = ResourcesReady,
			RenderReady = RenderReady,
			DisplayServerName = displayServerName,
			HeadlessMode = AutoTestCli.IsHeadlessDisplayServer(displayServerName),
			InvokedFromCli = IsAutoTestCliEnabled,
			StartupState = (_startupCoordinator?.State ?? StartupState.LoadingHeavyAssets).ToString().ToLowerInvariant(),
			StartupSyncFallbackUsed = false,
			StartupLoadPath = _startupCoordinator?.LastFailedPath,
			Turn = _state.Turn,
			PlayerId = player?.Id,
			PlayerPos = player == null
				? null
				: new AutoTestPlayerPosition { X = player.X, Y = player.Y, Z = player.Z },
			PlayerGold = player?.Gold ?? 0,
			PlayerDead = PlayerDead,
			ActorCount = _state.Actors.Count,
			CurrentPresetScenarioId = _session?.CurrentPresetScenarioId,
			CurrentSavePath = _session?.CurrentSavePath,
			WatchMode = _watchModeEnabled,
			TimelineAutoAdvancePending = _timelineAutoAdvancePending,
			BusyOperationActive = _busyOperationActive,
			InputFocus = _inputModule == null ? string.Empty : _inputModule.Focus.ToString().ToLowerInvariant(),
			FocusedPanelId = _panels?.FocusedId,
			InventoryOpen = InventoryOpen,
			ChestOpen = ChestOpen,
			DialogOpen = _dialogUI?.InDialog == true,
			DialogOptionCount = _dialogPanel?.Visible == true ? _dialogPanel.OptionCount : 0,
			TradeOpen = _tradeUI?.InTrade == true,
			TradeCurrentTab = _tradePanel?.Visible == true
				? _tradePanel.CurrentTab.ToString().ToLowerInvariant()
				: null,
			TradeItemCount = _tradePanel?.Visible == true ? _tradePanel.ItemCount : 0,
			GroundItemCount = groundItemCount,
			WeatherTypeId = weatherSample?.TypeId,
			WeatherIntensityId = weatherSample?.IntensityId,
			WeatherExposed = weatherExposed,
			ViewportWidth = Mathf.RoundToInt(viewportSize.X),
			ViewportHeight = Mathf.RoundToInt(viewportSize.Y),
			MapViewportWidth = mapViewportSize.X,
			MapViewportHeight = mapViewportSize.Y,
			LastLogLine = _log?.LastLine,
		};
	}
}
