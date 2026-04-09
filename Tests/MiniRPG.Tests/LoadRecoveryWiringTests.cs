using System;
using System.IO;
using Xunit;

namespace MiniRPG.Tests;

public sealed class LoadRecoveryWiringTests
{
	[Fact]
	public void MainScene_ContainsLoadRecoveryDialog()
	{
		var text = File.ReadAllText(GetRepoPath("App", "Main.tscn"));

		Assert.Contains("LoadRecoveryDialog", text, StringComparison.Ordinal);
	}

	[Fact]
	public void MainCode_WiresLoadRecoveryDialogIntoModalAndCoordinatorFlow()
	{
		var mainText = File.ReadAllText(GetRepoPath("App", "Main.cs"));
		var coordinatorText = File.ReadAllText(GetRepoPath("App", "RuntimeUi", "MainAppFlowCoordinator.cs"));

		Assert.Contains("_loadRecoveryDialog = new LoadRecoveryDialogModule", mainText, StringComparison.Ordinal);
		Assert.Contains("_loadRecoveryDialog,", mainText, StringComparison.Ordinal);
		Assert.Contains("CloseLoadRecoveryDialog", mainText, StringComparison.Ordinal);
		Assert.Contains("HandleLoadRecoveryConfirmed", coordinatorText, StringComparison.Ordinal);
		Assert.Contains("_loadRecoveryDialog.Open", coordinatorText, StringComparison.Ordinal);
	}

	[Fact]
	public void LoadRecoveryScene_ContainsCandidateListAndActions()
	{
		var text = File.ReadAllText(GetRepoPath("Scene", "LoadRecoveryDialog.tscn"));

		Assert.Contains("CandidateList", text, StringComparison.Ordinal);
		Assert.Contains("ConfirmBtn", text, StringComparison.Ordinal);
		Assert.Contains("CancelBtn", text, StringComparison.Ordinal);
	}

	private static string GetRepoPath(params string[] parts) =>
		Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", Path.Combine(parts)));
}
