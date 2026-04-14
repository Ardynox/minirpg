using System.Collections.Generic;
using System.Text;
using Godot;
using MiniRPG.Core.AI;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Module.Panel;

namespace MiniRPG.Module;

public readonly record struct LookCellInfo(
	PlayerVisionBand VisionBand,
	string Text,
	Actor? InspectableActor,
	Item? InspectableCorpseItem = null);

public enum LookInspectTargetKind
{
	Actor,
	CorpseItem,
	CellOnly,
}

public readonly record struct LookInspectTarget(
	LookInspectTargetKind Kind,
	PlayerVisionBand VisionBand,
	Vector3I Cell,
	string Text,
	Actor? Actor = null,
	Item? Item = null);

/// <summary>
/// 查看/观察模块：提供玩家周边与任意坐标格子的只读描述。
/// </summary>
public static class LookModule
{
	public static string BuildLookText(GameState state, FogOfWarTracker fogTracker)
	{
		var sb = new StringBuilder();
		var player = ActorModule.GetPlayer(state);
		sb.Append(LocalizationService.T("look.position",
			("floor", state.PlayerZ),
			("x", state.PlayerX),
			("y", state.PlayerY),
			("turn", state.Turn)));
		if (player != null)
			sb.Append(LocalizationService.T("look.gold", ("gold", player.Gold)));

		if (state.World != null)
		{
			var weather = GetDisplayWeatherSample(state, state.PlayerX, state.PlayerY, state.PlayerZ);
			var exposure = player != null
				? DefaultEnvironmentExposureProvider.Instance.Capture(state, player)
				: EnvironmentExposureSnapshot.Neutral;
			var exposureState = DescribeExposureState(state, state.PlayerX, state.PlayerY, state.PlayerZ);
			sb.Append('\n');
			sb.Append(LocalizationService.TOrFallback(
				"look.weather.current.detail",
				"Weather: {weather} ({intensity}) | {temp}C | {exposure} | Shelter {shelter}% | Heat +{heat}C",
				("weather", GameLocalizer.LocalizeWeatherName(weather.TypeId)),
				("intensity", GameLocalizer.LocalizeWeatherIntensity(weather.IntensityId)),
				("temp", exposure.AmbientTemperature.ToString("0.#")),
				("exposure", exposureState),
				("shelter", (exposure.ShelterStrength * 100f).ToString("0")),
				("heat", exposure.HeatSourceTemperatureBonus.ToString("0.#"))));

			var underfoot = DescribeAccumulation(WeatherSurface.GetSurfaceState(state, state.PlayerX, state.PlayerY, state.PlayerZ));
			if (!string.IsNullOrEmpty(underfoot))
			{
				sb.Append('\n');
				sb.Append(LocalizationService.TOrFallback("look.weather.underfoot", "Underfoot: {value}", ("value", underfoot)));
			}
		}

		if (player != null && player.Limbs.Count > 0)
		{
			sb.Append("\n");
			sb.Append(LocalizationService.T("look.limbs.prefix"));
			var parts = new List<string>();
			foreach (var l in player.Limbs)
			{
				var vital = CombatModule.IsVitalLimb(l)
					? LocalizationService.T("look.vital_marker")
					: "";
				parts.Add($"{l.Name}{vital}({l.Durability}/{l.MaxDurability})");
			}
			sb.Append(string.Join(" ", parts));
		}

		var standingOn = state.World!.GetFixtureId(state.PlayerX, state.PlayerY, state.PlayerZ);
		if (!string.IsNullOrEmpty(standingOn))
			sb.Append(LocalizationService.T("look.underfoot", ("fixture", FixtureLabel(standingOn))));

		var groundItems = (state.World?.PeekGroundItems(state.PlayerX, state.PlayerY, state.PlayerZ) ?? []);
		if (groundItems.Count > 0)
		{
			var names = groundItems.ConvertAll(item => FormatLookItem(state, item));
			sb.Append("\n");
			sb.Append(LocalizationService.T("look.ground_items", ("items", string.Join(", ", names))));
		}

		var underfootFire = FireSystem.GetFireIntensityAt(state, state.PlayerX, state.PlayerY, state.PlayerZ);
		if (underfootFire > 0)
		{
			sb.Append("\n");
			sb.Append(LocalizationService.TOrFallback(
				"look.underfoot",
				"  Underfoot: {fixture}",
				("fixture", $"{GameLocalizer.LocalizeFixtureName(Entities.Fire)} {underfootFire}")));
		}

		var coActors = ActorModule.GetAllAt(state, state.PlayerX, state.PlayerY);
		foreach (var actor in coActors)
		{
			if (actor.Id == state.PlayerId) continue;
			sb.Append(LocalizationService.T("look.same_tile", ("actor", IdentificationModule.GetActorDisplayName(state, actor))));
		}

		var dirs = new (string Name, int Dx, int Dy)[]
		{
			(LocalizationService.T("look.direction.north"), 0, -1),
			(LocalizationService.T("look.direction.south"), 0, 1),
			(LocalizationService.T("look.direction.west"), -1, 0),
			(LocalizationService.T("look.direction.east"), 1, 0),
		};
		foreach (var (name, dx, dy) in dirs)
		{
			var tx = state.PlayerX + dx;
			var ty = state.PlayerY + dy;
			sb.Append($"  {name}: {CellLabel(state, fogTracker, tx, ty, state.PlayerZ)}");
		}

		return sb.ToString();
	}

	public static LookCellInfo DescribeCell(GameState state, FogOfWarTracker fogTracker, int x, int y, int z)
	{
		var band = fogTracker.GetVisionBand(x, y, z);
		var text = BuildInspectCellText(state, band, x, y, z);
		var actor = band is PlayerVisionBand.Focused or PlayerVisionBand.Peripheral
			? GetInspectableActor(state, x, y, z)
			: null;
		var corpse = band is PlayerVisionBand.Focused or PlayerVisionBand.Peripheral
			? GetInspectableCorpseItem(state, x, y, z)
			: null;
		return new LookCellInfo(band, text, actor, corpse);
	}

	public static Actor? TryGetInspectableActor(GameState state, FogOfWarTracker fogTracker, int x, int y, int z) =>
		DescribeCell(state, fogTracker, x, y, z).InspectableActor;

	public static Item? TryGetInspectableCorpseItem(GameState state, FogOfWarTracker fogTracker, int x, int y, int z) =>
		DescribeCell(state, fogTracker, x, y, z).InspectableCorpseItem;

	public static LookInspectTarget ResolveInspectTarget(GameState state, FogOfWarTracker fogTracker, int x, int y, int z)
	{
		var info = DescribeCell(state, fogTracker, x, y, z);
		var cell = new Vector3I(x, y, z);
		if (info.InspectableActor != null)
			return new LookInspectTarget(LookInspectTargetKind.Actor, info.VisionBand, cell, info.Text, Actor: info.InspectableActor);
		if (info.InspectableCorpseItem != null)
			return new LookInspectTarget(LookInspectTargetKind.CorpseItem, info.VisionBand, cell, info.Text, Item: info.InspectableCorpseItem);

		return new LookInspectTarget(LookInspectTargetKind.CellOnly, info.VisionBand, cell, info.Text);
	}

	public static string? BuildHoverActorSummary(GameState state, int x, int y, int z)
	{
		var actors = ActorModule.GetAllAt(state, x, y, z);
		if (actors.Count == 0)
			return null;

		return string.Join(", ", actors.ConvertAll(actor => IdentificationModule.GetActorDisplayName(state, actor)));
	}

	public static string? BuildHoverItemSummary(GameState state, int x, int y, int z)
	{
		if (state.World == null)
			return null;

		var items = state.World.PeekGroundItems(x, y, z);
		if (items.Count == 0)
			return null;

		return string.Join(", ", items.ConvertAll(item => FormatLookItem(state, item)));
	}

	private static string BuildInspectCellText(GameState state, PlayerVisionBand band, int x, int y, int z)
	{
		var parts = new List<string>
		{
			LocalizationService.T("look.inspect.position",
				("floor", z),
				("x", x),
				("y", y),
				("vision", LocalizationService.T(GetVisionBandKey(band))))
		};

		switch (band)
		{
			case PlayerVisionBand.Focused:
			case PlayerVisionBand.Peripheral:
				AppendFocusedDetails(parts, state, x, y, z);
				break;
			case PlayerVisionBand.Memory:
				parts.Add(LocalizationService.T("look.inspect.summary", ("detail", MemoryCellLabel(state, x, y, z))));
				break;
			default:
				parts.Add(LocalizationService.T("look.inspect.summary", ("detail", LocalizationService.T("look.cell.unknown"))));
				break;
		}

		return string.Join("  ", parts);
	}

	private static void AppendFocusedDetails(List<string> parts, GameState state, int x, int y, int z)
	{
		var surface = WeatherSurface.GetSurfaceState(state, x, y, z);
		var terrain = surface.BaseTerrain.Solid
			? LocalizationService.T("look.cell.wall")
			: GameLocalizer.LocalizeTerrainName(surface.EffectiveTerrainId);
		parts.Add(LocalizationService.T("look.inspect.terrain", ("terrain", terrain)));

		if (surface.IsExposed)
		{
			var sample = WeatherRules.GetLocalWeather(state, x, y, z);
			parts.Add(LocalizationService.TOrFallback(
				"look.inspect.weather",
				"Weather: {weather} ({intensity})",
				("weather", GameLocalizer.LocalizeWeatherName(sample.TypeId)),
				("intensity", GameLocalizer.LocalizeWeatherIntensity(sample.IntensityId))));
		}

		var accumulation = DescribeAccumulation(surface);
		if (!string.IsNullOrEmpty(accumulation))
			parts.Add(LocalizationService.TOrFallback("look.inspect.accumulation", "Surface: {value}", ("value", accumulation)));

		var fixtureId = state.World!.GetFixtureId(x, y, z);
		if (!string.IsNullOrEmpty(fixtureId))
			parts.Add(LocalizationService.T("look.inspect.fixture", ("fixture", FixtureLabel(fixtureId))));

		var fireIntensity = FireSystem.GetFireIntensityAt(state, x, y, z);
		if (fireIntensity > 0)
		{
			parts.Add(LocalizationService.TOrFallback(
				"look.inspect.hazard",
				"Hazard: {hazard}",
				("hazard", $"{GameLocalizer.LocalizeFixtureName(Entities.Fire)} {fireIntensity}")));
		}

		var actors = ActorModule.GetAllAt(state, x, y, z);
		if (actors.Count > 0)
			parts.Add(LocalizationService.T("look.inspect.actors", ("actors", string.Join(", ", actors.ConvertAll(actor => IdentificationModule.GetActorDisplayName(state, actor))))));

		var items = state.World!.PeekGroundItems(x, y, z);
		if (items.Count > 0)
			parts.Add(LocalizationService.T("look.inspect.items", ("items", string.Join(", ", items.ConvertAll(item => FormatLookItem(state, item))))));

		if (parts.Count == 2)
			parts.Add(LocalizationService.T("look.inspect.empty"));
	}

	private static Actor? GetInspectableActor(GameState state, int x, int y, int z)
	{
		var actors = ActorModule.GetAllAt(state, x, y, z);
		if (actors.Count == 0)
			return null;

		foreach (var actor in actors)
		{
			if (actor.Id != state.PlayerId)
				return actor;
		}

		return actors[0];
	}

	private static Item? GetInspectableCorpseItem(GameState state, int x, int y, int z)
	{
		if (state.World == null)
			return null;

		var items = state.World.PeekGroundItems(x, y, z);
		foreach (var item in items)
		{
			if (item.IsCorpse)
				return item;
		}

		return null;
	}

	private static string GetVisionBandKey(PlayerVisionBand band) => band switch
	{
		PlayerVisionBand.Focused => "look.inspect.vision.focused",
		PlayerVisionBand.Peripheral => "look.inspect.vision.peripheral",
		PlayerVisionBand.Memory => "look.inspect.vision.memory",
		_ => "look.inspect.vision.unknown",
	};

	private static string CellLabel(GameState state, FogOfWarTracker fogTracker, int x, int y, int z)
	{
		var band = fogTracker.GetVisionBand(x, y, z);
		return band switch
		{
			PlayerVisionBand.Focused => FocusedCellLabel(state, x, y, z),
			PlayerVisionBand.Peripheral => PeripheralCellLabel(state, x, y, z),
			PlayerVisionBand.Memory => MemoryCellLabel(state, x, y, z),
			_ => LocalizationService.T("look.cell.unknown"),
		};
	}

	private static string FocusedCellLabel(GameState state, int x, int y, int z)
	{
		var surface = WeatherSurface.GetSurfaceState(state, x, y, z);
		if (surface.BaseTerrain.Solid)
			return LocalizationService.T("look.cell.wall");

		var actors = ActorModule.GetAllAt(state, x, y, z);
		if (actors.Count > 0)
			return string.Join("+", actors.ConvertAll(actor => IdentificationModule.GetActorDisplayName(state, actor)));

		var fireIntensity = FireSystem.GetFireIntensityAt(state, x, y, z);
		if (fireIntensity > 0)
			return GameLocalizer.LocalizeFixtureName(Entities.Fire);

		var items = state.World!.PeekGroundItems(x, y, z);
		if (items.Count > 0)
			return LocalizationService.T("look.cell.items", ("count", items.Count));

		var fixtureId = state.World!.GetFixtureId(x, y, z);
		if (!string.IsNullOrEmpty(fixtureId))
			return FixtureLabel(fixtureId);

		var accumulation = DescribeAccumulation(surface);
		return string.IsNullOrEmpty(accumulation)
			? LocalizationService.T("look.cell.empty")
			: accumulation;
	}

	private static string PeripheralCellLabel(GameState state, int x, int y, int z)
	{
		var surface = WeatherSurface.GetSurfaceState(state, x, y, z);
		if (surface.BaseTerrain.Solid)
			return LocalizationService.T("look.cell.obstacle");

		var actors = ActorModule.GetAllAt(state, x, y, z);
		if (actors.Count > 0)
		{
			foreach (var actor in actors)
			{
				if (actor.Id == state.PlayerId) continue;
				if (FactionRelation.IsHostile(Factions.Player, actor.Faction))
					return LocalizationService.T("look.cell.hostile_shape");
			}

			return LocalizationService.T("look.cell.moving_shape");
		}

		var groundItems = state.World!.PeekGroundItems(x, y, z);
		if (groundItems.Count > 0)
			return LocalizationService.T("look.cell.something");
		if (FireSystem.GetFireIntensityAt(state, x, y, z) > 0)
			return GameLocalizer.LocalizeFixtureName(Entities.Fire);
		if (!string.IsNullOrEmpty(state.World!.GetFixtureId(x, y, z)))
			return LocalizationService.T("look.cell.something");
		var accumulation = DescribeAccumulation(surface);
		return string.IsNullOrEmpty(accumulation)
			? LocalizationService.T("look.cell.empty")
			: accumulation;
	}

	private static string MemoryCellLabel(GameState state, int x, int y, int z)
	{
		var surface = WeatherSurface.GetSurfaceState(state, x, y, z);
		if (surface.BaseTerrain.Solid)
			return LocalizationService.T("look.cell.memory_wall");

		if (surface.HasSnowCover || surface.HasSandCover || surface.HasIceGloss)
			return GameLocalizer.LocalizeTerrainName(surface.EffectiveTerrainId);

		var accumulation = DescribeAccumulation(surface);
		return string.IsNullOrEmpty(accumulation)
			? LocalizationService.T("look.cell.memory_empty")
			: accumulation;
	}

	private static string DescribeAccumulation(WeatherSurfaceState surface)
	{
		var parts = new List<string>();
		if (surface.Accumulation.IceDepth > 0)
			parts.Add(LocalizationService.TOrFallback("weather.accum.ice", "icy"));
		if (surface.Accumulation.SnowDepth > 0)
			parts.Add(LocalizationService.TOrFallback("weather.accum.snow", "snow-covered"));
		if (surface.Accumulation.SandDepth > 0)
			parts.Add(LocalizationService.TOrFallback("weather.accum.sand", "sand-covered"));
		if (surface.Accumulation.Wetness > 0)
			parts.Add(LocalizationService.TOrFallback("weather.accum.wet", "wet"));
		return string.Join(", ", parts);
	}

	private static string FormatLookItem(GameState state, Item item) =>
		$"{(item.IsCorpse ? ItemFormatHelper.BuildCorpseDisplayName(item) : GameLocalizer.LocalizeItemName(item.Id, item.Name))} ({ItemConditionFormatter.BuildInlineDurability(item)})";

	private static WeatherSample GetDisplayWeatherSample(GameState state, int x, int y, int z)
	{
		if (state.World == null || state.Weather == null)
			return new WeatherSample(WeatherType.Clear, WeatherIntensity.Normal);

		return WeatherFieldSampler.Sample(state, x, y, z == 0 ? z : 0, state.Turn, state.Weather.FrontPhase);
	}

	private static float GetAmbientTemperature(GameState state, Actor? actor, int x, int y, int z)
	{
		if (actor != null)
			return DefaultEnvironmentExposureProvider.Instance.Capture(state, actor).AmbientTemperature;

		if (state.World == null || state.Weather == null)
			return EnvironmentExposureSnapshot.Neutral.AmbientTemperature;

		if (z != 0)
			return GameConfig.Weather.UndergroundNeutralTemperatureC;

		var sample = GetDisplayWeatherSample(state, x, y, z);
		if (state.World.IsWeatherExposed(x, y, z))
			return sample.AmbientTemperatureC;

		return Lerp(sample.AmbientTemperatureC, GameConfig.Weather.ShelterNeutralTemperatureC, GameConfig.Weather.ShelterTemperatureLerp);
	}

	private static string DescribeExposureState(GameState state, int x, int y, int z)
	{
		if (z != 0)
			return LocalizationService.TOrFallback("look.weather.exposure.underground", "underground");

		if (state.World?.IsWeatherExposed(x, y, z) == true)
			return LocalizationService.TOrFallback("look.weather.exposure.exposed", "exposed");

		return LocalizationService.TOrFallback("look.weather.exposure.sheltered", "sheltered");
	}

	private static float Lerp(float from, float to, float t) =>
		from + (to - from) * System.Math.Clamp(t, 0f, 1f);

	public static string FixtureLabel(string id) => id switch
	{
		Entities.StairDown => LocalizationService.T("fixture.stair_down"),
		Entities.StairUp => LocalizationService.T("fixture.stair_up"),
		Entities.Nest => LocalizationService.T("fixture.nest"),
		Entities.House => LocalizationService.T("fixture.house"),
		Entities.Item => LocalizationService.T("fixture.item"),
		Entities.Door => LocalizationService.T("fixture.door"),
		Entities.Campfire => LocalizationService.TOrFallback("fixture.campfire", "Campfire"),
		_ => id,
	};
}
