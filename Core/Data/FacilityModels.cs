using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniRPG.Core.Config;

namespace MiniRPG.Core.Data;

public enum FacilityStage
{
	Blueprint,
	DeliverMaterials,
	Construct,
	Active,
	Broken,
}

public enum FacilityRotation
{
	North = 0,
	East = 1,
	South = 2,
	West = 3,
}

public enum FacilityUseSlotPurpose
{
	Work,
	Sleep,
	Trade,
	Input,
	Output,
	Fuel,
	Access,
}

public enum WorkTicketType
{
	DeliverConstructionMaterial,
	ConstructFacility,
	RefuelFacility,
	SupplyFacilityInput,
	ProduceRecipe,
	HaulFacilityOutput,
	RepairFacility,
}

public enum OutputTargetStrategy
{
	FacilityOutputBuffer,
	OwnerDomainStockpile,
	Market,
}

public enum EconomicDomainKind
{
	Player,
	Household,
	Shop,
	Public,
	Institution,
}

public enum StockpilePriority
{
	Low = 1,
	Normal = 2,
	High = 3,
	Critical = 4,
}

public static class DomainIds
{
	public const string Player = "player_domain";
	public const string Public = "public_domain";
}

public static class WorkBrainIds
{
	public const string DomainWorker = "domain_worker";
}

public static class FacilityIds
{
	public const string Shelf = "shelf";
	public const string FireBrazier = "fire_brazier";
	public const string Bed = "bed";
	public const string DormitoryBed = "dormitory_bed";
	public const string Stove = "stove";
	public const string ButcherTable = "butcher_table";
	public const string Smithy = "smithy";
	public const string Loom = "loom";
	public const string HerbalBench = "herbal_bench";
	public const string MarketStall = "market_stall";
}

public static class RoomRoleIds
{
	public const string Storage = "storage";
	public const string Kitchen = "kitchen";
	public const string Workshop = "workshop";
	public const string Dormitory = "dormitory";
	public const string Clinic = "clinic";
	public const string Market = "market";
}

public static class FacilityInteractionModes
{
	public const string PersonalUse = "personal_use";
	public const string DomainOrder = "domain_order";
}

public static class RecipeTagIds
{
	public const string Cooking = "cooking";
	public const string Butchering = "butchering";
	public const string Smithing = "smithing";
	public const string Tailoring = "tailoring";
	public const string Herbalism = "herbalism";
}

public static class FacilityTags
{
	public const string Storage = "storage";
	public const string Heat = "heat";
	public const string Sleep = "sleep";
	public const string Cooking = "cooking";
	public const string Butchery = "butchery";
	public const string Smithing = "smithing";
	public const string Tailoring = "tailoring";
	public const string Herbalism = "herbalism";
	public const string Trade = "trade";
}

public sealed class ItemAmount
{
	[JsonPropertyName("itemId")]
	public string ItemId { get; set; } = "";

	[JsonPropertyName("count")]
	public int Count { get; set; } = 1;

	public ItemAmount Clone() => new()
	{
		ItemId = ItemId,
		Count = Count,
	};
}

public readonly record struct ZoneCell(
	[property: JsonPropertyName("x")] int X,
	[property: JsonPropertyName("y")] int Y,
	[property: JsonPropertyName("z")] int Z);

public readonly record struct RoomModifierSet(
	[property: JsonPropertyName("workSpeed")] float WorkSpeed,
	[property: JsonPropertyName("restQuality")] float RestQuality,
	[property: JsonPropertyName("foodHandling")] float FoodHandling,
	[property: JsonPropertyName("storage")] float Storage,
	[property: JsonPropertyName("trade")] float Trade)
{
	public static readonly RoomModifierSet Neutral = new(1f, 1f, 1f, 1f, 1f);

	public RoomModifierSet MergeWith(RoomModifierSet other) => new(
		WorkSpeed * other.WorkSpeed,
		RestQuality * other.RestQuality,
		FoodHandling * other.FoodHandling,
		Storage * other.Storage,
		Trade * other.Trade);
}

public sealed class FacilityFootprintCell
{
	[JsonPropertyName("x")]
	public int X { get; set; }

	[JsonPropertyName("y")]
	public int Y { get; set; }

	[JsonPropertyName("passable")]
	public bool Passable { get; set; }

	[JsonPropertyName("blocksSight")]
	public bool BlocksSight { get; set; }

	[JsonPropertyName("glyph")]
	public string Glyph { get; set; } = "";

	public FacilityFootprintCell Clone() => new()
	{
		X = X,
		Y = Y,
		Passable = Passable,
		BlocksSight = BlocksSight,
		Glyph = Glyph,
	};
}

public sealed class FacilityUseSlot
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = "";

	[JsonPropertyName("x")]
	public int X { get; set; }

	[JsonPropertyName("y")]
	public int Y { get; set; }

	[JsonPropertyName("purpose")]
	public FacilityUseSlotPurpose Purpose { get; set; } = FacilityUseSlotPurpose.Work;

	public FacilityUseSlot Clone() => new()
	{
		Id = Id,
		X = X,
		Y = Y,
		Purpose = Purpose,
	};
}

public sealed class FacilityDef
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = "";

	[JsonPropertyName("name")]
	public string Name { get; set; } = "";

	[JsonPropertyName("glyph")]
	public string Glyph { get; set; } = "F";

	[JsonPropertyName("description")]
	public string Description { get; set; } = "";

	[JsonPropertyName("canRotate")]
	public bool CanRotate { get; set; } = true;

	[JsonPropertyName("footprint")]
	public List<FacilityFootprintCell> Footprint { get; set; } = [];

	[JsonPropertyName("useSlots")]
	public List<FacilityUseSlot> UseSlots { get; set; } = [];

	[JsonPropertyName("constructionCost")]
	public List<ItemAmount> ConstructionCost { get; set; } = [];

	[JsonPropertyName("fuelItemIds")]
	public List<string> FuelItemIds { get; set; } = [];

	[JsonPropertyName("fuelCapacity")]
	public int FuelCapacity { get; set; }

	[JsonPropertyName("fuelTicksPerItem")]
	public int FuelTicksPerItem { get; set; }

	[JsonPropertyName("heatPerFuelledTickC")]
	public float HeatPerFuelledTickC { get; set; }

	[JsonPropertyName("dryingBonusPerFuelledTick")]
	public float DryingBonusPerFuelledTick { get; set; }

	[JsonPropertyName("maxHitPoints")]
	public int MaxHitPoints { get; set; } = 20;

	[JsonPropertyName("tags")]
	public List<string> Tags { get; set; } = [];

	[JsonPropertyName("roomTags")]
	public List<string> RoomTags { get; set; } = [];

	[JsonPropertyName("interactions")]
	public List<string> Interactions { get; set; } = [];

	[JsonPropertyName("allowedRecipeTags")]
	public List<string> AllowedRecipeTags { get; set; } = [];

	public bool RequiresFuel => FuelCapacity > 0 && FuelTicksPerItem > 0 && FuelItemIds.Count > 0;

	public FacilityFootprintCell GetAnchorCell() =>
		Footprint.Count > 0
			? Footprint[0]
			: new FacilityFootprintCell { X = 0, Y = 0, Passable = true, Glyph = Glyph };
}

public sealed class RecipeDef
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = "";

	[JsonPropertyName("name")]
	public string Name { get; set; } = "";

	[JsonPropertyName("tags")]
	public List<string> Tags { get; set; } = [];

	[JsonPropertyName("inputs")]
	public List<ItemAmount> Inputs { get; set; } = [];

	[JsonPropertyName("outputs")]
	public List<ItemAmount> Outputs { get; set; } = [];

	[JsonPropertyName("workTurns")]
	public int WorkTurns { get; set; } = 1;

	[JsonPropertyName("fuelPerCraft")]
	public int FuelPerCraft { get; set; }

	[JsonPropertyName("requiredProfessionIds")]
	public List<string> RequiredProfessionIds { get; set; } = [];

	[JsonPropertyName("requiredCapacities")]
	public Dictionary<string, float> RequiredCapacities { get; set; } = new(StringComparer.Ordinal);

	[JsonPropertyName("outputTarget")]
	public OutputTargetStrategy OutputTarget { get; set; } = OutputTargetStrategy.OwnerDomainStockpile;
}

public sealed class BillDef
{
	public string Id { get; set; } = "";
	public string RecipeId { get; set; } = "";
	public bool Enabled { get; set; } = true;
	public bool AllowPersonalUse { get; set; } = true;
	public bool AllowDomainUse { get; set; } = true;
	public int TargetCount { get; set; } = 1;
	public string OwnerDomainId { get; set; } = "";

	public BillDef Clone() => new()
	{
		Id = Id,
		RecipeId = RecipeId,
		Enabled = Enabled,
		AllowPersonalUse = AllowPersonalUse,
		AllowDomainUse = AllowDomainUse,
		TargetCount = TargetCount,
		OwnerDomainId = OwnerDomainId,
	};
}

public sealed class EconomicDomain
{
	public string Id { get; set; } = "";
	public string Name { get; set; } = "";
	public EconomicDomainKind Kind { get; set; } = EconomicDomainKind.Household;
	public string RepresentativeActorId { get; set; } = "";
	public bool IsPublic { get; set; }

	public EconomicDomain Clone() => new()
	{
		Id = Id,
		Name = Name,
		Kind = Kind,
		RepresentativeActorId = RepresentativeActorId,
		IsPublic = IsPublic,
	};
}

public sealed class StockpileZone
{
	public string Id { get; set; } = "";
	public string Name { get; set; } = "";
	public string OwnerDomainId { get; set; } = "";
	public StockpilePriority Priority { get; set; } = StockpilePriority.Normal;
	public bool AllowInput { get; set; } = true;
	public bool AllowOutput { get; set; } = true;
	public List<ZoneCell> Cells { get; set; } = [];
	public List<string> AllowedItemIds { get; set; } = [];
	public List<string> AllowedCategories { get; set; } = [];
	public List<string> AllowedTags { get; set; } = [];

	public bool AcceptsItem(Item item)
	{
		if (AllowedItemIds.Count > 0
			&& !AllowedItemIds.Contains(item.Id, StringComparer.Ordinal))
		{
			return false;
		}

		if (AllowedCategories.Count > 0
			&& !AllowedCategories.Contains(item.Category, StringComparer.Ordinal))
		{
			return false;
		}

		if (AllowedTags.Count > 0
			&& !AllowedTags.Any(tag => item.Tags.ContainsKey(tag)))
		{
			return false;
		}

		return true;
	}

	public StockpileZone Clone() => new()
	{
		Id = Id,
		Name = Name,
		OwnerDomainId = OwnerDomainId,
		Priority = Priority,
		AllowInput = AllowInput,
		AllowOutput = AllowOutput,
		Cells = [.. Cells],
		AllowedItemIds = [.. AllowedItemIds],
		AllowedCategories = [.. AllowedCategories],
		AllowedTags = [.. AllowedTags],
	};
}

public sealed class WorkTicket
{
	public string Id { get; set; } = "";
	public WorkTicketType Type { get; set; } = WorkTicketType.ProduceRecipe;
	public int Priority { get; set; }
	public string FacilityId { get; set; } = "";
	public string OwnerDomainId { get; set; } = "";
	public string RecipeId { get; set; } = "";
	public string BillId { get; set; } = "";
	public string RequiredItemId { get; set; } = "";
	public int RequiredCount { get; set; } = 1;
	public string TargetUseSlotId { get; set; } = "";
	public string ReservedByActorId { get; set; } = "";
	public int WorkRemaining { get; set; } = 1;
}

public sealed class JobBoardState
{
	public Dictionary<string, WorkTicket> Tickets { get; set; } = new(StringComparer.Ordinal);
	public int NextSequence { get; set; } = 1;
	public int LastRebuildTurn { get; set; } = -1;

	public string AllocateTicketId()
	{
		var id = $"ticket_{NextSequence}";
		NextSequence++;
		return id;
	}

	public void Clear()
	{
		Tickets.Clear();
		LastRebuildTurn = -1;
	}

	public void ClearReservations()
	{
		foreach (var ticket in Tickets.Values)
			ticket.ReservedByActorId = string.Empty;
	}
}

public sealed class FacilityInstance
{
	public string Id { get; set; } = "";
	public string FacilityDefId { get; set; } = "";
	public int AnchorX { get; set; }
	public int AnchorY { get; set; }
	public int Z { get; set; }
	public FacilityRotation Rotation { get; set; }
	public FacilityStage Stage { get; set; } = FacilityStage.Blueprint;
	public string OwnerDomainId { get; set; } = "";
	public bool AllowPersonalUse { get; set; } = true;
	public bool AllowDomainOrders { get; set; } = true;
	public int HitPoints { get; set; } = 20;
	public int MaxHitPoints { get; set; } = 20;
	public int FuelTicksRemaining { get; set; }
	public List<ItemAmount> DeliveredConstructionMaterials { get; set; } = [];
	public List<Item> InputBuffer { get; set; } = [];
	public List<Item> OutputBuffer { get; set; } = [];
	public List<Item> FuelBuffer { get; set; } = [];
	public List<BillDef> Bills { get; set; } = [];
	public Dictionary<string, string> SlotReservations { get; set; } = new(StringComparer.Ordinal);
	public string CachedRoomRoleId { get; set; } = "";

	[JsonIgnore]
	public bool IsOperational => Stage == FacilityStage.Active && HitPoints > 0;

	public FacilityInstance Clone() => new()
	{
		Id = Id,
		FacilityDefId = FacilityDefId,
		AnchorX = AnchorX,
		AnchorY = AnchorY,
		Z = Z,
		Rotation = Rotation,
		Stage = Stage,
		OwnerDomainId = OwnerDomainId,
		AllowPersonalUse = AllowPersonalUse,
		AllowDomainOrders = AllowDomainOrders,
		HitPoints = HitPoints,
		MaxHitPoints = MaxHitPoints,
		FuelTicksRemaining = FuelTicksRemaining,
		DeliveredConstructionMaterials = DeliveredConstructionMaterials.Select(static item => item.Clone()).ToList(),
		InputBuffer = CloneItems(InputBuffer),
		OutputBuffer = CloneItems(OutputBuffer),
		FuelBuffer = CloneItems(FuelBuffer),
		Bills = Bills.Select(static bill => bill.Clone()).ToList(),
		SlotReservations = new Dictionary<string, string>(SlotReservations, StringComparer.Ordinal),
		CachedRoomRoleId = CachedRoomRoleId,
	};

	private static List<Item> CloneItems(IEnumerable<Item> items) =>
		items.Select(static item => ItemSnapshotMapper.CreateItem(ItemSnapshotMapper.BuildSnapshot(item))).ToList();
}

public sealed class RoomRoleDef
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = "";

	[JsonPropertyName("name")]
	public string Name { get; set; } = "";

	[JsonPropertyName("facilityTags")]
	public List<string> FacilityTags { get; set; } = [];

	[JsonPropertyName("facilityIds")]
	public List<string> FacilityIds { get; set; } = [];

	[JsonPropertyName("minimumScore")]
	public int MinimumScore { get; set; } = 1;

	[JsonPropertyName("modifiers")]
	public RoomModifierSet Modifiers { get; set; } = RoomModifierSet.Neutral;
}

public sealed class RoomSnapshot
{
	public string Id { get; set; } = "";
	public int Z { get; set; }
	public bool IsIndoors { get; set; }
	public int CellCount { get; set; }
	public float ShelterStrength { get; set; }
	public string PrimaryRoleId { get; set; } = "";
	public RoomModifierSet Modifiers { get; set; } = RoomModifierSet.Neutral;
	public List<ZoneCell> Cells { get; set; } = [];
}

public static class FacilityRegistry
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		Converters = { new JsonStringEnumConverter() },
	};

	private static readonly Dictionary<string, FacilityDef> Registry = new(StringComparer.Ordinal);

	public static IReadOnlyDictionary<string, FacilityDef> All => Registry;

	public static void Clear() => Registry.Clear();

	public static void Register(FacilityDef def)
	{
		if (string.IsNullOrWhiteSpace(def.Id))
			return;

		Registry[def.Id] = def;
	}

	public static FacilityDef? Get(string id) =>
		Registry.GetValueOrDefault(id);

	public static void Load(string relativeDataPath = "facilities.json")
	{
		Clear();
		if (!GameDataLocator.TryReadText(relativeDataPath, out var json, out _))
			return;

		var defs = JsonSerializer.Deserialize<List<FacilityDef>>(json, JsonOptions) ?? [];
		foreach (var def in defs)
		{
			if (def.Footprint.Count == 0)
				def.Footprint.Add(new FacilityFootprintCell { X = 0, Y = 0, Passable = true, Glyph = def.Glyph });
			Register(def);
		}
	}
}

public static class RecipeRegistry
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		Converters = { new JsonStringEnumConverter() },
	};

	private static readonly Dictionary<string, RecipeDef> Registry = new(StringComparer.Ordinal);

	public static IReadOnlyDictionary<string, RecipeDef> All => Registry;

	public static void Clear() => Registry.Clear();

	public static void Register(RecipeDef def)
	{
		if (string.IsNullOrWhiteSpace(def.Id))
			return;

		Registry[def.Id] = def;
	}

	public static RecipeDef? Get(string id) =>
		Registry.GetValueOrDefault(id);

	public static void Load(string relativeDataPath = "recipes.json")
	{
		Clear();
		if (!GameDataLocator.TryReadText(relativeDataPath, out var json, out _))
			return;

		var defs = JsonSerializer.Deserialize<List<RecipeDef>>(json, JsonOptions) ?? [];
		foreach (var def in defs)
			Register(def);
	}
}

public static class RoomRoleRegistry
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		Converters = { new JsonStringEnumConverter() },
	};

	private static readonly Dictionary<string, RoomRoleDef> Registry = new(StringComparer.Ordinal);

	public static IReadOnlyDictionary<string, RoomRoleDef> All => Registry;

	public static void Clear() => Registry.Clear();

	public static void Register(RoomRoleDef def)
	{
		if (string.IsNullOrWhiteSpace(def.Id))
			return;

		Registry[def.Id] = def;
	}

	public static RoomRoleDef? Get(string id) =>
		Registry.GetValueOrDefault(id);

	public static void Load(string relativeDataPath = "room_roles.json")
	{
		Clear();
		if (!GameDataLocator.TryReadText(relativeDataPath, out var json, out _))
			return;

		var defs = JsonSerializer.Deserialize<List<RoomRoleDef>>(json, JsonOptions) ?? [];
		foreach (var def in defs)
			Register(def);
	}
}
