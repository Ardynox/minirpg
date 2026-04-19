using System;
using System.Linq;
using System.Reflection;
using MiniRPG.Core.Multiplayer;
using Xunit;

namespace MiniRPG.Tests;

/// <summary>
/// Compile-time-enforceable guard that every value in <see cref="ClientCommandKind"/>
/// has a matching deserialization branch in <see cref="ProtocolSerializer.DeserializeCommand"/>.
///
/// This was wired up after P0-5 (the Climb branch had been silently absent
/// since the protocol gained the enum value). Future enum additions that
/// forget to update the switch will trip this test in CI instead of breaking
/// multiplayer at runtime.
/// </summary>
public sealed class ProtocolSerializerEnumExhaustivenessTests
{
	[Fact]
	public void EveryClientCommandKind_HasADeserializationBranch()
	{
		var missing = Enum.GetValues(typeof(ClientCommandKind))
			.Cast<ClientCommandKind>()
			.Where(kind => !TryRoundTripDefaultCommand(kind, out _))
			.ToList();

		Assert.True(
			missing.Count == 0,
			$"ProtocolSerializer.DeserializeCommand is missing a branch for: {string.Join(", ", missing)}. "
			+ "Add a switch arm to ProtocolSerializer.DeserializeCommand and the matching record in Protocol.cs.");
	}

	[Theory]
	[MemberData(nameof(EnumerateClientCommandKinds))]
	public void RoundTrip_ForEveryClientCommandKind_PreservesKind(ClientCommandKind kind)
	{
		Assert.True(
			TryRoundTripDefaultCommand(kind, out var roundTrippedKind),
			$"No deserialization branch for {kind} in ProtocolSerializer.DeserializeCommand.");
		Assert.Equal(kind, roundTrippedKind);
	}

	public static System.Collections.Generic.IEnumerable<object[]> EnumerateClientCommandKinds()
		=> Enum.GetValues(typeof(ClientCommandKind))
			.Cast<ClientCommandKind>()
			.Select(kind => new object[] { kind });

	private static bool TryRoundTripDefaultCommand(ClientCommandKind kind, out ClientCommandKind decodedKind)
	{
		decodedKind = default;
		var commandType = LocateCommandType(kind);
		Assert.NotNull(commandType);

		var instance = (ClientCommand?)Activator.CreateInstance(commandType!);
		Assert.NotNull(instance);

		var bytes = ProtocolSerializer.SerializeCommand(instance!);
		var decoded = ProtocolSerializer.DeserializeCommand(bytes);
		if (decoded == null)
			return false;

		decodedKind = decoded.Kind;
		Assert.Equal(commandType, decoded.GetType());
		return true;
	}

	private static Type? LocateCommandType(ClientCommandKind kind)
	{
		// Each ClientCommandKind has a sibling sealed record in the same
		// assembly; locate it by reflection so the test does not require
		// hand-maintained mapping (which would defeat the guard).
		var assembly = typeof(ClientCommand).Assembly;
		var commandTypes = assembly
			.GetTypes()
			.Where(static t => !t.IsAbstract && typeof(ClientCommand).IsAssignableFrom(t))
			.ToList();

		foreach (var candidate in commandTypes)
		{
			ClientCommand? probe;
			try
			{
				probe = (ClientCommand?)Activator.CreateInstance(candidate);
			}
			catch (MissingMethodException)
			{
				continue;
			}
			catch (TargetInvocationException)
			{
				continue;
			}

			if (probe != null && probe.Kind == kind)
				return candidate;
		}

		return null;
	}
}

/// <summary>
/// Symmetric exhaustiveness guard for <see cref="ServerMessageKind"/>.
/// </summary>
public sealed class ProtocolSerializerServerMessageExhaustivenessTests
{
	[Fact]
	public void EveryServerMessageKind_HasADeserializationBranch()
	{
		var missing = Enum.GetValues(typeof(ServerMessageKind))
			.Cast<ServerMessageKind>()
			.Where(kind => !TryRoundTripDefaultMessage(kind, out _))
			.ToList();

		Assert.True(
			missing.Count == 0,
			$"ProtocolSerializer.DeserializeMessage is missing a branch for: {string.Join(", ", missing)}.");
	}

	[Theory]
	[MemberData(nameof(EnumerateServerMessageKinds))]
	public void RoundTrip_ForEveryServerMessageKind_PreservesKind(ServerMessageKind kind)
	{
		Assert.True(
			TryRoundTripDefaultMessage(kind, out var decodedKind),
			$"No deserialization branch for {kind} in ProtocolSerializer.DeserializeMessage.");
		Assert.Equal(kind, decodedKind);
	}

	public static System.Collections.Generic.IEnumerable<object[]> EnumerateServerMessageKinds()
		=> Enum.GetValues(typeof(ServerMessageKind))
			.Cast<ServerMessageKind>()
			.Select(kind => new object[] { kind });

	private static bool TryRoundTripDefaultMessage(ServerMessageKind kind, out ServerMessageKind decodedKind)
	{
		decodedKind = default;
		var messageType = LocateMessageType(kind);
		Assert.NotNull(messageType);

		var instance = (ServerMessage?)Activator.CreateInstance(messageType!);
		Assert.NotNull(instance);

		var bytes = ProtocolSerializer.SerializeMessage(instance!);
		var decoded = ProtocolSerializer.DeserializeMessage(bytes);
		if (decoded == null)
			return false;

		decodedKind = decoded.Kind;
		Assert.Equal(messageType, decoded.GetType());
		return true;
	}

	private static Type? LocateMessageType(ServerMessageKind kind)
	{
		var assembly = typeof(ServerMessage).Assembly;
		var messageTypes = assembly
			.GetTypes()
			.Where(static t => !t.IsAbstract && typeof(ServerMessage).IsAssignableFrom(t))
			.ToList();

		foreach (var candidate in messageTypes)
		{
			ServerMessage? probe;
			try
			{
				probe = (ServerMessage?)Activator.CreateInstance(candidate);
			}
			catch (MissingMethodException)
			{
				continue;
			}
			catch (TargetInvocationException)
			{
				continue;
			}

			if (probe != null && probe.Kind == kind)
				return candidate;
		}

		return null;
	}
}
