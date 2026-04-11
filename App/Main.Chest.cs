using System;
using System.Collections.Generic;
using System.Text;

namespace MiniRPG;

public partial class Main
{
	private void OpenChestPanel(Item chestItem, ContainerSourceKind source, string? ownerActorId = null)
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
		var chest = EnsureChestPanel();
		chest.Open(chestItem);
		_panels.PushFocus(chest);
	}

	private void CloseChestPanel()
	{
		if (_chestPanel?.CurrentChest != null)
			PersistOpenChestState(_chestPanel.CurrentChest);

		_openChestContext = null;
		_openChestPos = null;
		if (_chestPanel != null)
		{
			_chestPanel.Close();
			_panels.OnPanelClosed(_chestPanel);
		}
		_groundPanel.Invalidate();
		_groundPanel.Refresh();
		FlushMap();
	}

	private void CheckChestRange()
	{
		if (_openChestPos == null || _chestPanel == null || !_chestPanel.Visible) return;
		var (cx, cy, _) = _openChestPos.Value;
		var dist = Math.Max(Math.Abs(_state.PlayerX - cx), Math.Abs(_state.PlayerY - cy));
		if (dist > 1)
		{
			_log.Add(LocalizationService.T("log.chest.out_of_range"));
			CloseChestPanel();
		}
	}

	private void OpenPutIntoChestSelection(Item chestItem)
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

		var chest = EnsureChestPanel();
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
			var result = ExecuteOpenChestCommand(new ChestPutClientCommand
			{
				ActorId = player.Id,
				InventoryIndex = invIdx,
			});
			ApplyServerActionResult(result);
			_panels.SetFocus(chest);
			chest.Refresh();
			FlushMap();
		}, () => { _log.Add(LocalizationService.T("ui.selection.canceled")); _panels.SetFocus(chest); });
	}

	private void PersistOpenChestState(Item chestItem)
	{
		if (IsMultiplayerSession)
			return;
		if (_state.World == null || _openChestContext == null || _openChestContext.Value.Source != ContainerSourceKind.Ground)
			return;

		var (x, y, z) = (_openChestContext.Value.X, _openChestContext.Value.Y, _openChestContext.Value.Z);
		_state.World.UpdateGroundItem(x, y, z, chestItem);
	}

	private void TakeChestItem(Item chestItem, int itemIndex)
	{
		if (chestItem.Contents == null || itemIndex < 0 || itemIndex >= chestItem.Contents.Count)
			return;

		var player = ActorModule.GetPlayer(_state);
		if (player == null)
			return;

		var result = ExecuteOpenChestCommand(new ChestTakeClientCommand
		{
			ActorId = player.Id,
			ItemInstanceId = chestItem.Contents[itemIndex].InstanceId,
		});
		ApplyServerActionResult(result);
	}

	private void TakeAllChestItems(Item chestItem)
	{
		var player = ActorModule.GetPlayer(_state);
		if (player == null)
			return;

		var result = ExecuteOpenChestCommand(new ChestTakeAllClientCommand
		{
			ActorId = player.Id,
		});
		ApplyServerActionResult(result);
	}

	private ServerActionResult ExecuteOpenChestCommand(ClientCommand command)
	{
		if (_openChestContext == null)
		{
			return ServerActionResult.Reject(
				LocalizationService.TOrFallback("ui.chest.closed", "Container is no longer available."),
				"missing_open_container");
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
		if (TrySubmitClientCommand(resolvedCommand))
			return ServerActionResult.Accept();

		return ServerActionGateway.Execute(_state, resolvedCommand);
	}

	private void ApplyServerActionResult(ServerActionResult result)
	{
		foreach (var log in result.Logs)
			_log.Add(log);

		if (result.Events.Count > 0)
			Dispatch(result.Events);
	}
}
