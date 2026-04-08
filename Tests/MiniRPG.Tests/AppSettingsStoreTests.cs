using MiniRPG.Core.Config;
using Xunit;

namespace MiniRPG.Tests;

public sealed class AppSettingsStoreTests
{
	[Fact]
	public void EnableDebugPanel_CanRoundTripThroughSettingsStore()
	{
		var original = AppSettingsStore.LoadEnableDebugPanel();
		try
		{
			AppSettingsStore.SaveEnableDebugPanel(false);
			Assert.False(AppSettingsStore.LoadEnableDebugPanel());

			AppSettingsStore.SaveEnableDebugPanel(true);
			Assert.True(AppSettingsStore.LoadEnableDebugPanel());
		}
		finally
		{
			AppSettingsStore.SaveEnableDebugPanel(original);
		}
	}
}
