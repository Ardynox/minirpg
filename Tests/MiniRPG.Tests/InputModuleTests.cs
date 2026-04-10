using System;
using System.Collections.Generic;
using System.IO;
using MiniRPG.Module;
using Xunit;

namespace MiniRPG.Tests;

public sealed class InputModuleTests
{
	[Fact]
	public void HandleDirectionKeyCore_MapsFallbackUpAndDownCommands()
	{
		using var harness = new Harness();

		harness.Module.EnterDirectionMode("dig");

		Assert.True(harness.Module.HandleDirectionKeyCore(actionId: null, Godot.Key.U));
		Assert.Equal(InputFocus.Action, harness.Module.Focus);
		Assert.Equal(":dig_up", Assert.Single(harness.Commands));

		harness.Commands.Clear();
		harness.Module.EnterDirectionMode("dig");
		Assert.True(harness.Module.HandleDirectionKeyCore(actionId: null, Godot.Key.J));
		Assert.Equal(":dig_down", Assert.Single(harness.Commands));

		harness.Commands.Clear();
		harness.Module.EnterDirectionMode("climb");
		Assert.True(harness.Module.HandleDirectionKeyCore(actionId: null, Godot.Key.U));
		Assert.Equal(":climb_up", Assert.Single(harness.Commands));

		harness.Commands.Clear();
		harness.Module.EnterDirectionMode("climb");
		Assert.True(harness.Module.HandleDirectionKeyCore(actionId: null, Godot.Key.J));
		Assert.Equal(InputFocus.Action, harness.Module.Focus);
		Assert.Equal(":climb_down", Assert.Single(harness.Commands));
	}

	[Fact]
	public void HandleDirectionKeyCore_PreservesExistingDirectionalBindings()
	{
		using var harness = new Harness();

		harness.Module.EnterDirectionMode("dig");

		Assert.True(harness.Module.HandleDirectionKeyCore("direction_n", Godot.Key.W));
		Assert.Equal(":dig_n", Assert.Single(harness.Commands));
		Assert.Equal(InputFocus.Action, harness.Module.Focus);
	}

	[Fact]
	public void HandleDirectionKeyCore_CancelEmitsDirCancel()
	{
		using var harness = new Harness();

		harness.Module.EnterDirectionMode("dig");

		Assert.True(harness.Module.HandleDirectionKeyCore("direction_cancel", Godot.Key.Escape));
		Assert.Equal(":dir_cancel", Assert.Single(harness.Commands));
		Assert.Equal(InputFocus.Action, harness.Module.Focus);
	}

	[Fact]
	public void HandleDirectionKeyCore_IgnoresUnknownKeysWithoutChangingFocus()
	{
		using var harness = new Harness();

		harness.Module.EnterDirectionMode("dig");

		Assert.False(harness.Module.HandleDirectionKeyCore(actionId: null, Godot.Key.X));
		Assert.Empty(harness.Commands);
		Assert.Equal(InputFocus.Direction, harness.Module.Focus);
	}

	private sealed class Harness : IDisposable
	{
		private readonly string _root = TestSupport.CreateTempDirectory("input-module");

		public Harness()
		{
			TestSupport.EnsureGameplayDataLoaded();
			Bindings = new InputBindingService(Path.Combine(_root, "keybindings.json"));
			Module = new InputModule(Bindings);
			Module.CommandReceived += command => Commands.Add(command);
		}

		public InputBindingService Bindings { get; }
		public InputModule Module { get; }
		public List<string> Commands { get; } = [];

		public void Dispose()
		{
			TestSupport.TryDeleteDirectory(_root);
		}
	}
}
