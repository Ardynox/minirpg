using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MiniRPG.Core.AI;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Core.Map;
using MiniRPG.Core.Weather;
using MiniRPG.Core.World;
using MiniRPG.Module;
using MiniRPG.Module.Render;
using Xunit;

namespace MiniRPG.Tests;

public sealed class AutoTestRegressionTests
{
	[Fact]
	public void IsDead_TreatsLegacyVitalTagAsVital()
	{
		TestSupport.EnsureGameplayDataLoaded();

		var actor = new Actor
		{
			Id = "legacy_vital_actor",
			DisplayName = "Legacy Vital Actor",
			Limbs =
			[
				new Limb
				{
					Id = "core",
					Name = "Core",
					MaxDurability = 10,
					Durability = 10,
					Material = "flesh",
					BodyPart = BodyParts.Torso,
					Capacities = new Dictionary<string, float>
					{
						[Caps.BloodCirculation] = 1f,
						[Caps.Consciousness] = 1f,
						[Caps.Moving] = 1f,
						[Caps.Metabolism] = 1f,
					},
					Tags = new Dictionary<string, int>
					{
						["\u8981\u5bb3"] = 1,
					},
				},
			],
		};

		var limb = Assert.Single(actor.Limbs);
		Assert.False(limb.Tags.ContainsKey(CombatModule.VitalTag));
		Assert.True(CombatModule.IsVitalLimb(limb));
		Assert.False(CombatModule.IsDead(actor));
	}

	[Fact]
	public void TickAll_RecordsObservers_ForGoblinUsingLegacyVitalData()
	{
		TestSupport.EnsureGameplayDataLoaded();

		var state = CreateLegacyObserverState();
		var player = ActorModule.GetPlayer(state)!;
		var goblin = state.Actors["arena_goblin"];

		Assert.Contains(goblin.Limbs, limb => limb.Tags.ContainsKey("\u8981\u5bb3"));
		Assert.False(CombatModule.IsDead(goblin));

		var scheduledObserverCount = CountScheduledAiObservers(
			state,
			player.X,
			player.Y,
			Math.Max(0, GameConfig.AIVision.ActivationViewRange),
			state.Turn + 1);
		Assert.True(scheduledObserverCount > 0);

		_ = AIDispatcher.TickAll(
			state,
			state.PlayerX,
			state.PlayerY,
			Math.Max(0, GameConfig.AIVision.ActivationViewRange));

		Assert.True(AIVisionBatch.LastMetrics.ObserverCount > 0);
	}

	[Fact]
	public void LoadPresetScenario_QaCombatArena_RestoresVitalLimbsForAiObservers()
	{
		TestSupport.EnsureGameplayDataLoaded();

		var state = new GameState();
		var session = new GameSessionModule(state, new FogOfWarTracker());

		Assert.Equal(SaveLoadStatus.Success, session.LoadPresetScenario("qa_combat_arena"));

		var player = ActorModule.GetPlayer(state)!;
		var goblin = state.Actors["arena_goblin"];
		Assert.False(CombatModule.IsDead(goblin));

		var scheduledObserverCount = CountScheduledAiObservers(
			state,
			player.X,
			player.Y,
			Math.Max(0, GameConfig.AIVision.ActivationViewRange),
			state.Turn + 1);
		Assert.True(scheduledObserverCount > 0);

		_ = TurnModule.Tick(state);

		Assert.True(AIVisionBatch.LastMetrics.ObserverCount > 0);
	}

	[Fact]
	public void WeatherAwarePartialSightProbe_ShrinksSurfaceRange_AndMatchesFogTracker()
	{
		TestSupport.EnsureGameplayDataLoaded();

		var state = CreateVisionProbeState();
		var player = ActorModule.GetPlayer(state)!;
		var tracker = new FogOfWarTracker();
		player.FacingX = 1;
		player.FacingY = 0;

		var sightLimbs = GetCapacityLimbs(player, Caps.Sight);
		Assert.NotEmpty(sightLimbs);
		Assert.True(sightLimbs.Count > 1);
		sightLimbs[0].Durability = 0;
		player.InvalidateCapacityCache();
		var partialSight = player.GetCapacity(Caps.Sight);

		var clearProbe = AutoTestVisionProbeHelper.Create(
			tracker.BaseVisionRadius,
			partialSight,
			tracker.RearVisionRatio,
			tracker.MinimumVisionRadius,
			tracker.MinimumRearVisionRadius,
			tracker.AmbientLight,
			weatherExposed: true,
			new WeatherSample(WeatherType.Clear, WeatherIntensity.Normal));

		state.Weather.DebugOverride = new WeatherDebugOverride
		{
			Type = WeatherType.Fog,
			Intensity = WeatherIntensity.Heavy,
		};
		var fogSample = WeatherRules.GetLocalWeather(state, player.X, player.Y, player.Z);
		var fogProbe = AutoTestVisionProbeHelper.Create(
			tracker.BaseVisionRadius,
			partialSight,
			tracker.RearVisionRatio,
			tracker.MinimumVisionRadius,
			tracker.MinimumRearVisionRadius,
			tracker.AmbientLight,
			weatherExposed: true,
			fogSample);

		Assert.Equal(WeatherType.Fog, fogSample.Type);
		Assert.Equal(WeatherIntensity.Heavy, fogSample.Intensity);
		Assert.True(WeatherRules.GetVisionMultiplier(fogSample) < 1f);
		Assert.True(fogProbe.EffectiveBaseVisionRadius < clearProbe.EffectiveBaseVisionRadius);
		Assert.True(fogProbe.PartialVisibleForwardOffset < clearProbe.PartialVisibleForwardOffset);
		Assert.True(fogProbe.PartialHiddenForwardOffset < clearProbe.PartialHiddenForwardOffset);

		var fogVisibleX = player.X + fogProbe.PartialVisibleForwardOffset;
		var fogHiddenX = player.X + fogProbe.PartialHiddenForwardOffset;
		state.World!.SetTerrain(fogVisibleX, player.Y, player.Z, Terrains.Floor);
		state.World.SetTerrain(fogHiddenX, player.Y, player.Z, Terrains.Floor);
		tracker.Clear();
		tracker.Update(state);
		Assert.Equal(PlayerVisionBand.Focused, tracker.GetVisionBand(fogVisibleX, player.Y, player.Z));
		Assert.Equal(PlayerVisionBand.Unknown, tracker.GetVisionBand(fogHiddenX, player.Y, player.Z));

		var shelteredProbe = AutoTestVisionProbeHelper.Create(
			tracker.BaseVisionRadius,
			partialSight,
			tracker.RearVisionRatio,
			tracker.MinimumVisionRadius,
			tracker.MinimumRearVisionRadius,
			tracker.AmbientLight,
			weatherExposed: false,
			fogSample);
		Assert.Equal(clearProbe.EffectiveBaseVisionRadius, shelteredProbe.EffectiveBaseVisionRadius);
		Assert.Equal(clearProbe.PartialVisibleForwardOffset, shelteredProbe.PartialVisibleForwardOffset);
		Assert.Equal(clearProbe.PartialHiddenForwardOffset, shelteredProbe.PartialHiddenForwardOffset);

		player.Z = 1;
		state.PlayerZ = 1;
		var undergroundVisibleX = player.X + shelteredProbe.PartialVisibleForwardOffset;
		var undergroundHiddenX = player.X + shelteredProbe.PartialHiddenForwardOffset;
		state.World.SetTerrain(player.X, player.Y, player.Z, Terrains.Floor);
		state.World.SetTerrain(undergroundVisibleX, player.Y, player.Z, Terrains.Floor);
		state.World.SetTerrain(undergroundHiddenX, player.Y, player.Z, Terrains.Floor);
		tracker.Clear();
		tracker.Update(state);
		Assert.Equal(PlayerVisionBand.Focused, tracker.GetVisionBand(undergroundVisibleX, player.Y, player.Z));
		Assert.Equal(PlayerVisionBand.Unknown, tracker.GetVisionBand(undergroundHiddenX, player.Y, player.Z));
	}

	[Fact]
	public void SaveLoad_RoundTripsWeatherFrontPhase_AfterAdvanceWorld()
	{
		TestSupport.EnsureGameplayDataLoaded();
		using var continueStateScope = new ContinueStateScope();
		var root = TestSupport.CreateTempDirectory("autotest-weather-roundtrip");

		try
		{
			var state = new GameState();
			var session = new GameSessionModule(state, new FogOfWarTracker(), root);
			session.NewGame();

			Assert.NotNull(ActorModule.GetPlayer(state));
			TurnModule.AdvanceWorld(state);
			var phaseBeforeSave = state.Weather.FrontPhase;

			var savePath = Path.Combine(root, "save", "weather-roundtrip.json");
			session.SaveGame(savePath);

			var saveFile = SaveModule.DeserializeSaveFile(File.ReadAllText(savePath));
			Assert.NotNull(saveFile);
			Assert.NotNull(saveFile!.Payload.Weather);
			Assert.Equal(phaseBeforeSave, saveFile.Payload.Weather!.FrontPhase, 4);

			var restoredState = new GameState();
			var restoredSession = new GameSessionModule(restoredState, new FogOfWarTracker(), root);
			var loadStatus = restoredSession.LoadGame(savePath);

			Assert.Equal(SaveLoadStatus.Success, loadStatus);
			Assert.NotNull(restoredState.Weather);
			Assert.Equal(saveFile.Payload.Weather.FrontPhase, restoredState.Weather.FrontPhase, 4);
			Assert.Equal(phaseBeforeSave, restoredState.Weather.FrontPhase, 4);
		}
		finally
		{
			TestSupport.TryDeleteDirectory(root);
		}
	}

	private static GameState CreateLegacyObserverState()
	{
		var state = new GameState
		{
			WorldSeed = 424242,
			PlayerId = "player",
			PlayerX = 1,
			PlayerY = 1,
			PlayerZ = 0,
			GeneratorId = "flat_floor",
			ViewModeId = "single_layer",
			Actors = new Dictionary<string, Actor>(StringComparer.Ordinal),
			World = new WorldMap(424242, new FlatFloorGenerator()),
			Weather = WeatherState.CreateDefault(424242),
		};
		state.Weather.DebugOverride = new WeatherDebugOverride
		{
			Type = WeatherType.Clear,
			Intensity = WeatherIntensity.Normal,
		};

		var player = PresetDB.SpawnActor("player", "player");
		player.X = state.PlayerX;
		player.Y = state.PlayerY;
		player.Z = state.PlayerZ;
		player.FacingX = 1;
		player.FacingY = 0;
		player.BrainId = null;

		var goblin = PresetDB.SpawnActor("goblin", "arena_goblin");
		goblin.X = 3;
		goblin.Y = 1;
		goblin.Z = 0;
		goblin.FacingX = -1;
		goblin.FacingY = 0;
		goblin.BrainId = "simple";

		ActorModule.Add(state, player);
		ActorModule.Add(state, goblin);
		return state;
	}

	private static GameState CreateVisionProbeState()
	{
		var state = new GameState
		{
			WorldSeed = 20260408,
			PlayerId = "player",
			PlayerX = 1,
			PlayerY = 1,
			PlayerZ = 0,
			GeneratorId = "flat_floor",
			ViewModeId = "single_layer",
			Actors = new Dictionary<string, Actor>(StringComparer.Ordinal),
			World = new WorldMap(20260408, new FlatFloorGenerator()),
			Weather = WeatherState.CreateDefault(20260408),
		};

		var player = PresetDB.SpawnActor("player", "player");
		player.X = state.PlayerX;
		player.Y = state.PlayerY;
		player.Z = state.PlayerZ;
		player.FacingX = 1;
		player.FacingY = 0;
		player.BrainId = null;
		ActorModule.Add(state, player);
		return state;
	}

	private static List<Limb> GetCapacityLimbs(Actor actor, string capacityId) =>
		actor.Limbs
			.Where(limb => limb.Capacities.TryGetValue(capacityId, out var weight) && weight > 0f)
			.ToList();

	private static int CountScheduledAiObservers(
		GameState state,
		int viewCenterX,
		int viewCenterY,
		int viewRange,
		int turnNumber)
	{
		var simplifiedUpdateInterval = Math.Max(1, GameConfig.AIVision.SimplifiedUpdateIntervalTurns);
		var count = 0;

		foreach (var actor in state.Actors.Values)
		{
			if (actor.BrainId == null || actor.Id == state.PlayerId || CombatModule.IsDead(actor))
				continue;

			var detail = AIDispatcher.Classify(state, actor, viewCenterX, viewCenterY, viewRange);
			if (detail == SimDetail.Summary && actor.AwarenessState != AwarenessState.Idle)
				detail = SimDetail.Simplified;
			if (detail == SimDetail.Summary)
				continue;
			if (detail == SimDetail.Simplified
				&& actor.AwarenessState == AwarenessState.Idle
				&& turnNumber % simplifiedUpdateInterval != 0)
			{
				continue;
			}

			count++;
		}

		return count;
	}

	private sealed class FlatFloorGenerator : IMapGenerator
	{
		public string Id => "flat_floor";
		public string Name => "Flat Floor";

		public void GenerateChunk(ChunkData chunk, int worldSeed)
		{
			chunk.Fill(TerrainRegistry.GetId(Terrains.Floor));
		}

		public void PopulateChunk(ChunkData chunk, int worldSeed)
		{
		}
	}
}
