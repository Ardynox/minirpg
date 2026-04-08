using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;

namespace MiniRPG.Core.Map;

public enum SaveLoadStatus
{
	Success,
	NotFound,
	Incompatible,
}

public sealed class SaveFile
{
	[JsonPropertyName("version")]
	public required int Version { get; set; }

	[JsonPropertyName("header")]
	public required SaveHeader Header { get; set; }

	[JsonPropertyName("payload")]
	public required SavePayload Payload { get; set; }
}

public sealed class SaveHeader
{
	[JsonPropertyName("title")]
	public required string Title { get; set; }

	[JsonPropertyName("savedAtUtc")]
	public required DateTimeOffset SavedAtUtc { get; set; }

	[JsonPropertyName("turn")]
	public required int Turn { get; set; }

	[JsonPropertyName("playerZ")]
	public required int PlayerZ { get; set; }

	[JsonPropertyName("generatorId")]
	public required string GeneratorId { get; set; }

	[JsonPropertyName("viewModeId")]
	public required string ViewModeId { get; set; }

	[JsonPropertyName("worldId")]
	public string? WorldId { get; set; }

	[JsonPropertyName("worldName")]
	public string? WorldName { get; set; }

	[JsonPropertyName("characterId")]
	public string? CharacterId { get; set; }

	[JsonPropertyName("characterName")]
	public string? CharacterName { get; set; }
}

public sealed class SaveHeaderContext
{
	public string? WorldId { get; init; }
	public string? WorldName { get; init; }
	public string? CharacterId { get; init; }
	public string? CharacterName { get; init; }
}

public sealed class SavePayload
{
	[JsonPropertyName("worldSeed")]
	public required int WorldSeed { get; set; }

	[JsonPropertyName("turn")]
	public required int Turn { get; set; }

	[JsonPropertyName("playerX")]
	public required int PlayerX { get; set; }

	[JsonPropertyName("playerY")]
	public required int PlayerY { get; set; }

	[JsonPropertyName("playerZ")]
	public required int PlayerZ { get; set; }

	[JsonPropertyName("playerId")]
	public required string PlayerId { get; set; }

	[JsonPropertyName("playerAppearanceId")]
	public string? PlayerAppearanceId { get; set; }

	[JsonPropertyName("bumpAttack")]
	public bool BumpAttack { get; set; }

	[JsonPropertyName("watchMode")]
	public bool WatchMode { get; set; }

	[JsonPropertyName("killCount")]
	public required int KillCount { get; set; }

	[JsonPropertyName("generatorId")]
	public required string GeneratorId { get; set; }

	[JsonPropertyName("viewModeId")]
	public required string ViewModeId { get; set; }

	[JsonPropertyName("actors")]
	public required List<ActorSnapshot> Actors { get; set; }

	[JsonPropertyName("quests")]
	public required List<QuestSnapshot> Quests { get; set; }

	[JsonPropertyName("dirtyChunks")]
	public required List<ChunkSnapshot> DirtyChunks { get; set; }

	[JsonPropertyName("timeline")]
	public required TimelineSnapshot Timeline { get; set; }

	[JsonPropertyName("weather")]
	public WeatherStateSnapshot? Weather { get; set; }

	[JsonPropertyName("identifiedActorTypes")]
	public List<string>? IdentifiedActorTypes { get; set; }

	[JsonPropertyName("identifiedItemTypes")]
	public List<string>? IdentifiedItemTypes { get; set; }

	[JsonPropertyName("facilities")]
	public List<FacilityInstance>? Facilities { get; set; }

	[JsonPropertyName("stockpileZones")]
	public List<StockpileZone>? StockpileZones { get; set; }

	[JsonPropertyName("economicDomains")]
	public List<EconomicDomain>? EconomicDomains { get; set; }
}

public sealed class WeatherStateSnapshot
{
	[JsonPropertyName("frontPhase")]
	public float FrontPhase { get; set; }

	[JsonPropertyName("debugTypeId")]
	public string? DebugTypeId { get; set; }

	[JsonPropertyName("debugIntensityId")]
	public string? DebugIntensityId { get; set; }

	[JsonPropertyName("lastLocalTypeId")]
	public string? LastLocalTypeId { get; set; }

	[JsonPropertyName("lastLocalIntensityId")]
	public string? LastLocalIntensityId { get; set; }

	[JsonPropertyName("lastLocalTurn")]
	public int? LastLocalTurn { get; set; }
}

public sealed class TimelineSnapshot
{
	[JsonPropertyName("currentActorId")]
	public string? CurrentActorId { get; set; }

	[JsonPropertyName("lastActorId")]
	public string? LastActorId { get; set; }

	[JsonPropertyName("actors")]
	public required List<TimelineActorSnapshot> Actors { get; set; }
}

public sealed class TimelineActorSnapshot
{
	[JsonPropertyName("actorId")]
	public required string ActorId { get; set; }

	[JsonPropertyName("charge")]
	public required float Charge { get; set; }
}

public sealed class ActorSnapshot
{
	[JsonPropertyName("id")]
	public required string Id { get; set; }

	[JsonPropertyName("x")]
	public required int X { get; set; }

	[JsonPropertyName("y")]
	public required int Y { get; set; }

	[JsonPropertyName("z")]
	public required int Z { get; set; }

	[JsonPropertyName("glyph")]
	public required string Glyph { get; set; }

	[JsonPropertyName("displayName")]
	public required string DisplayName { get; set; }

	[JsonPropertyName("templateId")]
	public string? TemplateId { get; set; }

	[JsonPropertyName("facingX")]
	public required int FacingX { get; set; }

	[JsonPropertyName("facingY")]
	public required int FacingY { get; set; }

	[JsonPropertyName("faction")]
	public required string Faction { get; set; }

	[JsonPropertyName("brainId")]
	public string? BrainId { get; set; }

	[JsonPropertyName("primaryDomainId")]
	public string? PrimaryDomainId { get; set; }

	[JsonPropertyName("accessibleDomainIds")]
	public List<string>? AccessibleDomainIds { get; set; }

	[JsonPropertyName("workBrainId")]
	public string? WorkBrainId { get; set; }

	[JsonPropertyName("awarenessState")]
	public AwarenessState AwarenessState { get; set; }

	[JsonPropertyName("hasHomePosition")]
	public bool HasHomePosition { get; set; }

	[JsonPropertyName("homeX")]
	public int HomeX { get; set; }

	[JsonPropertyName("homeY")]
	public int HomeY { get; set; }

	[JsonPropertyName("homeZ")]
	public int HomeZ { get; set; }

	[JsonPropertyName("alertTargetActorId")]
	public string? AlertTargetActorId { get; set; }

	[JsonPropertyName("lastKnownTargetX")]
	public int LastKnownTargetX { get; set; }

	[JsonPropertyName("lastKnownTargetY")]
	public int LastKnownTargetY { get; set; }

	[JsonPropertyName("lastKnownTargetZ")]
	public int LastKnownTargetZ { get; set; }

	[JsonPropertyName("stateTurns")]
	public int StateTurns { get; set; }

	[JsonPropertyName("searchTurnsRemaining")]
	public int SearchTurnsRemaining { get; set; }

	[JsonPropertyName("gold")]
	public required int Gold { get; set; }

	[JsonPropertyName("inventory")]
	public required List<ItemSnapshot> Inventory { get; set; }

	[JsonPropertyName("shopSlots")]
	public required List<ShopSlotSnapshot> ShopSlots { get; set; }

	[JsonPropertyName("limbs")]
	public required List<LimbSnapshot> Limbs { get; set; }

	[JsonPropertyName("race")]
	public RaceSnapshot? Race { get; set; }

	[JsonPropertyName("profession")]
	public ProfessionSnapshot? Profession { get; set; }

	[JsonPropertyName("buffs")]
	public required List<BuffSnapshot> Buffs { get; set; }

	[JsonPropertyName("experiences")]
	public required List<ExperienceSnapshot> Experiences { get; set; }

	[JsonPropertyName("skillCooldowns")]
	public Dictionary<string, int> SkillCooldowns { get; set; } = new(StringComparer.Ordinal);

	[JsonPropertyName("dialogMood")]
	public required float DialogMood { get; set; }

	[JsonPropertyName("dialogAffinity")]
	public required float DialogAffinity { get; set; }

	[JsonPropertyName("dialogMemory")]
	public required List<string> DialogMemory { get; set; }

	[JsonPropertyName("dialogTalkCount")]
	public required int DialogTalkCount { get; set; }

	[JsonPropertyName("dialogPersonality")]
	public required Dictionary<string, float> DialogPersonality { get; set; }

	[JsonPropertyName("dialogNeeds")]
	public required Dictionary<string, float> DialogNeeds { get; set; }

	[JsonPropertyName("needs")]
	public Dictionary<string, NeedStateSnapshot>? Needs { get; set; }

	[JsonPropertyName("thoughts")]
	public List<ThoughtStateSnapshot>? Thoughts { get; set; }

	[JsonPropertyName("moodValue")]
	public float? MoodValue { get; set; }

	[JsonPropertyName("needsLastUpdatedTurn")]
	public int? NeedsLastUpdatedTurn { get; set; }

	[JsonPropertyName("healthConditions")]
	public List<HealthConditionStateSnapshot>? HealthConditions { get; set; }

	[JsonPropertyName("painValue")]
	public float? PainValue { get; set; }

	[JsonPropertyName("bloodLossValue")]
	public float? BloodLossValue { get; set; }

	[JsonPropertyName("wetnessValue")]
	public float? WetnessValue { get; set; }

	[JsonPropertyName("healthLastUpdatedTurn")]
	public int? HealthLastUpdatedTurn { get; set; }
}

public sealed class NeedStateSnapshot
{
	[JsonPropertyName("id")]
	public required string Id { get; set; }

	[JsonPropertyName("current")]
	public required float Current { get; set; }

	[JsonPropertyName("min")]
	public required float Min { get; set; }

	[JsonPropertyName("max")]
	public required float Max { get; set; }

	[JsonPropertyName("lastUpdatedTurn")]
	public required int LastUpdatedTurn { get; set; }
}

public sealed class ThoughtStateSnapshot
{
	[JsonPropertyName("id")]
	public required string Id { get; set; }

	[JsonPropertyName("moodOffset")]
	public required float MoodOffset { get; set; }

	[JsonPropertyName("expiresOnTurn")]
	public required int ExpiresOnTurn { get; set; }

	[JsonPropertyName("source")]
	public required string Source { get; set; }
}

public sealed class HealthConditionStateSnapshot
{
	[JsonPropertyName("id")]
	public required string Id { get; set; }

	[JsonPropertyName("limbId")]
	public string? LimbId { get; set; }

	[JsonPropertyName("severity")]
	public float Severity { get; set; }

	[JsonPropertyName("permanent")]
	public bool Permanent { get; set; }

	[JsonPropertyName("source")]
	public string Source { get; set; } = "";

	[JsonPropertyName("createdOnTurn")]
	public int CreatedOnTurn { get; set; }

	[JsonPropertyName("lastUpdatedTurn")]
	public int LastUpdatedTurn { get; set; }

	[JsonPropertyName("tendedQuality")]
	public float TendedQuality { get; set; }

	[JsonPropertyName("tendedOnTurn")]
	public int TendedOnTurn { get; set; } = -1;

	[JsonPropertyName("infectionProgress")]
	public float InfectionProgress { get; set; }
}

public sealed class ItemSnapshot
{
	[JsonPropertyName("id")]
	public required string Id { get; set; }

	[JsonPropertyName("instanceId")]
	public string? InstanceId { get; set; }

	[JsonPropertyName("name")]
	public required string Name { get; set; }

	[JsonPropertyName("materialId")]
	public string? MaterialId { get; set; }

	[JsonPropertyName("price")]
	public required int Price { get; set; }

	[JsonPropertyName("equipped")]
	public required bool Equipped { get; set; }

	[JsonPropertyName("category")]
	public required string Category { get; set; }

	[JsonPropertyName("ownerDomainId")]
	public string? OwnerDomainId { get; set; }

	[JsonPropertyName("subCategory")]
	public string? SubCategory { get; set; }

	[JsonPropertyName("techTier")]
	public string? TechTier { get; set; }

	[JsonPropertyName("weight")]
	public required float Weight { get; set; }

	[JsonPropertyName("maxStack")]
	public int? MaxStack { get; set; }

	[JsonPropertyName("stackCount")]
	public int? StackCount { get; set; }

	[JsonPropertyName("ammoType")]
	public string? AmmoType { get; set; }

	[JsonPropertyName("magazineSize")]
	public int? MagazineSize { get; set; }

	[JsonPropertyName("loadedAmmo")]
	public int? LoadedAmmo { get; set; }

	[JsonPropertyName("maxDurability")]
	public int? MaxDurability { get; set; }

	[JsonPropertyName("durability")]
	public int? Durability { get; set; }

	[JsonPropertyName("bodyPart")]
	public required string BodyPart { get; set; }

	[JsonPropertyName("layer")]
	public required EquipLayer Layer { get; set; }

	[JsonPropertyName("coveredParts")]
	public required List<string> CoveredParts { get; set; }

	[JsonPropertyName("coldInsulation")]
	public float? ColdInsulation { get; set; }

	[JsonPropertyName("heatInsulation")]
	public float? HeatInsulation { get; set; }

	[JsonPropertyName("waterproofing")]
	public float? Waterproofing { get; set; }

	[JsonPropertyName("portableWarmthC")]
	public float? PortableWarmthC { get; set; }

	[JsonPropertyName("portableDryingBonus")]
	public float? PortableDryingBonus { get; set; }

	[JsonPropertyName("sharpArmor")]
	public required float SharpArmor { get; set; }

	[JsonPropertyName("bluntArmor")]
	public required float BluntArmor { get; set; }

	[JsonPropertyName("sharpDamage")]
	public required float SharpDamage { get; set; }

	[JsonPropertyName("bluntDamage")]
	public required float BluntDamage { get; set; }

	[JsonPropertyName("grantedSkills")]
	public required List<string> GrantedSkills { get; set; }

	[JsonPropertyName("contents")]
	public List<ItemSnapshot>? Contents { get; set; }

	[JsonPropertyName("surgery")]
	public ItemSurgerySnapshot? Surgery { get; set; }

	[JsonPropertyName("corpse")]
	public ItemCorpseSnapshot? Corpse { get; set; }

	[JsonPropertyName("tags")]
	public required Dictionary<string, int> Tags { get; set; }
}

public sealed class ItemSurgerySnapshot
{
	[JsonPropertyName("operationId")]
	public string? OperationId { get; set; }

	[JsonPropertyName("replacementLimbPresetId")]
	public string? ReplacementLimbPresetId { get; set; }

	[JsonPropertyName("harvestedLimbPresetId")]
	public string? HarvestedLimbPresetId { get; set; }

	[JsonPropertyName("role")]
	public string? Role { get; set; }
}

public sealed class ItemCorpseSnapshot
{
	[JsonPropertyName("corpseProfileId")]
	public string? CorpseProfileId { get; set; }

	[JsonPropertyName("sourceActorTemplateId")]
	public string? SourceActorTemplateId { get; set; }

	[JsonPropertyName("sourceRaceId")]
	public string? SourceRaceId { get; set; }

	[JsonPropertyName("sourceActorName")]
	public string? SourceActorName { get; set; }

	[JsonPropertyName("stripped")]
	public bool Stripped { get; set; }

	[JsonPropertyName("butchered")]
	public bool Butchered { get; set; }

	[JsonPropertyName("remainingLimbIds")]
	public List<string>? RemainingLimbIds { get; set; }
}

public sealed class ShopSlotSnapshot
{
	[JsonPropertyName("item")]
	public required ItemSnapshot Item { get; set; }

	[JsonPropertyName("stock")]
	public required int Stock { get; set; }
}

public sealed class LimbSnapshot
{
	[JsonPropertyName("id")]
	public required string Id { get; set; }

	[JsonPropertyName("name")]
	public required string Name { get; set; }

	[JsonPropertyName("maxDurability")]
	public required int MaxDurability { get; set; }

	[JsonPropertyName("durability")]
	public required int Durability { get; set; }

	[JsonPropertyName("permanentDamage")]
	public int? PermanentDamage { get; set; }

	[JsonPropertyName("material")]
	public required string Material { get; set; }

	[JsonPropertyName("bodyPart")]
	public required string BodyPart { get; set; }

	[JsonPropertyName("equipLayers")]
	public required List<EquipLayer> EquipLayers { get; set; }

	[JsonPropertyName("equipSlots")]
	public required List<EquipSlotSnapshot> EquipSlots { get; set; }

	[JsonPropertyName("capacities")]
	public required Dictionary<string, float> Capacities { get; set; }

	[JsonPropertyName("tags")]
	public required Dictionary<string, int> Tags { get; set; }
}

public sealed class EquipSlotSnapshot
{
	[JsonPropertyName("limbId")]
	public required string LimbId { get; set; }

	[JsonPropertyName("bodyPart")]
	public required string BodyPart { get; set; }

	[JsonPropertyName("layer")]
	public required EquipLayer Layer { get; set; }

	[JsonPropertyName("itemId")]
	public string? ItemId { get; set; }
}

public sealed class RaceSnapshot
{
	[JsonPropertyName("id")]
	public required string Id { get; set; }

	[JsonPropertyName("name")]
	public required string Name { get; set; }

	[JsonPropertyName("needProfileId")]
	public string? NeedProfileId { get; set; }

	[JsonPropertyName("healthProfileId")]
	public string? HealthProfileId { get; set; }

	[JsonPropertyName("tags")]
	public required Dictionary<string, int> Tags { get; set; }
}

public sealed class ProfessionSnapshot
{
	[JsonPropertyName("id")]
	public required string Id { get; set; }

	[JsonPropertyName("name")]
	public required string Name { get; set; }

	[JsonPropertyName("tags")]
	public required Dictionary<string, int> Tags { get; set; }
}

public sealed class BuffSnapshot
{
	[JsonPropertyName("id")]
	public required string Id { get; set; }

	[JsonPropertyName("name")]
	public required string Name { get; set; }

	[JsonPropertyName("remainingTurns")]
	public required int RemainingTurns { get; set; }

	[JsonPropertyName("tags")]
	public required Dictionary<string, int> Tags { get; set; }
}

public sealed class ExperienceSnapshot
{
	[JsonPropertyName("id")]
	public required string Id { get; set; }

	[JsonPropertyName("name")]
	public required string Name { get; set; }

	[JsonPropertyName("tags")]
	public required Dictionary<string, int> Tags { get; set; }
}

public sealed class QuestSnapshot
{
	[JsonPropertyName("id")]
	public required string Id { get; set; }

	[JsonPropertyName("title")]
	public required string Title { get; set; }

	[JsonPropertyName("description")]
	public required string Description { get; set; }

	[JsonPropertyName("source")]
	public required string Source { get; set; }

	[JsonPropertyName("status")]
	public required QuestStatus Status { get; set; }

	[JsonPropertyName("acceptedTurn")]
	public required int AcceptedTurn { get; set; }

	[JsonPropertyName("finishedTurn")]
	public required int FinishedTurn { get; set; }

	[JsonPropertyName("objectives")]
	public required List<QuestObjectiveSnapshot> Objectives { get; set; }

	[JsonPropertyName("tags")]
	public required Dictionary<string, string> Tags { get; set; }
}

public sealed class QuestObjectiveSnapshot
{
	[JsonPropertyName("text")]
	public required string Text { get; set; }

	[JsonPropertyName("current")]
	public required int Current { get; set; }

	[JsonPropertyName("target")]
	public required int Target { get; set; }
}

public sealed class ChunkSnapshot
{
	[JsonPropertyName("cx")]
	public required int Cx { get; set; }

	[JsonPropertyName("cy")]
	public required int Cy { get; set; }

	[JsonPropertyName("cz")]
	public required int Cz { get; set; }

	[JsonPropertyName("terrainIds")]
	public required ushort[] TerrainIds { get; set; }

	[JsonPropertyName("hardness")]
	public required byte[] Hardness { get; set; }

	[JsonPropertyName("stacks")]
	public required List<CellStackSnapshot> Stacks { get; set; }

	[JsonPropertyName("nests")]
	public required List<NestSnapshot> Nests { get; set; }

	[JsonPropertyName("snowDepth")]
	public byte[]? SnowDepth { get; set; }

	[JsonPropertyName("sandDepth")]
	public byte[]? SandDepth { get; set; }

	[JsonPropertyName("wetness")]
	public byte[]? Wetness { get; set; }

	[JsonPropertyName("iceDepth")]
	public byte[]? IceDepth { get; set; }

	[JsonPropertyName("lastWeatherSimTurn")]
	public int? LastWeatherSimTurn { get; set; }
}

public sealed class CellStackSnapshot
{
	[JsonPropertyName("index")]
	public required int Index { get; set; }

	[JsonPropertyName("entities")]
	public required List<CellEntitySnapshot> Entities { get; set; }
}

public sealed class CellEntitySnapshot
{
	[JsonPropertyName("type")]
	public required CellEntityType Type { get; set; }

	[JsonPropertyName("glyph")]
	public required string Glyph { get; set; }

	[JsonPropertyName("entityId")]
	public required string EntityId { get; set; }

	[JsonPropertyName("meta")]
	public Dictionary<string, string>? Meta { get; set; }
}

public sealed class NestSnapshot
{
	[JsonPropertyName("x")]
	public required int X { get; set; }

	[JsonPropertyName("y")]
	public required int Y { get; set; }

	[JsonPropertyName("spawnInterval")]
	public required int SpawnInterval { get; set; }

	[JsonPropertyName("turnsSinceSpawn")]
	public required int TurnsSinceSpawn { get; set; }

	[JsonPropertyName("maxSpawned")]
	public required int MaxSpawned { get; set; }

	[JsonPropertyName("templateId")]
	public required string TemplateId { get; set; }
}
