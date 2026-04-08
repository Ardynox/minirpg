using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.AI;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;
using MiniRPG.Core.Health;
using MiniRPG.Core.Needs;
using MiniRPG.Core.Weather;
using MiniRPG.Core.World;
using Xunit;

namespace MiniRPG.Tests;

public sealed class HealthSystemTests
{
	public HealthSystemTests()
	{
		TestSupport.EnsureGameplayDataLoaded();
	}

	[Fact]
	public void AddOrUpdateInjury_UsesConditionSeverityScale()
	{
		var actor = CreateActor("hero", "human");
		var condition = HealthSystem.AddOrUpdateInjury(actor, HealthConditionIds.CutWound, actor.Limbs[0].Id, 2f, "test", 0);

		Assert.Equal(8f, condition.Severity, 3);
	}

	[Fact]
	public void Sync_ProgressesBleedingAndProfileSuppressions()
	{
		var dirtyExposure = new EnvironmentExposureSnapshot(
			IsIndoors: false,
			Cleanliness: 0f,
			SleepSurfaceQuality: 0f,
			WetnessDelta: 0f,
			AmbientTemperature: 21f,
			ShelterStrength: 0f,
			HeatSourceTemperatureBonus: 0f,
			DryingBonus: 0f,
			HasWeatherData: false);

		var human = CreateActor("human_actor", "human");
		HealthSystem.AddOrUpdateInjury(human, HealthConditionIds.CutWound, human.Limbs[0].Id, 6f, "test", 0);
		HealthSystem.Sync(human, 5, dirtyExposure);
		Assert.True(human.PainValue > 0f);
		Assert.True(human.BloodLossValue > 0f);
		Assert.Contains(human.HealthConditions, condition => condition.Id == HealthConditionIds.Infection);

		var treant = CreateActor("treant_actor", "treant", material: "wood");
		HealthSystem.AddOrUpdateInjury(treant, HealthConditionIds.CutWound, treant.Limbs[0].Id, 6f, "test", 0);
		HealthSystem.Sync(treant, 5, dirtyExposure);
		Assert.True(treant.PainValue > 0f);
		Assert.Equal(0f, treant.BloodLossValue);
		Assert.DoesNotContain(treant.HealthConditions, condition => condition.Id == HealthConditionIds.Infection);

		var undead = CreateActor("undead_actor", "undead");
		HealthSystem.AddOrUpdateInjury(undead, HealthConditionIds.CutWound, undead.Limbs[0].Id, 6f, "test", 0);
		HealthSystem.Sync(undead, 5, dirtyExposure);
		Assert.Equal(0f, undead.PainValue);
		Assert.Equal(0f, undead.BloodLossValue);
	}

	[Fact]
	public void TryTendSelf_UsesBestMedicalSupply()
	{
		var state = CreateState();
		var actor = CreateActor("doctor", "human", faction: Factions.Friendly);
		actor.Inventory.Add(PresetDB.CloneItem("bandage"));
		actor.Inventory.Add(PresetDB.CloneItem("herbal_medicine"));
		HealthSystem.AddOrUpdateInjury(actor, HealthConditionIds.CutWound, actor.Limbs[0].Id, 4f, "test", 0);
		ActorModule.Add(state, actor);

		var result = HealthActionModule.TryTendSelf(state, actor);

		Assert.True(result.Consumed);
		Assert.DoesNotContain(actor.Inventory, item => item.Id == "herbal_medicine");
		Assert.Contains(actor.Inventory, item => item.Id == "bandage");
		var treated = Assert.Single(actor.HealthConditions, condition => condition.Id == HealthConditionIds.CutWound);
		Assert.True(treated.TendedOnTurn >= 0);
		Assert.True(treated.TendedQuality > 0f);
		Assert.Contains(result.Events, evt => evt.Type == "treatment_applied");
	}

	[Fact]
	public void RestCompletion_AppliesSleepHygieneThoughts()
	{
		var state = CreateState();
		state.World!.SetTerrain(1, 1, 0, Terrains.Water);
		var actor = CreateActor("sleeper", "human", faction: Factions.Player);
		actor.Inventory.Add(PresetDB.CloneItem("bedroll"));
		NeedSystem.SetNeedValue(actor, NeedIds.Rest, 70f);
		ActorModule.Add(state, actor);

		var result = NeedActionModule.TryRest(state, actor, RestContext.ForPlayerBedroll(NeedActionModule.GetBedrollQuality(actor)));

		Assert.True(result.Consumed);
		Assert.Contains(actor.Thoughts, thought => thought.Id == "slept_bedroll");
		Assert.Contains(actor.Thoughts, thought => thought.Id == "slept_wet");
		Assert.Contains(actor.Thoughts, thought => thought.Id == "slept_dirty");
		Assert.Contains(result.Events, evt => evt.Type == "rest_completed");
	}

	[Fact]
	public void HealthBehavior_SelfTendsBeforeHelpingOthers()
	{
		var state = CreateState();
		var healer = CreateActor("healer", "human", faction: Factions.Friendly, x: 2, y: 2);
		var patient = CreateActor("patient", "human", faction: Factions.Friendly, x: 3, y: 2);
		healer.Inventory.Add(PresetDB.CloneItem("bandage"));
		HealthSystem.AddOrUpdateInjury(healer, HealthConditionIds.CutWound, healer.Limbs[0].Id, 4f, "test", 0);
		HealthSystem.AddOrUpdateInjury(patient, HealthConditionIds.CutWound, patient.Limbs[0].Id, 8f, "test", 0);
		ActorModule.Add(state, healer);
		ActorModule.Add(state, patient);

		var perception = PerceptionBuilder.Build(state, healer, SimDetail.Full);
		var result = HealthBehaviorModule.TryExecute(state, healer, perception, tickBuffs: false);

		Assert.True(result.Consumed);
		Assert.True(healer.HealthConditions.Single(condition => condition.Id == HealthConditionIds.CutWound).TendedOnTurn >= 0);
		Assert.True(patient.HealthConditions.Single(condition => condition.Id == HealthConditionIds.CutWound).TendedOnTurn < 0);
	}

	[Fact]
	public void TimelineTurnManager_EmitsHealthDeathAndRemovesActor()
	{
		var state = CreateState();
		var player = CreateActor("player", "human", faction: Factions.Player, brainId: null, x: 1, y: 1);
		var enemy = CreateActor("enemy", "human", faction: Factions.Hostile, x: 2, y: 1);
		ActorModule.Add(state, player);
		ActorModule.Add(state, enemy);
		state.PlayerId = player.Id;
		state.PlayerX = player.X;
		state.PlayerY = player.Y;
		state.PlayerZ = player.Z;
		HealthSystem.EnsureInitialized(enemy, 0);
		enemy.BloodLossValue = 100f;
		state.Timeline.CurrentActorId = enemy.Id;
		state.Timeline.Actors.Add(new TimelineActorState { ActorId = player.Id, Charge = 50f });
		state.Timeline.Actors.Add(new TimelineActorState { ActorId = enemy.Id, Charge = TimelineTurnManager.ActionThreshold });

		var result = TimelineTurnManager.AdvanceAuto(state, watchModeEnabled: false);

		Assert.True(result.ActionConsumed, $"acting={result.ActingActorId ?? "<null>"} events={string.Join(",", result.Events.Select(evt => evt.Type))} current={state.Timeline.CurrentActorId ?? "<null>"}");
		Assert.Contains(result.Events, evt => evt.Type == "death_blood_loss" && evt.TargetId == enemy.Id);
		Assert.DoesNotContain(enemy.Id, state.Actors.Keys);
	}

	[Fact]
	public void Sync_WeatherExposureCanCauseHypothermia_AndClothingMitigates()
	{
		var state = CreateState();
		state.Weather.DebugOverride = new WeatherDebugOverride
		{
			Type = WeatherType.Snow,
			Intensity = WeatherIntensity.Heavy,
		};
		var (x, y) = FindSurfaceCell(state, static sample => sample.AmbientTemperatureC <= -8f);
		EnsureFloor(state, x, y);
		EnsureFloor(state, x + 1, y);

		var exposed = CreateWeatherActor("cold_exposed", x, y);
		var clothed = CreateWeatherActor("cold_clothed", x + 1, y);
		Equip(clothed, "parka");
		Equip(clothed, "tuque");

		HealthSystem.Sync(exposed, 5, DefaultEnvironmentExposureProvider.Instance.Capture(state, exposed));
		HealthSystem.Sync(clothed, 5, DefaultEnvironmentExposureProvider.Instance.Capture(state, clothed));

		var exposedHypothermia = Assert.Single(exposed.HealthConditions, condition => condition.Id == HealthConditionIds.Hypothermia);
		var clothedHypothermia = clothed.HealthConditions.FirstOrDefault(condition => condition.Id == HealthConditionIds.Hypothermia);
		Assert.True(exposedHypothermia.Severity > 0f);
		Assert.True((clothedHypothermia?.Severity ?? 0f) < exposedHypothermia.Severity);
	}

	[Fact]
	public void Sync_CampfireMitigatesHypothermia()
	{
		var state = CreateState();
		state.Weather.DebugOverride = new WeatherDebugOverride
		{
			Type = WeatherType.Snow,
			Intensity = WeatherIntensity.Heavy,
		};
		var (x, y) = FindSurfaceCell(state, static sample => sample.AmbientTemperatureC <= -8f);
		EnsureFloor(state, x, y);
		EnsureFloor(state, x + 1, y);
		EnsureFloor(state, x + 2, y);

		var exposed = CreateWeatherActor("campfire_exposed", x, y);
		var warmed = CreateWeatherActor("campfire_warmed", x + 1, y);
		state.World!.SetFixture(x + 2, y, 0, "*", Entities.Campfire);

		HealthSystem.Sync(exposed, 5, DefaultEnvironmentExposureProvider.Instance.Capture(state, exposed));
		HealthSystem.Sync(warmed, 5, DefaultEnvironmentExposureProvider.Instance.Capture(state, warmed));

		var exposedHypothermia = Assert.Single(exposed.HealthConditions, condition => condition.Id == HealthConditionIds.Hypothermia);
		var warmedHypothermia = warmed.HealthConditions.FirstOrDefault(condition => condition.Id == HealthConditionIds.Hypothermia);
		Assert.True((warmedHypothermia?.Severity ?? 0f) < exposedHypothermia.Severity);
	}

	[Fact]
	public void Sync_ClosedIndoorRoomRetainsHeatBetterThanOpenGround()
	{
		var state = CreateState();
		state.Weather.DebugOverride = new WeatherDebugOverride
		{
			Type = WeatherType.Snow,
			Intensity = WeatherIntensity.Heavy,
		};
		var (x, y) = FindSurfaceCell(state, static sample => sample.AmbientTemperatureC <= -8f);
		EnsureFloor(state, x, y);
		BuildRoofedRoom(state, x + 3, y - 1, width: 3, height: 3);

		var exposed = CreateWeatherActor("open_ground", x, y);
		var indoor = CreateWeatherActor("indoor_room", x + 4, y);

		HealthSystem.Sync(exposed, 5, DefaultEnvironmentExposureProvider.Instance.Capture(state, exposed));
		HealthSystem.Sync(indoor, 5, DefaultEnvironmentExposureProvider.Instance.Capture(state, indoor));

		var exposedHypothermia = Assert.Single(exposed.HealthConditions, condition => condition.Id == HealthConditionIds.Hypothermia);
		var indoorHypothermia = indoor.HealthConditions.FirstOrDefault(condition => condition.Id == HealthConditionIds.Hypothermia);
		Assert.True((indoorHypothermia?.Severity ?? 0f) < exposedHypothermia.Severity);
	}

	[Fact]
	public void Sync_WeatherExposureCanCauseHeatstroke_WhileShelterPreventsIt()
	{
		var state = CreateState();
		var (x, y) = FindSurfaceCell(state, static sample => sample.AmbientTemperatureC >= 36.05f);
		EnsureFloor(state, x, y);
		EnsureFloor(state, x + 1, y);
		EnsureFloor(state, x + 2, y);

		var exposed = CreateWeatherActor("hot_exposed", x, y);
		var clothed = CreateWeatherActor("hot_clothed", x + 1, y);
		var sheltered = CreateWeatherActor("hot_sheltered", x + 2, y);
		Equip(clothed, "duster");
		state.World!.SetFixture(sheltered.X, sheltered.Y, sheltered.Z, "H", Entities.House);

		HealthSystem.Sync(exposed, 5, DefaultEnvironmentExposureProvider.Instance.Capture(state, exposed));
		HealthSystem.Sync(clothed, 5, DefaultEnvironmentExposureProvider.Instance.Capture(state, clothed));
		HealthSystem.Sync(sheltered, 5, DefaultEnvironmentExposureProvider.Instance.Capture(state, sheltered));

		var exposedHeatstroke = Assert.Single(exposed.HealthConditions, condition => condition.Id == HealthConditionIds.Heatstroke);
		var clothedHeatstroke = clothed.HealthConditions.FirstOrDefault(condition => condition.Id == HealthConditionIds.Heatstroke);
		Assert.True(exposedHeatstroke.Severity > 0f);
		Assert.True((clothedHeatstroke?.Severity ?? 0f) < exposedHeatstroke.Severity);
		Assert.DoesNotContain(sheltered.HealthConditions, condition => condition.Id == HealthConditionIds.Heatstroke);
	}

	private static GameState CreateState()
	{
		return new GameState
		{
			PlayerId = "player",
			PlayerX = 1,
			PlayerY = 1,
			PlayerZ = 0,
			WorldSeed = 1234,
			World = new WorldMap(1234, new FloorGenerator()),
		};
	}

	private static Actor CreateActor(
		string id,
		string raceId,
		string faction = Factions.Friendly,
		string? brainId = "simple",
		int x = 1,
		int y = 1,
		string material = "flesh")
	{
		var actor = new Actor
		{
			Id = id,
			DisplayName = id,
			Faction = faction,
			BrainId = brainId,
			X = x,
			Y = y,
			Z = 0,
			Race = new Race
			{
				Id = raceId,
				Name = raceId,
				NeedProfileId = NeedCatalog.ResolveProfileId(raceId, null),
				HealthProfileId = HealthCatalog.ResolveProfileId(raceId, null),
			},
			Limbs =
			[
				new Limb
				{
					Id = $"{id}_core",
					Name = "Core",
					MaxDurability = 10,
					Durability = 10,
					Material = material,
					BodyPart = BodyParts.Torso,
					Capacities = new Dictionary<string, float>
					{
						[Caps.BloodCirculation] = 1f,
						[Caps.Moving] = 1f,
						[Caps.Consciousness] = 1f,
						[Caps.Metabolism] = 1f,
						[Caps.Sight] = 1f,
						[Caps.Manipulation] = 1f,
					},
					Tags = new Dictionary<string, int>
					{
						[CombatModule.VitalTag] = 1,
					},
				},
			],
		};
		NeedSystem.EnsureInitialized(actor, 0);
		HealthSystem.EnsureInitialized(actor, 0);
		return actor;
	}

	private static Actor CreateWeatherActor(string id, int x, int y)
	{
		var actor = CreateActor(id, "human", x: x, y: y);
		actor.Limbs =
		[
			CreateBodyLimb($"{id}_head", BodyParts.Head),
			CreateBodyLimb($"{id}_torso", BodyParts.Torso),
			CreateBodyLimb($"{id}_arm", BodyParts.Arm),
			CreateBodyLimb($"{id}_leg", BodyParts.Leg),
		];
		HealthSystem.EnsureInitialized(actor, 0);
		return actor;
	}

	private static Limb CreateBodyLimb(string id, string bodyPart) => new()
	{
		Id = id,
		Name = bodyPart,
		MaxDurability = 10,
		Durability = 10,
		Material = "flesh",
		BodyPart = bodyPart,
		Capacities = new Dictionary<string, float>
		{
			[Caps.BloodCirculation] = 1f,
			[Caps.Moving] = 1f,
			[Caps.Consciousness] = 1f,
			[Caps.Metabolism] = 1f,
			[Caps.Manipulation] = 1f,
		},
		Tags = new Dictionary<string, int>(),
	};

	private static void Equip(Actor actor, string itemId)
	{
		var item = PresetDB.CloneItem(itemId);
		item.Equipped = true;
		actor.Inventory.Add(item);
	}

	private static (int X, int Y) FindSurfaceCell(GameState state, Func<WeatherSample, bool> predicate)
	{
		for (var cx = 0; cx < 256; cx++)
		{
			for (var cy = 0; cy < 256; cy++)
			{
				var x = cx * CoordUtil.ChunkSize + 1;
				var y = cy * CoordUtil.ChunkSize + 1;
				var sample = WeatherFieldSampler.Sample(state, x, y, 0, state.Turn, state.Weather.FrontPhase);
				if (predicate(sample))
					return (x, y);
			}
		}

		throw new InvalidOperationException("Unable to find a matching weather sample.");
	}

	private static void EnsureFloor(GameState state, int x, int y, int z = 0) =>
		state.World!.SetTerrain(x, y, z, Terrains.Floor);

	private static void BuildRoofedRoom(GameState state, int left, int top, int width, int height)
	{
		for (var x = left; x < left + width; x++)
		{
			for (var y = top; y < top + height; y++)
			{
				var isBorder = x == left || x == left + width - 1 || y == top || y == top + height - 1;
				state.World!.SetTerrain(x, y, 0, isBorder ? Terrains.WallStone : Terrains.Floor);
				if (!isBorder)
					state.World.SetFixture(x, y, 0, "H", Entities.House);
			}
		}
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
