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
}

public enum ServerMessageKind
{
	JoinAccepted,
	RoomSnapshot,
	StateDelta,
	EventBatch,
	CommandRejected,
	RosterChanged,
	ReservationBusy,
	ReconnectClaimed,
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

public abstract record ClientCommand(ClientCommandKind Kind)
{
	[JsonPropertyName("requestId")]
	public string RequestId { get; init; } = Guid.NewGuid().ToString("N");

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

public sealed record FacilityDeliverClientCommand() : ClientCommand(ClientCommandKind.FacilityDeliver)
{
	public string FacilityId { get; init; } = string.Empty;
}

public sealed record FacilityConstructClientCommand() : ClientCommand(ClientCommandKind.FacilityConstruct)
{
	public string FacilityId { get; init; } = string.Empty;
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

public abstract record ServerMessage(ServerMessageKind Kind)
{
	[JsonPropertyName("requestId")]
	public string? RequestId { get; init; }
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

public sealed record StateDeltaMessage() : ServerMessage(ServerMessageKind.StateDelta)
{
	public long Sequence { get; init; }
	public List<GameEvent> Events { get; init; } = [];
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
