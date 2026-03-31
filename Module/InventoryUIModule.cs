using System.Text;
using MiniRPG.Core;

namespace MiniRPG.Module;

/// <summary>
/// 背包 UI 流程：物品列表、装备/使用/丢弃操作。
/// </summary>
public class InventoryUIModule
{
	private readonly IGameUI _ui;

	public InventoryUIModule(IGameUI ui) => _ui = ui;

	public void Open()
	{
		var player = ActorModule.GetPlayer(_ui.State);
		if (player == null) return;
		ShowInventory(player);
	}

	private void ShowInventory(Actor player)
	{
		var items = InventoryModule.List(player);
		if (items.Count == 0)
		{
			_ui.AddLog("背包是空的 🎒");
			return;
		}

		_ui.AddLog($"═══ 背包 ═══  💰{player.Gold}G");
		for (var i = 0; i < items.Count; i++)
		{
			var (_, item) = items[i];
			var eqMark = item.Equipped ? " [已装备]" : "";
			var tagDesc = TradeUIModule.FormatItemTags(item);
			_ui.AddLog($"  [{i + 1}] {item.Name}{eqMark}  {item.Price}G{tagDesc}");
		}
		_ui.AddLog("  [0] 关闭");

		_ui.EnterSelection(n =>
		{
			if (n == 0) { _ui.AddLog("关闭背包"); return; }
			if (n < 1 || n > items.Count) { _ui.AddLog("无效选择"); return; }

			var (idx, item) = items[n - 1];
			ShowItemActions(player, idx, item);
		});
	}

	private void ShowItemActions(Actor player, int invIndex, Item item)
	{
		var eqLabel = item.Equipped ? "卸下" : "装备";
		var hasUse = item.Tags.ContainsKey("治疗");

		var sb = new StringBuilder($"{item.Name}：");
		sb.Append($"  [1] {eqLabel}");
		if (hasUse) sb.Append("  [2] 使用");
		sb.Append($"  [{(hasUse ? 3 : 2)}] 丢弃");
		sb.Append("  [0] 返回");
		_ui.AddLog(sb.ToString());

		_ui.EnterSelection(n =>
		{
			if (n == 0) { ShowInventory(player); return; }

			if (n == 1)
			{
				var r = InventoryModule.ToggleEquip(player, invIndex);
				_ui.AddLog(r.Message);
			}
			else if (hasUse && n == 2)
			{
				var r = InventoryModule.Use(player, invIndex);
				_ui.AddLog(r.Message);
			}
			else if ((!hasUse && n == 2) || (hasUse && n == 3))
			{
				var events = InteractionModule.DropItem(_ui.State, player, invIndex);
				_ui.Dispatch(events);
			}
			else
			{
				_ui.AddLog("无效选择");
			}

			ShowInventory(player);
		});
	}
}
