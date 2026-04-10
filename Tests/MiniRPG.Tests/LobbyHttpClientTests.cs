using System;
using System.Linq;
using System.Threading.Tasks;
using MiniRPG.Core.Multiplayer;
using MiniRPG.Module.Network;
using Xunit;

namespace MiniRPG.Tests;

public sealed class LobbyHttpClientTests
{
	[Fact]
	public async Task LobbyHttpClient_RoundTripsBasicRoomApis()
	{
		var prefix = $"http://127.0.0.1:{PickAvailablePort()}/";
		var lobby = new InMemoryLobbyService();
		using var service = new LobbyHttpService(lobby, prefix);
		service.Start();

		try
		{
			using var client = new LobbyHttpClient(prefix);
			Assert.True(await client.CheckHealthAsync());

			var created = await client.CreateRoomAsync(new LobbyCreateRoomRequest
			{
				RoomDisplayName = "Alpha",
				OwnerDisplayName = "Owner",
				ServerEndpoint = "enet://127.0.0.1:2455",
				PrimaryActorId = "hero",
			});
			Assert.False(string.IsNullOrWhiteSpace(created.RoomId));
			Assert.False(string.IsNullOrWhiteSpace(created.RoomCode));

			var rooms = await client.ListRoomsAsync();
			Assert.Contains(rooms, room => room.RoomId == created.RoomId);

			var resolved = await client.ResolveRoomCodeAsync(created.RoomCode);
			Assert.Equal(created.RoomId, resolved.RoomId);

			var joined = await client.JoinRoomAsync(new LobbyJoinRoomRequest
			{
				RoomId = created.RoomId,
				DisplayName = "Guest",
				PrimaryActorId = "scout",
			});
			Assert.Equal(created.RoomId, joined.RoomId);
			Assert.Equal(2, joined.Room.Players.Count);
			Assert.Contains(joined.Room.Players.Values, player => string.Equals(player.DisplayName, "Guest", StringComparison.Ordinal));
		}
		finally
		{
			await service.StopAsync();
		}
	}

	private static int PickAvailablePort()
	{
		for (var i = 0; i < 20; i++)
		{
			var candidate = 25000 + Random.Shared.Next(0, 2000);
			if (candidate != 2455)
				return candidate;
		}

		return 25100;
	}
}
