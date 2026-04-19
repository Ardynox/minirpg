using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Conversation;
using MiniRPG.Core.Data;
using MiniRPG.Core.Event;
using MiniRPG.Core.Facility;
using MiniRPG.Core.Farm;
using MiniRPG.Core.Health;
using MiniRPG.Core.Social;
using MiniRPG.Core.Weather;
using MiniRPG.Core.World;
using MiniRPG.Core.Zone;

namespace MiniRPG.Core.Map;

/// <summary>
/// 存档模块：负责运行时对象与显式存档快照之间的映射。
/// </summary>
public static class SaveModule
{
	public const int MinimumCompatibleVersion = 5;
	public const int CurrentVersion = 7;

	private static readonly JsonSerializerOptions JsonOpts = new()
	{
		WriteIndented = true,
		PropertyNameCaseInsensitive = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		Converters = { new JsonStringEnumConverter() },
	};

	public static void SaveGame(GameState state, string filePath, SaveHeaderContext? headerContext = null)
	{
		var saveFile = BuildSnapshot(state, headerContext);
		saveFile.Header.Title = BuildSaveTitle(filePath, headerContext);
		WriteSaveFile(saveFile, filePath);
	}

	public static SaveLoadStatus LoadGame(GameState state, string filePath)
	{
		var status = ReadSaveFile(filePath, out var saveFile);
		if (status != SaveLoadStatus.Success || saveFile == null)
			return status;

		ApplySnapshot(state, saveFile);
		return SaveLoadStatus.Success;
	}

	public static SaveLoadStatus ReadSaveFile(string filePath, out SaveFile? saveFile) =>
		TryReadSaveFile(filePath, out saveFile);

	public static SaveLoadStatus TryReadSaveHeader(string filePath, out SaveHeader? header)
	{
		header = null;
		if (!File.Exists(filePath))
			return SaveLoadStatus.NotFound;

		try
		{
			header = DeserializeSaveHeader(File.ReadAllText(filePath));
			return header != null ? SaveLoadStatus.Success : SaveLoadStatus.Incompatible;
		}
		catch
		{
			header = null;
			return SaveLoadStatus.Incompatible;
		}
	}

	public static void WriteSaveFile(SaveFile saveFile, string filePath)
	{
		var json = SerializeSaveFile(saveFile);
		EnsureDir(filePath);
		File.WriteAllText(filePath, json);
	}

	public static string SerializeSaveFile(SaveFile saveFile)
	{
		var json = JsonSerializer.Serialize(saveFile, JsonOpts);
		return CanonicalizeJson(json);
	}

	public static SaveFile? DeserializeSaveFile(string json)
	{
		try
		{
			var saveFile = JsonSerializer.Deserialize<SaveFile>(json, JsonOpts);
			if (saveFile == null || !IsCompatibleVersion(saveFile.Version))
				return null;

			if (saveFile.SaveVersion <= 0)
				saveFile.SaveVersion = saveFile.Version;

			return saveFile;
		}
		catch
		{
			return null;
		}
	}

	public static SaveHeader? DeserializeSaveHeader(string json)
	{
		try
		{
			using var doc = JsonDocument.Parse(json);
			var root = doc.RootElement;
			if (root.ValueKind != JsonValueKind.Object)
				return null;

			if (!root.TryGetProperty("version", out var versionElement)
				|| versionElement.ValueKind != JsonValueKind.Number
				|| !IsCompatibleVersion(versionElement.GetInt32()))
				return null;

			if (!root.TryGetProperty("header", out var headerElement)
				|| headerElement.ValueKind != JsonValueKind.Object)
				return null;

			if (!root.TryGetProperty("payload", out var payloadElement)
				|| payloadElement.ValueKind != JsonValueKind.Object)
				return null;

			return TryParseSaveHeader(headerElement, out var header)
				? header
				: null;
		}
		catch
		{
			return null;
		}
	}

	public static SaveFile BuildSnapshot(GameState state, SaveHeaderContext? headerContext = null)
	{
		state.EnsureDefaultEconomicDomains();
		foreach (var actor in state.Actors.Values)
		{
			NeedSystem.Sync(actor, state.Turn);
			HealthSystem.Sync(actor, state.Turn, DefaultEnvironmentExposureProvider.Instance.Capture(state, actor));
		}

		var payload = new SavePayload
		{
			WorldSeed = state.WorldSeed,
			Turn = state.Turn,
			PlayerX = state.PlayerX,
			PlayerY = state.PlayerY,
			PlayerZ = state.PlayerZ,
			PlayerId = state.PlayerId,
			PlayerAppearanceId = state.PlayerAppearanceId,
			BumpAttack = false,
			WatchMode = false,
			KillCount = state.KillCount,
			GeneratorId = state.GeneratorId,
			ViewModeId = state.ViewModeId,
			Actors = MapList(state.Actors.Values.OrderBy(static actor => actor.Id, StringComparer.Ordinal), BuildActorSnapshot),
			Quests = MapList(state.Quests, BuildQuestSnapshot),
			DirtyChunks = BuildDirtyChunkSnapshots(state),
			Timeline = BuildTimelineSnapshot(state.Timeline),
			Weather = BuildWeatherStateSnapshot(state.Weather, state.WorldSeed),
			IdentifiedActorTypes = [.. state.IdentifiedActorTypes.OrderBy(static id => id, StringComparer.Ordinal)],
			IdentifiedItemTypes = [.. state.IdentifiedItemTypes.OrderBy(static id => id, StringComparer.Ordinal)],
			Facilities = MapList(
				state.Facilities.Values.OrderBy(static facility => facility.Id, StringComparer.Ordinal),
				static facility => facility.Clone()),
			StockpileZones = MapList(state.StockpileZones, static zone => zone.Clone()),
			EconomicDomains = MapList(
				state.EconomicDomains.Values.OrderBy(static domain => domain.Id, StringComparer.Ordinal),
				static domain => domain.Clone()),
			Room = BuildRoomSnapshot(state.Room),
			Party = BuildPartySnapshot(state.Party),
			Social = BuildSocialSnapshot(state.SocialState),
			Storyteller = BuildStorytellerSnapshot(state.StorytellerState),
			Zones = BuildZoneSnapshots(state.Zones),
			Crops = BuildCropSnapshots(state.Crops),
			ActiveConversations = state.ActiveConversations.Count == 0
				? []
				: [.. state.ActiveConversations
					.OrderBy(static kv => kv.Key, StringComparer.Ordinal)
					.Select(static kv => ConversationStateSnapshot.FromRuntime(kv.Value))],
		};

		return new SaveFile
		{
			Version = CurrentVersion,
			SaveVersion = CurrentVersion,
			Header = BuildHeader(payload, headerContext),
			Payload = payload,
		};
	}

	public static void ApplySnapshot(GameState state, SaveFile saveFile)
	{
		var payload = saveFile.Payload;
		state.Reset();
		state.World = null;
		state.WorldSeed = payload.WorldSeed;
		state.Turn = payload.Turn;
		state.PlayerX = payload.PlayerX;
		state.PlayerY = payload.PlayerY;
		state.PlayerZ = payload.PlayerZ;
		state.PlayerId = payload.PlayerId;
		state.PlayerAppearanceId = PlayerAppearanceCatalog.NormalizeId(payload.PlayerAppearanceId);
		state.KillCount = payload.KillCount;
		state.GeneratorId = payload.GeneratorId;
		state.ViewModeId = payload.ViewModeId;
		state.Timeline = CreateTimelineState(payload.Timeline);
		state.Weather = CreateWeatherState(payload.Weather, payload.WorldSeed);
		state.IdentifiedActorTypes = new HashSet<string>(
			payload.IdentifiedActorTypes ?? [],
			StringComparer.Ordinal);
		state.IdentifiedItemTypes = new HashSet<string>(
			payload.IdentifiedItemTypes ?? [],
			StringComparer.Ordinal);
		state.EconomicDomains = payload.EconomicDomains?.ToDictionary(
			static domain => domain.Id,
			static domain => domain.Clone(),
			StringComparer.Ordinal) ?? new Dictionary<string, EconomicDomain>(StringComparer.Ordinal);
		state.EnsureDefaultEconomicDomains();
		state.Facilities = payload.Facilities?.ToDictionary(
			static facility => facility.Id,
			static facility => facility.Clone(),
			StringComparer.Ordinal) ?? new Dictionary<string, FacilityInstance>(StringComparer.Ordinal);
		state.StockpileZones = payload.StockpileZones != null
			? MapList(payload.StockpileZones, static zone => zone.Clone())
			: [];
		state.JobBoardState = new JobBoardState();
		state.Actors = payload.Actors.ToDictionary(
			static snapshot => snapshot.Id,
			CreateActor,
			StringComparer.Ordinal);
		foreach (var actor in state.Actors.Values)
		{
			NeedSystem.EnsureInitialized(actor, state.Turn);
			HealthSystem.EnsureInitialized(actor, state.Turn);
		}
		state.Room = CreateRoomRuntimeState(payload.Room);
		RoomRuntimeModule.RefreshControlledActorIds(state);
		RoomRuntimeModule.SyncLegacyPlayerAlias(state);
		state.Party = CreatePartyState(payload.Party);
		state.SocialState = CreateSocialState(payload.Social);
		state.StorytellerState = CreateStorytellerState(payload.Storyteller);
		state.Zones = CreateZoneDictionary(payload.Zones);
		state.Crops = CreateCropDictionary(payload.Crops);
		state.Quests = MapList(payload.Quests, CreateQuest);
		state.ActiveConversations = payload.ActiveConversations is { Count: > 0 }
			? payload.ActiveConversations.ToDictionary(
				static s => s.NpcActorId,
				static s => s.ToRuntime(),
				StringComparer.Ordinal)
			: new Dictionary<string, ConversationState>(StringComparer.Ordinal);

		DirtyChunkCache.Clear();
		foreach (var chunk in payload.DirtyChunks)
		{
			var coord = new ChunkCoord(chunk.Cx, chunk.Cy, chunk.Cz);
			DirtyChunkCache[coord] = chunk;
		}

		FacilityConstructionModule.RebuildConstructionTickets(state);
	}

	internal static readonly Dictionary<ChunkCoord, ChunkSnapshot> DirtyChunkCache = new();

	static SaveModule()
	{
		WeatherSurface.AccumulationStore = new SaveModuleWeatherAdapter();
	}

	/// <summary>供 ChunkManager.OnChunkLoad 使用：从存档缓存中恢复 dirty chunk。</summary>
	public static ChunkData? LoadChunkFromCache(ChunkCoord coord)
	{
		if (!DirtyChunkCache.TryGetValue(coord, out var snapshot))
			return null;

		return CreateChunkData(snapshot);
	}

	/// <summary>供 ChunkManager.OnChunkUnload 使用：将 dirty chunk 写入缓存。</summary>
	public static void SaveChunkToCache(ChunkCoord coord, ChunkData chunk)
	{
		DirtyChunkCache[coord] = BuildChunkSnapshot(coord, chunk);
	}

	private static List<ChunkSnapshot> BuildDirtyChunkSnapshots(GameState state)
	{
		if (state.World == null)
			return [];

		var dirtyChunks = new List<ChunkSnapshot>();
		foreach (var (coord, chunk) in state.World.Chunks.LoadedChunks.OrderBy(static entry => entry.Key.Cz)
			.ThenBy(static entry => entry.Key.Cy)
			.ThenBy(static entry => entry.Key.Cx))
		{
			if (!chunk.Dirty)
				continue;

			dirtyChunks.Add(BuildChunkSnapshot(coord, chunk));
		}

		return dirtyChunks;
	}

	private static TimelineSnapshot BuildTimelineSnapshot(TimelineState timeline) => new()
	{
		CurrentActorId = timeline.CurrentActorId,
		LastActorId = timeline.LastActorId,
		Actors = MapList(timeline.Actors.OrderBy(static entry => entry.ActorId, StringComparer.Ordinal), static entry => new TimelineActorSnapshot
		{
			ActorId = entry.ActorId,
			Charge = entry.Charge,
		}),
	};

	private static TimelineState CreateTimelineState(TimelineSnapshot snapshot) => new()
	{
		CurrentActorId = snapshot.CurrentActorId,
		LastActorId = snapshot.LastActorId,
		Actors = MapList(snapshot.Actors, static entry => new TimelineActorState
		{
			ActorId = entry.ActorId,
			Charge = entry.Charge,
		}),
	};

	private static WeatherStateSnapshot BuildWeatherStateSnapshot(WeatherState? weather, int worldSeed)
	{
		var source = weather ?? WeatherState.CreateDefault(worldSeed);
		return new WeatherStateSnapshot
		{
			FrontPhase = source.FrontPhase,
			DebugTypeId = source.DebugOverride != null ? WeatherIds.ToId(source.DebugOverride.Type) : null,
			DebugIntensityId = source.DebugOverride != null ? WeatherIds.ToId(source.DebugOverride.Intensity) : null,
			LastLocalTypeId = source.LastLocalWeather != null ? WeatherIds.ToId(source.LastLocalWeather.Type) : null,
			LastLocalIntensityId = source.LastLocalWeather != null ? WeatherIds.ToId(source.LastLocalWeather.Intensity) : null,
			LastLocalTurn = source.LastLocalWeather?.Turn,
		};
	}

	private static WeatherState CreateWeatherState(WeatherStateSnapshot? snapshot, int worldSeed)
	{
		if (snapshot == null)
			return WeatherState.CreateDefault(worldSeed);

		var result = new WeatherState
		{
			FrontPhase = snapshot.FrontPhase,
		};

		if (WeatherIds.TryParseType(snapshot.DebugTypeId, out var debugType)
			&& WeatherIds.TryParseIntensity(snapshot.DebugIntensityId, out var debugIntensity))
		{
			result.DebugOverride = new WeatherDebugOverride
			{
				Type = debugType,
				Intensity = debugIntensity,
			};
		}

		if (WeatherIds.TryParseType(snapshot.LastLocalTypeId, out var localType)
			&& WeatherIds.TryParseIntensity(snapshot.LastLocalIntensityId, out var localIntensity))
		{
			result.LastLocalWeather = new WeatherLocalSnapshot
			{
				Type = localType,
				Intensity = localIntensity,
				Turn = snapshot.LastLocalTurn ?? 0,
			};
		}

		return result;
	}

	private static RoomRuntimeSnapshot? BuildRoomSnapshot(RoomRuntimeState room)
	{
		if (!room.IsActive)
			return null;

		return new RoomRuntimeSnapshot
		{
			RoomId = room.RoomId,
			RoomCode = room.RoomCode,
			Players = MapList(
				room.Players.Values.OrderBy(static player => player.PlayerSessionId, StringComparer.Ordinal),
				static player => new RoomPlayerStateSnapshot
				{
					PlayerSessionId = player.PlayerSessionId,
					DisplayName = player.DisplayName,
					PrimaryActorId = player.PrimaryActorId,
					DelegatedActorIds = [.. player.DelegatedActorIds.OrderBy(static id => id, StringComparer.Ordinal)],
					CurrentControllerActorIds = [.. player.CurrentControllerActorIds.OrderBy(static id => id, StringComparer.Ordinal)],
					JoinToken = player.JoinToken,
					ReconnectToken = player.ReconnectToken,
					ReconnectDeadlineUtc = player.ReconnectDeadlineUtc,
					Connected = player.Connected,
					IsRoomOwner = player.IsRoomOwner,
				}),
			InteractionReservations = MapList(
				room.InteractionReservations.Values.OrderBy(static reservation => reservation.ReservationKey, StringComparer.Ordinal),
				static reservation => new InteractionReservationSnapshot
				{
					ReservationKey = reservation.ReservationKey,
					PlayerSessionId = reservation.PlayerSessionId,
					LastHeartbeatUtc = reservation.LastHeartbeatUtc,
					ExpiresAtUtc = reservation.ExpiresAtUtc,
				}),
			ActorControlBindings = MapList(
				room.ActorControlBindings.OrderBy(static entry => entry.Key, StringComparer.Ordinal),
				static entry => new ActorControlBindingSnapshot
				{
					ActorId = entry.Key,
					PrimaryOwnerPlayerId = entry.Value.PrimaryOwnerPlayerId,
					TemporaryControllerPlayerId = entry.Value.TemporaryControllerPlayerId,
					CanBeDelegated = entry.Value.CanBeDelegated,
				}),
			LastSnapshotSequence = room.LastSnapshotSequence,
		};
	}

	private static RoomRuntimeState CreateRoomRuntimeState(RoomRuntimeSnapshot? snapshot)
	{
		var room = new RoomRuntimeState();
		if (snapshot == null)
			return room;

		room.RoomId = snapshot.RoomId ?? string.Empty;
		room.RoomCode = snapshot.RoomCode ?? string.Empty;
		room.LastSnapshotSequence = snapshot.LastSnapshotSequence;

		foreach (var player in snapshot.Players ?? [])
		{
			if (string.IsNullOrWhiteSpace(player.PlayerSessionId))
				continue;

			room.Players[player.PlayerSessionId] = new RoomPlayerState
			{
				PlayerSessionId = player.PlayerSessionId,
				DisplayName = player.DisplayName ?? player.PlayerSessionId,
				PrimaryActorId = player.PrimaryActorId ?? string.Empty,
				DelegatedActorIds = player.DelegatedActorIds != null
					? [.. player.DelegatedActorIds]
					: [],
				CurrentControllerActorIds = player.CurrentControllerActorIds != null
					? [.. player.CurrentControllerActorIds]
					: [],
				JoinToken = player.JoinToken ?? string.Empty,
				ReconnectToken = player.ReconnectToken ?? string.Empty,
				ReconnectDeadlineUtc = player.ReconnectDeadlineUtc,
				Connected = player.Connected,
				IsRoomOwner = player.IsRoomOwner,
			};
		}

		foreach (var reservation in snapshot.InteractionReservations ?? [])
		{
			if (string.IsNullOrWhiteSpace(reservation.ReservationKey))
				continue;

			room.InteractionReservations[reservation.ReservationKey] = new InteractionReservation
			{
				ReservationKey = reservation.ReservationKey,
				PlayerSessionId = reservation.PlayerSessionId ?? string.Empty,
				LastHeartbeatUtc = reservation.LastHeartbeatUtc,
				ExpiresAtUtc = reservation.ExpiresAtUtc,
			};
		}

		foreach (var binding in snapshot.ActorControlBindings ?? [])
		{
			if (string.IsNullOrWhiteSpace(binding.ActorId))
				continue;

			room.ActorControlBindings[binding.ActorId] = new ActorControlBinding
			{
				PrimaryOwnerPlayerId = binding.PrimaryOwnerPlayerId ?? string.Empty,
				TemporaryControllerPlayerId = binding.TemporaryControllerPlayerId,
				CanBeDelegated = binding.CanBeDelegated,
			};
		}

		return room;
	}

	private static PartySnapshot BuildPartySnapshot(PartyState party) => new()
	{
		MemberIds = [.. party.MemberIds],
		ActiveActorId = party.ActiveId,
		MaxSize = party.MaxSize,
	};

	private static PartyState CreatePartyState(PartySnapshot? snapshot)
	{
		if (snapshot == null)
			return new PartyState();

		return new PartyState
		{
			MemberIds = snapshot.MemberIds != null ? [.. snapshot.MemberIds] : [],
			ActiveId = snapshot.ActiveActorId ?? string.Empty,
			MaxSize = snapshot.MaxSize > 0 ? snapshot.MaxSize : 6,
		};
	}

	private static SocialSnapshot BuildSocialSnapshot(SocialState social) => new()
	{
		Relations = MapList(
			social.Relations
				.OrderBy(static entry => entry.Key, StringComparer.Ordinal)
				.Select(static entry => entry.Value),
			static relation => new RelationEntrySnapshot
			{
				FromId = relation.FromId,
				ToId = relation.ToId,
				Opinion = relation.Opinion,
				Tags = [.. relation.Tags.OrderBy(static tag => tag, StringComparer.Ordinal)],
				LastInteractionTurn = relation.LastInteractionTurn,
			}),
		SocialCooldowns = new Dictionary<string, int>(social.SocialCooldowns, StringComparer.Ordinal),
	};

	private static SocialState CreateSocialState(SocialSnapshot? snapshot)
	{
		var state = new SocialState();
		if (snapshot == null)
			return state;

		if (snapshot.Relations != null)
		{
			foreach (var entry in snapshot.Relations)
			{
				if (string.IsNullOrEmpty(entry.FromId) || string.IsNullOrEmpty(entry.ToId))
					continue;

				var key = $"{entry.FromId}:{entry.ToId}";
				state.Relations[key] = new RelationEntry
				{
					FromId = entry.FromId,
					ToId = entry.ToId,
					Opinion = entry.Opinion,
					Tags = entry.Tags != null ? [.. entry.Tags] : [],
					LastInteractionTurn = entry.LastInteractionTurn,
				};
			}
		}

		if (snapshot.SocialCooldowns != null)
		{
			foreach (var (actorId, turn) in snapshot.SocialCooldowns)
				state.SocialCooldowns[actorId] = turn;
		}

		return state;
	}

	private static StorytellerSnapshot BuildStorytellerSnapshot(StorytellerState story) => new()
	{
		LastIncidentTurn = new Dictionary<string, int>(story.LastIncidentTurn, StringComparer.Ordinal),
		PendingIncidents = MapList(
			story.PendingIncidents,
			static pending => new PendingIncidentSnapshot
			{
				IncidentDefId = pending.IncidentDefId,
				TriggerTurn = pending.TriggerTurn,
				Params = new Dictionary<string, string>(pending.Params, StringComparer.Ordinal),
			}),
		History = MapList(
			story.History,
			static record => new IncidentRecordSnapshot
			{
				DefId = record.DefId,
				Turn = record.Turn,
				Category = record.Category,
			}),
		ThreatLevel = story.ThreatLevel,
		LastCheckTurn = story.LastCheckTurn,
	};

	private static StorytellerState CreateStorytellerState(StorytellerSnapshot? snapshot)
	{
		var state = new StorytellerState
		{
			LastIncidentTurn = new Dictionary<string, int>(StringComparer.Ordinal),
		};
		if (snapshot == null)
			return state;

		state.ThreatLevel = snapshot.ThreatLevel;
		state.LastCheckTurn = snapshot.LastCheckTurn;

		if (snapshot.LastIncidentTurn != null)
		{
			foreach (var (defId, turn) in snapshot.LastIncidentTurn)
				state.LastIncidentTurn[defId] = turn;
		}

		if (snapshot.PendingIncidents != null)
		{
			foreach (var pending in snapshot.PendingIncidents)
			{
				if (string.IsNullOrEmpty(pending.IncidentDefId))
					continue;

				state.PendingIncidents.Add(new PendingIncident
				{
					IncidentDefId = pending.IncidentDefId,
					TriggerTurn = pending.TriggerTurn,
					Params = pending.Params != null
						? new Dictionary<string, string>(pending.Params, StringComparer.Ordinal)
						: new Dictionary<string, string>(StringComparer.Ordinal),
				});
			}
		}

		if (snapshot.History != null)
		{
			foreach (var record in snapshot.History)
			{
				if (string.IsNullOrEmpty(record.DefId))
					continue;

				state.History.Add(new IncidentRecord
				{
					DefId = record.DefId,
					Turn = record.Turn,
					Category = record.Category,
				});
			}
		}

		return state;
	}

	private static List<ZoneSnapshot> BuildZoneSnapshots(IDictionary<string, ZoneDef> zones) => MapList(
		zones.Values.OrderBy(static zone => zone.Id, StringComparer.Ordinal),
		static zone => new ZoneSnapshot
		{
			Id = zone.Id,
			Name = zone.Name,
			Type = zone.Type,
			OwnerDomainId = zone.OwnerDomainId,
			Cells = [.. zone.Cells],
			CropId = zone.CropId,
			AllowedAnimalIds = [.. zone.AllowedAnimalIds],
			AllowedItemIds = [.. zone.AllowedItemIds],
			AllowedCategories = [.. zone.AllowedCategories],
			Priority = zone.Priority,
			Enabled = zone.Enabled,
		});

	private static Dictionary<string, ZoneDef> CreateZoneDictionary(List<ZoneSnapshot>? snapshots)
	{
		var result = new Dictionary<string, ZoneDef>(StringComparer.Ordinal);
		if (snapshots == null)
			return result;

		foreach (var snapshot in snapshots)
		{
			if (string.IsNullOrEmpty(snapshot.Id))
				continue;

			result[snapshot.Id] = new ZoneDef
			{
				Id = snapshot.Id,
				Name = snapshot.Name,
				Type = snapshot.Type,
				OwnerDomainId = snapshot.OwnerDomainId,
				Cells = snapshot.Cells != null ? [.. snapshot.Cells] : [],
				CropId = snapshot.CropId,
				AllowedAnimalIds = snapshot.AllowedAnimalIds != null ? [.. snapshot.AllowedAnimalIds] : [],
				AllowedItemIds = snapshot.AllowedItemIds != null ? [.. snapshot.AllowedItemIds] : [],
				AllowedCategories = snapshot.AllowedCategories != null ? [.. snapshot.AllowedCategories] : [],
				Priority = snapshot.Priority,
				Enabled = snapshot.Enabled,
			};
		}

		return result;
	}

	private static List<CropInstanceSnapshot> BuildCropSnapshots(IDictionary<string, CropInstance> crops) => MapList(
		crops.Values.OrderBy(static crop => crop.Id, StringComparer.Ordinal),
		static crop => new CropInstanceSnapshot
		{
			Id = crop.Id,
			CropDefId = crop.CropDefId,
			X = crop.X,
			Y = crop.Y,
			Z = crop.Z,
			Growth = crop.Growth,
			Mature = crop.Mature,
			Withered = crop.Withered,
			PlantedTurn = crop.PlantedTurn,
		});

	private static Dictionary<string, CropInstance> CreateCropDictionary(List<CropInstanceSnapshot>? snapshots)
	{
		var result = new Dictionary<string, CropInstance>(StringComparer.Ordinal);
		if (snapshots == null)
			return result;

		foreach (var snapshot in snapshots)
		{
			if (string.IsNullOrEmpty(snapshot.Id))
				continue;

			result[snapshot.Id] = new CropInstance
			{
				Id = snapshot.Id,
				CropDefId = snapshot.CropDefId,
				X = snapshot.X,
				Y = snapshot.Y,
				Z = snapshot.Z,
				Growth = snapshot.Growth,
				Mature = snapshot.Mature,
				Withered = snapshot.Withered,
				PlantedTurn = snapshot.PlantedTurn,
			};
		}

		return result;
	}

	private static ChunkSnapshot BuildChunkSnapshot(ChunkCoord coord, ChunkData chunk) => new()
	{
		Cx = coord.Cx,
		Cy = coord.Cy,
		Cz = coord.Cz,
		TerrainIds = [.. chunk.TerrainIds],
		Hardness = [.. chunk.Hardness],
		Stacks = chunk.Entities
			.OrderBy(static entry => entry.Key)
			.Select(static entry => new CellStackSnapshot
			{
				Index = entry.Key,
				Entities = MapList(entry.Value, BuildCellEntitySnapshot),
			})
			.ToList(),
		Nests = MapList(chunk.Nests, BuildNestSnapshot),
		SnowDepth = [.. chunk.SnowDepth],
		SandDepth = [.. chunk.SandDepth],
		Wetness = [.. chunk.Wetness],
		IceDepth = [.. chunk.IceDepth],
		GrassCover = [.. chunk.GrassCover],
		LastWeatherSimTurn = chunk.LastWeatherSimTurn,
	};

	private static ChunkData CreateChunkData(ChunkSnapshot snapshot)
	{
		var chunk = new ChunkData
		{
			Coord = new ChunkCoord(snapshot.Cx, snapshot.Cy, snapshot.Cz),
			Dirty = true,
			TerrainIds = [.. snapshot.TerrainIds],
			Hardness = [.. snapshot.Hardness],
			SnowDepth = CloneOrDefault(snapshot.SnowDepth, ChunkData.Area),
			SandDepth = CloneOrDefault(snapshot.SandDepth, ChunkData.Area),
			Wetness = CloneOrDefault(snapshot.Wetness, ChunkData.Area),
			IceDepth = CloneOrDefault(snapshot.IceDepth, ChunkData.Area),
			GrassCover = CloneOrDefault(snapshot.GrassCover, ChunkData.Area),
			LastWeatherSimTurn = snapshot.LastWeatherSimTurn ?? 0,
		};

		foreach (var stack in snapshot.Stacks)
			chunk.Entities[stack.Index] = MapList(stack.Entities, CreateCellEntity);

		chunk.Nests = MapList(snapshot.Nests, CreateNest);
		return chunk;
	}

	private static ActorSnapshot BuildActorSnapshot(Actor actor) => new()
	{
		Id = actor.Id,
		X = actor.X,
		Y = actor.Y,
		Z = actor.Z,
		Glyph = actor.Glyph,
		DisplayName = actor.DisplayName,
		TemplateId = actor.TemplateId,
		FacingX = actor.FacingX,
		FacingY = actor.FacingY,
		Faction = actor.Faction,
		BrainId = actor.BrainId,
		PrimaryDomainId = actor.PrimaryDomainId,
		AccessibleDomainIds = [.. actor.AccessibleDomainIds.OrderBy(static id => id, StringComparer.Ordinal)],
		WorkBrainId = actor.WorkBrainId,
		AwarenessState = actor.AwarenessState,
		HasHomePosition = actor.HasHomePosition,
		HomeX = actor.HomeX,
		HomeY = actor.HomeY,
		HomeZ = actor.HomeZ,
		AlertTargetActorId = actor.AlertTargetActorId,
		LastKnownTargetX = actor.LastKnownTargetX,
		LastKnownTargetY = actor.LastKnownTargetY,
		LastKnownTargetZ = actor.LastKnownTargetZ,
		StateTurns = actor.StateTurns,
		SearchTurnsRemaining = actor.SearchTurnsRemaining,
		Gold = actor.Gold,
		Inventory = MapList(actor.Inventory, BuildItemSnapshot),
		ShopSlots = MapList(actor.ShopSlots, BuildShopSlotSnapshot),
		Limbs = MapList(actor.Limbs, BuildLimbSnapshot),
		Race = actor.Race != null ? BuildRaceSnapshot(actor.Race) : null,
		Profession = actor.Profession != null ? BuildProfessionSnapshot(actor.Profession) : null,
		Buffs = MapList(actor.Buffs, BuildBuffSnapshot),
		Experiences = MapList(actor.Experiences, BuildExperienceSnapshot),
		SkillCooldowns = CopyDictionary(actor.SkillCooldowns),
		DialogMood = actor.DialogMood,
		DialogAffinity = actor.DialogAffinity,
		DialogMemory = [.. actor.DialogMemory],
		DialogTalkCount = actor.DialogTalkCount,
		DialogPersonality = CopyDictionary(actor.DialogPersonality),
		DialogNeeds = CopyDictionary(actor.DialogNeeds),
		Needs = actor.Needs.ToDictionary(
			static entry => entry.Key,
			static entry => new NeedStateSnapshot
			{
				Id = entry.Value.Id,
				Current = entry.Value.Current,
				Min = entry.Value.Min,
				Max = entry.Value.Max,
				LastUpdatedTurn = entry.Value.LastUpdatedTurn,
			},
			StringComparer.Ordinal),
		Thoughts = MapList(actor.Thoughts, static thought => new ThoughtStateSnapshot
		{
			Id = thought.Id,
			MoodOffset = thought.MoodOffset,
			ExpiresOnTurn = thought.ExpiresOnTurn,
			Source = thought.Source,
		}),
		MoodValue = actor.MoodValue,
		NeedsLastUpdatedTurn = actor.NeedsLastUpdatedTurn,
		HealthConditions = MapList(actor.HealthConditions, static condition => new HealthConditionStateSnapshot
		{
			Id = condition.Id,
			LimbId = condition.LimbId,
			Severity = condition.Severity,
			Permanent = condition.Permanent,
			Source = condition.Source,
			CreatedOnTurn = condition.CreatedOnTurn,
			LastUpdatedTurn = condition.LastUpdatedTurn,
			TendedQuality = condition.TendedQuality,
			TendedOnTurn = condition.TendedOnTurn,
			InfectionProgress = condition.InfectionProgress,
		}),
		PainValue = actor.PainValue,
		BloodLossValue = actor.BloodLossValue,
		WetnessValue = actor.WetnessValue,
		HealthLastUpdatedTurn = actor.HealthLastUpdatedTurn,
		Sex = actor.Sex,
		BirthTurn = actor.BirthTurn,
		PregnancyTicksRemaining = actor.PregnancyTicksRemaining,
		MateActorId = actor.MateActorId,
		MotherActorId = actor.MotherActorId,
		FatherActorId = actor.FatherActorId,
		Genome = actor.Genome?.Clone(),
		LastResolvedLifeStage = actor.LastResolvedLifeStage,
		CarriedByActorId = actor.CarriedByActorId,
		CarriedInfantId = actor.CarriedInfantId,
	};

	private static Actor CreateActor(ActorSnapshot snapshot)
	{
		var actor = new Actor
		{
			Id = snapshot.Id,
			X = snapshot.X,
			Y = snapshot.Y,
			Z = snapshot.Z,
			Glyph = snapshot.Glyph,
			DisplayName = snapshot.DisplayName,
			TemplateId = snapshot.TemplateId ?? string.Empty,
			FacingX = snapshot.FacingX,
			FacingY = snapshot.FacingY,
			Faction = snapshot.Faction,
			BrainId = snapshot.BrainId,
			PrimaryDomainId = snapshot.PrimaryDomainId ?? DomainIds.Public,
			AccessibleDomainIds = snapshot.AccessibleDomainIds != null
				? new HashSet<string>(snapshot.AccessibleDomainIds, StringComparer.Ordinal)
				: new HashSet<string>(StringComparer.Ordinal),
			WorkBrainId = snapshot.WorkBrainId ?? string.Empty,
			AwarenessState = snapshot.AwarenessState,
			HasHomePosition = snapshot.HasHomePosition,
			HomeX = snapshot.HomeX,
			HomeY = snapshot.HomeY,
			HomeZ = snapshot.HomeZ,
			AlertTargetActorId = snapshot.AlertTargetActorId,
			LastKnownTargetX = snapshot.LastKnownTargetX,
			LastKnownTargetY = snapshot.LastKnownTargetY,
			LastKnownTargetZ = snapshot.LastKnownTargetZ,
			StateTurns = snapshot.StateTurns,
			SearchTurnsRemaining = snapshot.SearchTurnsRemaining,
			Gold = snapshot.Gold,
			Inventory = MapList(snapshot.Inventory, CreateItem),
			ShopSlots = MapList(snapshot.ShopSlots, CreateShopSlot),
			Limbs = MapList(snapshot.Limbs, CreateLimb),
			Race = snapshot.Race != null ? CreateRace(snapshot.Race) : null,
			Profession = snapshot.Profession != null ? CreateProfession(snapshot.Profession) : null,
			Buffs = MapList(snapshot.Buffs, CreateBuff),
			Experiences = MapList(snapshot.Experiences, CreateExperience),
			SkillCooldowns = CopyDictionary(snapshot.SkillCooldowns),
			DialogMood = snapshot.DialogMood,
			DialogAffinity = snapshot.DialogAffinity,
			DialogMemory = [.. snapshot.DialogMemory],
			DialogTalkCount = snapshot.DialogTalkCount,
			DialogPersonality = CopyDictionary(snapshot.DialogPersonality),
			DialogNeeds = CopyDictionary(snapshot.DialogNeeds),
			Needs = snapshot.Needs?.ToDictionary(
				static entry => entry.Key,
				static entry => new NeedState
				{
					Id = entry.Value.Id,
					Current = entry.Value.Current,
					Min = entry.Value.Min,
					Max = entry.Value.Max,
					LastUpdatedTurn = entry.Value.LastUpdatedTurn,
				},
				StringComparer.Ordinal) ?? new Dictionary<string, NeedState>(StringComparer.Ordinal),
			Thoughts = snapshot.Thoughts != null
				? MapList(snapshot.Thoughts, static thought => new ThoughtState
				{
					Id = thought.Id,
					MoodOffset = thought.MoodOffset,
					ExpiresOnTurn = thought.ExpiresOnTurn,
					Source = thought.Source,
				})
				: [],
			MoodValue = snapshot.MoodValue ?? 0f,
			NeedsLastUpdatedTurn = snapshot.NeedsLastUpdatedTurn ?? 0,
			HealthConditions = snapshot.HealthConditions != null
				? MapList(snapshot.HealthConditions, static condition => new HealthConditionState
				{
					Id = condition.Id,
					LimbId = condition.LimbId,
					Severity = condition.Severity,
					Permanent = condition.Permanent,
					Source = condition.Source,
					CreatedOnTurn = condition.CreatedOnTurn,
					LastUpdatedTurn = condition.LastUpdatedTurn,
					TendedQuality = condition.TendedQuality,
					TendedOnTurn = condition.TendedOnTurn,
					InfectionProgress = condition.InfectionProgress,
				})
				: [],
			PainValue = snapshot.PainValue ?? 0f,
			BloodLossValue = snapshot.BloodLossValue ?? 0f,
			WetnessValue = snapshot.WetnessValue ?? 0f,
			HealthLastUpdatedTurn = snapshot.HealthLastUpdatedTurn ?? 0,
			Sex = snapshot.Sex ?? Sex.Female,
			BirthTurn = snapshot.BirthTurn ?? -1,
			PregnancyTicksRemaining = snapshot.PregnancyTicksRemaining,
			MateActorId = snapshot.MateActorId,
			MotherActorId = snapshot.MotherActorId,
			FatherActorId = snapshot.FatherActorId,
			Genome = snapshot.Genome?.Clone(),
			LastResolvedLifeStage = snapshot.LastResolvedLifeStage ?? LifeStage.Adult,
			CarriedByActorId = snapshot.CarriedByActorId,
			CarriedInfantId = snapshot.CarriedInfantId,
		};

		EnsureVitalLimbTags(actor);
		return actor.WithNormalizedEquipment();
	}

	private static ItemSnapshot BuildItemSnapshot(Item item) =>
		ItemSnapshotMapper.BuildSnapshot(item);

	private static Item CreateItem(ItemSnapshot snapshot) =>
		ItemSnapshotMapper.CreateItem(snapshot);

	private static ShopSlotSnapshot BuildShopSlotSnapshot(ShopSlot slot) => new()
	{
		Item = BuildItemSnapshot(slot.Item),
		Stock = slot.Stock,
	};

	private static ShopSlot CreateShopSlot(ShopSlotSnapshot snapshot) => new()
	{
		Item = CreateItem(snapshot.Item),
		Stock = snapshot.Stock,
	};

	private static LimbSnapshot BuildLimbSnapshot(Limb limb) => new()
	{
		Id = limb.Id,
		Name = limb.Name,
		MaxDurability = limb.MaxDurability,
		Durability = limb.Durability,
		PermanentDamage = limb.PermanentDamage,
		Material = limb.Material,
		BodyPart = limb.BodyPart,
		EquipLayers = [.. limb.EquipLayers],
		EquipSlots = MapList(limb.EquipSlots, BuildEquipSlotSnapshot),
		Capacities = CopyDictionary(limb.Capacities),
		Tags = CopyDictionary(limb.Tags),
	};

	private static Limb CreateLimb(LimbSnapshot snapshot) => new()
	{
		Id = snapshot.Id,
		Name = snapshot.Name,
		MaxDurability = snapshot.MaxDurability,
		Durability = snapshot.Durability,
		PermanentDamage = snapshot.PermanentDamage ?? 0,
		Material = snapshot.Material,
		BodyPart = snapshot.BodyPart,
		EquipLayers = [.. snapshot.EquipLayers],
		EquipSlots = MapList(snapshot.EquipSlots, CreateEquipSlot),
		Capacities = CopyDictionary(snapshot.Capacities),
		Tags = CopyDictionary(snapshot.Tags),
	};

	private static void EnsureVitalLimbTags(Actor actor)
	{
		if (actor.Limbs.Count == 0 || actor.Limbs.Any(CombatModule.IsVitalLimb))
			return;

		PresetDB.Load();
		foreach (var limb in actor.Limbs)
		{
			if (!HasVitalCapacity(limb))
				continue;

			limb.Tags[CombatModule.VitalTag] = 1;
		}
	}

	private static bool HasVitalCapacity(Limb limb)
	{
		foreach (var (capacityId, weight) in limb.Capacities)
		{
			if (weight <= 0f)
				continue;
			if (!PresetDB.Capacities.TryGetValue(capacityId, out var definition))
				continue;
			if (!string.IsNullOrWhiteSpace(definition.VitalEffect))
				return true;
		}

		return false;
	}

	private static EquipSlotSnapshot BuildEquipSlotSnapshot(EquipSlot slot) => new()
	{
		LimbId = slot.LimbId,
		BodyPart = slot.BodyPart,
		Layer = slot.Layer,
		ItemId = slot.ItemId,
	};

	private static EquipSlot CreateEquipSlot(EquipSlotSnapshot snapshot) => new()
	{
		LimbId = snapshot.LimbId,
		BodyPart = snapshot.BodyPart,
		Layer = snapshot.Layer,
		ItemId = snapshot.ItemId,
	};

	private static RaceSnapshot BuildRaceSnapshot(Race race) => new()
	{
		Id = race.Id,
		Name = race.Name,
		NeedProfileId = race.NeedProfileId,
		HealthProfileId = race.HealthProfileId,
		Tags = CopyDictionary(race.Tags),
	};

	private static Race CreateRace(RaceSnapshot snapshot) => new()
	{
		Id = snapshot.Id,
		Name = snapshot.Name,
		NeedProfileId = snapshot.NeedProfileId ?? string.Empty,
		HealthProfileId = snapshot.HealthProfileId ?? string.Empty,
		Tags = CopyDictionary(snapshot.Tags),
	};

	private static ProfessionSnapshot BuildProfessionSnapshot(Profession profession) => new()
	{
		Id = profession.Id,
		Name = profession.Name,
		Tags = CopyDictionary(profession.Tags),
	};

	private static Profession CreateProfession(ProfessionSnapshot snapshot) => new()
	{
		Id = snapshot.Id,
		Name = snapshot.Name,
		Tags = CopyDictionary(snapshot.Tags),
	};

	private static BuffSnapshot BuildBuffSnapshot(Buff buff) => new()
	{
		Id = buff.Id,
		Name = buff.Name,
		RemainingTurns = buff.RemainingTurns,
		Tags = CopyDictionary(buff.Tags),
	};

	private static Buff CreateBuff(BuffSnapshot snapshot) => new()
	{
		Id = snapshot.Id,
		Name = snapshot.Name,
		RemainingTurns = snapshot.RemainingTurns,
		Tags = CopyDictionary(snapshot.Tags),
	};

	private static ExperienceSnapshot BuildExperienceSnapshot(Experience experience) => new()
	{
		Id = experience.Id,
		Name = experience.Name,
		Tags = CopyDictionary(experience.Tags),
	};

	private static Experience CreateExperience(ExperienceSnapshot snapshot) => new()
	{
		Id = snapshot.Id,
		Name = snapshot.Name,
		Tags = CopyDictionary(snapshot.Tags),
	};

	private static QuestSnapshot BuildQuestSnapshot(Quest quest) => new()
	{
		Id = quest.Id,
		Title = quest.Title,
		Description = quest.Description,
		Source = quest.Source,
		Status = quest.Status,
		AcceptedTurn = quest.AcceptedTurn,
		FinishedTurn = quest.FinishedTurn,
		Objectives = MapList(quest.Objectives, BuildQuestObjectiveSnapshot),
		Tags = CopyDictionary(quest.Tags),
	};

	private static Quest CreateQuest(QuestSnapshot snapshot) => new()
	{
		Id = snapshot.Id,
		Title = snapshot.Title,
		Description = snapshot.Description,
		Source = snapshot.Source,
		Status = snapshot.Status,
		AcceptedTurn = snapshot.AcceptedTurn,
		FinishedTurn = snapshot.FinishedTurn,
		Objectives = MapList(snapshot.Objectives, CreateQuestObjective),
		Tags = CopyDictionary(snapshot.Tags),
	};

	private static QuestObjectiveSnapshot BuildQuestObjectiveSnapshot(QuestObjective objective) => new()
	{
		Text = objective.Text,
		Current = objective.Current,
		Target = objective.Target,
	};

	private static QuestObjective CreateQuestObjective(QuestObjectiveSnapshot snapshot) => new()
	{
		Text = snapshot.Text,
		Current = snapshot.Current,
		Target = snapshot.Target,
	};

	private static CellEntitySnapshot BuildCellEntitySnapshot(CellEntity entity) => new()
	{
		Type = entity.Type,
		Glyph = entity.Glyph,
		EntityId = entity.EntityId,
		Meta = entity.Meta != null ? CopyDictionary(entity.Meta) : null,
	};

	private static CellEntity CreateCellEntity(CellEntitySnapshot snapshot) => new()
	{
		Type = snapshot.Type,
		Glyph = snapshot.Glyph,
		EntityId = snapshot.EntityId,
		Meta = snapshot.Meta != null ? CopyDictionary(snapshot.Meta) : null,
	};

	private static NestSnapshot BuildNestSnapshot(NestData nest) => new()
	{
		X = nest.X,
		Y = nest.Y,
		SpawnInterval = nest.SpawnInterval,
		TurnsSinceSpawn = nest.TurnsSinceSpawn,
		MaxSpawned = nest.MaxSpawned,
		TemplateId = nest.TemplateId,
	};

	private static NestData CreateNest(NestSnapshot snapshot) => new()
	{
		X = snapshot.X,
		Y = snapshot.Y,
		SpawnInterval = snapshot.SpawnInterval,
		TurnsSinceSpawn = snapshot.TurnsSinceSpawn,
		MaxSpawned = snapshot.MaxSpawned,
		TemplateId = snapshot.TemplateId,
	};

	private static SaveLoadStatus TryReadSaveFile(string filePath, out SaveFile? saveFile)
	{
		saveFile = null;
		if (!File.Exists(filePath))
			return SaveLoadStatus.NotFound;

		try
		{
			saveFile = DeserializeSaveFile(File.ReadAllText(filePath));
			if (saveFile == null)
			{
				saveFile = null;
				return SaveLoadStatus.Incompatible;
			}

			return SaveLoadStatus.Success;
		}
		catch
		{
			saveFile = null;
			return SaveLoadStatus.Incompatible;
		}
	}

	private static SaveHeader BuildHeader(SavePayload payload, SaveHeaderContext? headerContext) => new()
	{
		Title = headerContext?.CharacterName ?? headerContext?.WorldName ?? string.Empty,
		SavedAtUtc = DateTimeOffset.UtcNow,
		Turn = payload.Turn,
		PlayerZ = payload.PlayerZ,
		GeneratorId = payload.GeneratorId,
		ViewModeId = payload.ViewModeId,
		WorldId = headerContext?.WorldId,
		WorldName = headerContext?.WorldName,
		CharacterId = headerContext?.CharacterId,
		CharacterName = headerContext?.CharacterName,
	};

	private static string BuildSaveTitle(string filePath, SaveHeaderContext? headerContext)
	{
		if (!string.IsNullOrWhiteSpace(headerContext?.CharacterName))
			return headerContext.CharacterName!;

		var fileName = Path.GetFileName(filePath);
		return fileName switch
		{
			"quicksave.json" => "quicksave",
			"save.json" => "manual",
			_ => Path.GetFileNameWithoutExtension(filePath),
		};
	}

	private static string CanonicalizeJson(string json)
	{
		using var doc = JsonDocument.Parse(json);
		using var stream = new MemoryStream();
		using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

		WriteCanonicalElement(writer, doc.RootElement);
		writer.Flush();
		return Encoding.UTF8.GetString(stream.ToArray());
	}

	private static void WriteCanonicalElement(Utf8JsonWriter writer, JsonElement element)
	{
		switch (element.ValueKind)
		{
			case JsonValueKind.Object:
				writer.WriteStartObject();
				foreach (var property in element.EnumerateObject().OrderBy(static item => item.Name, StringComparer.Ordinal))
				{
					writer.WritePropertyName(property.Name);
					WriteCanonicalElement(writer, property.Value);
				}
				writer.WriteEndObject();
				return;

			case JsonValueKind.Array:
				writer.WriteStartArray();
				foreach (var item in element.EnumerateArray())
					WriteCanonicalElement(writer, item);
				writer.WriteEndArray();
				return;

			default:
				element.WriteTo(writer);
				return;
		}
	}

	private static List<TTarget> MapList<TSource, TTarget>(
		IEnumerable<TSource> source,
		Func<TSource, TTarget> map)
	{
		var result = new List<TTarget>();
		foreach (var item in source)
			result.Add(map(item));
		return result;
	}

	private static Dictionary<TKey, TValue> CopyDictionary<TKey, TValue>(IDictionary<TKey, TValue> source)
		where TKey : notnull
	{
		// 透传 source 的 comparer：避免把 Ordinal dict 静默降级为默认 EqualityComparer，
		// 否则 round-trip 后字典查询语义会与运行时不一致（特别是 string key）。
		var comparer = source is Dictionary<TKey, TValue> typed
			? typed.Comparer
			: EqualityComparer<TKey>.Default;
		var copy = new Dictionary<TKey, TValue>(source.Count, comparer);
		foreach (var (key, value) in source)
			copy[key] = value;
		return copy;
	}

	private static byte[] CloneOrDefault(byte[]? source, int expectedLength)
	{
		if (source == null || source.Length != expectedLength)
			return new byte[expectedLength];

		return [.. source];
	}

	private static void EnsureDir(string filePath)
	{
		var dir = Path.GetDirectoryName(filePath);
		if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
			Directory.CreateDirectory(dir);
	}

	private static bool TryParseSaveHeader(JsonElement headerElement, out SaveHeader header)
	{
		header = null!;
		if (!TryReadRequiredString(headerElement, "title", out var title)
			|| !TryReadDateTimeOffset(headerElement, "savedAtUtc", out var savedAtUtc)
			|| !TryReadInt32(headerElement, "turn", out var turn)
			|| !TryReadInt32(headerElement, "playerZ", out var playerZ)
			|| !TryReadRequiredString(headerElement, "generatorId", out var generatorId)
			|| !TryReadRequiredString(headerElement, "viewModeId", out var viewModeId))
		{
			return false;
		}

		header = new SaveHeader
		{
			Title = title,
			SavedAtUtc = savedAtUtc,
			Turn = turn,
			PlayerZ = playerZ,
			GeneratorId = generatorId,
			ViewModeId = viewModeId,
			WorldId = TryReadOptionalString(headerElement, "worldId"),
			WorldName = TryReadOptionalString(headerElement, "worldName"),
			CharacterId = TryReadOptionalString(headerElement, "characterId"),
			CharacterName = TryReadOptionalString(headerElement, "characterName"),
		};
		return true;
	}

	private static bool TryReadRequiredString(JsonElement element, string propertyName, out string value)
	{
		value = string.Empty;
		if (!element.TryGetProperty(propertyName, out var property)
			|| property.ValueKind != JsonValueKind.String)
		{
			return false;
		}

		var parsed = property.GetString();
		if (string.IsNullOrWhiteSpace(parsed))
			return false;

		value = parsed;
		return true;
	}

	private static string? TryReadOptionalString(JsonElement element, string propertyName)
	{
		if (!element.TryGetProperty(propertyName, out var property)
			|| property.ValueKind == JsonValueKind.Null
			|| property.ValueKind == JsonValueKind.Undefined)
		{
			return null;
		}

		return property.ValueKind == JsonValueKind.String
			? property.GetString()
			: null;
	}

	private static bool TryReadInt32(JsonElement element, string propertyName, out int value)
	{
		value = 0;
		return element.TryGetProperty(propertyName, out var property)
			&& property.ValueKind == JsonValueKind.Number
			&& property.TryGetInt32(out value);
	}

	private static bool TryReadDateTimeOffset(JsonElement element, string propertyName, out DateTimeOffset value)
	{
		value = default;
		if (!element.TryGetProperty(propertyName, out var property)
			|| property.ValueKind != JsonValueKind.String)
		{
			return false;
		}

		var raw = property.GetString();
		return !string.IsNullOrWhiteSpace(raw)
			&& DateTimeOffset.TryParse(raw, out value);
	}

	private static bool IsCompatibleVersion(int version) =>
		version >= MinimumCompatibleVersion && version <= CurrentVersion;

	private static Actor WithNormalizedEquipment(this Actor actor)
	{
		InventoryModule.NormalizeEquipmentReferences(actor);
		return actor;
	}
}
