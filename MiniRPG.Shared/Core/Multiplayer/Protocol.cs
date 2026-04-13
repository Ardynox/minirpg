using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MiniRPG.Core.Multiplayer;

public enum ClientCommandKind
{
	Move,
	Dig,
	Attack,
	CastSkill,
	EatInventory,
	Rest,
	TerrainBuild,
	TerrainDemolish,
	FacilityPlaceBlueprint,
	FacilityDemolish,
	FacilityDeliver,
	FacilityConstruct,
	Interact,
	Pickup,
	InventoryToggleEquip,
	InventoryDrop,
	ChestTake,
	ChestTakeAll,
	ChestPut,
	TradeBuy,
	TradeSell,
	DialogChoose,
	OpenModal,
	CloseModal,
	DelegateActor,
	ReclaimPrimaryActor,
	AssignPrimaryActor,
	KickPlayer,
	StartCombat,
	EndTurn,
	UseSkill,
	EndCombat,
	Climb,
}

public enum ServerMessageKind
{
	JoinAccepted,
	RoomSnapshot,
	EventBatch,
	CommandRejected,
	RosterChanged,
	ReservationBusy,
	ReconnectClaimed,
	ModeTransition,
}

public enum ContainerSourceKind
{
	Ground,
	Inventory,
}

public enum TradeGoodSourceKind
{
	Shop,
	Inventory,
}

public enum ErrorCode
{
	None,
	UnsupportedCommand,
	DeserializationError,
	InvalidJoinRequest,
	RoomNotFound,
	ConnectFailed,
	InvalidJoinToken,
	InvalidReconnectToken,
	ReconnectExpired,
	UnauthorizedActor,
	ReservationBusy,
	MissingPlayerSession,
	InvalidActor,
	InvalidTarget,
	InvalidInteraction,
	InvalidContainer,
	ItemNotFound,
	InvalidInventoryIndex,
	InventoryToggleRejected,
	ItemEquipped,
	InvalidTradeActor,
	TradeGoodMissing,
	TradeBuyRejected,
	TradeSellRejected,
	InvalidDialog,
	InvalidModal,
	InvalidAssignRequest,
	AssignPrimaryActorFailed,
	InvalidKickRequest,
	KickPlayerFailed,
	DelegateFailed,
	ReclaimFailed,
	InvalidModeTransition,
	NotInCombat,
	NotYourTurn,
	EndTurnRejected,
	StartCombatRejected,
	EndCombatRejected,
	UseSkillRejected,
	PvpDisabled,
	FriendlyFireDisabled,
}

public static class ProtocolDefaults
{
	public const int CurrentVersion = 1;
}

public abstract record ClientCommand(ClientCommandKind Kind)
{
	[JsonPropertyName("protocolVersion")]
	public int ProtocolVersion { get; init; } = ProtocolDefaults.CurrentVersion;

	[JsonPropertyName("requestId")]
	public string RequestId { get; init; } = Guid.NewGuid().ToString("N");

	[JsonPropertyName("clientTick")]
	public long ClientTick { get; init; }

	[JsonPropertyName("playerSessionId")]
	public string? PlayerSessionId { get; init; }

	[JsonPropertyName("actorId")]
	public string? ActorId { get; init; }
}

public sealed record MoveClientCommand() : ClientCommand(ClientCommandKind.Move)
{
	public int Dx { get; init; }
	public int Dy { get; init; }
}

public sealed record DigClientCommand() : ClientCommand(ClientCommandKind.Dig)
{
	public int Dx { get; init; }
	public int Dy { get; init; }
	public string SkillId { get; init; } = string.Empty;
}

public sealed record AttackClientCommand() : ClientCommand(ClientCommandKind.Attack)
{
	public string? SkillId { get; init; }
	public string TargetActorId { get; init; } = string.Empty;
	public string? TargetLimbId { get; init; }
}

public sealed record CastSkillClientCommand() : ClientCommand(ClientCommandKind.CastSkill)
{
	public string SkillId { get; init; } = string.Empty;
	public SkillTargetType TargetType { get; init; }
	public string? TargetActorId { get; init; }
	public string? TargetLimbId { get; init; }
	public string? TargetItemId { get; init; }
	public int TargetX { get; init; }
	public int TargetY { get; init; }
	public int TargetZ { get; init; }
}

public sealed record EatInventoryClientCommand() : ClientCommand(ClientCommandKind.EatInventory)
{
	public int InventoryIndex { get; init; }
}

public sealed record RestClientCommand() : ClientCommand(ClientCommandKind.Rest)
{
}

public sealed record TerrainBuildClientCommand() : ClientCommand(ClientCommandKind.TerrainBuild)
{
	public string TerrainId { get; init; } = string.Empty;
	public int TargetX { get; init; }
	public int TargetY { get; init; }
	public int TargetZ { get; init; }
}

public sealed record TerrainDemolishClientCommand() : ClientCommand(ClientCommandKind.TerrainDemolish)
{
	public int TargetX { get; init; }
	public int TargetY { get; init; }
	public int TargetZ { get; init; }
}

public sealed record FacilityPlaceBlueprintClientCommand() : ClientCommand(ClientCommandKind.FacilityPlaceBlueprint)
{
	public string FacilityDefId { get; init; } = string.Empty;
	public int TargetX { get; init; }
	public int TargetY { get; init; }
	public int TargetZ { get; init; }
	public FacilityRotation Rotation { get; init; }
}

public sealed record FacilityDemolishClientCommand() : ClientCommand(ClientCommandKind.FacilityDemolish)
{
	public string FacilityId { get; init; } = string.Empty;
}

public sealed record FacilityDeliverClientCommand() : ClientCommand(ClientCommandKind.FacilityDeliver)
{
	public string FacilityId { get; init; } = string.Empty;
}

public sealed record FacilityConstructClientCommand() : ClientCommand(ClientCommandKind.FacilityConstruct)
{
	public string FacilityId { get; init; } = string.Empty;
}

public sealed record ClimbClientCommand() : ClientCommand(ClientCommandKind.Climb)
{
	/// <summary>Logical Z direction: -1 = up, +1 = down.</summary>
	public int Dz { get; init; }
}

public sealed record InteractClientCommand() : ClientCommand(ClientCommandKind.Interact)
{
	public string TargetActorId { get; init; } = string.Empty;
	public string InteractionDefId { get; init; } = string.Empty;
}

public sealed record PickupClientCommand() : ClientCommand(ClientCommandKind.Pickup)
{
	public string ItemInstanceId { get; init; } = string.Empty;
}

public sealed record InventoryToggleEquipClientCommand() : ClientCommand(ClientCommandKind.InventoryToggleEquip)
{
	public int InventoryIndex { get; init; }
}

public sealed record InventoryDropClientCommand() : ClientCommand(ClientCommandKind.InventoryDrop)
{
	public int InventoryIndex { get; init; }
}

public sealed record ChestTakeClientCommand() : ClientCommand(ClientCommandKind.ChestTake)
{
	public ContainerSourceKind ContainerSource { get; init; }
	public string ContainerInstanceId { get; init; } = string.Empty;
	public string? ContainerOwnerActorId { get; init; }
	public int ContainerX { get; init; }
	public int ContainerY { get; init; }
	public int ContainerZ { get; init; }
	public string ItemInstanceId { get; init; } = string.Empty;
}

public sealed record ChestTakeAllClientCommand() : ClientCommand(ClientCommandKind.ChestTakeAll)
{
	public ContainerSourceKind ContainerSource { get; init; }
	public string ContainerInstanceId { get; init; } = string.Empty;
	public string? ContainerOwnerActorId { get; init; }
	public int ContainerX { get; init; }
	public int ContainerY { get; init; }
	public int ContainerZ { get; init; }
}

public sealed record ChestPutClientCommand() : ClientCommand(ClientCommandKind.ChestPut)
{
	public ContainerSourceKind ContainerSource { get; init; }
	public string ContainerInstanceId { get; init; } = string.Empty;
	public string? ContainerOwnerActorId { get; init; }
	public int ContainerX { get; init; }
	public int ContainerY { get; init; }
	public int ContainerZ { get; init; }
	public int InventoryIndex { get; init; }
}

public sealed record TradeBuyClientCommand() : ClientCommand(ClientCommandKind.TradeBuy)
{
	public string TraderActorId { get; init; } = string.Empty;
	public TradeGoodSourceKind GoodSource { get; init; }
	public int GoodIndex { get; init; }
}

public sealed record TradeSellClientCommand() : ClientCommand(ClientCommandKind.TradeSell)
{
	public string TraderActorId { get; init; } = string.Empty;
	public int InventoryIndex { get; init; }
}

public sealed record DialogChooseClientCommand() : ClientCommand(ClientCommandKind.DialogChoose)
{
	public string DialogId { get; init; } = string.Empty;
	public string OptionId { get; init; } = string.Empty;
}

public sealed record OpenModalClientCommand() : ClientCommand(ClientCommandKind.OpenModal)
{
	public string ModalId { get; init; } = string.Empty;
}

public sealed record CloseModalClientCommand() : ClientCommand(ClientCommandKind.CloseModal)
{
	public string ModalId { get; init; } = string.Empty;
}

public sealed record DelegateActorClientCommand() : ClientCommand(ClientCommandKind.DelegateActor)
{
	public string TargetPlayerSessionId { get; init; } = string.Empty;
}

public sealed record ReclaimPrimaryActorClientCommand() : ClientCommand(ClientCommandKind.ReclaimPrimaryActor)
{
}

public sealed record AssignPrimaryActorClientCommand() : ClientCommand(ClientCommandKind.AssignPrimaryActor)
{
	public string TargetPlayerSessionId { get; init; } = string.Empty;
	public string TargetActorId { get; init; } = string.Empty;
}

public sealed record KickPlayerClientCommand() : ClientCommand(ClientCommandKind.KickPlayer)
{
	public string TargetPlayerSessionId { get; init; } = string.Empty;
}

public sealed record StartCombatClientCommand() : ClientCommand(ClientCommandKind.StartCombat)
{
	public string TargetActorId { get; init; } = string.Empty;
}

public sealed record EndTurnClientCommand() : ClientCommand(ClientCommandKind.EndTurn)
{
}

public sealed record UseSkillClientCommand() : ClientCommand(ClientCommandKind.UseSkill)
{
	public string SkillId { get; init; } = string.Empty;
	public SkillTargetType TargetType { get; init; }
	public string? TargetActorId { get; init; }
	public string? TargetLimbId { get; init; }
	public string? TargetItemId { get; init; }
	public int TargetX { get; init; }
	public int TargetY { get; init; }
	public int TargetZ { get; init; }
}

public sealed record EndCombatClientCommand() : ClientCommand(ClientCommandKind.EndCombat)
{
}

public abstract record ServerMessage(ServerMessageKind Kind)
{
	[JsonPropertyName("protocolVersion")]
	public int ProtocolVersion { get; init; } = ProtocolDefaults.CurrentVersion;

	[JsonPropertyName("requestId")]
	public string? RequestId { get; init; }

	[JsonPropertyName("serverTick")]
	public long ServerTick { get; init; }

	[JsonPropertyName("snapshotSequence")]
	public long SnapshotSequence { get; init; }
}

public sealed record JoinAcceptedMessage() : ServerMessage(ServerMessageKind.JoinAccepted)
{
	public string RoomId { get; init; } = string.Empty;
	public string RoomCode { get; init; } = string.Empty;
	public string PlayerSessionId { get; init; } = string.Empty;
}

public sealed record RoomSnapshotMessage() : ServerMessage(ServerMessageKind.RoomSnapshot)
{
	public RoomRuntimeState Room { get; init; } = new();
	public SaveFile Snapshot { get; init; } = null!;
}

public sealed record EventBatchMessage() : ServerMessage(ServerMessageKind.EventBatch)
{
	public List<GameEvent> Events { get; init; } = [];
}

public sealed record CommandRejectedMessage() : ServerMessage(ServerMessageKind.CommandRejected)
{
	public string Reason { get; init; } = string.Empty;
	public string? Code { get; init; }
}

public sealed record RosterChangedMessage() : ServerMessage(ServerMessageKind.RosterChanged)
{
	public RoomRuntimeState Room { get; init; } = new();
}

public sealed record ReservationBusyMessage() : ServerMessage(ServerMessageKind.ReservationBusy)
{
	public string ReservationKey { get; init; } = string.Empty;
	public string BusyByPlayerSessionId { get; init; } = string.Empty;
}

public sealed record ReconnectClaimedMessage() : ServerMessage(ServerMessageKind.ReconnectClaimed)
{
	public string PlayerSessionId { get; init; } = string.Empty;
	public string ActorId { get; init; } = string.Empty;
}

public sealed record ModeTransitionMessage() : ServerMessage(ServerMessageKind.ModeTransition)
{
	public RoomSimulationMode FromMode { get; init; }
	public RoomSimulationMode ToMode { get; init; }
	public string Trigger { get; init; } = string.Empty;
	public string? TriggerActorId { get; init; }
	public long TransitionSequence { get; init; }
}
