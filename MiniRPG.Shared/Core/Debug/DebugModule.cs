using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Facility;
using MiniRPG.Core.World;

namespace MiniRPG.Core.Debug;

public static class DebugModule
{
	public readonly record struct DebugOption(string Id, string Label, string Group);

	public struct Result
	{
		public List<string> Logs;
		public bool NeedsFlush;
		public bool NeedsUiRefresh;
	}

	private const string GodBuffId = "debug_godmode";

	public static Result HandleCommand(string cmd, GameState state, IDebugSessionActions session)
	{
		var parts = cmd.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
		var verb = parts[0].ToLowerInvariant();
		var arg = parts.Length > 1 ? parts[1].Trim() : string.Empty;

		return verb switch
		{
			"/chest" => SpawnChestAtPlayer(state),
			"/gold" => AddGold(state, int.TryParse(arg, out var parsedGold) ? parsedGold : 1000),
			"/heal" => HealPlayer(state),
			"/spawn" => string.IsNullOrEmpty(arg) ? BuildSpawnTemplateHelp() : SpawnActorAtPlayerFacing(state, arg),
			"/god" => ToggleGodMode(state),
			"/down" => MoveDownFloor(state, session),
			"/npcs" => SpawnDialogTestNpcsNearPlayer(state),
			"/export_preset" => ExportPreset(session, arg),
			"/weather" => HandleWeatherCommandText(arg, state),
			"/facility" => HandleFacilityCommandText(arg, state),
			_ => CreateResult(
				"[debug] commands: /chest /gold /heal /spawn /god /down /npcs /export_preset /weather /facility"),
		};
	}

	public static IReadOnlyList<DebugOption> GetSpawnTemplateOptions()
	{
		var options = PresetDB.Actors
			.Where(static pair =>
				!string.Equals(pair.Key, "player", StringComparison.Ordinal)
				&& (string.Equals(pair.Value.Faction, Factions.Hostile, StringComparison.Ordinal)
					|| string.Equals(pair.Value.Faction, Factions.Friendly, StringComparison.Ordinal)))
			.Select(static pair =>
			{
				var name = string.IsNullOrWhiteSpace(pair.Value.DisplayName) ? pair.Key : pair.Value.DisplayName;
				return new DebugOption(pair.Key, $"{name} ({pair.Key})", pair.Value.Faction);
			})
			.OrderBy(static option =>
				string.Equals(option.Group, Factions.Hostile, StringComparison.Ordinal) ? 0 : 1)
			.ThenBy(static option => option.Label, StringComparer.CurrentCultureIgnoreCase)
			.ToArray();
		return options;
	}

	public static IReadOnlyList<DebugOption> GetFacilityOptions()
	{
		var options = FacilityRegistry.All.Values
			.Select(static def =>
			{
				var name = string.IsNullOrWhiteSpace(def.Name) ? def.Id : def.Name;
				return new DebugOption(def.Id, $"{name} ({def.Id})", string.Empty);
			})
			.OrderBy(static option => option.Label, StringComparer.CurrentCultureIgnoreCase)
			.ToArray();
		return options;
	}

	public static IReadOnlyList<string> BuildWeatherStatusLines(GameState state)
	{
		state.Weather ??= WeatherState.CreateDefault(state.WorldSeed);
		var sample = state.World != null
			? WeatherRules.GetLocalWeather(state, state.PlayerX, state.PlayerY, state.PlayerZ)
			: new WeatherSample(WeatherType.Clear, WeatherIntensity.Normal);
		var accumulation = WeatherSurface.GetAccumulation(state, state.PlayerX, state.PlayerY, state.PlayerZ);
		var debug = state.Weather.DebugOverride == null
			? LocalizationService.TOrFallback("debug.weather.override.off", "off")
			: $"{WeatherIds.ToId(state.Weather.DebugOverride.Type)}/{WeatherIds.ToId(state.Weather.DebugOverride.Intensity)}";
		return
		[
			LocalizationService.TOrFallback(
				"debug.weather.status",
				"[debug] weather turn={turn} local={local} debug={debug} phase={phase}",
				("turn", state.Turn),
				("local", $"{sample.TypeId}/{sample.IntensityId}"),
				("debug", debug),
				("phase", state.Weather.FrontPhase.ToString("0.000"))),
			LocalizationService.TOrFallback(
				"debug.weather.accum",
				"[debug] accum snow={snow} sand={sand} wet={wet} ice={ice}",
				("snow", accumulation.SnowDepth),
				("sand", accumulation.SandDepth),
				("wet", accumulation.Wetness),
				("ice", accumulation.IceDepth)),
		];
	}

	public static IReadOnlyList<string> BuildFacilityStatusLines(GameState state)
	{
		var player = ActorModule.GetPlayer(state);
		if (player == null)
			return ["[debug] no player"];

		if (!FacilityConstructionModule.TryFindNearbyFacility(state, player, static _ => true, includeCurrentCell: true, out var facility)
			|| facility == null)
		{
			return ["[debug] no nearby facility"];
		}

		var def = FacilityRegistry.Get(facility.FacilityDefId);
		var missing = FacilityConstructionModule.GetMissingConstructionMaterials(facility);
		var tickets = FacilityConstructionModule.GetConstructionTickets(state, facility.Id);
		return
		[
			$"[debug] facility {facility.Id} def={facility.FacilityDefId} name={def?.Name ?? facility.FacilityDefId}",
			$"[debug] stage={facility.Stage} owner={facility.OwnerDomainId} anchor=({facility.AnchorX},{facility.AnchorY},{facility.Z}) rot={facility.Rotation}",
			missing.Count == 0
				? "[debug] missing: none"
				: $"[debug] missing: {string.Join(", ", missing.Select(item => $"{item.ItemId}x{item.Count}"))}",
			tickets.Count == 0
				? "[debug] tickets: none"
				: $"[debug] tickets: {string.Join(" | ", tickets.Select(FormatTicket))}",
		];
	}

	public static Result SpawnChestAtPlayer(GameState state)
	{
		if (ActorModule.GetPlayer(state) == null)
			return CreateResult("[debug] no player");

		var count = SpawnChest(state, state.PlayerX, state.PlayerY);
		return CreateResult(
			$"[debug] spawned test chest with {count} items",
			needsFlush: true,
			needsUiRefresh: true);
	}

	public static Result AddGold(GameState state, int gold)
	{
		var player = ActorModule.GetPlayer(state);
		if (player == null)
			return CreateResult("[debug] no player");

		player.Gold += gold;
		return CreateResult(
			$"[debug] +{gold} gold (total: {player.Gold})",
			needsUiRefresh: true);
	}

	public static Result HealPlayer(GameState state)
	{
		var player = ActorModule.GetPlayer(state);
		if (player == null)
			return CreateResult("[debug] no player");

		HealAll(player);
		return CreateResult("[debug] restored all limb durability", needsUiRefresh: true);
	}

	public static Result ToggleGodMode(GameState state)
	{
		var player = ActorModule.GetPlayer(state);
		if (player == null)
			return CreateResult("[debug] no player");

		var on = ToggleGodMode(player);
		return CreateResult($"[debug] god mode: {(on ? "on" : "off")}", needsUiRefresh: true);
	}

	public static bool IsGodModeEnabled(GameState state)
	{
		var player = ActorModule.GetPlayer(state);
		return player != null && player.Buffs.Exists(static buff => buff.Id == GodBuffId);
	}

	public static Result MoveDownFloor(GameState state, IDebugSessionActions session)
	{
		if (ActorModule.GetPlayer(state) == null)
			return CreateResult("[debug] no player");

		session.ChangeFloor(goDown: true);
		return CreateResult(
			$"[debug] moved to floor {state.PlayerZ}",
			needsFlush: true,
			needsUiRefresh: true);
	}

	public static Result SpawnDialogTestNpcsNearPlayer(GameState state)
	{
		var player = ActorModule.GetPlayer(state);
		if (player == null)
			return CreateResult("[debug] no player");

		var npcsSpawned = SpawnDialogTestNpcs(state, player);
		return CreateResult(
			$"[debug] spawned {npcsSpawned} dialog test NPCs",
			needsFlush: true,
			needsUiRefresh: true);
	}

	public static Result SpawnActorAtPlayerFacing(GameState state, string templateId)
	{
		var player = ActorModule.GetPlayer(state);
		if (player == null)
			return CreateResult("[debug] no player");

		var sx = state.PlayerX + player.FacingX;
		var sy = state.PlayerY + player.FacingY;
		var spawned = SpawnEnemy(state, templateId, sx, sy);
		if (spawned == null)
			return CreateResult($"[debug] unknown template: {templateId}");

		return CreateResult(
			$"[debug] spawned {spawned.DisplayName} at ({sx},{sy})",
			needsFlush: true,
			needsUiRefresh: true);
	}

	public static Result ExportPreset(IDebugSessionActions session, string scenarioId)
	{
		if (string.IsNullOrWhiteSpace(scenarioId))
			return CreateResult("[debug] usage: /export_preset <scenario_id>");

		try
		{
			var exportPath = session.ExportPresetScenario(scenarioId);
			return CreateResult($"[debug] exported preset to: {exportPath}");
		}
		catch (Exception ex)
		{
			return CreateResult($"[debug] export failed: {ex.Message}");
		}
	}

	public static Result QueryWeatherStatus(GameState state) =>
		CreateResult(BuildWeatherStatusLines(state));

	public static Result LockWeather(GameState state, WeatherType type, WeatherIntensity intensity)
	{
		state.Weather ??= WeatherState.CreateDefault(state.WorldSeed);
		state.Weather.DebugOverride = new WeatherDebugOverride
		{
			Type = type,
			Intensity = intensity,
		};

		return CreateResult(
			LocalizationService.TOrFallback(
				"debug.weather.lock.set",
				"[debug] weather locked to {value}",
				("value", $"{WeatherIds.ToId(type)}/{WeatherIds.ToId(intensity)}")),
			needsFlush: true,
			needsUiRefresh: true);
	}

	public static Result UnlockWeather(GameState state)
	{
		state.Weather ??= WeatherState.CreateDefault(state.WorldSeed);
		state.Weather.DebugOverride = null;
		return CreateResult(
			LocalizationService.TOrFallback(
				"debug.weather.unlock",
				"[debug] weather override cleared"),
			needsFlush: true,
			needsUiRefresh: true);
	}

	public static Result StepWeather(GameState state, int turns)
	{
		turns = Math.Clamp(turns, 1, 512);
		for (var i = 0; i < turns; i++)
			TurnModule.AdvanceWorld(state);

		return CreateResult(
			LocalizationService.TOrFallback(
				"debug.weather.step.done",
				"[debug] advanced weather by {turns} turn(s)",
				("turns", turns)),
			needsFlush: true,
			needsUiRefresh: true);
	}

	public static Result ClearWeatherAccumulation(GameState state)
	{
		WeatherAccumulationSimulator.ClearAccumulation(state);
		return CreateResult(
			LocalizationService.TOrFallback(
				"debug.weather.clear_accum",
				"[debug] cleared weather accumulation"),
			needsFlush: true,
			needsUiRefresh: true);
	}

	public static Result QueryFacilityStatus(GameState state) =>
		CreateResult(BuildFacilityStatusLines(state));

	public static Result PlaceFacility(GameState state, string facilityId, string? directionText)
	{
		var player = ActorModule.GetPlayer(state);
		if (player == null)
			return CreateResult("[debug] no player");
		if (string.IsNullOrWhiteSpace(facilityId))
			return CreateResult("[debug] usage: /facility place <facility_id> [north|east|south|west]");

		if (!TryResolvePlacement(directionText, player, out var dx, out var dy, out var rotation))
			return CreateResult("[debug] direction must be north, east, south, or west");

		var placement = FacilityConstructionModule.TryPlaceBlueprint(
			state,
			facilityId,
			player.X + dx,
			player.Y + dy,
			player.Z,
			rotation,
			player.PrimaryDomainId);
		if (!placement.Success || placement.Facility == null)
		{
			if (string.Equals(placement.FailureReason, "blocked", StringComparison.Ordinal))
			{
				return CreateResult(
					$"[debug] facility blocked at: {string.Join(", ", placement.Blockers.Select(cell => $"({cell.X},{cell.Y},{cell.Z})"))}");
			}

			return CreateResult(placement.FailureReason switch
			{
				"missing_world" => "[debug] world is not initialized",
				"unknown_facility" => $"[debug] unknown facility: {facilityId}",
				_ => "[debug] facility placement failed",
			});
		}

		var missing = FacilityConstructionModule.GetMissingConstructionMaterials(placement.Facility);
		return CreateResult(
		[
			$"[debug] placed {placement.FacilityId} stage={placement.Facility.Stage} at ({placement.Facility.AnchorX},{placement.Facility.AnchorY},{placement.Facility.Z}) rot={placement.Facility.Rotation}",
			missing.Count == 0
				? "[debug] missing: none"
				: $"[debug] missing: {string.Join(", ", missing.Select(item => $"{item.ItemId}x{item.Count}"))}",
		],
			needsFlush: true,
			needsUiRefresh: true);
	}

	public static int SpawnChest(GameState state, int x, int y)
	{
		if (state.World == null)
			return 0;

		var chest = PresetDB.CloneItem("chest_wooden");
		chest.Name = "Debug Chest";

		var count = 0;
		foreach (var preset in PresetDB.Items.Values)
		{
			if (preset.Id == "chest_wooden")
				continue;

			var item = PresetDB.CloneItem(preset.Id);
			if (item.IsEquippable || item.Category is ItemCategories.Weapon or ItemCategories.Armor or ItemCategories.Tool)
			{
				chest.Contents!.Add(item);
				count++;
			}
		}

		state.World.PlaceItem(x, y, state.PlayerZ, chest);
		return count;
	}

	public static void HealAll(Actor actor)
	{
		foreach (var limb in actor.Limbs)
			limb.Durability = limb.MaxDurability;
	}

	public static Actor? SpawnEnemy(GameState state, string templateId, int x, int y)
	{
		if (!PresetDB.Actors.ContainsKey(templateId))
			return null;

		var id = $"debug_{templateId}_{state.Turn}_{x}_{y}";
		var actor = ActorTemplates.Spawn(templateId, id);
		actor.X = x;
		actor.Y = y;
		actor.Z = state.PlayerZ;
		ActorModule.Add(state, actor);
		return actor;
	}

	public static bool ToggleGodMode(Actor actor)
	{
		var existing = actor.Buffs.Find(b => b.Id == GodBuffId);
		if (existing != null)
		{
			actor.Buffs.Remove(existing);
			return false;
		}

		actor.Buffs.Add(new Buff
		{
			Id = GodBuffId,
			Name = "God Mode",
			RemainingTurns = -1,
			Tags = new Dictionary<string, int>(),
		});
		return true;
	}

	public static List<string> GetMonsterTemplateIds()
	{
		return PresetDB.Actors
			.Where(static pair => string.Equals(pair.Value.Faction, Factions.Hostile, StringComparison.Ordinal))
			.Select(static pair => pair.Key)
			.OrderBy(static id => id, StringComparer.Ordinal)
			.ToList();
	}

	public static int SpawnDialogTestNpcs(GameState state, Actor player)
	{
		string[] templates =
		[
			"merchant", "elder", "villager", "blacksmith_npc",
			"herbalist_npc", "cook_npc", "guard_npc", "tailor_npc",
			"elf_trader", "orc_merchant",
		];

		int[][] offsets =
		[
			[1, 0], [2, 0], [3, 0], [0, 1], [1, 1],
			[2, 1], [3, 1], [0, 2], [1, 2], [2, 2],
		];

		var count = 0;
		for (var i = 0; i < templates.Length; i++)
		{
			var tx = player.X + offsets[i][0];
			var ty = player.Y + offsets[i][1];
			var npc = SpawnEnemy(state, templates[i], tx, ty);
			if (npc == null)
				continue;

			npc.BrainId = null;
			ImmobilizeActor(npc);
			count++;
		}

		return count;
	}

	public static List<string> GetFriendlyTemplateIds()
	{
		return PresetDB.Actors
			.Where(static pair =>
				string.Equals(pair.Value.Faction, Factions.Friendly, StringComparison.Ordinal)
				&& !string.Equals(pair.Key, "player", StringComparison.Ordinal))
			.Select(static pair => pair.Key)
			.OrderBy(static id => id, StringComparer.Ordinal)
			.ToList();
	}

	private static Result BuildSpawnTemplateHelp() =>
		CreateResult(
		[
			$"[debug] hostile: {string.Join(", ", GetMonsterTemplateIds())}",
			$"[debug] friendly: {string.Join(", ", GetFriendlyTemplateIds())}",
		]);

	private static void ImmobilizeActor(Actor actor)
	{
		actor.Limbs.RemoveAll(l =>
			l.BodyPart is "arm" or "hand" or "leg" or "foot" or "paw" or "hoof");
	}

	private static Result HandleFacilityCommandText(string arg, GameState state)
	{
		var parts = (arg ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (parts.Length == 0)
		{
			return CreateResult(
				"[debug] usage: /facility place <facility_id> [north|east|south|west] | /facility status | /facility deliver | /facility build");
		}

		return parts[0].ToLowerInvariant() switch
		{
			"place" => parts.Length < 2
				? CreateResult("[debug] usage: /facility place <facility_id> [north|east|south|west]")
				: PlaceFacility(state, parts[1], parts.Length >= 3 ? parts[2] : null),
			"status" => QueryFacilityStatus(state),
			"deliver" or "build" => CreateResult(
				$"[debug] use /facility {parts[0].ToLowerInvariant()} during your turn; the command is routed through the timeline input path"),
			_ => CreateResult(
				"[debug] usage: /facility place <facility_id> [north|east|south|west] | /facility status | /facility deliver | /facility build"),
		};
	}

	private static string FormatTicket(WorkTicket ticket) => ticket.Type switch
	{
		WorkTicketType.DeliverConstructionMaterial => $"deliver {ticket.RequiredItemId}x{ticket.RequiredCount}",
		WorkTicketType.ConstructFacility => $"construct work={ticket.WorkRemaining}",
		_ => ticket.Type.ToString(),
	};

	private static bool TryResolvePlacement(string? directionText, Actor player, out int dx, out int dy, out FacilityRotation rotation)
	{
		if (string.IsNullOrWhiteSpace(directionText))
		{
			if (TryResolveFacingDirection(player.FacingX, player.FacingY, out dx, out dy, out rotation))
				return true;

			dx = 0;
			dy = 1;
			rotation = FacilityRotation.South;
			return true;
		}

		return TryResolveDirection(directionText, out dx, out dy, out rotation);
	}

	private static bool TryResolveFacingDirection(int facingX, int facingY, out int dx, out int dy, out FacilityRotation rotation)
	{
		if (facingX == 0 && facingY == -1)
		{
			dx = 0;
			dy = -1;
			rotation = FacilityRotation.North;
			return true;
		}

		if (facingX == 1 && facingY == 0)
		{
			dx = 1;
			dy = 0;
			rotation = FacilityRotation.East;
			return true;
		}

		if (facingX == 0 && facingY == 1)
		{
			dx = 0;
			dy = 1;
			rotation = FacilityRotation.South;
			return true;
		}

		if (facingX == -1 && facingY == 0)
		{
			dx = -1;
			dy = 0;
			rotation = FacilityRotation.West;
			return true;
		}

		dx = 0;
		dy = 0;
		rotation = FacilityRotation.South;
		return false;
	}

	private static bool TryResolveDirection(string directionText, out int dx, out int dy, out FacilityRotation rotation)
	{
		switch (directionText.ToLowerInvariant())
		{
			case "north":
				dx = 0;
				dy = -1;
				rotation = FacilityRotation.North;
				return true;

			case "east":
				dx = 1;
				dy = 0;
				rotation = FacilityRotation.East;
				return true;

			case "south":
				dx = 0;
				dy = 1;
				rotation = FacilityRotation.South;
				return true;

			case "west":
				dx = -1;
				dy = 0;
				rotation = FacilityRotation.West;
				return true;

			default:
				dx = 0;
				dy = 0;
				rotation = FacilityRotation.South;
				return false;
		}
	}

	private static Result HandleWeatherCommandText(string arg, GameState state)
	{
		var parts = (arg ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (parts.Length == 0)
			return CreateResult(LocalizeWeatherUsage());

		switch (parts[0].ToLowerInvariant())
		{
			case "status":
				return QueryWeatherStatus(state);

			case "lock":
				if (parts.Length < 2 || !WeatherIds.TryParseType(parts[1], out var type))
				{
					return CreateResult(LocalizationService.TOrFallback(
						"debug.weather.lock.usage",
						"[debug] usage: /weather lock <clear|rain|fog|snow|storm|thunderstorm|sandstorm> [light|normal|heavy]"));
				}

				var intensity = WeatherIntensity.Normal;
				if (parts.Length >= 3 && !WeatherIds.TryParseIntensity(parts[2], out intensity))
				{
					return CreateResult(LocalizationService.TOrFallback(
						"debug.weather.lock.invalid_intensity",
						"[debug] intensity must be light, normal, or heavy"));
				}

				return LockWeather(state, type, intensity);

			case "unlock":
				return UnlockWeather(state);

			case "step":
				var turns = 1;
				if (parts.Length >= 2 && (!int.TryParse(parts[1], out turns) || turns <= 0))
				{
					return CreateResult(LocalizationService.TOrFallback(
						"debug.weather.step.usage",
						"[debug] usage: /weather step <positive_turns>"));
				}

				return StepWeather(state, turns);

			case "clear_accum":
				return ClearWeatherAccumulation(state);

			default:
				return CreateResult(LocalizeWeatherUsage());
		}
	}

	private static string LocalizeWeatherUsage() =>
		LocalizationService.TOrFallback(
			"debug.weather.usage",
			"[debug] usage: /weather status | /weather lock <type> [light|normal|heavy] | /weather unlock | /weather step <turns> | /weather clear_accum");

	private static Result CreateResult(string log, bool needsFlush = false, bool needsUiRefresh = false) =>
		CreateResult([log], needsFlush, needsUiRefresh);

	private static Result CreateResult(IReadOnlyCollection<string> logs, bool needsFlush = false, bool needsUiRefresh = false) => new()
	{
		Logs = [.. logs],
		NeedsFlush = needsFlush,
		NeedsUiRefresh = needsUiRefresh,
	};
}
