using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiniRPG.Core.Multiplayer;

/// <summary>
/// Polymorphic JSON serializer for <see cref="ClientCommand"/> and <see cref="ServerMessage"/>.
/// Uses the Kind discriminator to dispatch to the correct concrete type.
/// </summary>
public static class ProtocolSerializer
{
	private static readonly JsonSerializerOptions Options = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		Converters =
		{
			new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
		},
	};

	public static byte[] SerializeCommand(ClientCommand command) =>
		JsonSerializer.SerializeToUtf8Bytes(command, command.GetType(), Options);

	public static byte[] SerializeMessage(ServerMessage message) =>
		JsonSerializer.SerializeToUtf8Bytes(message, message.GetType(), Options);

	public static ClientCommand? DeserializeCommand(ReadOnlySpan<byte> data)
	{
		// All JSON entry points must absorb malformed payloads silently so a single
		// dirty packet cannot kill the server / client process. Callers that need a
		// rejection wire response check the null return and emit ErrorCode.DeserializationError.
		try
		{
			var raw = data.ToArray();
			using var doc = JsonDocument.Parse(raw);
			if (!doc.RootElement.TryGetProperty("kind", out var kindProp))
				return null;

			var kindStr = kindProp.GetString();
			if (!Enum.TryParse<ClientCommandKind>(kindStr, ignoreCase: true, out var kind))
				return null;
			return kind switch
			{
				ClientCommandKind.Move => JsonSerializer.Deserialize<MoveClientCommand>(raw, Options),
				ClientCommandKind.Dig => JsonSerializer.Deserialize<DigClientCommand>(raw, Options),
				ClientCommandKind.Attack => JsonSerializer.Deserialize<AttackClientCommand>(raw, Options),
				ClientCommandKind.CastSkill => JsonSerializer.Deserialize<CastSkillClientCommand>(raw, Options),
				ClientCommandKind.EatInventory => JsonSerializer.Deserialize<EatInventoryClientCommand>(raw, Options),
				ClientCommandKind.Rest => JsonSerializer.Deserialize<RestClientCommand>(raw, Options),
				ClientCommandKind.TerrainBuild => JsonSerializer.Deserialize<TerrainBuildClientCommand>(raw, Options),
				ClientCommandKind.TerrainDemolish => JsonSerializer.Deserialize<TerrainDemolishClientCommand>(raw, Options),
				ClientCommandKind.FacilityPlaceBlueprint => JsonSerializer.Deserialize<FacilityPlaceBlueprintClientCommand>(raw, Options),
				ClientCommandKind.FacilityDemolish => JsonSerializer.Deserialize<FacilityDemolishClientCommand>(raw, Options),
				ClientCommandKind.FacilityDeliver => JsonSerializer.Deserialize<FacilityDeliverClientCommand>(raw, Options),
				ClientCommandKind.FacilityConstruct => JsonSerializer.Deserialize<FacilityConstructClientCommand>(raw, Options),
				ClientCommandKind.Interact => JsonSerializer.Deserialize<InteractClientCommand>(raw, Options),
				ClientCommandKind.Pickup => JsonSerializer.Deserialize<PickupClientCommand>(raw, Options),
				ClientCommandKind.InventoryToggleEquip => JsonSerializer.Deserialize<InventoryToggleEquipClientCommand>(raw, Options),
				ClientCommandKind.InventoryDrop => JsonSerializer.Deserialize<InventoryDropClientCommand>(raw, Options),
				ClientCommandKind.InventoryMoveItem => JsonSerializer.Deserialize<InventoryMoveItemClientCommand>(raw, Options),
				ClientCommandKind.InventoryRotateItem => JsonSerializer.Deserialize<InventoryRotateItemClientCommand>(raw, Options),
				ClientCommandKind.InventoryAutoPack => JsonSerializer.Deserialize<InventoryAutoPackClientCommand>(raw, Options),
				ClientCommandKind.ChestTake => JsonSerializer.Deserialize<ChestTakeClientCommand>(raw, Options),
				ClientCommandKind.ChestTakeAll => JsonSerializer.Deserialize<ChestTakeAllClientCommand>(raw, Options),
				ClientCommandKind.ChestPut => JsonSerializer.Deserialize<ChestPutClientCommand>(raw, Options),
				ClientCommandKind.TradeBuy => JsonSerializer.Deserialize<TradeBuyClientCommand>(raw, Options),
				ClientCommandKind.TradeSell => JsonSerializer.Deserialize<TradeSellClientCommand>(raw, Options),
				ClientCommandKind.OpenModal => JsonSerializer.Deserialize<OpenModalClientCommand>(raw, Options),
				ClientCommandKind.CloseModal => JsonSerializer.Deserialize<CloseModalClientCommand>(raw, Options),
				ClientCommandKind.DelegateActor => JsonSerializer.Deserialize<DelegateActorClientCommand>(raw, Options),
				ClientCommandKind.ReclaimPrimaryActor => JsonSerializer.Deserialize<ReclaimPrimaryActorClientCommand>(raw, Options),
				ClientCommandKind.AssignPrimaryActor => JsonSerializer.Deserialize<AssignPrimaryActorClientCommand>(raw, Options),
				ClientCommandKind.KickPlayer => JsonSerializer.Deserialize<KickPlayerClientCommand>(raw, Options),
				ClientCommandKind.StartCombat => JsonSerializer.Deserialize<StartCombatClientCommand>(raw, Options),
				ClientCommandKind.EndTurn => JsonSerializer.Deserialize<EndTurnClientCommand>(raw, Options),
				ClientCommandKind.UseSkill => JsonSerializer.Deserialize<UseSkillClientCommand>(raw, Options),
				ClientCommandKind.EndCombat => JsonSerializer.Deserialize<EndCombatClientCommand>(raw, Options),
				ClientCommandKind.Climb => JsonSerializer.Deserialize<ClimbClientCommand>(raw, Options),
				ClientCommandKind.GiveItem => JsonSerializer.Deserialize<GiveItemClientCommand>(raw, Options),
				ClientCommandKind.ApplyGeneModification => JsonSerializer.Deserialize<ApplyGeneModificationClientCommand>(raw, Options),
				ClientCommandKind.ConversationChoose => JsonSerializer.Deserialize<ConversationChooseClientCommand>(raw, Options),
				ClientCommandKind.ConversationAdvanceLine => JsonSerializer.Deserialize<ConversationAdvanceLineClientCommand>(raw, Options),
				ClientCommandKind.ConversationLeave => JsonSerializer.Deserialize<ConversationLeaveClientCommand>(raw, Options),
				ClientCommandKind.ConversationRequestTakeover => JsonSerializer.Deserialize<ConversationRequestTakeoverClientCommand>(raw, Options),
				ClientCommandKind.ConversationApproveTakeover => JsonSerializer.Deserialize<ConversationApproveTakeoverClientCommand>(raw, Options),
				_ => null,
			};
		}
		catch (JsonException)
		{
			return null;
		}
		catch (NotSupportedException)
		{
			return null;
		}
		catch (ArgumentException)
		{
			return null;
		}
	}

	public static byte[] SerializeConnectRequest(GameServerConnectRequest request) =>
		JsonSerializer.SerializeToUtf8Bytes(request, Options);

	public static GameServerConnectRequest? DeserializeConnectRequest(ReadOnlySpan<byte> data)
	{
		try
		{
			return JsonSerializer.Deserialize<GameServerConnectRequest>(data, Options);
		}
		catch (JsonException)
		{
			return null;
		}
		catch (NotSupportedException)
		{
			return null;
		}
		catch (ArgumentException)
		{
			return null;
		}
	}

	public static ServerMessage? DeserializeMessage(ReadOnlySpan<byte> data)
	{
		try
		{
			var raw = data.ToArray();
			using var doc = JsonDocument.Parse(raw);
			if (!doc.RootElement.TryGetProperty("kind", out var kindProp))
				return null;

			var kindStr = kindProp.GetString();
			if (!Enum.TryParse<ServerMessageKind>(kindStr, ignoreCase: true, out var kind))
				return null;
			return kind switch
			{
				ServerMessageKind.JoinAccepted => JsonSerializer.Deserialize<JoinAcceptedMessage>(raw, Options),
				ServerMessageKind.RoomSnapshot => JsonSerializer.Deserialize<RoomSnapshotMessage>(raw, Options),
				ServerMessageKind.EventBatch => JsonSerializer.Deserialize<EventBatchMessage>(raw, Options),
				ServerMessageKind.CommandRejected => JsonSerializer.Deserialize<CommandRejectedMessage>(raw, Options),
				ServerMessageKind.RosterChanged => JsonSerializer.Deserialize<RosterChangedMessage>(raw, Options),
				ServerMessageKind.ReservationBusy => JsonSerializer.Deserialize<ReservationBusyMessage>(raw, Options),
				ServerMessageKind.ReconnectClaimed => JsonSerializer.Deserialize<ReconnectClaimedMessage>(raw, Options),
				ServerMessageKind.ModeTransition => JsonSerializer.Deserialize<ModeTransitionMessage>(raw, Options),
				_ => null,
			};
		}
		catch (JsonException)
		{
			return null;
		}
		catch (NotSupportedException)
		{
			return null;
		}
		catch (ArgumentException)
		{
			return null;
		}
	}
}
