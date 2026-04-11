using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Core.Weather;
using MiniRPG.Core.World;

namespace MiniRPG.Core.Health;

public static class FireSystem
{
	private const string IntensityMetaKey = "intensity";
	private const string FuelMetaKey = "fuel";
	private const string LastUpdatedTurnMetaKey = "lastUpdatedTurn";
	private const string DurabilityMetaKey = "durability";
	private const string MaxDurabilityMetaKey = "maxDurability";
	private const string MaterialMetaKey = "material";
	private const string FlammableMetaKey = "flammable";
	private const string ControlledFireSourceMetaKey = "controlledFireSource";

	private static readonly (int Dx, int Dy)[] CardinalDirs =
	[
		(0, -1),
		(0, 1),
		(-1, 0),
		(1, 0),
	];

	public static List<GameEvent> Advance(GameState state)
	{
		var events = new List<GameEvent>();
		if (state.World == null)
			return events;

		var hazards = SnapshotFireHazards(state.World);
		foreach (var hazard in hazards)
			events.AddRange(AdvanceHazard(state, hazard));

		// Only sort/process actors that are actually on fire — avoids O(N log N) over all actors each turn
		var burningActors = state.Actors.Values
			.Where(static a => !CombatModule.IsDead(a) && GetOnFireCondition(a) != null)
			.OrderBy(static a => a.Z)
			.ThenBy(static a => a.Y)
			.ThenBy(static a => a.X)
			.ThenBy(static a => a.Id, StringComparer.Ordinal)
			.ToList();
		foreach (var actor in burningActors)
		{
			if (!state.Actors.ContainsKey(actor.Id))
				continue;
			events.AddRange(AdvanceActorBurning(state, actor));
		}

		return events;
	}

	public static List<GameEvent> TryIgniteActor(GameState state, Actor actor, int intensity = -1, string source = "fire")
	{
		var events = new List<GameEvent>();
		var fireIntensity = intensity > 0 ? intensity : GameConfig.Fire.InitialIntensity;
		var severityGain = Math.Max(1f, fireIntensity * Math.Max(1f, GameConfig.Fire.OnFireSeverityGainPerIntensity));
		var existing = GetOnFireCondition(actor);
		var wasBurning = existing != null;
		if (existing == null)
		{
			existing = new HealthConditionState
			{
				Id = HealthConditionIds.OnFire,
				Severity = 0f,
				Permanent = false,
				Source = source,
				CreatedOnTurn = state.Turn,
				LastUpdatedTurn = state.Turn,
			};
			actor.HealthConditions.Add(existing);
		}

		existing.Severity = Math.Clamp(existing.Severity + severityGain, HealthSystem.ValueMin, HealthSystem.ValueMax);
		existing.Source = source;
		existing.LastUpdatedTurn = state.Turn;

		if (!wasBurning)
		{
			var igniteEvent = new GameEvent("actor_ignited")
			{
				TargetX = actor.X,
				TargetY = actor.Y,
				TargetZ = actor.Z,
				Damage = fireIntensity,
				ActionName = source,
			};
			IdentificationModule.PopulateTargetIdentity(igniteEvent, state, actor);
			events.Add(igniteEvent);
		}

		return events;
	}

	public static List<GameEvent> TryIgniteCell(
		GameState state,
		int x,
		int y,
		int z,
		int intensity = -1,
		int fuel = -1,
		bool isSpread = false)
	{
		var events = new List<GameEvent>();
		if (state.World == null)
			return events;

		var existing = state.World.GetEntity(x, y, z, CellEntityType.Hazard, Entities.Fire);
		var resolvedIntensity = Math.Clamp(
			intensity > 0 ? intensity : GameConfig.Fire.InitialIntensity,
			1,
			Math.Max(1, GameConfig.Fire.MaxIntensity));
		var resolvedFuel = Math.Max(1, fuel > 0 ? fuel : GameConfig.Fire.InitialFuel);

		if (existing != null)
		{
			var nextIntensity = Math.Max(ReadInt(existing.Meta, IntensityMetaKey, 1), resolvedIntensity);
			var nextFuel = Math.Max(ReadInt(existing.Meta, FuelMetaKey, 1), resolvedFuel);
			state.World.UpdateEntity(x, y, z, CellEntityType.Hazard, Entities.Fire, entity =>
			{
				entity.Glyph = "*";
				entity.Meta = BuildFireMeta(nextIntensity, nextFuel, state.Turn);
			});
			return events;
		}

		state.World.PushEntity(x, y, z, new CellEntity
		{
			Type = CellEntityType.Hazard,
			Glyph = "*",
			EntityId = Entities.Fire,
			Meta = BuildFireMeta(resolvedIntensity, resolvedFuel, state.Turn),
		});

		events.Add(new GameEvent(isSpread ? "fire_spread" : "fire_started")
		{
			TargetX = x,
			TargetY = y,
			TargetZ = z,
			Damage = resolvedIntensity,
		});
		return events;
	}

	public static ActionExecutionResult TryExtinguish(GameState state, Actor actor, int targetX, int targetY, int targetZ)
	{
		var result = new ActionExecutionResult();
		if (state.World == null || !CanExtinguishAt(state, actor, targetX, targetY, targetZ, out _))
			return result;

		if (targetX == actor.X && targetY == actor.Y && targetZ == actor.Z)
		{
			var onFire = GetOnFireCondition(actor);
			if (onFire != null)
			{
				onFire.Severity = Math.Max(0f, onFire.Severity - Math.Max(1, GameConfig.Fire.SelfExtinguishPower));
				onFire.LastUpdatedTurn = state.Turn;
				if (onFire.Severity <= 0.05f)
					actor.HealthConditions.Remove(onFire);

				var extinguishedEvent = new GameEvent("fire_extinguished")
				{
					TargetX = actor.X,
					TargetY = actor.Y,
					TargetZ = actor.Z,
					Damage = GameConfig.Fire.SelfExtinguishPower,
					ActionName = "self",
				};
				IdentificationModule.PopulateInitiatorIdentity(extinguishedEvent, state, actor);
				IdentificationModule.PopulateTargetIdentity(extinguishedEvent, state, actor);
				result.Events.Add(extinguishedEvent);
				result.Consumed = true;
			}
		}

		var hazard = state.World.GetEntity(targetX, targetY, targetZ, CellEntityType.Hazard, Entities.Fire);
		if (hazard != null)
		{
			var intensity = Math.Max(0, ReadInt(hazard.Meta, IntensityMetaKey, 1) - Math.Max(1, GameConfig.Fire.ExtinguishPower));
			var fuel = Math.Max(0, ReadInt(hazard.Meta, FuelMetaKey, 1) - Math.Max(1, GameConfig.Fire.ExtinguishPower));
			if (intensity <= 0 || fuel <= 0)
			{
				state.World.RemoveEntity(targetX, targetY, targetZ, CellEntityType.Hazard, Entities.Fire);
			}
			else
			{
				state.World.UpdateEntity(targetX, targetY, targetZ, CellEntityType.Hazard, Entities.Fire, entity =>
				{
					entity.Meta = BuildFireMeta(intensity, fuel, state.Turn);
				});
			}

			var extinguishedEvent = new GameEvent("fire_extinguished")
			{
				TargetX = targetX,
				TargetY = targetY,
				TargetZ = targetZ,
				Damage = GameConfig.Fire.ExtinguishPower,
			};
			IdentificationModule.PopulateInitiatorIdentity(extinguishedEvent, state, actor);
			result.Events.Add(extinguishedEvent);
			result.Consumed = true;
		}

		return result;
	}

	public static bool CanExtinguishAt(GameState state, Actor actor, int targetX, int targetY, int targetZ, out SkillCastFailureReason reason)
	{
		reason = SkillCastFailureReason.InvalidTarget;
		if (state.World == null || actor.Z != targetZ)
			return false;

		var distance = Math.Abs(actor.X - targetX) + Math.Abs(actor.Y - targetY);
		if (distance > 1)
		{
			reason = SkillCastFailureReason.OutOfRange;
			return false;
		}

		if (targetX == actor.X && targetY == actor.Y)
		{
			if (GetOnFireCondition(actor) != null)
				return true;

			if (GetFireIntensityAt(state, targetX, targetY, targetZ) > 0)
				return true;
		}

		return GetFireIntensityAt(state, targetX, targetY, targetZ) > 0;
	}

	public static int GetFireIntensityAt(GameState state, int x, int y, int z)
	{
		if (state.World == null)
			return 0;

		var fire = state.World.GetEntity(x, y, z, CellEntityType.Hazard, Entities.Fire);
		return fire == null ? 0 : ReadInt(fire.Meta, IntensityMetaKey, 1);
	}

	public static bool IsDangerousCell(GameState state, int x, int y, int z) =>
		GetFireIntensityAt(state, x, y, z) > 0;

	public static bool IsSafeWalkableForActor(GameState state, Actor actor, int x, int y, int z) =>
		state.World != null
		&& actor.Z == z
		&& state.World.IsWalkable(x, y, z)
		&& !IsDangerousCell(state, x, y, z);

	private static List<GameEvent> AdvanceHazard(GameState state, FireHazardSnapshot snapshot)
	{
		var events = new List<GameEvent>();
		var world = state.World;
		if (world == null)
			return events;

		var entity = world.GetEntity(snapshot.X, snapshot.Y, snapshot.Z, CellEntityType.Hazard, Entities.Fire);
		if (entity == null)
			return events;

		var intensity = Math.Clamp(ReadInt(entity.Meta, IntensityMetaKey, snapshot.Intensity), 1, Math.Max(1, GameConfig.Fire.MaxIntensity));
		var fuel = Math.Max(0, ReadInt(entity.Meta, FuelMetaKey, snapshot.Fuel));

		events.AddRange(DamageGroundItems(state, snapshot.X, snapshot.Y, snapshot.Z, intensity));
		events.AddRange(DamageFixture(state, snapshot.X, snapshot.Y, snapshot.Z, intensity));
		events.AddRange(DamageTerrain(state, snapshot.X, snapshot.Y, snapshot.Z, intensity));
		events.AddRange(IgniteActorsNearFire(state, snapshot.X, snapshot.Y, snapshot.Z, intensity));
		events.AddRange(SpreadFire(state, snapshot.X, snapshot.Y, snapshot.Z, intensity));

		var surface = WeatherSurface.GetAccumulation(state, snapshot.X, snapshot.Y, snapshot.Z);
		var decay = Math.Max(1, GameConfig.Fire.FuelDecayPerTurn);
		decay += surface.Wetness / Math.Max(1, GameConfig.Fire.WetnessDecayDivisor);
		if (IsRainSuppressing(state, snapshot.X, snapshot.Y, snapshot.Z))
			decay += Math.Max(0, GameConfig.Fire.RainDecayBonus);

		var nextFuel = Math.Max(0, fuel - decay);
		var nextIntensity = Math.Clamp(intensity, 0, Math.Max(1, GameConfig.Fire.MaxIntensity));
		if (nextFuel < intensity * 2)
			nextIntensity = Math.Max(0, nextIntensity - 1);
		if (surface.Wetness >= GameConfig.Weather.WetGlossThreshold)
			nextIntensity = Math.Max(0, nextIntensity - 1);

		if (nextFuel <= 0 || nextIntensity <= 0)
		{
			world.RemoveEntity(snapshot.X, snapshot.Y, snapshot.Z, CellEntityType.Hazard, Entities.Fire);
			events.Add(new GameEvent("fire_extinguished")
			{
				TargetX = snapshot.X,
				TargetY = snapshot.Y,
				TargetZ = snapshot.Z,
			});
			return events;
		}

		world.UpdateEntity(snapshot.X, snapshot.Y, snapshot.Z, CellEntityType.Hazard, Entities.Fire, fire =>
		{
			fire.Meta = BuildFireMeta(nextIntensity, nextFuel, state.Turn);
		});
		return events;
	}

	private static List<GameEvent> AdvanceActorBurning(GameState state, Actor actor)
	{
		var events = new List<GameEvent>();
		var onFire = GetOnFireCondition(actor);
		if (onFire == null)
			return events;

		var intensity = Math.Max(1, (int)MathF.Ceiling(onFire.Severity / Math.Max(1f, GameConfig.Fire.OnFireSeverityGainPerIntensity)));
		var damage = Math.Max(1, (int)MathF.Round(intensity * Math.Max(0.1f, GameConfig.Fire.ActorDamagePerIntensity), MidpointRounding.AwayFromZero));
		var limb = PickBurnTargetLimb(actor);
		if (limb != null)
			events.AddRange(CombatModule.ApplyEnvironmentalDamage(state, actor, limb, damage, DamageTypes.Fire));

		events.AddRange(DamageInventoryItems(state, actor, intensity));

		onFire.Severity = Math.Max(0f, onFire.Severity - Math.Max(2f, intensity));
		onFire.LastUpdatedTurn = state.Turn;
		if (onFire.Severity <= 0.05f)
		{
			actor.HealthConditions.Remove(onFire);
			var extinguishedEvent = new GameEvent("fire_extinguished")
			{
				TargetX = actor.X,
				TargetY = actor.Y,
				TargetZ = actor.Z,
				ActionName = "actor",
			};
			IdentificationModule.PopulateTargetIdentity(extinguishedEvent, state, actor);
			events.Add(extinguishedEvent);
		}

		return events;
	}

	private static List<GameEvent> DamageGroundItems(GameState state, int x, int y, int z, int intensity)
	{
		var events = new List<GameEvent>();
		var world = state.World;
		if (world == null)
			return events;

		var items = world.PeekGroundItems(x, y, z)
			.OrderBy(static item => item.InstanceId, StringComparer.Ordinal)
			.ToList();
		foreach (var item in items)
			events.AddRange(ApplyFireToGroundItem(state, item, x, y, z, intensity));
		return events;
	}

	private static List<GameEvent> ApplyFireToGroundItem(GameState state, Item item, int x, int y, int z, int intensity)
	{
		var events = new List<GameEvent>();
		var world = state.World;
		if (world == null)
			return events;

		var damage = ResolveItemDamage(intensity);
		if (damage <= 0)
			return events;

		item.EnsureRuntimeState();
		item.ApplyDurabilityDamage(damage);
		events.Add(CreateItemEvent("item_burned", state, item, x, y, z, damage));
		if (item.Durability > 0)
		{
			world.UpdateGroundItem(x, y, z, item);
			return events;
		}

		SpillContentsToGround(state, item, x, y, z);
		world.RemoveEntity(x, y, z, CellEntityType.Item, item.InstanceId);
		events.Add(CreateItemEvent("item_destroyed_by_fire", state, item, x, y, z, damage));
		return events;
	}

	private static List<GameEvent> DamageFixture(GameState state, int x, int y, int z, int intensity)
	{
		var events = new List<GameEvent>();
		var world = state.World;
		if (world == null)
			return events;

		var fixture = world.GetFirstEntity(x, y, z, CellEntityType.Fixture);
		if (fixture == null || IsControlledFireSource(fixture) || !IsFlammableFixture(fixture))
			return events;

		var damage = Math.Max(1, (int)MathF.Round(intensity * Math.Max(0.1f, GameConfig.Fire.FixtureDamagePerIntensity), MidpointRounding.AwayFromZero));
		var maxDurability = ReadInt(fixture.Meta, MaxDurabilityMetaKey, FixtureRegistry.Get(fixture.EntityId)?.MaxDurability ?? 0);
		var durability = ReadInt(fixture.Meta, DurabilityMetaKey, maxDurability);
		var nextDurability = Math.Max(0, durability - damage);

		if (nextDurability > 0)
		{
			world.UpdateEntity(x, y, z, CellEntityType.Fixture, fixture.EntityId, entity =>
			{
				entity.Meta ??= new Dictionary<string, string>(StringComparer.Ordinal);
				entity.Meta[DurabilityMetaKey] = nextDurability.ToString(CultureInfo.InvariantCulture);
				entity.Meta[MaxDurabilityMetaKey] = maxDurability.ToString(CultureInfo.InvariantCulture);
			});
			return events;
		}

		var fixtureDef = FixtureRegistry.Get(fixture.EntityId);
		world.RemoveEntity(x, y, z, CellEntityType.Fixture, fixture.EntityId);
		if (fixtureDef != null
			&& !fixtureDef.RemovedOnDestroy
			&& !string.IsNullOrWhiteSpace(fixtureDef.BurnsInto)
			&& !string.Equals(fixtureDef.BurnsInto, Entities.Fire, StringComparison.Ordinal))
		{
			world.SetFixture(x, y, z, WorldMap.ResolveFixtureGlyph(fixtureDef.BurnsInto), fixtureDef.BurnsInto);
		}

		events.Add(new GameEvent("fixture_burned_down")
		{
			ActionName = fixture.EntityId,
			TargetX = x,
			TargetY = y,
			TargetZ = z,
			Damage = damage,
		});
		return events;
	}

	private static List<GameEvent> DamageTerrain(GameState state, int x, int y, int z, int intensity)
	{
		var events = new List<GameEvent>();
		var world = state.World;
		if (world == null)
			return events;

		var terrain = world.GetTerrain(x, y, z);
		var hardness = world.GetHardness(x, y, z);
		if (hardness <= 0 || GetMaterialFlammability(terrain.Material) <= 0f)
			return events;

		var damage = Math.Max(1, (int)MathF.Round(intensity * Math.Max(0.1f, GameConfig.Fire.TerrainDamagePerIntensity), MidpointRounding.AwayFromZero));
		var nextHardness = Math.Max(0, hardness - damage);
		if (nextHardness > 0)
		{
			world.SetHardness(x, y, z, (byte)Math.Min(byte.MaxValue, nextHardness));
			return events;
		}

		var burnsInto = string.IsNullOrWhiteSpace(terrain.BreaksInto) ? Terrains.Rubble : terrain.BreaksInto;
		world.SetTerrain(x, y, z, burnsInto);
		return events;
	}

	private static List<GameEvent> IgniteActorsNearFire(GameState state, int x, int y, int z, int intensity)
	{
		var events = new List<GameEvent>();
		var world = state.World;
		if (world == null)
			return events;

		foreach (var actor in world.GetActorsAt(x, y, z, state.Actors))
			events.AddRange(TryIgniteActor(state, actor, intensity, source: "fire"));

		if (intensity < Math.Max(1, GameConfig.Fire.AdjacentActorIgniteThreshold))
			return events;

		foreach (var (dx, dy) in CardinalDirs)
		{
			foreach (var actor in world.GetActorsAt(x + dx, y + dy, z, state.Actors))
				events.AddRange(TryIgniteActor(state, actor, Math.Max(1, intensity - 2), source: "fire"));
		}

		return events;
	}

	private static List<GameEvent> SpreadFire(GameState state, int x, int y, int z, int intensity)
	{
		var events = new List<GameEvent>();
		if (state.World == null)
			return events;

		foreach (var (dx, dy) in CardinalDirs)
		{
			var targetX = x + dx;
			var targetY = y + dy;
			if (GetFireIntensityAt(state, targetX, targetY, z) > 0)
				continue;

			var flammability = GetCellFlammability(state, targetX, targetY, z);
			if (flammability < Math.Max(0f, GameConfig.Fire.IgniteThreshold))
				continue;

			var suppression = GetSuppressionFactor(state, targetX, targetY, z);
			var chance = (GameConfig.Fire.SpreadBaseChance + intensity * GameConfig.Fire.SpreadIntensityFactor) * flammability * suppression;
			var roll = WeatherMath.HashNormalized(
				state.WorldSeed,
				state.Turn,
				x,
				y,
				targetX,
				targetY,
				z,
				977);
			if (roll > Math.Clamp(chance, 0f, 1f))
				continue;

			events.AddRange(TryIgniteCell(
				state,
				targetX,
				targetY,
				z,
				intensity: Math.Max(1, intensity - 1),
				fuel: Math.Max(2, intensity * 2),
				isSpread: true));
		}

		return events;
	}

	private static List<GameEvent> DamageInventoryItems(GameState state, Actor actor, int intensity)
	{
		var events = new List<GameEvent>();
		var items = actor.Inventory.ToList();
		foreach (var item in items)
		{
			var damage = ResolveItemDamage(Math.Max(1, intensity / 2));
			if (damage <= 0)
				continue;

			item.ApplyDurabilityDamage(damage);
			events.Add(CreateItemEvent("item_burned", state, item, actor.X, actor.Y, actor.Z, damage, actor));
			if (item.Durability > 0)
				continue;

			if (item.Equipped)
				InventoryModule.Unequip(actor, item, state);

			SpillContentsToGround(state, item, actor.X, actor.Y, actor.Z);
			actor.Inventory.Remove(item);
			events.Add(CreateItemEvent("item_destroyed_by_fire", state, item, actor.X, actor.Y, actor.Z, damage, actor));
		}

		return events;
	}

	private static void SpillContentsToGround(GameState state, Item item, int x, int y, int z)
	{
		if (state.World == null || item.Contents == null || item.Contents.Count == 0)
			return;

		foreach (var content in item.Contents.ToList())
		{
			content.EnsureRuntimeState();
			state.World.PlaceItem(x, y, z, content);
		}
		item.Contents.Clear();
	}

	private static HealthConditionState? GetOnFireCondition(Actor actor) =>
		actor.HealthConditions.FirstOrDefault(condition =>
			string.Equals(condition.Id, HealthConditionIds.OnFire, StringComparison.Ordinal)
			&& string.IsNullOrEmpty(condition.LimbId));

	private static Limb? PickBurnTargetLimb(Actor actor) =>
		actor.Limbs
			.OrderByDescending(static limb => limb.BodyPart == BodyParts.Torso)
			.ThenByDescending(CombatModule.IsVitalLimb)
			.ThenBy(static limb => limb.Id, StringComparer.Ordinal)
			.FirstOrDefault();

	private static bool IsFlammableFixture(CellEntity fixture)
	{
		var flammable = fixture.Meta != null && TryReadBool(fixture.Meta, FlammableMetaKey, out var metaFlammable)
			? metaFlammable
			: FixtureRegistry.Get(fixture.EntityId)?.Flammable ?? false;
		if (flammable)
			return true;

		var materialId = fixture.Meta?.GetValueOrDefault(MaterialMetaKey, string.Empty) ?? string.Empty;
		return GetMaterialFlammability(materialId) > 0f;
	}

	private static bool IsControlledFireSource(CellEntity fixture) =>
		fixture.Meta != null && TryReadBool(fixture.Meta, ControlledFireSourceMetaKey, out var metaControlled)
			? metaControlled
			: FixtureRegistry.Get(fixture.EntityId)?.ControlledFireSource ?? false;

	private static int ResolveItemDamage(int intensity) =>
		Math.Max(1, (int)MathF.Round(intensity * Math.Max(0.1f, GameConfig.Fire.ItemDamagePerIntensity), MidpointRounding.AwayFromZero));

	private static float GetCellFlammability(GameState state, int x, int y, int z)
	{
		var world = state.World;
		if (world == null)
			return 0f;

		var best = 0f;
		var terrain = world.GetTerrain(x, y, z);
		if (world.GetHardness(x, y, z) > 0)
			best = Math.Max(best, GetMaterialFlammability(terrain.Material));

		var fixture = world.GetFirstEntity(x, y, z, CellEntityType.Fixture);
		if (fixture != null && !IsControlledFireSource(fixture))
		{
			var fixtureMaterial = fixture.Meta?.GetValueOrDefault(MaterialMetaKey, string.Empty) ?? string.Empty;
			best = Math.Max(best, GetMaterialFlammability(fixtureMaterial));
			if (IsFlammableFixture(fixture))
				best = Math.Max(best, 0.35f);
		}

		foreach (var item in world.PeekGroundItems(x, y, z))
			best = Math.Max(best, GetMaterialFlammability(item.MaterialId));

		return Math.Clamp(best, 0f, 1f);
	}

	private static float GetMaterialFlammability(string? materialId)
	{
		if (string.IsNullOrWhiteSpace(materialId))
			return 0f;

		return Math.Clamp(MaterialRegistry.Get(materialId).Flammability, 0f, 1f);
	}

	private static bool IsRainSuppressing(GameState state, int x, int y, int z)
	{
		if (state.World == null || !state.World.IsWeatherExposed(x, y, z))
			return false;

		var weather = WeatherRules.GetLocalWeather(state, x, y, z);
		return weather.Type is WeatherType.Rain or WeatherType.Storm or WeatherType.Thunderstorm or WeatherType.Snow;
	}

	private static float GetSuppressionFactor(GameState state, int x, int y, int z)
	{
		var surface = WeatherSurface.GetAccumulation(state, x, y, z);
		var wetnessFactor = 1f - Math.Clamp(surface.Wetness / 100f, 0f, 0.8f);
		if (!IsRainSuppressing(state, x, y, z))
			return wetnessFactor;

		var weather = WeatherRules.GetLocalWeather(state, x, y, z);
		var rainFactor = weather.Intensity switch
		{
			WeatherIntensity.Heavy => 0.25f,
			WeatherIntensity.Normal => 0.45f,
			_ => 0.65f,
		};
		return wetnessFactor * rainFactor;
	}

	private static Dictionary<string, string> BuildFireMeta(int intensity, int fuel, int turn) =>
		new(StringComparer.Ordinal)
		{
			[IntensityMetaKey] = intensity.ToString(CultureInfo.InvariantCulture),
			[FuelMetaKey] = fuel.ToString(CultureInfo.InvariantCulture),
			[LastUpdatedTurnMetaKey] = turn.ToString(CultureInfo.InvariantCulture),
		};

	private static int ReadInt(IReadOnlyDictionary<string, string>? meta, string key, int fallback)
	{
		if (meta == null || !meta.TryGetValue(key, out var raw) || !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
			return fallback;
		return value;
	}

	private static bool TryReadBool(IReadOnlyDictionary<string, string> meta, string key, out bool value)
	{
		value = false;
		return meta.TryGetValue(key, out var raw) && bool.TryParse(raw, out value);
	}

	private static GameEvent CreateItemEvent(
		string type,
		GameState state,
		Item item,
		int x,
		int y,
		int z,
		int damage,
		Actor? actor = null)
	{
		var evt = new GameEvent(type)
		{
			TargetX = x,
			TargetY = y,
			TargetZ = z,
			Damage = damage,
		};
		IdentificationModule.PopulateItemIdentity(evt, state, item);
		if (actor != null)
			IdentificationModule.PopulateTargetIdentity(evt, state, actor);
		return evt;
	}

	private static List<FireHazardSnapshot> SnapshotFireHazards(WorldMap world)
	{
		var hazards = new List<FireHazardSnapshot>();
		foreach (var (coord, chunk) in world.Chunks.LoadedChunks
			.OrderBy(static entry => entry.Key.Cz)
			.ThenBy(static entry => entry.Key.Cy)
			.ThenBy(static entry => entry.Key.Cx))
		{
			foreach (var (index, entities) in chunk.Entities.OrderBy(static entry => entry.Key))
			{
				var fire = entities.FirstOrDefault(entity =>
					entity.Type == CellEntityType.Hazard
					&& string.Equals(entity.EntityId, Entities.Fire, StringComparison.Ordinal));
				if (fire == null)
					continue;

				var (lx, ly) = CoordUtil.IndexToLocal(index);
				var worldPos = CoordUtil.LocalToWorld(coord, lx, ly);
				hazards.Add(new FireHazardSnapshot(
					worldPos.X,
					worldPos.Y,
					worldPos.Z,
					Math.Max(1, ReadInt(fire.Meta, IntensityMetaKey, GameConfig.Fire.InitialIntensity)),
					Math.Max(1, ReadInt(fire.Meta, FuelMetaKey, GameConfig.Fire.InitialFuel))));
			}
		}

		return hazards;
	}

	private readonly record struct FireHazardSnapshot(int X, int Y, int Z, int Intensity, int Fuel);
}
