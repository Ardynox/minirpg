using MiniRPG.Core.Dialog;
using Xunit;

namespace MiniRPG.Tests;

/// <summary>
/// DialogPool tests. Load() depends on GameDataLocator + Godot file system,
/// so we test the public API after loading via TestSupport.
/// </summary>
public sealed class DialogPoolTests
{
	public DialogPoolTests()
	{
		TestSupport.EnsureGameplayDataLoaded();
		DialogPool.Load();
	}

	[Fact]
	public void GetAll_ReturnsNonEmpty()
	{
		var all = DialogPool.GetAll();
		Assert.NotNull(all);
		Assert.NotEmpty(all);
	}

	[Fact]
	public void GetGreetCandidates_ReturnsEntries()
	{
		var greets = DialogPool.GetGreetCandidates();
		Assert.NotNull(greets);
		// Should have at least some greet entries
		Assert.NotEmpty(greets);
	}

	[Fact]
	public void GetByCategory_Greet_MatchesGetGreetCandidates()
	{
		var greets = DialogPool.GetGreetCandidates();
		var byCategory = DialogPool.GetByCategory("greet");
		Assert.Equal(greets.Count, byCategory.Count);
	}

	[Fact]
	public void GetByCategory_Unknown_ReturnsEmpty()
	{
		var result = DialogPool.GetByCategory("nonexistent_category_xyz");
		Assert.Empty(result);
	}

	[Fact]
	public void FindById_ExistingEntry_ReturnsEntry()
	{
		var all = DialogPool.GetAll();
		// Find first entry with a non-empty ID
		var withId = all.Find(e => !string.IsNullOrEmpty(e.Id));
		if (withId == null) return; // Skip if no entries have IDs

		var found = DialogPool.FindById(withId.Id);
		Assert.NotNull(found);
		Assert.Equal(withId.Id, found!.Id);
	}

	[Fact]
	public void FindById_NonExistent_ReturnsNull()
	{
		Assert.Null(DialogPool.FindById("nonexistent_dialog_id_xyz"));
	}

	[Fact]
	public void GetAll_EntriesHaveTemplates()
	{
		var all = DialogPool.GetAll();
		// At least some entries should have non-empty templates
		Assert.Contains(all, e => !string.IsNullOrWhiteSpace(e.Template));
	}

	[Fact]
	public void GetAll_Idempotent()
	{
		var a = DialogPool.GetAll();
		var b = DialogPool.GetAll();
		Assert.Equal(a.Count, b.Count);
	}
}
