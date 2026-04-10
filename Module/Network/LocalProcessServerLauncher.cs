using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MiniRPG.Core.Multiplayer;

namespace MiniRPG.Module.Network;

public sealed class LocalProcessServerLauncher : ILocalServerLauncher
{
	private readonly string _projectRootPath;

	private Process? _ownedProcess;
	private string? _ownedExecutablePath;

	public LocalProcessServerLauncher(string projectRootPath)
	{
		_projectRootPath = Path.GetFullPath(projectRootPath ?? throw new ArgumentNullException(nameof(projectRootPath)));
	}

	public bool IsOwnedProcessRunning => _ownedProcess is { HasExited: false };

	public async Task<LocalServerLaunchResult> EnsureRunningAsync(
		LocalServerLaunchOptions options,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(options);

		if (IsOwnedProcessRunning)
		{
			if (await CheckHealthAsync(options, cancellationToken).ConfigureAwait(false))
			{
				return LocalServerLaunchResult.Ok(
					options.LobbyBaseUrl,
					_ownedExecutablePath);
			}

			TerminateOwnedProcess();
		}

		var executablePath = ResolveExecutablePath(options.ExecutablePath);
		if (executablePath == null)
		{
			return LocalServerLaunchResult.Fail(
				"MiniRPG.Server executable was not found. Configure the path in multiplayer settings or build the server project first.");
		}

		try
		{
			_ownedProcess = StartProcess(executablePath, options);
			_ownedExecutablePath = executablePath;
		}
		catch (Exception ex)
		{
			TerminateOwnedProcess();
			return LocalServerLaunchResult.Fail($"Failed to start local server: {ex.Message}");
		}

		var deadline = DateTimeOffset.UtcNow + options.HealthCheckTimeout;
		while (DateTimeOffset.UtcNow < deadline)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (_ownedProcess == null || _ownedProcess.HasExited)
			{
				var exitCode = _ownedProcess?.ExitCode;
				TerminateOwnedProcess();
				return LocalServerLaunchResult.Fail(
					exitCode.HasValue
						? $"Local server exited before health check completed (exit code {exitCode.Value})."
						: "Local server exited before health check completed.");
			}

			if (await CheckHealthAsync(options, cancellationToken).ConfigureAwait(false))
			{
				return LocalServerLaunchResult.Ok(options.LobbyBaseUrl, executablePath);
			}

			await Task.Delay(options.HealthCheckInterval, cancellationToken).ConfigureAwait(false);
		}

		TerminateOwnedProcess();
		return LocalServerLaunchResult.Fail("Local server health check timed out.");
	}

	public ValueTask DisposeAsync()
	{
		TerminateOwnedProcess();
		return ValueTask.CompletedTask;
	}

	private async Task<bool> CheckHealthAsync(LocalServerLaunchOptions options, CancellationToken cancellationToken)
	{
		try
		{
			using var client = new LobbyHttpClient(options.LobbyBaseUrl);
			return await client.CheckHealthAsync(cancellationToken).ConfigureAwait(false);
		}
		catch
		{
			return false;
		}
	}

	private string? ResolveExecutablePath(string? configuredPath)
	{
		if (TryResolveExistingFile(configuredPath, out var explicitPath))
			return explicitPath;

		var developmentCandidates = new[]
		{
			Path.Combine(_projectRootPath, "MiniRPG.Server", "bin", "Debug", "net8.0", "MiniRPG.Server.exe"),
			Path.Combine(_projectRootPath, "MiniRPG.Server", "bin", "Debug", "net8.0", "MiniRPG.Server.dll"),
		};
		foreach (var candidate in developmentCandidates)
		{
			if (TryResolveExistingFile(candidate, out var path))
				return path;
		}

		var runtimeDirectory = AppContext.BaseDirectory;
		var packagedCandidates = new[]
		{
			Path.Combine(runtimeDirectory, "MiniRPG.Server.exe"),
			Path.Combine(runtimeDirectory, "MiniRPG.Server.dll"),
		};
		foreach (var candidate in packagedCandidates)
		{
			if (TryResolveExistingFile(candidate, out var path))
				return path;
		}

		return null;
	}

	private static bool TryResolveExistingFile(string? rawPath, out string fullPath)
	{
		if (string.IsNullOrWhiteSpace(rawPath))
		{
			fullPath = string.Empty;
			return false;
		}

		fullPath = Path.GetFullPath(rawPath.Trim());
		return File.Exists(fullPath);
	}

	private static Process StartProcess(string executablePath, LocalServerLaunchOptions options)
	{
		var startInfo = BuildStartInfo(executablePath, options);
		return Process.Start(startInfo)
			?? throw new InvalidOperationException("Process.Start returned null.");
	}

	private static ProcessStartInfo BuildStartInfo(string executablePath, LocalServerLaunchOptions options)
	{
		var arguments = BuildArguments(options);
		if (string.Equals(Path.GetExtension(executablePath), ".dll", StringComparison.OrdinalIgnoreCase))
		{
			return new ProcessStartInfo
			{
				FileName = "dotnet",
				Arguments = $"\"{executablePath}\" {arguments}",
				UseShellExecute = false,
				CreateNoWindow = true,
				WorkingDirectory = Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory,
			};
		}

		return new ProcessStartInfo
		{
			FileName = executablePath,
			Arguments = arguments,
			UseShellExecute = false,
			CreateNoWindow = true,
			WorkingDirectory = Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory,
		};
	}

	private static string BuildArguments(LocalServerLaunchOptions options) =>
		$"--lobby-prefix \"{options.LobbyBaseUrl}\" --game-address \"{options.GameAddress}\" --game-port {options.GamePort}";

	private void TerminateOwnedProcess()
	{
		if (_ownedProcess != null)
		{
			try
			{
				if (!_ownedProcess.HasExited)
					_ownedProcess.Kill(entireProcessTree: true);
			}
			catch
			{
				// Ignore process teardown failures during shutdown.
			}
			finally
			{
				_ownedProcess.Dispose();
				_ownedProcess = null;
			}
		}

		_ownedExecutablePath = null;
	}
}
