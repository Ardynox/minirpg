using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;
using MiniRPG.Core.Map;

namespace MiniRPG.Core.Health;

public static class SurgeryModule
{
	private const string OperateFailedEventType = "operate_failed";
	private const string CorpseSpawnedEventType = "corpse_spawned";
	private const string CorpseStrippedEventType = "corpse_stripped";
	private const string CorpseButcheredEventType = "corpse_butchered";
	private const string CorpseHarvestedEventType = "corpse_harvested";
	private const string SurgeryInstalledEventType = "surgery_installed";
	private const string LiveHarvestedEventType = "live_harvested";

	public static bool IsCorpseItem(Item? item) => item?.Corpse != null;

	public static IReadOnlyList<string> GetCorpseHarvestableLimbIds(Item corpse)
	{
		if (corpse.Corpse == null || corpse.Corpse.Butchered)
			return [];

		return corpse.Corpse.RemainingLimbIds
			.Where(static limbId => !string.IsNullOrWhiteSpace(limbId))
			.Where(static limbId => SurgeryOperationRegistry.FindMatching(SurgeryOperationModes.CorpseHarvest, limbId).Count > 0)
			.Distinct(StringComparer.Ordinal)
			.OrderBy(static limbId => limbId, StringComparer.Ordinal)
			.ToArray();
	}

	public static IReadOnlyList<string> GetLiveOperationLimbIds(Actor surgeon, Actor target)
	{
		var ids = new HashSet<string>(StringComparer.Ordinal);

		foreach (var limb in target.Limbs)
		{
			if (FindBestInstallItem(surgeon, limb.Id) != null
				|| SurgeryOperationRegistry.FindMatching(SurgeryOperationModes.LiveHarvest, limb.Id).Count > 0)
			{
				ids.Add(limb.Id);
			}
		}

		if (target.Race != null
			&& PresetDB.Races.TryGetValue(target.Race.Id, out var race))
		{
			foreach (var limbId in race.DefaultLimbs)
			{
				if (target.Limbs.Any(existing => string.Equals(existing.Id, limbId, StringComparison.Ordinal)))
					continue;

				if (FindBestInstallItem(surgeon, limbId) != null)
					ids.Add(limbId);
			}
		}

		return ids
			.OrderBy(static limbId => limbId, StringComparer.Ordinal)
			.ToArray();
	}

	public static Item? FindBestInstallItem(Actor surgeon, string limbId)
	{
		var indexed = FindBestInstallItemIndex(surgeon, limbId);
		return indexed?.Item;
	}

	public static Item? CreateSeveredLimbItem(string limbId)
	{
		var operation = SurgeryOperationRegistry.FindMatching(SurgeryOperationModes.CorpseHarvest, limbId).FirstOrDefault();
		if (operation == null || string.IsNullOrWhiteSpace(operation.ResultItemId))
			return null;

		return PresetDB.Items.ContainsKey(operation.ResultItemId)
			? PresetDB.CloneItem(operation.ResultItemId)
			: null;
	}

	public static bool TrySpawnCorpseOnDeath(GameState state, Actor actor, List<GameEvent> events, string deathEventType)
	{
		if (state.World == null)
			return false;

		var corpseItem = CreateCorpseFromActor(actor);
		if (corpseItem == null)
			return false;

		MapModule.PlaceItem(state, actor.X, actor.Y, actor.Z, corpseItem);
		var corpseEvent = new GameEvent(CorpseSpawnedEventType)
		{
			ActionName = deathEventType,
			TargetX = actor.X,
			TargetY = actor.Y,
			TargetZ = actor.Z,
		};
		IdentificationModule.PopulateTargetIdentity(corpseEvent, state, actor);
		IdentificationModule.PopulateItemIdentity(corpseEvent, state, corpseItem);
		events.Add(corpseEvent);
		return true;
	}

	public static ActionExecutionResult TryOperateOnCorpse(
		GameState state,
		Actor surgeon,
		Item corpse,
		InteractionDef skill,
		string? limbId,
		int x,
		int y,
		int z)
	{
		if (corpse.Corpse == null)
			return CreateFailure(state, surgeon, skill, corpse, null, "invalid_target");

		return skill.Id switch
		{
			"strip_corpse" => TryStripCorpse(state, surgeon, corpse, skill, x, y, z),
			"butcher_corpse" => TryButcherCorpse(state, surgeon, corpse, skill, x, y, z),
			_ => TryHarvestCorpse(state, surgeon, corpse, skill, limbId, x, y, z),
		};
	}

	public static ActionExecutionResult TryOperateOnActor(
		GameState state,
		Actor surgeon,
		Actor target,
		string? limbId,
		InteractionDef skill)
	{
		if (string.IsNullOrWhiteSpace(limbId))
			return CreateFailure(state, surgeon, skill, target: target, reason: "missing_target");
		if (string.Equals(surgeon.Id, target.Id, StringComparison.Ordinal))
			return CreateFailure(state, surgeon, skill, target: target, reason: "invalid_target");
		if (!IsStrictlyAdjacent(surgeon, target))
			return CreateFailure(state, surgeon, skill, target: target, reason: "out_of_range");
		if (!HasOperationCapacity(surgeon))
			return CreateFailure(state, surgeon, skill, target: target, reason: "invalid_target");

		var installItem = FindBestInstallItem(surgeon, limbId);
		if (installItem != null)
			return TryInstall(state, surgeon, target, limbId, skill, installItem);

		return TryHarvestLiveTarget(state, surgeon, target, limbId, skill);
	}

	private static ActionExecutionResult TryStripCorpse(
		GameState state,
		Actor surgeon,
		Item corpse,
		InteractionDef skill,
		int x,
		int y,
		int z)
	{
		if (!IsWithinTouchRange(surgeon, x, y, z))
			return CreateFailure(state, surgeon, skill, corpse, null, "out_of_range");
		if (corpse.Corpse!.Stripped || corpse.Contents == null || corpse.Contents.Count == 0)
			return CreateFailure(state, surgeon, skill, corpse, null, "invalid_target");

		foreach (var item in corpse.Contents)
		{
			item.Equipped = false;
			MapModule.PlaceItem(state, x, y, z, item);
		}

		corpse.Contents.Clear();
		corpse.Corpse.Stripped = true;
		MapModule.UpdateGroundItem(state, x, y, z, corpse);

		var result = new ActionExecutionResult { Consumed = true };
		var evt = new GameEvent(CorpseStrippedEventType)
		{
			ActionName = skill.Name,
			SkillId = skill.Id,
			TargetX = x,
			TargetY = y,
			TargetZ = z,
		};
		IdentificationModule.PopulateInitiatorIdentity(evt, state, surgeon);
		IdentificationModule.PopulateItemIdentity(evt, state, corpse);
		result.Events.Add(evt);
		return result;
	}

	private static ActionExecutionResult TryButcherCorpse(
		GameState state,
		Actor surgeon,
		Item corpse,
		InteractionDef skill,
		int x,
		int y,
		int z)
	{
		if (!IsWithinTouchRange(surgeon, x, y, z))
			return CreateFailure(state, surgeon, skill, corpse, null, "out_of_range");
		if (corpse.Corpse!.Butchered)
			return CreateFailure(state, surgeon, skill, corpse, null, "invalid_target");

		DropCorpseContentsToGround(state, corpse, x, y, z);

		var profile = CorpseProfileRegistry.Get(corpse.Corpse.CorpseProfileId);
		if (profile == null || profile.ButcherYields.Count == 0)
			return CreateFailure(state, surgeon, skill, corpse, null, "invalid_target");

		foreach (var yield in profile.ButcherYields)
			PlaceYield(state, x, y, z, yield.ItemId, yield.Count);

		corpse.Corpse.Butchered = true;
		MapModule.PickupItem(state, x, y, z, corpse.InstanceId);

		var result = new ActionExecutionResult { Consumed = true };
		var evt = new GameEvent(CorpseButcheredEventType)
		{
			ActionName = skill.Name,
			SkillId = skill.Id,
			TargetX = x,
			TargetY = y,
			TargetZ = z,
		};
		IdentificationModule.PopulateInitiatorIdentity(evt, state, surgeon);
		IdentificationModule.PopulateItemIdentity(evt, state, corpse);
		result.Events.Add(evt);
		return result;
	}

	private static ActionExecutionResult TryHarvestCorpse(
		GameState state,
		Actor surgeon,
		Item corpse,
		InteractionDef skill,
		string? limbId,
		int x,
		int y,
		int z)
	{
		if (!IsWithinTouchRange(surgeon, x, y, z))
			return CreateFailure(state, surgeon, skill, corpse, null, "out_of_range");
		if (string.IsNullOrWhiteSpace(limbId)
			|| corpse.Corpse!.Butchered
			|| !corpse.Corpse.RemainingLimbIds.Remove(limbId))
		{
			return CreateFailure(state, surgeon, skill, corpse, null, "invalid_target");
		}

		var operation = SurgeryOperationRegistry.FindMatching(SurgeryOperationModes.CorpseHarvest, limbId).FirstOrDefault();
		if (operation == null || !TryConsumeOperationNeeds(surgeon, operation))
		{
			if (!string.IsNullOrWhiteSpace(limbId))
				corpse.Corpse.RemainingLimbIds.Add(limbId);
			return CreateFailure(state, surgeon, skill, corpse, null, "invalid_target");
		}

		var harvested = CreateHarvestResultItem(operation.ResultItemId);
		if (harvested == null)
		{
			corpse.Corpse.RemainingLimbIds.Add(limbId);
			return CreateFailure(state, surgeon, skill, corpse, null, "invalid_target");
		}

		InventoryModule.Add(surgeon, harvested);
		MapModule.UpdateGroundItem(state, x, y, z, corpse);

		var result = new ActionExecutionResult { Consumed = true };
		var evt = new GameEvent(CorpseHarvestedEventType)
		{
			ActionName = skill.Name,
			SkillId = skill.Id,
			ItemName = harvested.Name,
			ItemTypeId = harvested.Id,
			ItemCategory = harvested.Category,
			LimbName = ResolveLimbName(limbId),
			TargetActorName = corpse.Name,
			TargetX = x,
			TargetY = y,
			TargetZ = z,
		};
		IdentificationModule.PopulateInitiatorIdentity(evt, state, surgeon);
		result.Events.Add(evt);
		return result;
	}

	private static ActionExecutionResult TryInstall(
		GameState state,
		Actor surgeon,
		Actor target,
		string limbId,
		InteractionDef skill,
		Item installItem)
	{
		if (AI.FactionRelation.IsHostile(surgeon.Faction, target.Faction))
			return CreateFailure(state, surgeon, skill, target: target, reason: "invalid_target");

		var metadata = installItem.Surgery;
		if (metadata == null || string.IsNullOrWhiteSpace(metadata.ReplacementLimbPresetId))
			return CreateFailure(state, surgeon, skill, target: target, reason: "invalid_target");

		var operation = ResolveInstallOperation(metadata.OperationId, limbId);
		if (operation == null || !TryConsumeOperationNeeds(surgeon, operation))
			return CreateFailure(state, surgeon, skill, target: target, reason: "invalid_target");

		if (TryFindInstallItemIndex(surgeon, installItem.InstanceId) is not { } installIndex)
			return CreateFailure(state, surgeon, skill, target: target, reason: "invalid_target");

		var blueprint = ResolveLimbBlueprint(target, limbId);
		if (blueprint == null)
			return CreateFailure(state, surgeon, skill, target: target, reason: "invalid_target");

		if (target.Limbs.FirstOrDefault(existing => string.Equals(existing.Id, limbId, StringComparison.Ordinal)) is { } existingLimb)
			RemoveLiveLimb(state, target, existingLimb, events: null);

		var replacement = PresetDB.CloneLimb(metadata.ReplacementLimbPresetId);
		replacement.Id = limbId;
		replacement.Name = blueprint.Name;
		if (!string.IsNullOrWhiteSpace(blueprint.BodyPart))
			replacement.BodyPart = blueprint.BodyPart;
		target.AttachLimb(replacement);

		InventoryModule.ConsumeAt(surgeon, installIndex, 1);
		ClearMissingLimbMarkers(target, limbId);
		HealthSystem.Sync(target, state.Turn, DefaultEnvironmentExposureProvider.Instance.Capture(state, target));

		var result = new ActionExecutionResult { Consumed = true };
		var evt = new GameEvent(SurgeryInstalledEventType)
		{
			ActionName = skill.Name,
			SkillId = skill.Id,
			LimbName = replacement.Name,
			TargetX = target.X,
			TargetY = target.Y,
			TargetZ = target.Z,
		};
		IdentificationModule.PopulateInitiatorIdentity(evt, state, surgeon);
		IdentificationModule.PopulateTargetIdentity(evt, state, target);
		IdentificationModule.PopulateItemIdentity(evt, state, installItem);
		result.Events.Add(evt);
		return result;
	}

	private static ActionExecutionResult TryHarvestLiveTarget(
		GameState state,
		Actor surgeon,
		Actor target,
		string limbId,
		InteractionDef skill)
	{
		if (!IsTargetIncapacitated(target))
			return CreateFailure(state, surgeon, skill, target: target, reason: "invalid_target");

		var targetLimb = target.Limbs.FirstOrDefault(existing => string.Equals(existing.Id, limbId, StringComparison.Ordinal));
		if (targetLimb == null)
			return CreateFailure(state, surgeon, skill, target: target, reason: "invalid_target");

		var operation = SurgeryOperationRegistry.FindMatching(SurgeryOperationModes.LiveHarvest, limbId).FirstOrDefault();
		if (operation == null || !TryConsumeOperationNeeds(surgeon, operation))
			return CreateFailure(state, surgeon, skill, target: target, reason: "invalid_target");

		var harvested = CreateHarvestResultItem(operation.ResultItemId);
		if (harvested == null)
			return CreateFailure(state, surgeon, skill, target: target, reason: "invalid_target");

		var result = new ActionExecutionResult { Consumed = true };
		RemoveLiveLimb(state, target, targetLimb, result.Events);
		InventoryModule.Add(surgeon, harvested);

		var evt = new GameEvent(LiveHarvestedEventType)
		{
			ActionName = skill.Name,
			SkillId = skill.Id,
			LimbName = targetLimb.Name,
			ItemName = harvested.Name,
			ItemTypeId = harvested.Id,
			ItemCategory = harvested.Category,
			TargetX = target.X,
			TargetY = target.Y,
			TargetZ = target.Z,
		};
		IdentificationModule.PopulateInitiatorIdentity(evt, state, surgeon);
		IdentificationModule.PopulateTargetIdentity(evt, state, target);
		result.Events.Add(evt);
		return result;
	}

	private static Item? CreateCorpseFromActor(Actor actor)
	{
		var profile = CorpseProfileRegistry.FindForActor(actor);
		if (profile == null || !PresetDB.Items.ContainsKey(profile.CorpseItemId))
			return null;

		var corpse = PresetDB.CloneItem(profile.CorpseItemId);
		corpse.Name = string.IsNullOrWhiteSpace(actor.DisplayName)
			? corpse.Name
			: $"{actor.DisplayName} {corpse.Name}";
		corpse.Contents = [];
		foreach (var item in actor.Inventory)
		{
			item.Equipped = false;
			corpse.Contents.Add(item);
		}

		corpse.Corpse = new ItemCorpseMetadata
		{
			CorpseProfileId = profile.Id,
			SourceActorTemplateId = actor.TemplateId,
			SourceRaceId = actor.Race?.Id ?? string.Empty,
			SourceActorName = actor.DisplayName,
			RemainingLimbIds = actor.Limbs
				.Select(static limb => limb.Id)
				.Where(static limbId => SurgeryOperationRegistry.FindMatching(SurgeryOperationModes.CorpseHarvest, limbId).Count > 0)
				.Distinct(StringComparer.Ordinal)
				.OrderBy(static limbId => limbId, StringComparer.Ordinal)
				.ToList(),
		};
		return corpse;
	}

	private static void RemoveLiveLimb(GameState state, Actor target, Limb targetLimb, List<GameEvent>? events)
	{
		HealthSystem.AddOrUpdateInjury(
			target,
			HealthConditionIds.MissingLimb,
			targetLimb.Id,
			targetLimb.MaxDurability,
			"health:surgery",
			state.Turn,
			events,
			state);

		var droppedItems = InventoryModule.OnLimbDestroyed(target, targetLimb);
		foreach (var dropped in droppedItems)
		{
			MapModule.PlaceItem(state, target.X, target.Y, target.Z, dropped);
			if (events == null)
				continue;

			var droppedEvent = new GameEvent("item_dropped")
			{
				TargetX = target.X,
				TargetY = target.Y,
				TargetZ = target.Z,
			};
			IdentificationModule.PopulateTargetIdentity(droppedEvent, state, target);
			IdentificationModule.PopulateItemIdentity(droppedEvent, state, dropped);
			events.Add(droppedEvent);
		}

		target.DetachLimb(targetLimb);

		var severed = CreateSeveredLimbItem(targetLimb.Id);
		if (severed != null)
			MapModule.PlaceItem(state, target.X, target.Y, target.Z, severed);

		HealthSystem.Sync(target, state.Turn, DefaultEnvironmentExposureProvider.Instance.Capture(state, target), events, state);

		if (HealthSystem.GetFatalCause(target) is { Length: > 0 } fatalCause)
		{
			var deathEvent = new GameEvent(fatalCause)
			{
				TargetX = target.X,
				TargetY = target.Y,
				TargetZ = target.Z,
			};
			IdentificationModule.PopulateTargetIdentity(deathEvent, state, target);
			events?.Add(deathEvent);
			TrySpawnCorpseOnDeath(state, target, events ?? [], fatalCause);
			ActorModule.Remove(state, target.Id);
			return;
		}

		if (CombatModule.IsDead(target) || CombatModule.CheckVitalStatus(target) == "death_instant")
		{
			var killedEvent = new GameEvent("actor_killed")
			{
				TargetX = target.X,
				TargetY = target.Y,
				TargetZ = target.Z,
			};
			IdentificationModule.PopulateTargetIdentity(killedEvent, state, target);
			events?.Add(killedEvent);
			TrySpawnCorpseOnDeath(state, target, events ?? [], "actor_killed");
			ActorModule.Remove(state, target.Id);
			return;
		}

		if (CombatModule.CheckVitalStatus(target) == "incapacitate" && events != null)
		{
			var incapacitated = new GameEvent("actor_incapacitated")
			{
				TargetX = target.X,
				TargetY = target.Y,
				TargetZ = target.Z,
			};
			IdentificationModule.PopulateTargetIdentity(incapacitated, state, target);
			events.Add(incapacitated);
		}
	}

	private static bool TryConsumeOperationNeeds(Actor surgeon, SurgeryOperationDef operation)
	{
		if (!HasOperationCapacity(surgeon))
			return false;

		var toolIndex = FindToolIndex(surgeon, operation.RequiredToolTag, operation.RequiredToolLevel);
		if (toolIndex == null)
			return false;

		var supplyIndex = FindSupplyIndex(
			surgeon,
			operation.RequiredSupplyTag,
			operation.RequiredSupplyLevel,
			Math.Max(1, operation.SupplyUnits),
			toolIndex.Value);
		if (supplyIndex == null)
			return false;

		return InventoryModule.ConsumeAt(surgeon, supplyIndex.Value, Math.Max(1, operation.SupplyUnits));
	}

	private static bool HasOperationCapacity(Actor surgeon) =>
		surgeon.GetCapacity(Caps.Consciousness) >= 0.2f
		&& surgeon.GetCapacity(Caps.Manipulation) >= 0.2f;

	private static int? FindToolIndex(Actor actor, string tag, int minLevel)
	{
		int? fallback = null;
		for (var i = 0; i < actor.Inventory.Count; i++)
		{
			var item = actor.Inventory[i];
			if (item.Tags.GetValueOrDefault(tag, 0) < minLevel)
				continue;

			if (IsDedicatedMedicalTool(item))
				return i;

			fallback ??= i;
		}

		return fallback;
	}

	private static int? FindSupplyIndex(Actor actor, string tag, int minLevel, int units, int excludedIndex)
	{
		int? fallback = null;
		for (var i = 0; i < actor.Inventory.Count; i++)
		{
			if (i == excludedIndex)
				continue;

			var item = actor.Inventory[i];
			if (item.Tags.GetValueOrDefault(tag, 0) < minLevel || item.SafeStackCount < units)
				continue;

			if (IsPreferredMedicalSupply(item))
				return i;

			fallback ??= i;
		}

		return fallback;
	}

	private static void PlaceYield(GameState state, int x, int y, int z, string itemId, int count)
	{
		if (!PresetDB.Items.ContainsKey(itemId) || count <= 0)
			return;

		var template = PresetDB.CloneItem(itemId);
		if (template.IsStackable)
		{
			template.StackCount = Math.Min(template.MaxStack, Math.Max(1, count));
			MapModule.PlaceItem(state, x, y, z, template);

			var remaining = count - template.StackCount;
			while (remaining > 0)
			{
				var extra = PresetDB.CloneItem(itemId);
				extra.StackCount = Math.Min(extra.MaxStack, remaining);
				MapModule.PlaceItem(state, x, y, z, extra);
				remaining -= extra.StackCount;
			}

			return;
		}

		for (var i = 0; i < count; i++)
			MapModule.PlaceItem(state, x, y, z, PresetDB.CloneItem(itemId));
	}

	private static void DropCorpseContentsToGround(GameState state, Item corpse, int x, int y, int z)
	{
		if (corpse.Contents == null || corpse.Contents.Count == 0)
			return;

		foreach (var item in corpse.Contents)
		{
			item.Equipped = false;
			MapModule.PlaceItem(state, x, y, z, item);
		}

		corpse.Contents.Clear();
		corpse.Corpse!.Stripped = true;
	}

	private static Limb? ResolveLimbBlueprint(Actor target, string limbId)
	{
		var current = target.Limbs.FirstOrDefault(existing => string.Equals(existing.Id, limbId, StringComparison.Ordinal));
		if (current != null)
			return new Limb
			{
				Id = current.Id,
				Name = current.Name,
				BodyPart = current.BodyPart,
			};

		if (PresetDB.Limbs.TryGetValue(limbId, out var preset))
		{
			return new Limb
			{
				Id = preset.Id,
				Name = preset.Name,
				BodyPart = preset.BodyPart,
			};
		}

		return null;
	}

	private static void ClearMissingLimbMarkers(Actor target, string limbId)
	{
		target.HealthConditions.RemoveAll(condition =>
			string.Equals(condition.Id, HealthConditionIds.MissingLimb, StringComparison.Ordinal)
			&& string.Equals(condition.LimbId, limbId, StringComparison.Ordinal));
	}

	private static SurgeryOperationDef? ResolveInstallOperation(string? operationId, string limbId)
	{
		if (!string.IsNullOrWhiteSpace(operationId))
		{
			var configured = SurgeryOperationRegistry.Get(operationId);
			if (configured != null
				&& string.Equals(configured.Mode, SurgeryOperationModes.LiveInstall, StringComparison.Ordinal))
			{
				return configured;
			}
		}

		return SurgeryOperationRegistry.FindMatching(SurgeryOperationModes.LiveInstall, limbId).FirstOrDefault();
	}

	private static Item? CreateHarvestResultItem(string itemId) =>
		PresetDB.Items.ContainsKey(itemId)
			? PresetDB.CloneItem(itemId)
			: null;

	private static (int Index, Item Item)? FindBestInstallItemIndex(Actor surgeon, string limbId)
	{
		(int Index, Item Item)? best = null;
		for (var i = 0; i < surgeon.Inventory.Count; i++)
		{
			var item = surgeon.Inventory[i];
			if (!MatchesInstallItem(item, limbId))
				continue;

			if (best == null
				|| CompareInstallItems(item, best.Value.Item) > 0)
			{
				best = (i, item);
			}
		}

		return best;
	}

	private static int? TryFindInstallItemIndex(Actor surgeon, string instanceId)
	{
		for (var i = 0; i < surgeon.Inventory.Count; i++)
		{
			if (string.Equals(surgeon.Inventory[i].InstanceId, instanceId, StringComparison.Ordinal))
				return i;
		}

		return null;
	}

	private static bool MatchesInstallItem(Item item, string limbId)
	{
		var metadata = item.Surgery;
		if (metadata == null || string.IsNullOrWhiteSpace(metadata.ReplacementLimbPresetId))
			return false;

		if (!string.IsNullOrWhiteSpace(metadata.OperationId))
		{
			var operation = SurgeryOperationRegistry.Get(metadata.OperationId);
			if (operation == null || !string.Equals(operation.Mode, SurgeryOperationModes.LiveInstall, StringComparison.Ordinal))
				return false;

			return operation.TargetLimbSuffixes.Any(suffix => limbId.EndsWith(suffix, StringComparison.Ordinal));
		}

		var replacement = PresetDB.Limbs.GetValueOrDefault(metadata.ReplacementLimbPresetId);
		var targetPreset = PresetDB.Limbs.GetValueOrDefault(limbId);
		if (replacement == null || targetPreset == null)
			return false;

		return string.Equals(replacement.BodyPart, targetPreset.BodyPart, StringComparison.Ordinal);
	}

	private static int CompareInstallItems(Item left, Item right)
	{
		var techCompare = GetTechTierRank(left.TechTier).CompareTo(GetTechTierRank(right.TechTier));
		if (techCompare != 0)
			return techCompare;

		var priceCompare = left.Price.CompareTo(right.Price);
		if (priceCompare != 0)
			return priceCompare;

		return string.Compare(left.Id, right.Id, StringComparison.Ordinal);
	}

	private static int GetTechTierRank(string techTier) => techTier switch
	{
		"archotech" => 4,
		"spacer" => 3,
		"industrial" => 2,
		"medieval" => 1,
		_ => 0,
	};

	private static bool IsTargetIncapacitated(Actor target) =>
		string.Equals(CombatModule.CheckVitalStatus(target), "incapacitate", StringComparison.Ordinal)
		|| target.GetCapacity(Caps.Consciousness) < 0.15f
		|| target.GetCapacity(Caps.Moving) < 0.05f;

	private static bool IsDedicatedMedicalTool(Item item) =>
		string.Equals(item.SubCategory, "medical_tool", StringComparison.Ordinal)
		|| string.Equals(item.Category, ItemCategories.Tool, StringComparison.Ordinal)
		|| item.GrantedSkills.Any(id => string.Equals(id, "operate", StringComparison.Ordinal));

	private static bool IsPreferredMedicalSupply(Item item) =>
		string.Equals(item.Category, ItemCategories.Consumable, StringComparison.Ordinal)
		|| item.IsStackable;

	private static bool IsWithinTouchRange(Actor actor, int x, int y, int z) =>
		actor.Z == z && Math.Abs(actor.X - x) + Math.Abs(actor.Y - y) <= 1;

	private static bool IsStrictlyAdjacent(Actor left, Actor right) =>
		left.Z == right.Z && Math.Abs(left.X - right.X) + Math.Abs(left.Y - right.Y) == 1;

	private static string ResolveLimbName(string limbId) =>
		PresetDB.Limbs.TryGetValue(limbId, out var preset)
			? preset.Name
			: limbId;

	private static ActionExecutionResult CreateFailure(
		GameState state,
		Actor surgeon,
		InteractionDef skill,
		Item? corpse = null,
		Actor? target = null,
		string reason = "invalid_target")
	{
		var result = new ActionExecutionResult();
		var evt = new GameEvent(OperateFailedEventType)
		{
			ActionName = skill.Name,
			SkillId = skill.Id,
			FailureReason = reason,
			TargetX = target?.X ?? surgeon.X,
			TargetY = target?.Y ?? surgeon.Y,
			TargetZ = target?.Z ?? surgeon.Z,
		};
		IdentificationModule.PopulateInitiatorIdentity(evt, state, surgeon);
		if (target != null)
			IdentificationModule.PopulateTargetIdentity(evt, state, target);
		if (corpse != null)
			IdentificationModule.PopulateItemIdentity(evt, state, corpse);
		result.Events.Add(evt);
		return result;
	}
}
