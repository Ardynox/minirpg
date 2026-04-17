using System;
using System.Collections.Generic;
using MiniRPG.Core.Data;
using MiniRPG.Core.Debug;
using MiniRPG.Core.Facility;
using MiniRPG.Core.Weather;
using MiniRPG.Core.World;
using MiniRPG.Module;
using MiniRPG.Module.Render;
using Xunit;

namespace MiniRPG.Tests;

public sealed class DebugModuleTests
{
	public DebugModuleTests()
	{
		TestSupport.EnsureGameplayDataLoaded();
	}

	[Fact]
	public void AddGold_AndHandleCommand_RemainCompatible()
	{
		var (state, player) = CreateDebugState();
		var root = TestSupport.CreateTempDirectory("debug-module-gold");
		try
		{
			var session = new GameSessionModule(state, new FogOfWarTracker(), root);
			var initialGold = player.Gold;

			var structured = DebugModule.AddGold(state, 250);
			var textCommand = DebugModule.HandleCommand("/gold 100", state, session);

			Assert.Equal(initialGold + 350, player.Gold);
			Assert.Contains($"[debug] +250 gold (total: {initialGold + 250})", structured.Logs);
			Assert.Contains($"[debug] +100 gold (total: {initialGold + 350})", textCommand.Logs);
			Assert.True(structured.NeedsUiRefresh);
		}
		finally
		{
			TestSupport.TryDeleteDirectory(root);
		}
	}

	[Fact]
	public void LockWeather_UpdatesOverride_AndStatusSummary()
	{
		var (state, _) = CreateDebugState();

		var result = DebugModule.LockWeather(state, WeatherType.Snow, WeatherIntensity.Heavy);
		var summary = DebugModule.BuildWeatherStatusLines(state);

		Assert.NotNull(state.Weather);
		Assert.NotNull(state.Weather!.DebugOverride);
		Assert.Equal(WeatherType.Snow, state.Weather.DebugOverride!.Type);
		Assert.Equal(WeatherIntensity.Heavy, state.Weather.DebugOverride.Intensity);
		Assert.Contains("snow/heavy", summary[0], StringComparison.OrdinalIgnoreCase);
		Assert.True(result.NeedsFlush);
	}

	[Fact]
	public void SetTurn_WritesAbsoluteTurn_AndRequestsRefresh()
	{
		var (state, _) = CreateDebugState();

		var result = DebugModule.SetTurn(state, 245);

		Assert.Equal(245, state.Turn);
		Assert.True(result.NeedsFlush);
		Assert.True(result.NeedsUiRefresh);
		Assert.Contains("turn set to 245", result.Logs[0], StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void ToggleSurfaceFreeMove_AndHandleCommand_RemainCompatible()
	{
		var (state, _) = CreateDebugState();
		var root = TestSupport.CreateTempDirectory("debug-module-surface-move");
		try
		{
			var session = new GameSessionModule(state, new FogOfWarTracker(), root);

			var structured = DebugModule.ToggleSurfaceFreeMove(state);
			var textCommand = DebugModule.HandleCommand("/surface_move", state, session);

			Assert.True(structured.NeedsFlush);
			Assert.True(structured.NeedsUiRefresh);
			Assert.Contains("enabled", structured.Logs[0], StringComparison.OrdinalIgnoreCase);
			Assert.Contains("disabled", textCommand.Logs[0], StringComparison.OrdinalIgnoreCase);
			Assert.False(state.RuntimeSurfaceFreeMove);
		}
		finally
		{
			TestSupport.TryDeleteDirectory(root);
		}
	}

	[Fact]
	public void SetTimeOfDay_PreservesCurrentDayIndex()
	{
		var (state, _) = CreateDebugState();
		state.Turn = DayNightCycle.TurnsPerDay * 3 + 41;

		var result = DebugModule.SetTimeOfDay(state, 7);

		Assert.Equal(DayNightCycle.TurnsPerDay * 3 + 7, state.Turn);
		Assert.True(result.NeedsFlush);
		Assert.True(result.NeedsUiRefresh);
		Assert.Equal(7, DebugModule.GetCurrentTimeOfDay(state));
	}

	[Fact]
	public void PlaceFacility_CreatesBlueprint_AndStatusSummaryReflectsIt()
	{
		var (state, player) = CreateDebugState();

		var result = DebugModule.PlaceFacility(state, FacilityIds.Stove, "east");

		Assert.True(result.NeedsFlush);
		Assert.True(state.World!.TryGetFacilityAt(player.X + 1, player.Y, player.Z, out var facility));
		Assert.NotNull(facility);

		var status = DebugModule.BuildFacilityStatusLines(state);
		Assert.Contains("facility", status[0], StringComparison.OrdinalIgnoreCase);
		Assert.Contains("stage=", status[1], StringComparison.OrdinalIgnoreCase);
	}

	private static (GameState State, Actor Player) CreateDebugState()
	{
		var state = new GameState
		{
			WorldSeed = 4242,
			PlayerId = "player",
			PlayerX = 10,
			PlayerY = 10,
			PlayerZ = 0,
			World = new WorldMap(4242, new FloorGenerator()),
			Actors = new Dictionary<string, Actor>(StringComparer.Ordinal),
		};

		var player = PresetDB.SpawnActor("player", "player");
		player.X = state.PlayerX;
		player.Y = state.PlayerY;
		player.Z = state.PlayerZ;
		player.FacingX = 1;
		player.FacingY = 0;
		player.BrainId = null;
		player.PrimaryDomainId = DomainIds.Player;
		ActorModule.Add(state, player);
		return (state, player);
	}

	private sealed class FloorGenerator : IMapGenerator
	{
		public string Id => "floor";
		public string Name => "Floor";

		public void GenerateChunk(ChunkData chunk, int worldSeed)
		{
			chunk.Fill(TerrainRegistry.GetId(Terrains.Floor));
		}

		public void PopulateChunk(ChunkData chunk, int worldSeed)
		{
		}
	}
}
