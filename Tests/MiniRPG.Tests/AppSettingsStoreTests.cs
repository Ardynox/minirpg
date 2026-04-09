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

	[Fact]
	public void MapZoomRange_CanRoundTripAndKeepsOrder()
	{
		var originalMin = AppSettingsStore.LoadMapZoomMin();
		var originalMax = AppSettingsStore.LoadMapZoomMax();
		try
		{
			AppSettingsStore.SaveMapZoomMin(0.5f);
			AppSettingsStore.SaveMapZoomMax(3.1f);
			Assert.Equal(0.5f, AppSettingsStore.LoadMapZoomMin(), 3);
			Assert.Equal(3.1f, AppSettingsStore.LoadMapZoomMax(), 3);

			AppSettingsStore.SaveMapZoomMin(3.5f);
			Assert.True(AppSettingsStore.LoadMapZoomMin() <= AppSettingsStore.LoadMapZoomMax());

			AppSettingsStore.SaveMapZoomMax(0.4f);
			Assert.True(AppSettingsStore.LoadMapZoomMin() <= AppSettingsStore.LoadMapZoomMax());
		}
		finally
		{
			AppSettingsStore.SaveMapZoomMin(originalMin);
			AppSettingsStore.SaveMapZoomMax(originalMax);
		}
	}
}
