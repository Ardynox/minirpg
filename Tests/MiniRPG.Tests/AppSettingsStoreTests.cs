using System;
using System.IO;
using System.Reflection;
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
	public void AudioVolumes_CanRoundTripAndClamp()
	{
		var originalMaster = AppSettingsStore.LoadMasterVolume();
		var originalMusic = AppSettingsStore.LoadMusicVolume();
		var originalSfx = AppSettingsStore.LoadSfxVolume();
		try
		{
			AppSettingsStore.SaveMasterVolume(0.25f);
			AppSettingsStore.SaveMusicVolume(0.5f);
			AppSettingsStore.SaveSfxVolume(0.75f);

			Assert.Equal(0.25f, AppSettingsStore.LoadMasterVolume(), 3);
			Assert.Equal(0.5f, AppSettingsStore.LoadMusicVolume(), 3);
			Assert.Equal(0.75f, AppSettingsStore.LoadSfxVolume(), 3);

			AppSettingsStore.SaveMasterVolume(-0.25f);
			AppSettingsStore.SaveMusicVolume(1.5f);
			AppSettingsStore.SaveSfxVolume(0.4f);

			Assert.Equal(0f, AppSettingsStore.LoadMasterVolume(), 3);
			Assert.Equal(1f, AppSettingsStore.LoadMusicVolume(), 3);
			Assert.Equal(0.4f, AppSettingsStore.LoadSfxVolume(), 3);
		}
		finally
		{
			AppSettingsStore.SaveMasterVolume(originalMaster);
			AppSettingsStore.SaveMusicVolume(originalMusic);
			AppSettingsStore.SaveSfxVolume(originalSfx);
		}
	}

	[Fact]
	public void AudioVolumes_DefaultToOne_WhenMissingFromSettingsFile()
	{
		var path = ResolveSettingsPath();
		var snapshot = CaptureSettingsFile(path);
		try
		{
			var directory = Path.GetDirectoryName(path);
			if (!string.IsNullOrWhiteSpace(directory))
				Directory.CreateDirectory(directory);

			File.WriteAllText(path, """
			{
			  "version": 8,
			  "locale": "en"
			}
			""");

			Assert.Equal(1f, AppSettingsStore.LoadMasterVolume(), 3);
			Assert.Equal(1f, AppSettingsStore.LoadMusicVolume(), 3);
			Assert.Equal(1f, AppSettingsStore.LoadSfxVolume(), 3);
		}
		finally
		{
			RestoreSettingsFile(path, snapshot);
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

	private static (bool Exists, string? Content) CaptureSettingsFile(string path) =>
		File.Exists(path)
			? (true, File.ReadAllText(path))
			: (false, null);

	private static void RestoreSettingsFile(string path, (bool Exists, string? Content) snapshot)
	{
		if (snapshot.Exists)
		{
			File.WriteAllText(path, snapshot.Content ?? string.Empty);
			return;
		}

		if (File.Exists(path))
			File.Delete(path);
	}

	private static string ResolveSettingsPath()
	{
		var method = typeof(AppSettingsStore).GetMethod("GetSettingsPath", BindingFlags.NonPublic | BindingFlags.Static);
		Assert.NotNull(method);
		return Assert.IsType<string>(method!.Invoke(null, null));
	}
}
