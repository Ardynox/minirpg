using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using MiniRPG.Core.Multiplayer;
using MiniRPG.Module.Network;
using Xunit;

namespace MiniRPG.Tests;

public sealed class LocalProcessServerLauncherTests
{
	[Fact]
	public async Task EnsureRunningAsync_WhenExecutableMissing_ReturnsFailure()
	{
		var root = TestSupport.CreateTempDirectory("local-server-launcher-missing");
		try
		{
			await using var launcher = new LocalProcessServerLauncher(root);

			var result = await launcher.EnsureRunningAsync(new LocalServerLaunchOptions
			{
				ExecutablePath = Path.Combine(root, "missing", "MiniRPG.Server.exe"),
				HealthCheckTimeout = TimeSpan.FromMilliseconds(50),
				HealthCheckInterval = TimeSpan.FromMilliseconds(10),
			});

			Assert.False(result.Success);
			Assert.False(launcher.IsOwnedProcessRunning);
			Assert.Contains("MiniRPG.Server executable was not found", result.FailureReason, StringComparison.Ordinal);
		}
		finally
		{
			TestSupport.TryDeleteDirectory(root);
		}
	}

	[Fact]
	public void ResolveExecutablePath_PrefersConfiguredPathOverDevelopmentFallback()
	{
		var root = TestSupport.CreateTempDirectory("local-server-launcher-resolve");
		try
		{
			var configuredPath = Path.Combine(root, "custom", "MiniRPG.Server.exe");
			Directory.CreateDirectory(Path.GetDirectoryName(configuredPath)!);
			File.WriteAllText(configuredPath, "configured");

			var developmentPath = Path.Combine(root, "MiniRPG.Server", "bin", "Debug", "net8.0", "MiniRPG.Server.exe");
			Directory.CreateDirectory(Path.GetDirectoryName(developmentPath)!);
			File.WriteAllText(developmentPath, "development");

			var launcher = new LocalProcessServerLauncher(root);
			var resolved = (string?)InvokeNonPublic(
				launcher,
				"ResolveExecutablePath",
				configuredPath);

			Assert.Equal(Path.GetFullPath(configuredPath), resolved);
		}
		finally
		{
			TestSupport.TryDeleteDirectory(root);
		}
	}

	[Fact]
	public void Constructor_WhenProjectRootPathEmpty_FallsBackToAppContextBaseDirectory()
	{
		var launcher = new LocalProcessServerLauncher(string.Empty);
		var projectRootPath = Assert.IsType<string>(GetPrivateField(launcher, "_projectRootPath"));
		Assert.Equal(Path.GetFullPath(AppContext.BaseDirectory), projectRootPath);
	}

	[Fact]
	public void ResolveExecutablePath_FindsPackagedServerInLanPackageLayout()
	{
		var root = TestSupport.CreateTempDirectory("local-server-launcher-packaged");
		try
		{
			var runtimeDirectory = Path.Combine(root, "Client", "Debug");
			Directory.CreateDirectory(runtimeDirectory);

			var packagedPath = Path.Combine(root, "Server", "MiniRPG.Server.exe");
			Directory.CreateDirectory(Path.GetDirectoryName(packagedPath)!);
			File.WriteAllText(packagedPath, "packaged");

			var launcher = CreateLauncher(root, runtimeDirectory);
			var resolved = (string?)InvokeNonPublic(
				launcher,
				"ResolveExecutablePath",
				new object?[] { null });

			Assert.Equal(Path.GetFullPath(packagedPath), resolved);
		}
		finally
		{
			TestSupport.TryDeleteDirectory(root);
		}
	}

	[Fact]
	public void BuildStartInfo_UsesDotnetForDllAndDirectExecForExe()
	{
		var options = new LocalServerLaunchOptions
		{
			LobbyBaseUrl = "http://127.0.0.1:5076/",
			GameAddress = "127.0.0.1",
			GamePort = 2455,
		};

		var dllStartInfo = Assert.IsType<ProcessStartInfo>(InvokeStaticNonPublic(
			typeof(LocalProcessServerLauncher),
			"BuildStartInfo",
			@"D:\Tools\MiniRPG.Server.dll",
			options));
		var exeStartInfo = Assert.IsType<ProcessStartInfo>(InvokeStaticNonPublic(
			typeof(LocalProcessServerLauncher),
			"BuildStartInfo",
			@"D:\Tools\MiniRPG.Server.exe",
			options));

		Assert.Equal("dotnet", dllStartInfo.FileName);
		Assert.Contains("MiniRPG.Server.dll", dllStartInfo.Arguments, StringComparison.Ordinal);
		Assert.Contains("--lobby-prefix", dllStartInfo.Arguments, StringComparison.Ordinal);

		Assert.Equal(@"D:\Tools\MiniRPG.Server.exe", exeStartInfo.FileName);
		Assert.DoesNotContain("MiniRPG.Server.dll", exeStartInfo.Arguments, StringComparison.Ordinal);
		Assert.Contains("--game-port 2455", exeStartInfo.Arguments, StringComparison.Ordinal);
	}

	private static object? GetPrivateField(object instance, string fieldName)
	{
		var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(field);
		return field!.GetValue(instance);
	}

	private static object? InvokeNonPublic(object instance, string methodName, params object?[] arguments)
	{
		var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(method);
		return method!.Invoke(instance, arguments);
	}

	private static object? InvokeStaticNonPublic(Type declaringType, string methodName, params object?[] arguments)
	{
		var method = declaringType.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
		Assert.NotNull(method);
		return method!.Invoke(null, arguments);
	}

	private static LocalProcessServerLauncher CreateLauncher(string? projectRootPath, string? runtimeDirectory)
	{
		var constructor = typeof(LocalProcessServerLauncher).GetConstructor(
			BindingFlags.Instance | BindingFlags.NonPublic,
			binder: null,
			new[] { typeof(string), typeof(string) },
			modifiers: null);
		Assert.NotNull(constructor);
		return Assert.IsType<LocalProcessServerLauncher>(constructor!.Invoke(new object?[] { projectRootPath, runtimeDirectory }));
	}
}
