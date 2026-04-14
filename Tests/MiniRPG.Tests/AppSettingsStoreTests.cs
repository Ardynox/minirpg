using System;
using MiniRPG.Core.Config;
using Xunit;

namespace MiniRPG.Tests;

public sealed class AppSettingsStoreTests
{
	[Fact]
	public void EnableDebugPanel_CanRoundTripThroughSettingsStore()
	{
		var original = AppSettingsStore.LoadEnableDebugPanel();
		try
		{
			AppSettingsStore.SaveEnableDebugPanel(false);
			Assert.False(AppSettingsStore.LoadEnableDebugPanel());

			AppSettingsStore.SaveEnableDebugPanel(true);
			Assert.True(AppSettingsStore.LoadEnableDebugPanel());
		}
		finally
		{
			AppSettingsStore.SaveEnableDebugPanel(original);
		}
	}

	[Fact]
	public void MapZoomRange_CanRoundTripAndKeepsOrder()
	{
		var originalMin = AppSettingsStore.LoadMapZoomMin();
		var originalMax = AppSettingsStore.LoadMapZoomMax();
		try
		{
			AppSettingsStore.SaveMapZoomMin(0.5f);
			AppSettingsStore.SaveMapZoomMax(3.1f);
			Assert.Equal(0.5f, AppSettingsStore.LoadMapZoomMin(), 3);
			Assert.Equal(3.1f, AppSettingsStore.LoadMapZoomMax(), 3);

			AppSettingsStore.SaveMapZoomMin(3.5f);
			Assert.True(AppSettingsStore.LoadMapZoomMin() <= AppSettingsStore.LoadMapZoomMax());

			AppSettingsStore.SaveMapZoomMax(0.4f);
			Assert.True(AppSettingsStore.LoadMapZoomMin() <= AppSettingsStore.LoadMapZoomMax());
		}
		finally
		{
			AppSettingsStore.SaveMapZoomMin(originalMin);
			AppSettingsStore.SaveMapZoomMax(originalMax);
		}
	}

	[Fact]
	public void FastTurnMode_CanRoundTripThroughSettingsStore()
	{
		var original = AppSettingsStore.LoadFastTurnMode();
		try
		{
			AppSettingsStore.SaveFastTurnMode(false);
			Assert.False(AppSettingsStore.LoadFastTurnMode());

			AppSettingsStore.SaveFastTurnMode(true);
			Assert.True(AppSettingsStore.LoadFastTurnMode());
		}
		finally
		{
			AppSettingsStore.SaveFastTurnMode(original);
		}
	}

	[Fact]
	public void AutoNavigationInterruptPolicy_CanRoundTripThroughSettingsStore()
	{
		var original = AppSettingsStore.LoadAutoNavigationInterruptPolicy();
		try
		{
			AppSettingsStore.SaveAutoNavigationInterruptPolicy(AutoNavigationInterruptPolicy.ManualOnly);
			Assert.Equal(AutoNavigationInterruptPolicy.ManualOnly, AppSettingsStore.LoadAutoNavigationInterruptPolicy());

			AppSettingsStore.SaveAutoNavigationInterruptPolicy(AutoNavigationInterruptPolicy.HostileProximityStop);
			Assert.Equal(AutoNavigationInterruptPolicy.HostileProximityStop, AppSettingsStore.LoadAutoNavigationInterruptPolicy());
		}
		finally
		{
			AppSettingsStore.SaveAutoNavigationInterruptPolicy(original);
		}
	}

	[Fact]
	public void MultiplayerSettings_CanRoundTripIncludingReconnectTicket()
	{
		var original = AppSettingsStore.LoadMultiplayerSettings();
		var deadline = new DateTimeOffset(2026, 4, 10, 18, 30, 0, TimeSpan.Zero);
		try
		{
			AppSettingsStore.SaveMultiplayerSettings(new MultiplayerSettings
			{
				DisplayName = "Guest",
				LobbyBaseUrl = "http://lobby.example",
				PreferredHostMode = MultiplayerHostMode.Remote,
				LocalServerExecutablePath = @"D:\Servers\MiniRPG.Server.exe",
				LocalLobbyPrefix = "http://127.0.0.1:6001",
				LocalGameAddress = "192.168.0.25",
				LocalGamePort = 3555,
				LastReconnectTicket = new MultiplayerReconnectTicket
				{
					LobbyBaseUrl = "http://lobby.example",
					RoomId = "room-alpha",
					RoomCode = "AB12CD",
					RoomDisplayName = "Alpha",
					ServerEndpoint = "enet://game.example:3555",
					PlayerSessionId = "player-guest",
					PrimaryActorId = "scout",
					ReconnectToken = "token-guest",
					DisplayName = "Guest",
					ReconnectDeadlineUtc = deadline,
				},
			});

			var loaded = AppSettingsStore.LoadMultiplayerSettings();

			Assert.Equal("Guest", loaded.DisplayName);
			Assert.Equal("http://lobby.example/", loaded.LobbyBaseUrl);
			Assert.Equal(MultiplayerHostMode.Remote, loaded.PreferredHostMode);
			Assert.Equal(@"D:\Servers\MiniRPG.Server.exe", loaded.LocalServerExecutablePath);
			Assert.Equal("http://127.0.0.1:6001/", loaded.LocalLobbyPrefix);
			Assert.Equal("192.168.0.25", loaded.LocalGameAddress);
			Assert.Equal(3555, loaded.LocalGamePort);
			Assert.NotNull(loaded.LastReconnectTicket);
			Assert.Equal("http://lobby.example/", loaded.LastReconnectTicket!.LobbyBaseUrl);
			Assert.Equal("room-alpha", loaded.LastReconnectTicket.RoomId);
			Assert.Equal("AB12CD", loaded.LastReconnectTicket.RoomCode);
			Assert.Equal("Alpha", loaded.LastReconnectTicket.RoomDisplayName);
			Assert.Equal("enet://game.example:3555", loaded.LastReconnectTicket.ServerEndpoint);
			Assert.Equal("player-guest", loaded.LastReconnectTicket.PlayerSessionId);
			Assert.Equal("scout", loaded.LastReconnectTicket.PrimaryActorId);
			Assert.Equal("token-guest", loaded.LastReconnectTicket.ReconnectToken);
			Assert.Equal("Guest", loaded.LastReconnectTicket.DisplayName);
			Assert.Equal(deadline, loaded.LastReconnectTicket.ReconnectDeadlineUtc);
		}
		finally
		{
			AppSettingsStore.SaveMultiplayerSettings(original);
		}
	}
}
