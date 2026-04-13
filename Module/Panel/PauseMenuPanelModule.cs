using System;
using System.Collections.Generic;
using Godot;

namespace MiniRPG.Module.Panel;

public sealed class PauseMenuPanelModule : IPauseMenuOverlay, IPanel
{
	private readonly PanelContainer _panel;
	private readonly Label _titleLabel;
	private readonly List<(PauseMenuAction Action, Button Button, string TextKey)> _items;
	private int _selectedIndex;

	public string PanelId => "pause_menu";
	public PanelContainer PanelNode => _panel;
	public bool Visible { get => _panel.Visible; set => _panel.Visible = value; }
	public bool ConsumeUnhandledKeys => true;
	public bool Dirty { get; set; }

	public event Action<PauseMenuAction>? ActionRequested;

	public PauseMenuPanelModule(PanelContainer panel)
	{
		_panel = panel;
		_titleLabel = panel.GetNode<Label>("Margin/VBox/Title");
		_items =
		[
			(PauseMenuAction.Resume, panel.GetNode<Button>("Margin/VBox/ContinueBtn"), "ui.pause_menu.continue"),
			(PauseMenuAction.QuickSave, panel.GetNode<Button>("Margin/VBox/QuickSaveBtn"), "ui.pause_menu.quick_save"),
			(PauseMenuAction.QuickLoad, panel.GetNode<Button>("Margin/VBox/QuickLoadBtn"), "ui.pause_menu.quick_load"),
			(PauseMenuAction.OpenSettings, panel.GetNode<Button>("Margin/VBox/SettingsBtn"), "ui.pause_menu.settings"),
			(PauseMenuAction.OpenMultiplayerRoom, panel.GetNode<Button>("Margin/VBox/MultiplayerRoomBtn"), "ui.pause_menu.multiplayer_room"),
			(PauseMenuAction.ReturnToMenu, panel.GetNode<Button>("Margin/VBox/ReturnToMenuBtn"), "ui.pause_menu.return_to_menu"),
		];

		for (var i = 0; i < _items.Count; i++)
		{
			var capturedIndex = i;
			_items[i].Button.Pressed += () =>
			{
				_selectedIndex = capturedIndex;
				UpdateSelectionTexts();
				ActionRequested?.Invoke(_items[capturedIndex].Action);
			};
		}

		_panel.Visible = false;
		RefreshTexts();
	}

	public void Open()
	{
		_selectedIndex = 0;
		Visible = true;
		UpdateSelectionTexts();
	}

	public void Close()
	{
		Visible = false;
	}

	public void RefreshTexts()
	{
		_titleLabel.Text = LocalizationService.T("ui.pause_menu.title");
		UpdateSelectionTexts();
	}

	public bool HandleCommand(string cmd)
	{
		switch (cmd)
		{
			case "up":
				MoveSelection(-1);
				return true;
			case "down":
				MoveSelection(1);
				return true;
			case "confirm":
				ActivateSelected();
				return true;
			case "1":
			case "2":
			case "3":
			case "4":
			case "5":
			case "6":
				ActivateByIndex(cmd[0] - '1');
				return true;
			case "close":
				ActionRequested?.Invoke(PauseMenuAction.Resume);
				return true;
			default:
				return false;
		}
	}

	private void MoveSelection(int delta)
	{
		_selectedIndex = (_selectedIndex + delta + _items.Count) % _items.Count;
		UpdateSelectionTexts();
	}

	private void ActivateSelected() =>
		ActionRequested?.Invoke(_items[_selectedIndex].Action);

	private void ActivateByIndex(int index)
	{
		if (index < 0 || index >= _items.Count)
			return;

		_selectedIndex = index;
		UpdateSelectionTexts();
		ActivateSelected();
	}

	private void UpdateSelectionTexts()
	{
		for (var i = 0; i < _items.Count; i++)
		{
			var text = LocalizationService.T(_items[i].TextKey);
			_items[i].Button.Text = i == _selectedIndex
				? $"> {text}"
				: text;
		}
	}
}
