using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using MiniRPG.Module;
using Xunit;

namespace MiniRPG.Tests;

public sealed class InputBindingServiceTests
{
	[Fact]
	public void Rebind_SwapsOccupiedGestureWithinSameContext()
	{
		using var harness = new Harness();

		var rebound = harness.Service.Rebind(
			InputBindingContext.Action,
			"move_south",
			slot: 0,
			InputGesture.FromKey(Key.W));

		Assert.True(rebound);
		Assert.Equal(Key.W, GetAction(InputBindingContext.Action, "move_south", harness.Service).Primary.Keycode);
		Assert.Equal(Key.S, GetAction(InputBindingContext.Action, "move_north", harness.Service).Primary.Keycode);
	}

	[Fact]
	public void Rebind_InvalidInputs_AreRejectedWithoutRaisingChanged()
	{
		using var harness = new Harness();
		var changedCount = 0;
		harness.Service.Changed += () => changedCount++;

		Assert.False(harness.Service.Rebind(InputBindingContext.Action, "move_north", -1, InputGesture.FromKey(Key.Z)));
		Assert.False(harness.Service.Rebind(InputBindingContext.Action, "move_north", 0, default));
		Assert.False(harness.Service.Rebind(InputBindingContext.Typing, "move_north", 0, InputGesture.FromKey(Key.Z)));
		Assert.False(harness.Service.Rebind(InputBindingContext.Action, "missing", 0, InputGesture.FromKey(Key.Z)));
		Assert.Equal(0, changedCount);
	}

	[Fact]
	public void ResetContextAndResetAll_RestoreDefaults()
	{
		using var harness = new Harness();

		Assert.True(harness.Service.Rebind(InputBindingContext.Action, "move_north", 0, InputGesture.FromKey(Key.Z)));
		Assert.True(harness.Service.Rebind(InputBindingContext.Typing, "typing_cancel", 0, InputGesture.FromKey(Key.X)));

		harness.Service.ResetContext(InputBindingContext.Action);
		Assert.Equal(Key.W, GetAction(InputBindingContext.Action, "move_north", harness.Service).Primary.Keycode);
		Assert.Equal(Key.X, GetAction(InputBindingContext.Typing, "typing_cancel", harness.Service).Primary.Keycode);

		harness.Service.ResetAll();
		Assert.Equal(Key.W, GetAction(InputBindingContext.Action, "move_north", harness.Service).Primary.Keycode);
		Assert.Equal(Key.Escape, GetAction(InputBindingContext.Typing, "typing_cancel", harness.Service).Primary.Keycode);
	}

	[Fact]
	public void LoadOrInitFromDisk_CorruptJsonFallsBackToDefaults()
	{
		using var harness = new Harness(initialFileContents: "{ invalid json");

		var moveNorth = GetAction(InputBindingContext.Action, "move_north", harness.Service);

		Assert.Equal(Key.W, moveNorth.Primary.Keycode);
		Assert.True(File.Exists(harness.Path));
	}

	[Fact]
	public void LoadOrInitFromDisk_BlankKindRowsUseLegacyKeyFallback()
	{
		var legacyJson = $$"""
		{
		  "version": 1,
		  "bindings": [
		    {
		      "context": "Action",
		      "actionId": "move_north",
		      "slot": 0,
		      "kind": "",
		      "keycode": {{(long)Key.Z}},
		      "mouseButton": 0,
		      "ctrl": false,
		      "alt": false,
		      "shift": true
		    }
		  ]
		}
		""";

		using var harness = new Harness(initialFileContents: legacyJson);

		var moveNorth = GetAction(InputBindingContext.Action, "move_north", harness.Service);
		Assert.Equal(Key.Z, moveNorth.Primary.Keycode);
		Assert.True(moveNorth.Primary.Shift);
	}

	[Fact]
	public void Rebind_MouseWheelPersistsAndReloads()
	{
		using var harness = new Harness();

		Assert.True(harness.Service.Rebind(
			InputBindingContext.Action,
			"skill_prev",
			slot: 0,
			InputGesture.FromMouseWheel(MouseButton.WheelDown, ctrl: true)));

		using (var document = JsonDocument.Parse(File.ReadAllText(harness.Path)))
		{
			var row = GetPropertyIgnoreCase(document.RootElement, "bindings")
				.EnumerateArray()
				.First(entry =>
					GetPropertyIgnoreCase(entry, "context").GetString() == "Action"
					&& GetPropertyIgnoreCase(entry, "actionId").GetString() == "skill_prev"
					&& GetPropertyIgnoreCase(entry, "slot").GetInt32() == 0);

			Assert.Equal("MouseWheel", GetPropertyIgnoreCase(row, "kind").GetString());
			Assert.Equal((int)MouseButton.WheelDown, GetPropertyIgnoreCase(row, "mouseButton").GetInt32());
			Assert.True(GetPropertyIgnoreCase(row, "ctrl").GetBoolean());
		}

		using var reloaded = new Harness(existingPath: harness.Path);
		var action = GetAction(InputBindingContext.Action, "skill_prev", reloaded.Service);
		Assert.Equal(InputGestureKind.MouseWheel, action.Primary.Kind);
		Assert.Equal(MouseButton.WheelDown, action.Primary.MouseButtonCode);
		Assert.True(action.Primary.Ctrl);
	}

	[Fact]
	public void Changed_FiresOnlyForSuccessfulMutations()
	{
		using var harness = new Harness();
		var changedCount = 0;
		harness.Service.Changed += () => changedCount++;

		Assert.True(harness.Service.Rebind(InputBindingContext.Action, "move_north", 0, InputGesture.FromKey(Key.Z)));
		Assert.False(harness.Service.Rebind(InputBindingContext.Action, "move_north", -1, InputGesture.FromKey(Key.X)));
		harness.Service.ResetContext(InputBindingContext.Action);
		harness.Service.ResetAll();

		Assert.Equal(3, changedCount);
	}

	private static BindingActionView GetAction(InputBindingContext context, string actionId, InputBindingService service) =>
		Assert.Single(service.GetActions(context).Where(action => action.Id == actionId));

	private static JsonElement GetPropertyIgnoreCase(JsonElement element, string propertyName)
	{
		foreach (var property in element.EnumerateObject())
		{
			if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
				return property.Value;
		}

		throw new Xunit.Sdk.XunitException($"Missing JSON property '{propertyName}'.");
	}

	private sealed class Harness : IDisposable
	{
		private readonly string? _ownedRoot;
		private readonly bool _deletePathOnDispose;

		public Harness(string? initialFileContents = null, string? existingPath = null)
		{
			TestSupport.EnsureGameplayDataLoaded();

			if (existingPath == null)
			{
				_ownedRoot = TestSupport.CreateTempDirectory("input-binding-service");
				Path = System.IO.Path.Combine(_ownedRoot, "keybindings.json");
				_deletePathOnDispose = false;
			}
			else
			{
				Path = existingPath;
				_deletePathOnDispose = false;
			}

			if (initialFileContents != null)
				File.WriteAllText(Path, initialFileContents);

			Service = new InputBindingService(Path);
		}

		public string Path { get; }
		public InputBindingService Service { get; }

		public void Dispose()
		{
			if (_deletePathOnDispose && File.Exists(Path))
			{
				try
				{
					File.Delete(Path);
				}
				catch
				{
					// Ignore cleanup failures in tests.
				}
			}

			if (_ownedRoot != null)
				TestSupport.TryDeleteDirectory(_ownedRoot);
		}
	}
}
