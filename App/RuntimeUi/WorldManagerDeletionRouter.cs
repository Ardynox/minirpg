using System;
using MiniRPG.Core.Config;
using MiniRPG.Module;

namespace MiniRPG;

/// <summary>
/// Executes the world-manager delete / save-data-delete / asset-cleanup
/// actions: dispatches to <see cref="GameSessionModule"/>, translates the
/// returned status into a localized banner, and triggers the panel
/// refresh + optional continue-state re-evaluation.
///
/// Extracted from <see cref="MainAppFlowCoordinator"/> because the three
/// handlers share an identical shape (status switch → banner message →
/// refresh) and were bloating the flow coordinator with near-duplicate
/// boilerplate.
/// </summary>
internal sealed class WorldManagerDeletionRouter
{
	private readonly GameSessionModule _session;
	private readonly WorldManagerModule _worldManager;
	private readonly WorldManagerStatusBanner _statusBanner;
	private readonly Action _refreshMainMenuContinueState;
	private readonly Action<string?, string?> _refreshContents;

	public WorldManagerDeletionRouter(
		GameSessionModule session,
		WorldManagerModule worldManager,
		WorldManagerStatusBanner statusBanner,
		Action refreshMainMenuContinueState,
		Action<string?, string?> refreshContents)
	{
		_session = session;
		_worldManager = worldManager;
		_statusBanner = statusBanner;
		_refreshMainMenuContinueState = refreshMainMenuContinueState;
		_refreshContents = refreshContents;
	}

	public void DeleteWorld(string worldId, string worldName)
	{
		var status = _session.DeleteWorld(worldId);
		switch (status)
		{
			case WorldDeletionStatus.Success:
				_statusBanner.Set(
					LocalizationService.T("ui.world_manager.status.deleted", ("world", worldName)),
					isError: false);
				_refreshMainMenuContinueState();
				_refreshContents(worldId, null);
				return;

			case WorldDeletionStatus.ActiveWorldLocked:
				_statusBanner.Set(
					LocalizationService.T("ui.world_manager.status.delete_current_world_locked"),
					isError: true);
				break;

			case WorldDeletionStatus.NotFound:
				_statusBanner.Set(
					LocalizationService.T("ui.world_manager.status.delete_not_found"),
					isError: true);
				break;

			default:
				_statusBanner.Set(
					LocalizationService.T("ui.world_manager.status.delete_failed"),
					isError: true);
				break;
		}

		_refreshContents(worldId, null);
	}

	public void DeleteWorldSaveData(string worldId, string worldName)
	{
		var status = _session.DeleteWorldSaveData(worldId);
		switch (status)
		{
			case WorldSaveDataDeletionStatus.Success:
				_statusBanner.Set(
					LocalizationService.T("ui.world_manager.status.delete_save_data_success", ("world", worldName)),
					isError: false);
				_refreshMainMenuContinueState();
				_refreshContents(worldId, null);
				return;

			case WorldSaveDataDeletionStatus.ActiveWorldLocked:
				_statusBanner.Set(
					LocalizationService.T("ui.world_manager.status.delete_save_data_locked"),
					isError: true);
				break;

			case WorldSaveDataDeletionStatus.NotFound:
				_statusBanner.Set(
					LocalizationService.T("ui.world_manager.status.delete_save_data_not_found"),
					isError: true);
				break;

			default:
				_statusBanner.Set(
					LocalizationService.T("ui.world_manager.status.delete_save_data_failed"),
					isError: true);
				break;
		}

		_refreshContents(worldId, null);
	}

	public void CleanWorldAssets(string worldId, string worldName)
	{
		var status = _session.DeleteWorldAssets(worldId);
		switch (status)
		{
			case WorldAssetCleanupStatus.Success:
				_statusBanner.Set(
					LocalizationService.T("ui.world_manager.status.clean_assets_success", ("world", worldName)),
					isError: false);
				_refreshContents(worldId, _worldManager.SelectedCharacterId);
				return;

			case WorldAssetCleanupStatus.NoAssets:
				_statusBanner.Set(
					LocalizationService.T("ui.world_manager.status.clean_assets_no_assets", ("world", worldName)),
					isError: false);
				break;

			case WorldAssetCleanupStatus.ActiveWorldLocked:
				_statusBanner.Set(
					LocalizationService.T("ui.world_manager.status.clean_assets_locked"),
					isError: true);
				break;

			case WorldAssetCleanupStatus.NotFound:
				_statusBanner.Set(
					LocalizationService.T("ui.world_manager.status.clean_assets_not_found"),
					isError: true);
				break;

			default:
				_statusBanner.Set(
					LocalizationService.T("ui.world_manager.status.clean_assets_failed"),
					isError: true);
				break;
		}

		_refreshContents(worldId, _worldManager.SelectedCharacterId);
	}
}
