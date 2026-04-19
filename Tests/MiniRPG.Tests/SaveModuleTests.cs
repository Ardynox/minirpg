using System;
using System.Collections.Generic;
using System.IO;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;
using MiniRPG.Core.Facility;
using MiniRPG.Core.Health;
using MiniRPG.Core.Map;
using MiniRPG.Core.Multiplayer;
using MiniRPG.Core.Needs;
using MiniRPG.Core.Weather;
using MiniRPG.Core.World;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace MiniRPG.Tests;

public sealed class SaveModuleTests
{
	public SaveModuleTests()
	{
		SkillCastingTestHelper.EnsureGameDataLoaded();
	}

	[Fact]
	public void BuildSnapshot_And_ApplySnapshot_RoundTripRuntimeStateAndDirtyChunks()
	{
		ResetSaveCache();
		var state = CreateSampleState();

		var saveFile = SaveModule.BuildSnapshot(state);
		var sourceActor = Assert.Single(state.Actors.Values);
		Assert.Equal(SaveModule.CurrentVersion, saveFile.Version);
		Assert.Equal(state.Turn, saveFile.Header.Turn);
		Assert.Single(saveFile.Payload.DirtyChunks);

		var restoredState = new GameState();
		SaveModule.ApplySnapshot(restoredState, saveFile);

		Assert.Equal(state.WorldSeed, restoredState.WorldSeed);
		Assert.Equal(state.Turn, restoredState.Turn);
		Assert.Equal(state.PlayerX, restoredState.PlayerX);
		Assert.Equal(state.PlayerY, restoredState.PlayerY);
		Assert.Equal(state.PlayerZ, restoredState.PlayerZ);
		Assert.Equal(state.PlayerId, restoredState.PlayerId);
		Assert.Equal(state.PlayerAppearanceId, restoredState.PlayerAppearanceId);
		Assert.Equal(state.GeneratorId, restoredState.GeneratorId);
		Assert.Equal(state.ViewModeId, restoredState.ViewModeId);
		Assert.Equal(state.KillCount, restoredState.KillCount);
		Assert.Equal(state.IdentifiedActorTypes, restoredState.IdentifiedActorTypes);
		Assert.Equal(state.IdentifiedItemTypes, restoredState.IdentifiedItemTypes);
		Assert.Equal(state.Timeline.CurrentActorId, restoredState.Timeline.CurrentActorId);
		Assert.Equal(state.Timeline.LastActorId, restoredState.Timeline.LastActorId);
		var timelineActor = Assert.Single(restoredState.Timeline.Actors);
		Assert.Equal("hero", timelineActor.ActorId);
		Assert.Equal(128f, timelineActor.Charge);
		Assert.NotNull(restoredState.Weather);
		Assert.Equal(1.5f, restoredState.Weather.FrontPhase, 3);
		Assert.NotNull(restoredState.Weather.DebugOverride);
		Assert.Equal(WeatherType.Snow, restoredState.Weather.DebugOverride!.Type);
		Assert.Equal(WeatherIntensity.Heavy, restoredState.Weather.DebugOverride.Intensity);
		Assert.NotNull(restoredState.Weather.LastLocalWeather);
		Assert.Equal(WeatherType.Fog, restoredState.Weather.LastLocalWeather!.Type);
		Assert.Equal(WeatherIntensity.Light, restoredState.Weather.LastLocalWeather.Intensity);
		Assert.Equal(41, restoredState.Weather.LastLocalWeather.Turn);

		var actor = Assert.Single(restoredState.Actors.Values);
		Assert.Equal("hero", actor.Id);
		Assert.Equal("Hero", actor.DisplayName);
		Assert.Equal("simple", actor.BrainId);
		Assert.Equal(AwarenessState.Searching, actor.AwarenessState);
		Assert.True(actor.HasHomePosition);
		Assert.Equal(2, actor.HomeX);
		Assert.Equal(3, actor.HomeY);
		Assert.Equal(0, actor.HomeZ);
		Assert.Equal("enemy_player", actor.AlertTargetActorId);
		Assert.Equal(8, actor.LastKnownTargetX);
		Assert.Equal(9, actor.LastKnownTargetY);
		Assert.Equal(0, actor.LastKnownTargetZ);
		Assert.Equal(4, actor.StateTurns);
		Assert.Equal(5, actor.SearchTurnsRemaining);
		Assert.Equal(88, actor.Gold);
		Assert.Equal(sourceActor.DialogMood, actor.DialogMood);
		Assert.Equal(sourceActor.MoodValue, actor.MoodValue);
		Assert.Equal(0.75f, actor.DialogAffinity);
		Assert.Equal(2, actor.DialogTalkCount);
		Assert.Single(actor.DialogMemory);
		Assert.Equal(2, actor.DialogPersonality["brave"]);
		Assert.InRange(actor.DialogNeeds["food"], 0.3999f, 0.4001f);
		Assert.Contains(NeedIds.Hunger, actor.Needs.Keys);
		Assert.Contains(NeedIds.Rest, actor.Needs.Keys);
		Assert.Contains(actor.Thoughts, thought => thought.Id == "peckish");
		Assert.Equal(2, actor.SkillCooldowns.Count);
		Assert.Equal(3, actor.SkillCooldowns["bow_shoot"]);
		Assert.Equal(1, actor.SkillCooldowns["sword_parry"]);

		var container = Assert.Single(actor.Inventory);
		Assert.True(container.Equipped);
		Assert.NotNull(container.Contents);
		Assert.Single(container.Contents!);
		Assert.Equal("leather", container.MaterialId);
		Assert.Equal("apple", container.Contents[0].Id);
		Assert.Equal(11f, container.ColdInsulation);
		Assert.Equal(2f, container.HeatInsulation);
		Assert.Equal(7f, container.Waterproofing);
		Assert.Equal(4f, container.PortableWarmthC);
		Assert.Equal(1.5f, container.PortableDryingBonus);
		Assert.Equal(1, container.Tags["utility"]);

		var shopSlot = Assert.Single(actor.ShopSlots);
		Assert.Equal("torch", shopSlot.Item.Id);
		Assert.Equal("wood", shopSlot.Item.MaterialId);
		Assert.Equal(3, shopSlot.Stock);

		var limb = Assert.Single(actor.Limbs);
		Assert.Equal("arm_left", limb.Id);
		Assert.Single(limb.EquipSlots);
		Assert.Equal(container.InstanceId, limb.EquipSlots[0].ItemId);
		Assert.Equal(0.8f, limb.Capacities["manipulation"]);
		Assert.Equal(1, limb.Tags["flesh"]);

		Assert.NotNull(actor.Race);
		Assert.Equal("human", actor.Race!.Id);
		Assert.NotNull(actor.Profession);
		Assert.Equal("merchant", actor.Profession!.Id);
		Assert.Single(actor.Buffs);
		Assert.Single(actor.Experiences);

		var quest = Assert.Single(restoredState.Quests);
		Assert.Equal("quest_trade", quest.Id);
		Assert.Equal(QuestStatus.Active, quest.Status);
		Assert.Single(quest.Objectives);
		Assert.Equal(1, quest.Objectives[0].Current);
		Assert.Equal("merchant", quest.Tags["giver"]);

		var restoredChunk = SaveModule.LoadChunkFromCache(new ChunkCoord(0, 0, 0));
		Assert.NotNull(restoredChunk);
		Assert.True(restoredChunk!.Dirty);
		Assert.Equal(77, restoredChunk.TerrainIds[0]);
		Assert.Equal(9, restoredChunk.Hardness[0]);
		Assert.True(restoredChunk.Entities.ContainsKey(0));
		Assert.Single(restoredChunk.Entities[0]);
		Assert.Equal("crate", restoredChunk.Entities[0][0].EntityId);
		Assert.Single(restoredChunk.Nests);
		Assert.Equal("goblin", restoredChunk.Nests[0].TemplateId);
		Assert.Equal(12, restoredChunk.SnowDepth[0]);
		Assert.Equal(8, restoredChunk.SandDepth[0]);
		Assert.Equal(5, restoredChunk.Wetness[0]);
		Assert.Equal(3, restoredChunk.IceDepth[0]);
		Assert.Equal(39, restoredChunk.LastWeatherSimTurn);
	}

	[Fact]
	public void SaveFile_SaveVersion_RoundTrips_AndLegacySaveDefaultsToVersion()
	{
		ResetSaveCache();
		var state = CreateSampleState();

		var saveFile = SaveModule.BuildSnapshot(state);
		Assert.Equal(SaveModule.CurrentVersion, saveFile.SaveVersion);

		var json = SaveModule.SerializeSaveFile(saveFile);
		var parsed = SaveModule.DeserializeSaveFile(json);
		Assert.NotNull(parsed);
		Assert.Equal(SaveModule.CurrentVersion, parsed!.SaveVersion);

		using var doc = System.Text.Json.JsonDocument.Parse(json);
		var root = doc.RootElement;
		Assert.True(root.TryGetProperty("saveVersion", out var saveVersionProperty));
		Assert.Equal(SaveModule.CurrentVersion, saveVersionProperty.GetInt32());

		const string legacyWithoutSaveVersion = """
		{
		  "version": 7,
		  "header": {
		    "title": "legacy",
		    "savedAtUtc": "2026-04-05T12:34:56+00:00",
		    "turn": 12,
		    "playerZ": 0,
		    "generatorId": "room_corridor",
		    "viewModeId": "single_layer"
		  },
		  "payload": {
		    "worldSeed": 1,
		    "turn": 12,
		    "playerX": 1,
		    "playerY": 1,
		    "playerZ": 0,
		    "playerId": "player",
		    "killCount": 0,
		    "generatorId": "room_corridor",
		    "viewModeId": "single_layer",
		    "actors": [],
		    "quests": [],
		    "dirtyChunks": [],
		    "timeline": {
		      "actors": []
		    }
		  }
		}
		""";

		var legacyParsed = SaveModule.DeserializeSaveFile(legacyWithoutSaveVersion);
		Assert.NotNull(legacyParsed);
		Assert.Equal(legacyParsed!.Version, legacyParsed.SaveVersion);
	}

	[Fact]
	public void BuildSnapshot_And_ApplySnapshot_RoundTripRoomSessions_ReconnectAndTimelineContext()
	{
		ResetSaveCache();
		var state = CreateSampleState();

		state.Room.RoomId = "room-alpha";
		state.Room.RoomCode = "AB12CD";
		state.Room.LastSnapshotSequence = 99;
		state.Room.Players["host"] = new RoomPlayerState
		{
			PlayerSessionId = "host",
			DisplayName = "Host",
			PrimaryActorId = "hero",
			DelegatedActorIds = ["hero"],
			CurrentControllerActorIds = ["hero"],
			JoinToken = "join-host",
			ReconnectToken = "reconnect-host",
			ReconnectDeadlineUtc = DateTimeOffset.Parse("2026-04-10T10:11:12+00:00"),
			Connected = false,
			IsRoomOwner = true,
		};
		state.Room.InteractionReservations["loot:6:7:0"] = new InteractionReservation
		{
			ReservationKey = "loot:6:7:0",
			PlayerSessionId = "host",
			LastHeartbeatUtc = DateTimeOffset.Parse("2026-04-10T10:00:00+00:00"),
			ExpiresAtUtc = DateTimeOffset.Parse("2026-04-10T10:00:15+00:00"),
		};
		state.Room.ActorControlBindings["hero"] = new ActorControlBinding
		{
			PrimaryOwnerPlayerId = "host",
			TemporaryControllerPlayerId = null,
			CanBeDelegated = true,
		};
		RoomRuntimeModule.RefreshControlledActorIds(state);

		var saveFile = SaveModule.BuildSnapshot(state);
		var restored = new GameState();
		SaveModule.ApplySnapshot(restored, saveFile);

		Assert.Equal("room-alpha", restored.Room.RoomId);
		Assert.Equal("AB12CD", restored.Room.RoomCode);
		Assert.Equal(99, restored.Room.LastSnapshotSequence);
		var restoredPlayer = Assert.Single(restored.Room.Players.Values);
		Assert.Equal("host", restoredPlayer.PlayerSessionId);
		Assert.Equal("join-host", restoredPlayer.JoinToken);
		Assert.Equal("reconnect-host", restoredPlayer.ReconnectToken);
		Assert.Equal(DateTimeOffset.Parse("2026-04-10T10:11:12+00:00"), restoredPlayer.ReconnectDeadlineUtc);
		Assert.False(restoredPlayer.Connected);
		Assert.True(restoredPlayer.IsRoomOwner);
		Assert.Contains("hero", restoredPlayer.CurrentControllerActorIds);
		Assert.Single(restored.Room.InteractionReservations);
		Assert.Single(restored.Room.ActorControlBindings);

		Assert.Equal(state.Timeline.CurrentActorId, restored.Timeline.CurrentActorId);
		Assert.Equal(state.Timeline.LastActorId, restored.Timeline.LastActorId);
		Assert.Equal(state.Timeline.Actors.Count, restored.Timeline.Actors.Count);
	}

	[Fact]
	public void TryReadSaveHeader_SucceedsWithoutDeserializingPayload()
	{
		ResetSaveCache();
		var path = CreateTempJsonPath();
		try
		{
			var content = $$"""
			{
			  "version": {{SaveModule.CurrentVersion}},
			  "header": {
			    "title": "manual",
			    "savedAtUtc": "2026-04-05T12:34:56+00:00",
			    "turn": 12,
			    "playerZ": 3,
			    "generatorId": "room_corridor",
			    "viewModeId": "single_layer"
			  },
			  "payload": {
			    "unexpected": "shape"
			  }
			}
			""";
			File.WriteAllText(path, content);

			var headerStatus = SaveModule.TryReadSaveHeader(path, out var header);
			Assert.Equal(SaveLoadStatus.Success, headerStatus);
			Assert.NotNull(header);
			Assert.Equal("manual", header!.Title);
			Assert.Equal(12, header.Turn);
			Assert.Equal(3, header.PlayerZ);

			var loadStatus = SaveModule.LoadGame(new GameState(), path);
			Assert.Equal(SaveLoadStatus.Incompatible, loadStatus);
		}
		finally
		{
			TryDelete(path);
		}
	}

	[Fact]
	public void BuildSnapshot_And_ApplySnapshot_RoundTripFacilitiesDomainsAndActorWorkState()
	{
		ResetSaveCache();
		var actor = new Actor
		{
			Id = "planner",
			X = 1,
			Y = 1,
			Z = 0,
			Glyph = "@",
			DisplayName = "Planner",
			Faction = Factions.Player,
			PrimaryDomainId = DomainIds.Player,
			AccessibleDomainIds = new HashSet<string>(StringComparer.Ordinal) { "guild_domain" },
			WorkBrainId = WorkBrainIds.DomainWorker,
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
						[Caps.Consciousness] = 1f,
						[Caps.Manipulation] = 1f,
					},
				},
			],
		};

		var input = PresetDB.CloneItem("raw_meat");
		input.OwnerDomainId = "guild_domain";
		var output = PresetDB.CloneItem("meal_simple");
		output.OwnerDomainId = "guild_domain";
		var fuel = PresetDB.CloneItem("mat_wood");
		fuel.OwnerDomainId = "guild_domain";

		var state = new GameState
		{
			WorldSeed = 2468,
			PlayerId = actor.Id,
			PlayerX = actor.X,
			PlayerY = actor.Y,
			PlayerZ = actor.Z,
			Actors = new Dictionary<string, Actor>(StringComparer.Ordinal)
			{
				[actor.Id] = actor,
			},
		};
		state.EconomicDomains["guild_domain"] = new EconomicDomain
		{
			Id = "guild_domain",
			Name = "Guild",
			Kind = EconomicDomainKind.Institution,
		};
		state.Facilities["stove_1"] = new FacilityInstance
		{
			Id = "stove_1",
			FacilityDefId = FacilityIds.Stove,
			AnchorX = 10,
			AnchorY = 12,
			Z = 0,
			Rotation = FacilityRotation.East,
			Stage = FacilityStage.Active,
			OwnerDomainId = "guild_domain",
			HitPoints = 54,
			MaxHitPoints = 54,
			FuelTicksRemaining = 45,
			InputBuffer = [input],
			OutputBuffer = [output],
			FuelBuffer = [fuel],
			Bills =
			[
				new BillDef
				{
					Id = "bill_1",
					RecipeId = "cook_simple_meal",
					OwnerDomainId = "guild_domain",
				},
			],
			CachedRoomRoleId = RoomRoleIds.Kitchen,
		};
		state.StockpileZones.Add(new StockpileZone
		{
			Id = "zone_food",
			Name = "Food Stockpile",
			OwnerDomainId = "guild_domain",
			Cells = [new ZoneCell(14, 12, 0)],
			AllowedCategories = [ItemCategories.Food],
		});

		var saveFile = SaveModule.BuildSnapshot(state);
		Assert.Single(saveFile.Payload.Facilities!);
		Assert.Single(saveFile.Payload.StockpileZones!);
		Assert.Contains(saveFile.Payload.EconomicDomains!, domain => domain.Id == "guild_domain");

		var restored = new GameState();
		SaveModule.ApplySnapshot(restored, saveFile);

		var restoredActor = Assert.Single(restored.Actors.Values);
		Assert.Equal(DomainIds.Player, restoredActor.PrimaryDomainId);
		Assert.Contains("guild_domain", restoredActor.AccessibleDomainIds);
		Assert.Equal(WorkBrainIds.DomainWorker, restoredActor.WorkBrainId);
		Assert.True(restoredActor.CanAccessDomain("guild_domain"));

		Assert.Contains("guild_domain", restored.EconomicDomains.Keys);
		var restoredFacility = Assert.Single(restored.Facilities.Values);
		Assert.Equal("guild_domain", restoredFacility.OwnerDomainId);
		Assert.Equal(FacilityIds.Stove, restoredFacility.FacilityDefId);
		Assert.Equal(RoomRoleIds.Kitchen, restoredFacility.CachedRoomRoleId);
		Assert.Equal("guild_domain", Assert.Single(restoredFacility.InputBuffer).OwnerDomainId);
		Assert.Equal("guild_domain", Assert.Single(restoredFacility.OutputBuffer).OwnerDomainId);
		Assert.Single(restored.StockpileZones);

		restored.World = new WorldMap(restored.WorldSeed, new StubGenerator());
		Assert.True(restored.World.TryGetFacilityAt(10, 12, 0, out var anchorFacility));
		Assert.Equal(restoredFacility.Id, anchorFacility!.Id);
		Assert.True(restored.World.TryGetFacilityAt(10, 13, 0, out var chimneyFacility, out var chimneyCell));
		Assert.Equal(restoredFacility.Id, chimneyFacility!.Id);
		Assert.NotNull(chimneyCell);
		Assert.Equal("C", restored.World.GetDisplayCell(10, 12, 0, restored.Actors));
		Assert.Equal("c", restored.World.GetDisplayCell(10, 13, 0, restored.Actors));
		Assert.False(restored.World.IsWalkable(10, 12, 0));
		Assert.True(restored.World.BlocksSight(10, 13, 0));
	}

	[Fact]
	public void ApplySnapshot_RebuildsConstructionTickets_FromDeliveredMaterials()
	{
		ResetSaveCache();
		var state = new GameState
		{
			WorldSeed = 2222,
			PlayerId = "player",
			PlayerX = 4,
			PlayerY = 4,
			PlayerZ = 0,
		};
		state.Facilities["stove_1"] = new FacilityInstance
		{
			Id = "stove_1",
			FacilityDefId = FacilityIds.Stove,
			AnchorX = 5,
			AnchorY = 4,
			Z = 0,
			Rotation = FacilityRotation.North,
			Stage = FacilityStage.DeliverMaterials,
			OwnerDomainId = DomainIds.Player,
			DeliveredConstructionMaterials =
			[
				new ItemAmount
				{
					ItemId = "mat_wood",
					Count = 2,
				},
			],
		};

		var saveFile = SaveModule.BuildSnapshot(state);
		var restored = new GameState();
		SaveModule.ApplySnapshot(restored, saveFile);

		var restoredFacility = Assert.Single(restored.Facilities.Values);
		Assert.Equal(FacilityStage.DeliverMaterials, restoredFacility.Stage);
		var missing = FacilityConstructionModule.GetMissingConstructionMaterials(restoredFacility)
			.ToDictionary(item => item.ItemId, item => item.Count, StringComparer.Ordinal);
		Assert.Equal(18, missing["mat_stone"]);
		Assert.Equal(4, missing["mat_wood"]);

		var tickets = FacilityConstructionModule.GetConstructionTickets(restored, restoredFacility.Id);
		Assert.Equal(2, tickets.Count);
		var requirements = tickets.ToDictionary(ticket => ticket.RequiredItemId, ticket => ticket.RequiredCount, StringComparer.Ordinal);
		Assert.Equal(18, requirements["mat_stone"]);
		Assert.Equal(4, requirements["mat_wood"]);
	}

	[Fact]
	public void BuildSnapshot_IncludesWorldMetadataInHeader()
	{
		ResetSaveCache();
		var state = CreateSampleState();

		var saveFile = SaveModule.BuildSnapshot(state, new SaveHeaderContext
		{
			WorldId = "world-alpha",
			WorldName = "Alpha",
			CharacterId = "hero-1",
			CharacterName = "Rook",
		});

		Assert.Equal("world-alpha", saveFile.Header.WorldId);
		Assert.Equal("Alpha", saveFile.Header.WorldName);
		Assert.Equal("hero-1", saveFile.Header.CharacterId);
		Assert.Equal("Rook", saveFile.Header.CharacterName);
		Assert.Equal("Rook", saveFile.Header.Title);
	}

	[Fact]
	public void LoadGame_Succeeds_WhenHeaderOmitsWorldMetadata()
	{
		ResetSaveCache();
		var path = CreateTempJsonPath();
		try
		{
			var saveFile = SaveModule.BuildSnapshot(CreateSampleState());
			saveFile.Header.Title = "sample";
			var json = SaveModule.SerializeSaveFile(saveFile);
			File.WriteAllText(path, json);

			var restoredState = new GameState();
			var loadStatus = SaveModule.LoadGame(restoredState, path);
			var headerStatus = SaveModule.TryReadSaveHeader(path, out var header);

			Assert.Equal(SaveLoadStatus.Success, loadStatus);
			Assert.Equal(SaveLoadStatus.Success, headerStatus);
			Assert.NotNull(header);
			Assert.Null(header!.WorldId);
			Assert.Null(header.CharacterId);
			Assert.Equal(saveFile.Payload.WorldSeed, restoredState.WorldSeed);
		}
		finally
		{
			TryDelete(path);
		}
	}

	[Fact]
	public void LoadGame_ReturnsIncompatible_ForLegacyRootFormat()
	{
		ResetSaveCache();
		var path = CreateTempJsonPath();
		try
		{
			File.WriteAllText(path, "{\"turn\":1,\"playerX\":2,\"playerY\":3}");

			var loadStatus = SaveModule.LoadGame(new GameState(), path);
			Assert.Equal(SaveLoadStatus.Incompatible, loadStatus);

			var headerStatus = SaveModule.TryReadSaveHeader(path, out var header);
			Assert.Equal(SaveLoadStatus.Incompatible, headerStatus);
			Assert.Null(header);
		}
		finally
		{
			TryDelete(path);
		}
	}

	[Fact]
	public void LoadGame_ReturnsIncompatible_ForVersion4Save()
	{
		ResetSaveCache();
		var path = CreateTempJsonPath();
		try
		{
			var content = """
			{
			  "version": 4,
			  "header": {
			    "title": "legacy-v4",
			    "savedAtUtc": "2026-04-05T12:34:56+00:00",
			    "turn": 12,
			    "playerZ": 0,
			    "generatorId": "room_corridor",
			    "viewModeId": "single_layer"
			  },
			  "payload": {
			    "worldSeed": 1,
			    "turn": 12,
			    "playerX": 1,
			    "playerY": 1,
			    "playerZ": 0,
			    "playerId": "player",
			    "killCount": 0,
			    "generatorId": "room_corridor",
			    "viewModeId": "single_layer",
			    "actors": [],
			    "quests": [],
			    "dirtyChunks": [],
			    "timeline": {
			      "actors": []
			    }
			  }
			}
			""";
			File.WriteAllText(path, content);

			Assert.Equal(SaveLoadStatus.Incompatible, SaveModule.LoadGame(new GameState(), path));
			Assert.Equal(SaveLoadStatus.Incompatible, SaveModule.TryReadSaveHeader(path, out var header));
			Assert.Null(header);
		}
		finally
		{
			TryDelete(path);
		}
	}

	[Fact]
	public void ApplySnapshot_BackfillsColdInsulation_FromLegacyWarmthTag()
	{
		ResetSaveCache();
		var saveFile = SaveModule.BuildSnapshot(CreateSampleState());
		var actorSnapshot = Assert.Single(saveFile.Payload.Actors);
		var itemSnapshot = Assert.Single(actorSnapshot.Inventory);
		itemSnapshot.ColdInsulation = null;
		itemSnapshot.HeatInsulation = null;
		itemSnapshot.Waterproofing = null;
		itemSnapshot.PortableWarmthC = null;
		itemSnapshot.PortableDryingBonus = null;
		itemSnapshot.Tags["保暖"] = 2;

		var restoredState = new GameState();
		SaveModule.ApplySnapshot(restoredState, saveFile);

		var restoredActor = Assert.Single(restoredState.Actors.Values);
		var restoredItem = Assert.Single(restoredActor.Inventory);
		Assert.Equal(20f, restoredItem.ColdInsulation);
		Assert.Equal(0f, restoredItem.HeatInsulation);
		Assert.Equal(0f, restoredItem.Waterproofing);
		Assert.Equal(0f, restoredItem.PortableWarmthC);
		Assert.Equal(0f, restoredItem.PortableDryingBonus);
	}

	[Fact]
	public void ApplySnapshot_BackfillsMaterialId_FromPreset_WhenLegacySaveOmitsIt()
	{
		ResetSaveCache();
		var state = CreateSampleState();
		var actor = Assert.Single(state.Actors.Values);
		actor.Inventory.Clear();
		actor.Inventory.Add(PresetDB.CloneItem("torch"));

		var saveFile = SaveModule.BuildSnapshot(state);
		var actorSnapshot = Assert.Single(saveFile.Payload.Actors);
		var itemSnapshot = Assert.Single(actorSnapshot.Inventory);
		itemSnapshot.MaterialId = null;

		var restoredState = new GameState();
		SaveModule.ApplySnapshot(restoredState, saveFile);

		var restoredActor = Assert.Single(restoredState.Actors.Values);
		var restoredItem = Assert.Single(restoredActor.Inventory);
		Assert.Equal("wood", restoredItem.MaterialId);
	}

	[Fact]
	public void BuildSnapshot_And_ApplySnapshot_RoundTripPartyState()
	{
		ResetSaveCache();
		var state = CreateSampleState();

		var memberA = new Actor
		{
			Id = "member_a",
			X = 1,
			Y = 1,
			Z = 0,
			Glyph = "@",
			DisplayName = "Member A",
			Faction = Factions.Player,
		};
		var memberB = new Actor
		{
			Id = "member_b",
			X = 2,
			Y = 1,
			Z = 0,
			Glyph = "@",
			DisplayName = "Member B",
			Faction = Factions.Player,
		};
		state.Actors[memberA.Id] = memberA;
		state.Actors[memberB.Id] = memberB;

		state.Party.MemberIds.Clear();
		state.Party.MemberIds.Add("hero");
		state.Party.MemberIds.Add(memberA.Id);
		state.Party.MemberIds.Add(memberB.Id);
		state.Party.ActiveId = memberA.Id;
		state.Party.MaxSize = 4;

		var saveFile = SaveModule.BuildSnapshot(state);
		Assert.NotNull(saveFile.Payload.Party);
		Assert.Equal(3, saveFile.Payload.Party!.MemberIds.Count);
		Assert.Equal("hero", saveFile.Payload.Party.MemberIds[0]);
		Assert.Equal(memberA.Id, saveFile.Payload.Party.ActiveActorId);
		Assert.Equal(4, saveFile.Payload.Party.MaxSize);

		var restored = new GameState();
		SaveModule.ApplySnapshot(restored, saveFile);

		Assert.Equal(["hero", "member_a", "member_b"], restored.Party.MemberIds);
		Assert.Equal("member_a", restored.Party.ActiveId);
		Assert.Equal(4, restored.Party.MaxSize);
	}

	[Fact]
	public void BuildSnapshot_And_ApplySnapshot_RoundTripSocialState()
	{
		ResetSaveCache();
		var state = CreateSampleState();

		state.SocialState.Relations["hero:enemy_player"] = new MiniRPG.Core.Social.RelationEntry
		{
			FromId = "hero",
			ToId = "enemy_player",
			Opinion = -42,
			Tags = ["rival", "threat"],
			LastInteractionTurn = 17,
		};
		state.SocialState.Relations["enemy_player:hero"] = new MiniRPG.Core.Social.RelationEntry
		{
			FromId = "enemy_player",
			ToId = "hero",
			Opinion = -55,
			Tags = ["target"],
			LastInteractionTurn = 18,
		};
		state.SocialState.SocialCooldowns["hero"] = 30;
		state.SocialState.SocialCooldowns["enemy_player"] = 32;

		var saveFile = SaveModule.BuildSnapshot(state);
		Assert.NotNull(saveFile.Payload.Social);
		Assert.Equal(2, saveFile.Payload.Social!.Relations.Count);

		var restored = new GameState();
		SaveModule.ApplySnapshot(restored, saveFile);

		Assert.Equal(2, restored.SocialState.Relations.Count);
		var heroToEnemy = restored.SocialState.Relations["hero:enemy_player"];
		Assert.Equal(-42, heroToEnemy.Opinion);
		Assert.Equal(17, heroToEnemy.LastInteractionTurn);
		Assert.Contains("rival", heroToEnemy.Tags);
		Assert.Contains("threat", heroToEnemy.Tags);

		var enemyToHero = restored.SocialState.Relations["enemy_player:hero"];
		Assert.Equal(-55, enemyToHero.Opinion);
		Assert.Equal("target", Assert.Single(enemyToHero.Tags));

		Assert.Equal(30, restored.SocialState.SocialCooldowns["hero"]);
		Assert.Equal(32, restored.SocialState.SocialCooldowns["enemy_player"]);
	}

	[Fact]
	public void BuildSnapshot_And_ApplySnapshot_RoundTripStorytellerState()
	{
		ResetSaveCache();
		var state = CreateSampleState();

		state.StorytellerState.ThreatLevel = 27.5f;
		state.StorytellerState.LastCheckTurn = 90;
		state.StorytellerState.LastIncidentTurn["raid_pirates"] = 60;
		state.StorytellerState.LastIncidentTurn["cold_snap"] = 75;
		state.StorytellerState.PendingIncidents.Add(new MiniRPG.Core.Event.PendingIncident
		{
			IncidentDefId = "wandering_trader",
			TriggerTurn = 110,
			Params = new Dictionary<string, string> { ["scale"] = "small" },
		});
		state.StorytellerState.History.Add(new MiniRPG.Core.Event.IncidentRecord
		{
			DefId = "raid_pirates",
			Turn = 60,
			Category = MiniRPG.Core.Event.IncidentCategory.Threat,
		});
		state.StorytellerState.History.Add(new MiniRPG.Core.Event.IncidentRecord
		{
			DefId = "cold_snap",
			Turn = 75,
			Category = MiniRPG.Core.Event.IncidentCategory.Neutral,
		});

		var saveFile = SaveModule.BuildSnapshot(state);
		Assert.NotNull(saveFile.Payload.Storyteller);
		Assert.Equal(27.5f, saveFile.Payload.Storyteller!.ThreatLevel);
		Assert.Equal(90, saveFile.Payload.Storyteller.LastCheckTurn);
		Assert.Equal(2, saveFile.Payload.Storyteller.LastIncidentTurn.Count);
		Assert.Single(saveFile.Payload.Storyteller.PendingIncidents);
		Assert.Equal(2, saveFile.Payload.Storyteller.History.Count);

		var restored = new GameState();
		SaveModule.ApplySnapshot(restored, saveFile);

		Assert.Equal(27.5f, restored.StorytellerState.ThreatLevel);
		Assert.Equal(90, restored.StorytellerState.LastCheckTurn);
		Assert.Equal(60, restored.StorytellerState.LastIncidentTurn["raid_pirates"]);
		Assert.Equal(75, restored.StorytellerState.LastIncidentTurn["cold_snap"]);

		var pending = Assert.Single(restored.StorytellerState.PendingIncidents);
		Assert.Equal("wandering_trader", pending.IncidentDefId);
		Assert.Equal(110, pending.TriggerTurn);
		Assert.Equal("small", pending.Params["scale"]);

		Assert.Equal(2, restored.StorytellerState.History.Count);
		Assert.Equal("raid_pirates", restored.StorytellerState.History[0].DefId);
		Assert.Equal(MiniRPG.Core.Event.IncidentCategory.Threat, restored.StorytellerState.History[0].Category);
		Assert.Equal("cold_snap", restored.StorytellerState.History[1].DefId);
		Assert.Equal(MiniRPG.Core.Event.IncidentCategory.Neutral, restored.StorytellerState.History[1].Category);
	}

	[Fact]
	public void ApplySnapshot_Storyteller_FallsBackToDefault_WhenSnapshotMissing()
	{
		ResetSaveCache();
		var state = CreateSampleState();
		var saveFile = SaveModule.BuildSnapshot(state);
		saveFile.Payload.Storyteller = null;

		var restored = new GameState();
		restored.StorytellerState.ThreatLevel = 99f;
		restored.StorytellerState.LastIncidentTurn["stale"] = 1;
		restored.StorytellerState.History.Add(new MiniRPG.Core.Event.IncidentRecord { DefId = "stale" });

		SaveModule.ApplySnapshot(restored, saveFile);

		Assert.Equal(0f, restored.StorytellerState.ThreatLevel);
		Assert.Empty(restored.StorytellerState.LastIncidentTurn);
		Assert.Empty(restored.StorytellerState.PendingIncidents);
		Assert.Empty(restored.StorytellerState.History);
	}

	[Fact]
	public void ApplySnapshot_Party_FallsBackToDefault_WhenSnapshotMissing()
	{
		ResetSaveCache();
		var state = CreateSampleState();
		var saveFile = SaveModule.BuildSnapshot(state);
		saveFile.Payload.Party = null;

		var restored = new GameState();
		restored.Party.MemberIds.Add("stale_member");
		restored.Party.ActiveId = "stale_member";
		restored.Party.MaxSize = 99;

		SaveModule.ApplySnapshot(restored, saveFile);

		Assert.Empty(restored.Party.MemberIds);
		Assert.Equal(string.Empty, restored.Party.ActiveId);
		Assert.Equal(6, restored.Party.MaxSize);
	}

	[Fact]
	public void BuildSnapshot_And_ApplySnapshot_RoundTripFireHazardsAndItemDurability()
	{
		ResetSaveCache();
		var state = CreateSampleState();
		var actor = Assert.Single(state.Actors.Values);
		var bag = Assert.Single(actor.Inventory);
		bag.EnsureRuntimeState();
		bag.ApplyDurabilityDamage(9);
		var apple = Assert.Single(bag.Contents!);
		apple.EnsureRuntimeState();
		apple.ApplyDurabilityDamage(2);
		FireSystem.TryIgniteCell(state, actor.X, actor.Y, actor.Z, intensity: 4, fuel: 15);
		FireSystem.TryIgniteActor(state, actor, intensity: 1);

		var saveFile = SaveModule.BuildSnapshot(state);
		var restoredState = new GameState();
		SaveModule.ApplySnapshot(restoredState, saveFile);
		restoredState.World = new WorldMap(restoredState.WorldSeed, new StubGenerator());
		restoredState.World.Chunks.OnChunkLoad = SaveModule.LoadChunkFromCache;
		restoredState.World.GetEntities(actor.X, actor.Y, actor.Z);

		var restoredActor = Assert.Single(restoredState.Actors.Values);
		var restoredBag = Assert.Single(restoredActor.Inventory);
		var restoredApple = Assert.Single(restoredBag.Contents!);

		Assert.Equal(bag.InstanceId, restoredBag.InstanceId);
		Assert.Equal(bag.Durability, restoredBag.Durability);
		Assert.Equal(apple.InstanceId, restoredApple.InstanceId);
		Assert.Equal(apple.Durability, restoredApple.Durability);
		Assert.Contains(restoredActor.HealthConditions, condition => condition.Id == HealthConditionIds.OnFire);
		Assert.Equal(4, FireSystem.GetFireIntensityAt(restoredState, actor.X, actor.Y, actor.Z));
	}

	[Fact]
	public void BuildSnapshot_And_ApplySnapshot_RoundTripMixedTechItemMetadata()
	{
		ResetSaveCache();
		var state = CreateSampleState();
		var actor = Assert.Single(state.Actors.Values);
		actor.Inventory.Clear();

		var ammo = PresetDB.CloneItem("ammo_rifle");
		ammo.StackCount = 17;

		var rifle = PresetDB.CloneItem("bolt_rifle");
		rifle.Equipped = true;
		rifle.LoadedAmmo = 2;

		var organ = PresetDB.CloneItem("organ_heart");

		var corpse = PresetDB.CloneItem("corpse_human");
		corpse.Corpse = new ItemCorpseMetadata
		{
			CorpseProfileId = "corpse_humanoid",
			SourceActorTemplateId = "player",
			SourceRaceId = "human",
			SourceActorName = "Downed Raider",
			Stripped = true,
			Butchered = false,
			RemainingLimbIds = ["human_heart", "human_left_eye"],
		};
		corpse.Contents =
		[
			PresetDB.CloneItem("ammo_revolver"),
		];
		corpse.Contents[0].StackCount = 7;

		actor.Inventory.Add(ammo);
		actor.Inventory.Add(rifle);
		actor.Inventory.Add(organ);
		actor.Inventory.Add(corpse);

		var saveFile = SaveModule.BuildSnapshot(state);
		var restoredState = new GameState();
		SaveModule.ApplySnapshot(restoredState, saveFile);

		var restoredActor = Assert.Single(restoredState.Actors.Values);
		Assert.Equal(4, restoredActor.Inventory.Count);

		var restoredAmmo = restoredActor.Inventory[0];
		Assert.Equal("ammo_rifle", restoredAmmo.Id);
		Assert.Equal(17, restoredAmmo.StackCount);
		Assert.Equal("rifle_round", restoredAmmo.AmmoType);

		var restoredRifle = restoredActor.Inventory[1];
		Assert.Equal("bolt_rifle", restoredRifle.Id);
		Assert.Equal("industrial", restoredRifle.TechTier);
		Assert.Equal("firearm_rifle", restoredRifle.SubCategory);
		Assert.Equal("rifle_round", restoredRifle.AmmoType);
		Assert.Equal(5, restoredRifle.MagazineSize);
		Assert.Equal(2, restoredRifle.LoadedAmmo);

		var restoredOrgan = restoredActor.Inventory[2];
		Assert.NotNull(restoredOrgan.Surgery);
		Assert.Equal("install_heart", restoredOrgan.Surgery!.OperationId);
		Assert.Equal("transplant_heart", restoredOrgan.Surgery.ReplacementLimbPresetId);
		Assert.Equal("天然移植", restoredOrgan.Surgery.Role);

		var restoredCorpse = restoredActor.Inventory[3];
		Assert.NotNull(restoredCorpse.Corpse);
		Assert.Equal("corpse_humanoid", restoredCorpse.Corpse!.CorpseProfileId);
		Assert.Equal("Downed Raider", restoredCorpse.Corpse.SourceActorName);
		Assert.True(restoredCorpse.Corpse.Stripped);
		Assert.False(restoredCorpse.Corpse.Butchered);
		Assert.Equal(["human_heart", "human_left_eye"], restoredCorpse.Corpse.RemainingLimbIds);
		Assert.NotNull(restoredCorpse.Contents);
		Assert.Single(restoredCorpse.Contents!);
		Assert.Equal("ammo_revolver", restoredCorpse.Contents[0].Id);
		Assert.Equal(7, restoredCorpse.Contents[0].StackCount);
	}

	private static GameState CreateSampleState()
	{
		var bag = new Item
		{
			Id = "bag",
			Name = "Bag",
			MaterialId = "leather",
			Price = 10,
			Equipped = true,
			Category = "tool",
			Weight = 1.5f,
			BodyPart = "arm",
			Layer = EquipLayer.Middle,
			CoveredParts = ["arm"],
			ColdInsulation = 11f,
			HeatInsulation = 2f,
			Waterproofing = 7f,
			PortableWarmthC = 4f,
			PortableDryingBonus = 1.5f,
			GrantedSkills = ["carry"],
			Contents =
			[
				new Item
					{
						Id = "apple",
						Name = "Apple",
					Price = 2,
					Category = "food",
					Weight = 0.2f,
					Tags = new Dictionary<string, int> { ["food"] = 1 },
				},
			],
			Tags = new Dictionary<string, int> { ["utility"] = 1 },
		};

		var actor = new Actor
		{
			Id = "hero",
			TemplateId = "player",
			X = 6,
			Y = 7,
			Z = 0,
			Glyph = "@",
			DisplayName = "Hero",
			FacingX = 1,
			FacingY = 0,
			Faction = Factions.Player,
			BrainId = "simple",
			AwarenessState = AwarenessState.Searching,
			HasHomePosition = true,
			HomeX = 2,
			HomeY = 3,
			HomeZ = 0,
			AlertTargetActorId = "enemy_player",
			LastKnownTargetX = 8,
			LastKnownTargetY = 9,
			LastKnownTargetZ = 0,
			StateTurns = 4,
			SearchTurnsRemaining = 5,
			Gold = 88,
			Inventory = [bag],
			ShopSlots =
			[
				new ShopSlot
				{
					Stock = 3,
					Item = new Item
					{
						Id = "torch",
						Name = "Torch",
						MaterialId = "wood",
						Price = 6,
						Category = "tool",
						Weight = 0.7f,
						Tags = new Dictionary<string, int> { ["light"] = 1 },
					},
				},
			],
			Limbs =
			[
				new Limb
				{
					Id = "arm_left",
					Name = "Left Arm",
					MaxDurability = 12,
					Durability = 9,
					Material = "flesh",
					BodyPart = "arm",
					EquipLayers = [EquipLayer.Middle],
					EquipSlots =
					[
						new EquipSlot
						{
							LimbId = "arm_left",
							BodyPart = "arm",
							Layer = EquipLayer.Middle,
							ItemId = "bag",
						},
					],
					Capacities = new Dictionary<string, float> { ["manipulation"] = 0.8f },
					Tags = new Dictionary<string, int> { ["flesh"] = 1 },
				},
			],
			Race = new Race
			{
				Id = "human",
				Name = "Human",
				Tags = new Dictionary<string, int> { ["warm_blooded"] = 1 },
			},
			Profession = new Profession
			{
				Id = "merchant",
				Name = "Merchant",
				Tags = new Dictionary<string, int> { ["trade"] = 2 },
			},
			Buffs =
			[
				new Buff
				{
					Id = "focus",
					Name = "Focus",
					RemainingTurns = 5,
					Tags = new Dictionary<string, int> { ["focus"] = 1 },
				},
			],
			Experiences =
			[
				new Experience
				{
					Id = "market_veteran",
					Name = "Market Veteran",
					Tags = new Dictionary<string, int> { ["trade"] = 1 },
				},
			],
			DialogMood = 0.25f,
			DialogAffinity = 0.75f,
			DialogMemory = ["met_player"],
			DialogTalkCount = 2,
			DialogPersonality = new Dictionary<string, float> { ["brave"] = 2.0f },
			DialogNeeds = new Dictionary<string, float> { ["food"] = 0.4f },
			SkillCooldowns = new Dictionary<string, int>
			{
				["bow_shoot"] = 3,
				["sword_parry"] = 1,
			},
		};

		var state = new GameState
		{
			Turn = 42,
			WorldSeed = 123456,
			PlayerX = actor.X,
			PlayerY = actor.Y,
			PlayerZ = actor.Z,
			PlayerId = actor.Id,
			PlayerAppearanceId = "enemy2",
			KillCount = 7,
			GeneratorId = "room_corridor",
			ViewModeId = "multi_layer",
			IdentifiedActorTypes = new HashSet<string>(StringComparer.Ordinal) { "goblin" },
			IdentifiedItemTypes = new HashSet<string>(StringComparer.Ordinal) { "apple", "bag" },
			Actors = new Dictionary<string, Actor> { [actor.Id] = actor },
			Quests =
			[
				new Quest
				{
					Id = "quest_trade",
					Title = "Trade Run",
					Description = "Bring goods to town",
					Source = "board",
					Status = QuestStatus.Active,
					AcceptedTurn = 12,
					Objectives =
					[
						new QuestObjective
						{
							Text = "Deliver cargo",
							Current = 1,
							Target = 3,
						},
					],
					Tags = new Dictionary<string, string> { ["giver"] = "merchant" },
				},
			],
			Timeline = new TimelineState
			{
				CurrentActorId = "hero",
				LastActorId = "hero",
				Actors =
				[
					new TimelineActorState
					{
						ActorId = "hero",
						Charge = 128f,
					},
				],
			},
			Weather = new WeatherState
			{
				FrontPhase = 1.5f,
				DebugOverride = new WeatherDebugOverride
				{
					Type = WeatherType.Snow,
					Intensity = WeatherIntensity.Heavy,
				},
				LastLocalWeather = new WeatherLocalSnapshot
				{
					Type = WeatherType.Fog,
					Intensity = WeatherIntensity.Light,
					Turn = 41,
				},
			},
		};

		var world = new WorldMap(state.WorldSeed, new StubGenerator());
		var chunk = world.Chunks.GetOrLoad(new ChunkCoord(0, 0, 0));
		chunk.TerrainIds[0] = 77;
		chunk.Hardness[0] = 9;
		chunk.SnowDepth[0] = 12;
		chunk.SandDepth[0] = 8;
		chunk.Wetness[0] = 5;
		chunk.IceDepth[0] = 3;
		chunk.LastWeatherSimTurn = 39;
		chunk.Entities[0] =
		[
			new CellEntity
			{
				Type = CellEntityType.Container,
				Glyph = "C",
				EntityId = "crate",
				Meta = new Dictionary<string, string> { ["loot"] = "apple" },
			},
		];
		chunk.Nests.Add(new NestData
		{
			X = 3,
			Y = 4,
			SpawnInterval = 6,
			TurnsSinceSpawn = 2,
			MaxSpawned = 3,
			TemplateId = "goblin",
		});
		chunk.Dirty = true;
		state.World = world;
		return state;
	}

	private static void ResetSaveCache()
	{
		SaveModule.ApplySnapshot(new GameState(), new SaveFile
		{
			Version = SaveModule.CurrentVersion,
			Header = new SaveHeader
			{
				Title = "empty",
				SavedAtUtc = DateTimeOffset.UtcNow,
				Turn = 0,
				PlayerZ = 0,
				GeneratorId = "room_corridor",
				ViewModeId = "single_layer",
			},
			Payload = new SavePayload
			{
				WorldSeed = 0,
				Turn = 0,
				PlayerX = 0,
				PlayerY = 0,
				PlayerZ = 0,
				PlayerId = "player",
				PlayerAppearanceId = null,
				KillCount = 0,
				GeneratorId = "room_corridor",
				ViewModeId = "single_layer",
				Actors = [],
				Quests = [],
				DirtyChunks = [],
				Timeline = new TimelineSnapshot
				{
					Actors = [],
				},
			},
		});
	}

	[Fact]
	public void ApplySnapshot_FallsBackToDefaultAppearance_WhenMissingOrInvalid()
	{
		var missingState = new GameState();
		SaveModule.ApplySnapshot(missingState, new SaveFile
		{
			Version = SaveModule.CurrentVersion,
			Header = new SaveHeader
			{
				Title = "missing",
				SavedAtUtc = DateTimeOffset.UtcNow,
				Turn = 0,
				PlayerZ = 0,
				GeneratorId = "room_corridor",
				ViewModeId = "single_layer",
			},
			Payload = new SavePayload
			{
				WorldSeed = 0,
				Turn = 0,
				PlayerX = 0,
				PlayerY = 0,
				PlayerZ = 0,
				PlayerId = "player",
				PlayerAppearanceId = null,
				KillCount = 0,
				GeneratorId = "room_corridor",
				ViewModeId = "single_layer",
				Actors = [],
				Quests = [],
				DirtyChunks = [],
				Timeline = new TimelineSnapshot
				{
					Actors = [],
				},
			},
		});
		Assert.Equal(GameState.DefaultPlayerAppearanceId, missingState.PlayerAppearanceId);

		var invalidState = new GameState();
		SaveModule.ApplySnapshot(invalidState, new SaveFile
		{
			Version = SaveModule.CurrentVersion,
			Header = new SaveHeader
			{
				Title = "invalid",
				SavedAtUtc = DateTimeOffset.UtcNow,
				Turn = 0,
				PlayerZ = 0,
				GeneratorId = "room_corridor",
				ViewModeId = "single_layer",
			},
			Payload = new SavePayload
			{
				WorldSeed = 0,
				Turn = 0,
				PlayerX = 0,
				PlayerY = 0,
				PlayerZ = 0,
				PlayerId = "player",
				PlayerAppearanceId = "missing_appearance",
				KillCount = 0,
				GeneratorId = "room_corridor",
				ViewModeId = "single_layer",
				Actors = [],
				Quests = [],
				DirtyChunks = [],
				Timeline = new TimelineSnapshot
				{
					Actors = [],
				},
			},
		});
		Assert.Equal(GameState.DefaultPlayerAppearanceId, invalidState.PlayerAppearanceId);
	}

	private static string CreateTempJsonPath() =>
		Path.Combine(Path.GetTempPath(), $"minirpg-save-{Guid.NewGuid():N}.json");

	private static void TryDelete(string path)
	{
		try
		{
			if (File.Exists(path))
				File.Delete(path);
		}
		catch
		{
			// Ignore cleanup failures in tests.
		}
	}

	private sealed class StubGenerator : IMapGenerator
	{
		public string Id => "stub";
		public string Name => "Stub";

		public void GenerateChunk(ChunkData chunk, int worldSeed)
		{
		}

		public void PopulateChunk(ChunkData chunk, int worldSeed)
		{
		}
	}
}
