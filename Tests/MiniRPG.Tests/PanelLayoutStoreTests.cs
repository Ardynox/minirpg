using System;
using System.IO;
using System.Text.Json;
using Godot;
using MiniRPG.Module.Panel;
using Xunit;

namespace MiniRPG.Tests;

public sealed class PanelLayoutStoreTests : IDisposable
{
	private readonly string _root = TestSupport.CreateTempDirectory("panel-layout-store");

	[Fact]
	public void Load_MissingFile_LeavesStoreEmpty()
	{
		var store = new PanelLayoutStore(CreateStorePath());

		store.Load();

		Assert.False(store.TryGetPosition("status", out _));
		Assert.False(store.TryGetAppearance("status", out var appearance));
		Assert.Null(appearance.Width);
		Assert.Null(appearance.Height);
		Assert.Null(appearance.ButtonScale);
	}

	[Fact]
	public void Load_InvalidJson_FallsBackToEmpty()
	{
		var path = CreateStorePath();
		File.WriteAllText(path, "{ invalid json", System.Text.Encoding.UTF8);
		var store = new PanelLayoutStore(path);

		store.Load();

		Assert.False(store.TryGetPosition("status", out _));
		Assert.False(store.TryGetAppearance("status", out _));
	}

	[Fact]
	public void SetWidthSetHeightAndRemoveAppearance_CleansEntryBeforeSave()
	{
		var path = CreateStorePath();
		var store = new PanelLayoutStore(path);

		store.SetWidth("status", 320f);
		store.SetHeight("status", 180f);
		store.SetButtonScale("status", 1.25f);

		Assert.True(store.TryGetAppearance("status", out var beforeReset));
		Assert.Equal(320f, beforeReset.Width);
		Assert.Equal(180f, beforeReset.Height);
		Assert.Equal(1.25f, beforeReset.ButtonScale);

		store.RemoveAppearance("status");
		Assert.False(store.TryGetAppearance("status", out _));

		store.Save();

		AssertEmptyJsonObject(path);
	}

	[Fact]
	public void SetWidthAndHeight_WithNonPositiveValues_ClearFieldsAndCleanup()
	{
		var path = CreateStorePath();
		var store = new PanelLayoutStore(path);

		store.SetWidth("inventory", 480f);
		store.SetHeight("inventory", 260f);

		store.SetWidth("inventory", 0f);
		Assert.True(store.TryGetAppearance("inventory", out var widthCleared));
		Assert.Null(widthCleared.Width);
		Assert.Equal(260f, widthCleared.Height);
		Assert.Null(widthCleared.ButtonScale);

		store.SetHeight("inventory", -1f);
		Assert.False(store.TryGetAppearance("inventory", out _));

		store.Save();

		AssertEmptyJsonObject(path);
	}

	public void Dispose()
	{
		TestSupport.TryDeleteDirectory(_root);
	}

	private string CreateStorePath() =>
		Path.Combine(_root, $"panel-layout-{Guid.NewGuid():N}.json").Replace('\\', '/');

	private static void AssertEmptyJsonObject(string path)
	{
		Assert.True(File.Exists(path));
		using var document = JsonDocument.Parse(File.ReadAllText(path));
		Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
		Assert.Empty(document.RootElement.EnumerateObject());
	}
}
