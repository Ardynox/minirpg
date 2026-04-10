using System;
using System.Threading;
using System.Threading.Tasks;
using MiniRPG.Core.Multiplayer;

namespace MiniRPG.Module.Network;

public sealed class NoopLocalServerLauncher : ILocalServerLauncher
{
	public bool IsOwnedProcessRunning => false;

	public ValueTask DisposeAsync() => ValueTask.CompletedTask;

	public Task<LocalServerLaunchResult> EnsureRunningAsync(LocalServerLaunchOptions options, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(options);
		return Task.FromResult(LocalServerLaunchResult.Fail("Local server launcher is not configured yet."));
	}
}
