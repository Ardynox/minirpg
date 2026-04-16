using MiniRPG.Module;
using MiniRPG.Module.Session;

namespace MiniRPG;

/// <summary>
/// Owns the transient status banner shown by the world-manager panel:
/// the last business-level message (delete/clean/load outcome) plus the
/// default fallback to the session's world-storage migration warning.
///
/// Extracted from <see cref="MainAppFlowCoordinator"/> so the coordinator
/// only tells the banner what happened; the banner alone decides whether
/// the explicit message or the migration warning wins, and forwards the
/// result to <see cref="WorldManagerModule.SetStatusMessage"/>.
/// </summary>
internal sealed class WorldManagerStatusBanner
{
	private readonly WorldManagerModule _worldManager;
	private readonly GameSessionModule _session;

	private string? _message;
	private bool _isError;

	public WorldManagerStatusBanner(WorldManagerModule worldManager, GameSessionModule session)
	{
		_worldManager = worldManager;
		_session = session;
	}

	/// <summary>Set an explicit status message and re-apply to the panel.</summary>
	public void Set(string message, bool isError)
	{
		_message = message;
		_isError = isError;
		Apply();
	}

	/// <summary>Drop the explicit message and re-apply (migration warning may still show).</summary>
	public void Clear()
	{
		_message = null;
		_isError = false;
		Apply();
	}

	/// <summary>
	/// Re-publish the current banner state. Explicit messages always win;
	/// otherwise surface the world-storage migration warning when present;
	/// otherwise clear the banner.
	/// </summary>
	public void Apply()
	{
		if (!string.IsNullOrWhiteSpace(_message))
		{
			_worldManager.SetStatusMessage(_message, _isError);
			return;
		}

		if (_session.HasWorldStorageMigrationFailures)
		{
			_worldManager.SetStatusMessage(
				LocalizationService.T(
					"ui.world_manager.status.migration_warning",
					("count", _session.WorldStorageMigrationFailureCount)),
				isError: true);
			return;
		}

		_worldManager.SetStatusMessage(null, isError: false);
	}
}
