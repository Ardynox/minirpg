using System.Text;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Multiplayer;
using Xunit;

namespace MiniRPG.Tests;

public sealed class MultiplayerProtocolHardeningTests
{
	[Fact]
	public void ProtocolSerializer_RoundTrip_PreservesProtocolVersion_AndWireErrorCode()
	{
		var original = new CommandRejectedMessage
		{
			RequestId = "req-1",
			ServerTick = 120,
			SnapshotSequence = 8,
			Reason = "Invalid",
			Code = ErrorCode.InvalidActor.ToWireCode(),
		};

		var bytes = ProtocolSerializer.SerializeMessage(original);
		var decoded = ProtocolSerializer.DeserializeMessage(bytes);
		var rejected = Assert.IsType<CommandRejectedMessage>(decoded);

		Assert.Equal(ProtocolDefaults.CurrentVersion, rejected.ProtocolVersion);
		Assert.Equal("req-1", rejected.RequestId);
		Assert.Equal(120, rejected.ServerTick);
		Assert.Equal(8, rejected.SnapshotSequence);
		Assert.Equal(ErrorCode.InvalidActor.ToWireCode(), rejected.Code);
		Assert.Equal(ErrorCode.InvalidActor, ProtocolErrorCodeExtensions.ParseWireCode(rejected.Code));
	}

	[Fact]
	public void ProtocolSerializer_RoundTrip_PreservesClientTick_AndRequestId()
	{
		var original = new MoveClientCommand
		{
			RequestId = "req-move-1",
			ClientTick = 42,
			PlayerSessionId = "player-1",
			ActorId = "actor-1",
			Dx = 1,
			Dy = -1,
		};

		var bytes = ProtocolSerializer.SerializeCommand(original);
		var decoded = ProtocolSerializer.DeserializeCommand(bytes);
		var move = Assert.IsType<MoveClientCommand>(decoded);

		Assert.Equal("req-move-1", move.RequestId);
		Assert.Equal(42, move.ClientTick);
		Assert.Equal("player-1", move.PlayerSessionId);
		Assert.Equal("actor-1", move.ActorId);
		Assert.Equal(1, move.Dx);
		Assert.Equal(-1, move.Dy);
	}

	[Fact]
	public void ProtocolSerializer_DeserializeMessage_MissingMetadataFields_DefaultsGracefully()
	{
		const string legacyJson =
			"{\"kind\":\"commandRejected\",\"protocolVersion\":1,\"requestId\":\"req-legacy\",\"reason\":\"legacy\"}";

		var decoded = ProtocolSerializer.DeserializeMessage(Encoding.UTF8.GetBytes(legacyJson));
		var rejected = Assert.IsType<CommandRejectedMessage>(decoded);

		Assert.Equal("req-legacy", rejected.RequestId);
		Assert.Equal(0, rejected.ServerTick);
		Assert.Equal(0, rejected.SnapshotSequence);
		Assert.Equal("legacy", rejected.Reason);
	}

	[Fact]
	public void ProtocolSerializer_RoundTrip_ModeTransitionMessage_AndCombatCommands()
	{
		var transition = new ModeTransitionMessage
		{
			RequestId = "req-transition-1",
			FromMode = RoomSimulationMode.ExploreRealtime,
			ToMode = RoomSimulationMode.CombatTurnBased,
			Trigger = "StartCombat",
			TriggerActorId = "actor-1",
			TransitionSequence = 3,
			ServerTick = 77,
			SnapshotSequence = 12,
		};

		var transitionBytes = ProtocolSerializer.SerializeMessage(transition);
		var decodedTransition = Assert.IsType<ModeTransitionMessage>(ProtocolSerializer.DeserializeMessage(transitionBytes));
		Assert.Equal(RoomSimulationMode.ExploreRealtime, decodedTransition.FromMode);
		Assert.Equal(RoomSimulationMode.CombatTurnBased, decodedTransition.ToMode);
		Assert.Equal("StartCombat", decodedTransition.Trigger);
		Assert.Equal(3, decodedTransition.TransitionSequence);

		var useSkill = new UseSkillClientCommand
		{
			RequestId = "req-skill-1",
			ActorId = "actor-1",
			PlayerSessionId = "player-1",
			SkillId = "sword_parry",
			TargetType = SkillTargetType.Self,
		};
		var useSkillBytes = ProtocolSerializer.SerializeCommand(useSkill);
		var decodedSkill = Assert.IsType<UseSkillClientCommand>(ProtocolSerializer.DeserializeCommand(useSkillBytes));
		Assert.Equal("req-skill-1", decodedSkill.RequestId);
		Assert.Equal("sword_parry", decodedSkill.SkillId);
	}

	[Fact]
	public void ErrorCodeWireMapping_UnknownCode_FallsBackToNone()
	{
		Assert.Equal(ErrorCode.None, ProtocolErrorCodeExtensions.ParseWireCode("not_defined"));
		Assert.Equal("trade_buy_rejected", ErrorCode.TradeBuyRejected.ToWireCode());
	}

	// ── P0-8: dirty-packet hardening ─────────────────────────────────────────
	// A single malformed packet must not bubble an exception up to the
	// transport poll loop; every JSON entry is expected to swallow malformed
	// input and return null so the caller can map it to ErrorCode.DeserializationError.

	[Theory]
	[InlineData(new byte[] { 0x00 })]
	[InlineData(new byte[] { 0x7B })] // bare "{" — truncated JSON
	[InlineData(new byte[] { 0x7B, 0x22, 0x6B, 0x69 })] // truncated mid-key
	[InlineData(new byte[] { 0xFF, 0xFE, 0xFD, 0xFC })] // arbitrary binary garbage
	public void DeserializeCommand_DirtyPayload_ReturnsNullWithoutThrowing(byte[] payload)
	{
		var result = Record.Exception(() => ProtocolSerializer.DeserializeCommand(payload));
		Assert.Null(result);
		Assert.Null(ProtocolSerializer.DeserializeCommand(payload));
	}

	[Theory]
	[InlineData(new byte[] { 0x00 })]
	[InlineData(new byte[] { 0x7B })]
	[InlineData(new byte[] { 0xFF, 0xFE, 0xFD, 0xFC })]
	public void DeserializeMessage_DirtyPayload_ReturnsNullWithoutThrowing(byte[] payload)
	{
		var result = Record.Exception(() => ProtocolSerializer.DeserializeMessage(payload));
		Assert.Null(result);
		Assert.Null(ProtocolSerializer.DeserializeMessage(payload));
	}

	[Fact]
	public void DeserializeCommand_UnknownEnumValue_ReturnsNullWithoutThrowing()
	{
		var json = Encoding.UTF8.GetBytes("{\"kind\":\"NotARealCommandKind\"}");

		Assert.Null(Record.Exception(() => ProtocolSerializer.DeserializeCommand(json)));
		Assert.Null(ProtocolSerializer.DeserializeCommand(json));
	}

	[Fact]
	public void DeserializeMessage_UnknownEnumValue_ReturnsNullWithoutThrowing()
	{
		var json = Encoding.UTF8.GetBytes("{\"kind\":\"NotARealMessageKind\"}");

		Assert.Null(Record.Exception(() => ProtocolSerializer.DeserializeMessage(json)));
		Assert.Null(ProtocolSerializer.DeserializeMessage(json));
	}

	[Fact]
	public void DeserializeCommand_KnownKindWithCorruptInnerEnum_ReturnsNullWithoutThrowing()
	{
		// Valid kind discriminator but a nested enum field carries garbage —
		// System.Text.Json throws JsonException with JsonStringEnumConverter.
		var json = Encoding.UTF8.GetBytes(
			"{\"kind\":\"chestTake\",\"containerSource\":\"NotARealEnumValue\"}");

		Assert.Null(Record.Exception(() => ProtocolSerializer.DeserializeCommand(json)));
		Assert.Null(ProtocolSerializer.DeserializeCommand(json));
	}

	[Fact]
	public void DeserializeConnectRequest_DirtyPayload_ReturnsNullWithoutThrowing()
	{
		var truncated = new byte[] { 0x7B, 0x22, 0x72 };
		var garbage = new byte[] { 0xFF, 0xFE };

		Assert.Null(Record.Exception(() => ProtocolSerializer.DeserializeConnectRequest(truncated)));
		Assert.Null(Record.Exception(() => ProtocolSerializer.DeserializeConnectRequest(garbage)));
		Assert.Null(ProtocolSerializer.DeserializeConnectRequest(truncated));
		Assert.Null(ProtocolSerializer.DeserializeConnectRequest(garbage));
	}

	[Fact]
	public void DeserializeCommand_RandomFuzz_NeverThrows()
	{
		// 100 random byte streams of varied length must never bubble an
		// exception up to the caller — regression for the dirty-packet kill bug.
		var rng = new System.Random(Seed: 0xC0FFEE);
		for (var i = 0; i < 100; i++)
		{
			var length = rng.Next(0, 256);
			var payload = new byte[length];
			rng.NextBytes(payload);

			var result = Record.Exception(() => ProtocolSerializer.DeserializeCommand(payload));
			Assert.Null(result);
		}
	}

	[Fact]
	public void DeserializeMessage_RandomFuzz_NeverThrows()
	{
		var rng = new System.Random(Seed: 0xBADC0DE);
		for (var i = 0; i < 100; i++)
		{
			var length = rng.Next(0, 256);
			var payload = new byte[length];
			rng.NextBytes(payload);

			var result = Record.Exception(() => ProtocolSerializer.DeserializeMessage(payload));
			Assert.Null(result);
		}
	}
}
