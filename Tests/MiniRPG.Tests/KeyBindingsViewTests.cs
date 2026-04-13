using System;
using System.IO;
using System.Linq;
using Godot;
using MiniRPG.Core.Config;
using MiniRPG.Module;
using Xunit;

namespace MiniRPG.Tests;

public sealed class KeyBindingsViewTests
{
	[Fact]
	public void HandleCommand_SwitchesContext_AndStartsCapture()
	{
		using var fixture = CreateFixture();

		Assert.Equal(InputBindingContext.Action, fixture.Controller.CurrentContext);

		Assert.True(fixture.Controller.HandleCommand("right"));
		Assert.Equal(InputBindingContext.Typing, fixture.Controller.CurrentContext);

		Assert.True(fixture.Controller.HandleCommand("confirm"));
		Assert.True(fixture.Controller.IsCapturing);
		Assert.Equal(0, fixture.Controller.CaptureSlot);
	}

	[Fact]
	public void HandleCommand_CloseWhileCapturing_CancelsCapture()
	{
		using var fixture = CreateFixture();
		fixture.Controller.HandleCommand("confirm");

		Assert.True(fixture.Controller.IsCapturing);
		Assert.True(fixture.Controller.HandleCommand("close"));
		Assert.False(fixture.Controller.IsCapturing);
		Assert.False(fixture.Controller.HandleCommand("close"));
	}

	[Fact]
	public void ResetCommands_RestoreCurrentContextAndAllContexts()
	{
		using var fixture = CreateFixture();

		fixture.Controller.HandleCommand("confirm");
		fixture.Controller.HandleKey(Key.Z);
		Assert.Equal(Key.Z, GetPrimaryKey(fixture.Bindings, InputBindingContext.Action, "move_north"));

		fixture.Controller.HandleCommand("action3");
		Assert.Equal(Key.W, GetPrimaryKey(fixture.Bindings, InputBindingContext.Action, "move_north"));

		fixture.Controller.HandleCommand("right");
		fixture.Controller.HandleCommand("confirm");
		fixture.Controller.HandleKey(Key.X);
		Assert.Equal(Key.X, GetPrimaryKey(fixture.Bindings, InputBindingContext.Typing, "typing_cancel"));

		fixture.Controller.HandleCommand("action4");
		Assert.Equal(Key.W, GetPrimaryKey(fixture.Bindings, InputBindingContext.Action, "move_north"));
		Assert.Equal(Key.Escape, GetPrimaryKey(fixture.Bindings, InputBindingContext.Typing, "typing_cancel"));
	}

	[Fact]
	public void ActionBindings_ReserveRForRuntimeRotation_AndKeepTypingShortcut()
	{
		using var fixture = CreateFixture();

		var toggleRender = fixture.Bindings.GetActions(InputBindingContext.Action)
			.Single(action => action.Id == "toggle_render");
		var debugPanel = fixture.Bindings.GetActions(InputBindingContext.Action)
			.Single(action => action.Id == "debug_panel");
		var openTyping = fixture.Bindings.GetActions(InputBindingContext.Action)
			.Single(action => action.Id == "open_typing");

		Assert.Equal(":render", toggleRender.Command);
		Assert.Equal(Key.R, toggleRender.Primary.Keycode);
		Assert.True(toggleRender.Primary.Ctrl);
		Assert.Equal(":debug_panel", debugPanel.Command);
		Assert.Equal(Key.F12, debugPanel.Primary.Keycode);
		Assert.Equal(":typing", openTyping.Command);
		Assert.Equal(Key.Enter, openTyping.Primary.Keycode);
		Assert.Equal(Key.T, openTyping.Secondary.Keycode);
	}

	private static TestFixture CreateFixture()
	{
		LocalizationService.Initialize();
		LocalizationService.SetLocale("en", notify: false);

		var path = Path.Combine(Path.GetTempPath(), $"minirpg-keybindings-{Guid.NewGuid():N}.json");
		var bindings = new InputBindingService(path);
		var controller = new KeyBindingsController(bindings);
		return new TestFixture(controller, bindings, path);
	}

	private static Key GetPrimaryKey(InputBindingService bindings, InputBindingContext context, string actionId)
	{
		foreach (var action in bindings.GetActions(context))
		{
			if (action.Id == actionId)
				return action.Primary.Keycode;
		}

		throw new Xunit.Sdk.XunitException($"Missing action '{actionId}' in context '{context}'.");
	}

	private static void TryDelete(string path)
	{
		try
		{
			if (File.Exists(path))
				File.Delete(path);
		}
		catch
		{
			// Ignore cleanup failures for tests.
		}
	}

	private sealed class TestFixture(
		KeyBindingsController controller,
		InputBindingService bindings,
		string path) : IDisposable
	{
		public KeyBindingsController Controller { get; } = controller;
		public InputBindingService Bindings { get; } = bindings;

		public void Dispose() => TryDelete(path);
	}
}
