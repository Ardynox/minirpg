using System;
using System.Threading;
using System.Threading.Tasks;

namespace MiniRPG.Core.Multiplayer;

public interface ILocalServerLauncher : IAsyncDisposable
{
	bool IsOwnedProcessRunning { get; }
	Task<LocalServerLaunchResult> EnsureRunningAsync(LocalServerLaunchOptions options, CancellationToken cancellationToken = default);
}

public sealed class LocalServerLaunchOptions
{
	public string? ExecutablePath { get; init; }
	public string LobbyBaseUrl { get; init; } = "http://127.0.0.1:5076/";
	public TimeSpan HealthCheckTimeout { get; init; } = TimeSpan.FromSeconds(12);
	public TimeSpan HealthCheckInterval { get; init; } = TimeSpan.FromMilliseconds(350);
}

public sealed class LocalServerLaunchResult
{
	public bool Success { get; init; }
	public string LobbyBaseUrl { get; init; } = string.Empty;
	public string? ExecutablePath { get; init; }
	public string? FailureReason { get; init; }

	public static LocalServerLaunchResult Ok(string lobbyBaseUrl, string? executablePath = null) => new()
	{
		Success = true,
		LobbyBaseUrl = lobbyBaseUrl,
		ExecutablePath = executablePath,
	};

	public static LocalServerLaunchResult Fail(string reason) => new()
	{
		Success = false,
		FailureReason = reason,
	};
}
