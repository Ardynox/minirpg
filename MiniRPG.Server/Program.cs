using System;
using System.Threading;
using MiniRPG.Core.Multiplayer;
using MiniRPG.Module.Network;

namespace MiniRPG.Server;

internal static class Program
{
	public static int Main(string[] args)
	{
		var options = ServerProgramOptions.Parse(args);
		var hostOptions = new DedicatedGameServerHostOptions
		{
			ServerEndpoint = $"enet://{options.GameAddress}:{options.GamePort}",
		};
		var gameHost = new DedicatedGameServerHost(hostOptions);
		var lobbyService = new HostedLobbyService(new InMemoryLobbyService(), gameHost);
		hostOptions.LobbyService = lobbyService;

		using var gameServer = new ENetGameServer(gameHost);
		var listenError = gameServer.Listen(options.GameAddress, options.GamePort, options.MaxClients);
		if (listenError != Godot.Error.Ok)
		{
			Console.Error.WriteLine($"Failed to start ENet listener: {listenError}");
			return 1;
		}

		using var lobbyHttp = new LobbyHttpService(lobbyService, options.LobbyPrefix);
		using var cts = new CancellationTokenSource();
		Console.CancelKeyPress += (_, eventArgs) =>
		{
			eventArgs.Cancel = true;
			cts.Cancel();
		};

		lobbyHttp.Start(cts.Token);
		Console.WriteLine($"Lobby listening on {options.LobbyPrefix}");
		Console.WriteLine($"Game server listening on enet://{options.GameAddress}:{options.GamePort}");
		Console.WriteLine("Press Ctrl+C to stop.");

		try
		{
			while (!cts.IsCancellationRequested)
			{
				gameServer.Poll();
				Thread.Sleep(1);
			}
		}
		finally
		{
			lobbyHttp.StopAsync().GetAwaiter().GetResult();
		}

		return 0;
	}
}

internal sealed class ServerProgramOptions
{
	public string LobbyPrefix { get; init; } = "http://127.0.0.1:5076/";
	public string GameAddress { get; init; } = "127.0.0.1";
	public int GamePort { get; init; } = 2455;
	public int MaxClients { get; init; } = 32;

	public static ServerProgramOptions Parse(string[] args)
	{
		var lobbyPrefix = "http://127.0.0.1:5076/";
		var gameAddress = "127.0.0.1";
		var gamePort = 2455;
		var maxClients = 32;

		for (var i = 0; i < args.Length; i++)
		{
			var arg = args[i];
			if (!arg.StartsWith("--", StringComparison.Ordinal))
				continue;
			if (i + 1 >= args.Length)
				throw new ArgumentException($"Missing value for '{arg}'.", nameof(args));

			var value = args[++i];
			switch (arg)
			{
				case "--lobby-prefix":
					lobbyPrefix = value;
					if (!lobbyPrefix.EndsWith("/", StringComparison.Ordinal))
						lobbyPrefix = $"{lobbyPrefix}/";
					break;
				case "--game-address":
					gameAddress = value;
					break;
				case "--game-port":
					gamePort = int.Parse(value);
					break;
				case "--max-clients":
					maxClients = int.Parse(value);
					break;
			}
		}

		return new ServerProgramOptions
		{
			LobbyPrefix = lobbyPrefix,
			GameAddress = gameAddress,
			GamePort = gamePort,
			MaxClients = maxClients,
		};
	}
}
