using System.Collections;
using System.Reflection;
using System.Text.Json;
using MiniRPG.Module.Panel;
using Xunit;

namespace MiniRPG.Tests;

public sealed class PanelLayoutStoreTests
{
	[Fact]
	public void LoadFromRawJson_MissingContentLeavesStoreEmpty()
	{
		var store = new PanelLayoutStore();
		store.SetWidth("status", 320f);

		var loaded = InvokeLoadFromRawJson(store, null);

		Assert.True(loaded);
		Assert.False(store.TryGetPosition("status", out _));
		Assert.False(store.TryGetAppearance("status", out var appearance));
		Assert.Null(appearance.Width);
		Assert.Null(appearance.Height);
		Assert.Null(appearance.ButtonScale);
	}

	[Fact]
	public void LoadFromRawJson_InvalidJsonFallsBackToEmpty()
	{
		var store = new PanelLayoutStore();
		store.SetHeight("status", 180f);

		var loaded = InvokeLoadFromRawJson(store, "{ invalid json");

		Assert.False(loaded);
		Assert.False(store.TryGetPosition("status", out _));
		Assert.False(store.TryGetAppearance("status", out _));
		Assert.Empty(GetEntries(store));
	}

	[Fact]
	public void SetWidthSetHeightAndRemoveAppearance_CleansEntry()
	{
		var store = new PanelLayoutStore();

		store.SetWidth("status", 320f);
		store.SetHeight("status", 180f);
		store.SetButtonScale("status", 1.25f);

		Assert.True(store.TryGetAppearance("status", out var beforeReset));
		Assert.Equal(320f, beforeReset.Width);
		Assert.Equal(180f, beforeReset.Height);
		Assert.Equal(1.25f, beforeReset.ButtonScale);

		store.RemoveAppearance("status");

		Assert.False(store.TryGetAppearance("status", out _));
		Assert.Empty(GetEntries(store));
	}

	[Fact]
	public void SerializeEntries_IgnoresNullFields()
	{
		var store = new PanelLayoutStore();

		store.SetWidth("inventory", 480f);
		store.SetHeight("inventory", 260f);
		store.SetButtonScale("inventory", 1.25f);
		store.SetWidth("inventory", 0f);

		var json = InvokeSerializeEntries(GetEntries(store));
		using var document = JsonDocument.Parse(json);
		var entry = document.RootElement.GetProperty("inventory");

		Assert.False(entry.TryGetProperty("width", out _));
		Assert.Equal(260f, entry.GetProperty("height").GetSingle());
		Assert.Equal(1.25f, entry.GetProperty("button_scale").GetSingle());
	}

	private static bool InvokeLoadFromRawJson(PanelLayoutStore store, string? raw)
	{
		var method = typeof(PanelLayoutStore).GetMethod("LoadFromRawJson", BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(method);
		return (bool)method!.Invoke(store, [raw])!;
	}

	private static IDictionary GetEntries(PanelLayoutStore store)
	{
		var field = typeof(PanelLayoutStore).GetField("_entries", BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(field);
		return Assert.IsAssignableFrom<IDictionary>(field!.GetValue(store));
	}

	private static string InvokeSerializeEntries(IDictionary entries)
	{
		var method = typeof(PanelLayoutStore).GetMethod("SerializeEntries", BindingFlags.Static | BindingFlags.NonPublic);
		Assert.NotNull(method);
		return (string)method!.Invoke(null, [entries])!;
	}
}
