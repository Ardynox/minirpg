using System;
using System.Text;

namespace MiniRPG;

internal sealed class ChestCoordinator
{
	private readonly GameState _state;
	private readonly LogModule _log;
	private readonly InputModule _inputModule;
	private readonly PanelManager _panels;
	private readonly GroundPanelModule _groundPanel;
	private readonly Func<bool> _isMultiplayerSession;
	private readonly Func<ChestPanelModule> _ensureChestPanel;
	private readonly Action<ClientCommand> _submitClientCommand;
	private readonly Action _flushMap;

	private (int x, int y, int z)? _openChestPos;
	private OpenContainerContext? _openChestContext;

	public ChestCoordinator(
		GameState state,
		LogModule log,
		InputModule inputModule,
		PanelManager panels,
		GroundPanelModule groundPanel,
		Func<bool> isMultiplayerSession,
		Func<ChestPanelModule> ensureChestPanel,
		Action<ClientCommand> submitClientCommand,
		Action flushMap)
	{
		_state = state;
		_log = log;
		_inputModule = inputModule;
		_panels = panels;
		_groundPanel = groundPanel;
		_isMultiplayerSession = isMultiplayerSession;
		_ensureChestPanel = ensureChestPanel;
		_submitClientCommand = submitClientCommand;
		_flushMap = flushMap;
	}

	public ChestPanelModule? ChestPanel { get; set; }

	public void OpenChestPanel(Item chestItem, ContainerSourceKind source, string? ownerActorId = null)
	{
		chestItem.EnsureRuntimeState();
		_openChestContext = new OpenContainerContext(
			source,
			chestItem.InstanceId,
			ownerActorId,
			_state.PlayerX,
			_state.PlayerY,
			_state.PlayerZ);
		_openChestPos = source == ContainerSourceKind.Ground
			? (_state.PlayerX, _state.PlayerY, _state.PlayerZ)
			: null;
		var chest = _ensureChestPanel();
		chest.Open(chestItem);
		_panels.PushFocus(chest);
	}

	public void CloseChestPanel()
	{
		if (ChestPanel?.CurrentChest != null)
			PersistOpenChestState(ChestPanel.CurrentChest);

		_openChestContext = null;
		_openChestPos = null;
		if (ChestPanel != null)
		{
			ChestPanel.Close();
			_panels.OnPanelClosed(ChestPanel);
		}
		_groundPanel.Invalidate();
		_groundPanel.Refresh();
		_flushMap();
	}

	public void CheckChestRange()
	{
		if (_openChestPos == null || ChestPanel == null || !ChestPanel.Visible) return;
		var (cx, cy, _) = _openChestPos.Value;
		var dist = Math.Max(Math.Abs(_state.PlayerX - cx), Math.Abs(_state.PlayerY - cy));
		if (dist > 1)
		{
			_log.Add(LocalizationService.T("log.chest.out_of_range"));
			CloseChestPanel();
		}
	}

	public void OpenPutIntoChestSelection(Item chestItem)
	{
		var player = ActorModule.GetPlayer(_state);
		if (player == null) return;

		var inv = InventoryModule.List(player);
		if (inv.Count == 0)
		{
			_log.Add(LocalizationService.T("ui.inventory.empty"));
			return;
		}

		var sb = new StringBuilder(LocalizationService.T("ui.chest.choose_put_item"));
		for (var i = 0; i < inv.Count; i++)
		{
			var (_, item) = inv[i];
			var eqMark = item.Equipped ? "[E]" : "";
			sb.Append($"  [{i + 1}] {eqMark}{ItemFormatHelper.GetDisplayName(_state, item)}");
		}
		sb.Append(LocalizationService.T("ui.selection.cancel_option"));
		_log.Add(sb.ToString());

		var chest = _ensureChestPanel();
		_inputModule.EnterSelection(n =>
		{
			if (n == 0) { _log.Add(LocalizationService.T("ui.selection.canceled")); _panels.SetFocus(chest); return; }
			if (n < 1 || n > inv.Count) { _log.Add(LocalizationService.T("ui.selection.invalid")); _panels.SetFocus(chest); return; }
			var (invIdx, item) = inv[n - 1];
			if (item.Equipped)
			{
				_log.Add(LocalizationService.T("log.inventory.unequip_first", ("item", ItemFormatHelper.GetDisplayName(_state, item))));
				_panels.SetFocus(chest);
				return;
			}
			ExecuteOpenChestCommand(new ChestPutClientCommand
			{
				ActorId = player.Id,
				InventoryIndex = invIdx,
			});
			_panels.SetFocus(chest);
			chest.Refresh();
			_flushMap();
		}, () => { _log.Add(LocalizationService.T("ui.selection.canceled")); _panels.SetFocus(chest); });
	}

	public void PersistOpenChestState(Item chestItem)
	{
		if (_isMultiplayerSession())
			return;
		if (_state.World == null || _openChestContext == null || _openChestContext.Value.Source != ContainerSourceKind.Ground)
			return;

		var (x, y, z) = (_openChestContext.Value.X, _openChestContext.Value.Y, _openChestContext.Value.Z);
		_state.World.UpdateGroundItem(x, y, z, chestItem);
	}

	public void TakeChestItem(Item chestItem, int itemIndex)
	{
		if (chestItem.Contents == null || itemIndex < 0 || itemIndex >= chestItem.Contents.Count)
			return;

		var player = ActorModule.GetPlayer(_state);
		if (player == null)
			return;

		ExecuteOpenChestCommand(new ChestTakeClientCommand
		{
			ActorId = player.Id,
			ItemInstanceId = chestItem.Contents[itemIndex].InstanceId,
		});
	}

	public void TakeAllChestItems(Item chestItem)
	{
		var player = ActorModule.GetPlayer(_state);
		if (player == null)
			return;

		ExecuteOpenChestCommand(new ChestTakeAllClientCommand
		{
			ActorId = player.Id,
		});
	}

	private void ExecuteOpenChestCommand(ClientCommand command)
	{
		if (_openChestContext == null)
		{
			_log.Add(LocalizationService.TOrFallback("ui.chest.closed", "Container is no longer available."));
			return;
		}

		var context = _openChestContext.Value;
		var resolvedCommand = command switch
		{
			ChestTakeClientCommand take => take with
			{
				ContainerSource = context.Source,
				ContainerInstanceId = context.ContainerInstanceId,
				ContainerOwnerActorId = context.OwnerActorId,
				ContainerX = context.X,
				ContainerY = context.Y,
				ContainerZ = context.Z,
			},
			ChestTakeAllClientCommand takeAll => takeAll with
			{
				ContainerSource = context.Source,
				ContainerInstanceId = context.ContainerInstanceId,
				ContainerOwnerActorId = context.OwnerActorId,
				ContainerX = context.X,
				ContainerY = context.Y,
				ContainerZ = context.Z,
			},
			ChestPutClientCommand put => put with
			{
				ContainerSource = context.Source,
				ContainerInstanceId = context.ContainerInstanceId,
				ContainerOwnerActorId = context.OwnerActorId,
				ContainerX = context.X,
				ContainerY = context.Y,
				ContainerZ = context.Z,
			},
			_ => command,
		};
		_submitClientCommand(resolvedCommand);
	}

	private readonly record struct OpenContainerContext(
		ContainerSourceKind Source,
		string ContainerInstanceId,
		string? OwnerActorId,
		int X,
		int Y,
		int Z);
}
