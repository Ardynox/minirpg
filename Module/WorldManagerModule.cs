using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MiniRPG.Core.Config;
using MiniRPG.Core.Map;

namespace MiniRPG.Module;

public sealed class WorldManagerModule : IModalInputLayer
{
	private static readonly Color RowNormalBackground = new(0.10f, 0.12f, 0.15f, 0.94f);
	private static readonly Color RowHoverBackground = new(0.15f, 0.18f, 0.22f, 0.96f);
	private static readonly Color RowBorderColor = new(0.26f, 0.29f, 0.35f, 1f);
	private static readonly Color RowSelectedBackground = new(0.28f, 0.21f, 0.10f, 0.98f);
	private static readonly Color RowSelectedHoverBackground = new(0.34f, 0.26f, 0.12f, 1f);
	private static readonly Color RowSelectedBorderColor = new(0.92f, 0.80f, 0.46f, 1f);
	private static readonly Color RowDisabledBackground = new(0.08f, 0.09f, 0.11f, 0.82f);
	private static readonly Color RowDisabledBorderColor = new(0.18f, 0.20f, 0.24f, 1f);
	private static readonly Color RowTextColor = new(0.95f, 0.96f, 0.98f, 1f);
	private static readonly Color RowDisabledTextColor = new(0.60f, 0.64f, 0.69f, 1f);

	private enum WorldsListFocus
	{
		Worlds,
		Characters,
	}

	private readonly PanelContainer _panel;
	private readonly Label _titleLabel;
	private readonly Label _subtitleLabel;
	private readonly Label _statusLabel;
	private readonly Button _worldsTabButton;
	private readonly Button _scenariosTabButton;
	private readonly Button _legacyTabButton;
	private readonly Control _worldsPage;
	private readonly Control _scenariosPage;
	private readonly Control _legacyPage;
	private readonly VBoxContainer _worldList;
	private readonly Label _selectedWorldTitle;
	private readonly Label _selectedWorldSummary;
	private readonly VBoxContainer _characterList;
	private readonly VBoxContainer _scenarioList;
	private readonly VBoxContainer _legacyList;
	private readonly Button _createWorldButton;
	private readonly Button _createCharacterButton;
	private readonly Button _continueCharacterButton;
	private readonly Button _backButton;

	private readonly List<Button> _worldButtons = [];
	private readonly List<Button> _characterButtons = [];
	private readonly List<Button> _scenarioButtons = [];
	private readonly List<Button> _legacyButtons = [];

	private IReadOnlyList<WorldEntryInfo> _worlds = Array.Empty<WorldEntryInfo>();
	private IReadOnlyList<SaveSlotInfo> _scenarios = Array.Empty<SaveSlotInfo>();
	private IReadOnlyList<SaveSlotInfo> _legacySaves = Array.Empty<SaveSlotInfo>();
	private WorldLaunchTab _currentTab = WorldLaunchTab.Worlds;
	private string? _selectedWorldId;
	private string? _selectedCharacterId;
	private string? _selectedScenarioId;
	private string? _selectedLegacySavePath;
	private WorldsListFocus _worldsListFocus = WorldsListFocus.Worlds;

	public event Action? CloseRequested;
	public event Action? CreateWorldRequested;
	public event Action<string>? CreateCharacterRequested;
	public event Action<string, string>? ContinueCharacterRequested;
	public event Action<string>? ScenarioRequested;
	public event Action<SaveSlotInfo>? LegacySaveRequested;

	public WorldManagerModule(PanelContainer panel)
	{
		_panel = panel;
		_titleLabel = panel.GetNode<Label>("Margin/VBox/Title");
		_subtitleLabel = panel.GetNode<Label>("Margin/VBox/Subtitle");
		_statusLabel = panel.GetNode<Label>("Margin/VBox/Status");
		_worldsTabButton = panel.GetNode<Button>("Margin/VBox/TabBar/WorldsTab");
		_scenariosTabButton = panel.GetNode<Button>("Margin/VBox/TabBar/ScenariosTab");
		_legacyTabButton = panel.GetNode<Button>("Margin/VBox/TabBar/LegacyTab");
		_worldsPage = panel.GetNode<Control>("Margin/VBox/Pages/WorldsPage");
		_scenariosPage = panel.GetNode<Control>("Margin/VBox/Pages/ScenariosPage");
		_legacyPage = panel.GetNode<Control>("Margin/VBox/Pages/LegacyPage");
		_worldList = panel.GetNode<VBoxContainer>("Margin/VBox/Pages/WorldsPage/Body/WorldColumn/WorldListScroll/WorldList");
		_selectedWorldTitle = panel.GetNode<Label>("Margin/VBox/Pages/WorldsPage/Body/DetailColumn/SelectedWorldTitle");
		_selectedWorldSummary = panel.GetNode<Label>("Margin/VBox/Pages/WorldsPage/Body/DetailColumn/SelectedWorldSummary");
		_characterList = panel.GetNode<VBoxContainer>("Margin/VBox/Pages/WorldsPage/Body/DetailColumn/CharacterListScroll/CharacterList");
		_scenarioList = panel.GetNode<VBoxContainer>("Margin/VBox/Pages/ScenariosPage/ScenarioListScroll/ScenarioList");
		_legacyList = panel.GetNode<VBoxContainer>("Margin/VBox/Pages/LegacyPage/LegacyListScroll/LegacyList");
		_createWorldButton = panel.GetNode<Button>("Margin/VBox/Footer/CreateWorldBtn");
		_createCharacterButton = panel.GetNode<Button>("Margin/VBox/Footer/CreateCharacterBtn");
		_continueCharacterButton = panel.GetNode<Button>("Margin/VBox/Footer/ContinueCharacterBtn");
		_backButton = panel.GetNode<Button>("Margin/VBox/Footer/BackBtn");

		_worldsTabButton.Pressed += () => SwitchTab(WorldLaunchTab.Worlds);
		_scenariosTabButton.Pressed += () => SwitchTab(WorldLaunchTab.Scenarios);
		_legacyTabButton.Pressed += () => SwitchTab(WorldLaunchTab.LegacySaves);
		_createWorldButton.Pressed += () => CreateWorldRequested?.Invoke();
		_createCharacterButton.Pressed += () =>
		{
			if (!string.IsNullOrWhiteSpace(_selectedWorldId))
				CreateCharacterRequested?.Invoke(_selectedWorldId);
		};
		_continueCharacterButton.Pressed += () =>
		{
			if (!string.IsNullOrWhiteSpace(_selectedWorldId) && !string.IsNullOrWhiteSpace(_selectedCharacterId))
				ContinueCharacterRequested?.Invoke(_selectedWorldId, _selectedCharacterId);
		};
		_backButton.Pressed += () => CloseRequested?.Invoke();
	}

	public bool Visible
	{
		get => _panel.Visible;
		set => _panel.Visible = value;
	}

	public void Open(
		IReadOnlyList<WorldEntryInfo> worlds,
		IReadOnlyList<SaveSlotInfo> scenarios,
		IReadOnlyList<SaveSlotInfo> legacySaves,
		string title,
		string subtitle,
		WorldLaunchTab initialTab = WorldLaunchTab.Worlds,
		string? selectedWorldId = null,
		string? selectedCharacterId = null)
	{
		_worlds = worlds;
		_scenarios = scenarios;
		_legacySaves = legacySaves;
		_titleLabel.Text = title;
		_subtitleLabel.Text = subtitle;
		_selectedWorldId = ResolveSelectedWorldId(selectedWorldId);
		_selectedCharacterId = ResolveSelectedCharacterId(selectedCharacterId);
		_selectedScenarioId = ResolveSelectedScenarioId(_selectedScenarioId);
		_selectedLegacySavePath = ResolveSelectedLegacySavePath(_selectedLegacySavePath);
		SwitchTab(initialTab, preserveVisibility: false);
		Visible = true;
		FocusCurrentSelection();
	}

	public void Close()
	{
		Visible = false;
	}

	public void RefreshTexts()
	{
		RefreshView();
	}

	public void SetStatusMessage(string? message, bool isError)
	{
		_statusLabel.Visible = !string.IsNullOrWhiteSpace(message);
		_statusLabel.Text = message ?? string.Empty;
		_statusLabel.Modulate = isError
			? new Color(1f, 0.58f, 0.58f, 1f)
			: Colors.White;
	}

	public bool HandleKeyInput(InputEventKey key)
	{
		if (!key.Pressed || key.Echo || key.AltPressed || key.CtrlPressed || key.MetaPressed)
			return false;

		switch (key.Keycode)
		{
			case Key.Escape:
				CloseRequested?.Invoke();
				return true;
			case Key.Left:
				MoveTab(-1);
				return true;
			case Key.Right:
				MoveTab(1);
				return true;
			case Key.Up:
				return MoveSelection(-1);
			case Key.Down:
				return MoveSelection(1);
			case Key.Enter:
			case Key.KpEnter:
				return ActivatePrimaryAction();
			default:
				return false;
		}
	}

	public string? SelectedWorldId => _selectedWorldId;
	public string? SelectedCharacterId => _selectedCharacterId;
	public WorldLaunchTab CurrentTab => _currentTab;

	private void SwitchTab(WorldLaunchTab tab, bool preserveVisibility = true)
	{
		_currentTab = tab;
		RefreshView();
		if (preserveVisibility)
		{
			Visible = true;
			FocusCurrentSelection();
		}
	}

	private void MoveTab(int delta)
	{
		var tabs = new[] { WorldLaunchTab.Worlds, WorldLaunchTab.Scenarios, WorldLaunchTab.LegacySaves };
		var currentIndex = Array.IndexOf(tabs, _currentTab);
		var nextIndex = (currentIndex + delta + tabs.Length) % tabs.Length;
		SwitchTab(tabs[nextIndex]);
	}

	private bool MoveSelection(int delta)
	{
		return _currentTab switch
		{
			WorldLaunchTab.Worlds => MoveWorldsSelection(delta),
			WorldLaunchTab.Scenarios => MoveScenarioSelection(delta),
			WorldLaunchTab.LegacySaves => MoveLegacySelection(delta),
			_ => false,
		};
	}

	private bool MoveWorldsSelection(int delta)
	{
		if (_worlds.Count == 0)
			return false;

		var focus = ResolveWorldsListFocus();
		if (focus == WorldsListFocus.Characters)
		{
			var selectedWorld = GetSelectedWorld();
			if (selectedWorld?.Characters.Count > 0)
			{
				var currentIndex = Math.Max(0, selectedWorld.Characters
					.ToList()
					.FindIndex(entry => string.Equals(entry.CharacterId, _selectedCharacterId, StringComparison.Ordinal)));
				var nextIndex = Math.Clamp(currentIndex + delta, 0, selectedWorld.Characters.Count - 1);
				SelectCharacterByIndex(nextIndex, focusButton: true);
				return true;
			}

			_worldsListFocus = WorldsListFocus.Worlds;
		}

		var worldIndex = Math.Max(0, _worlds
			.ToList()
			.FindIndex(entry => string.Equals(entry.WorldId, _selectedWorldId, StringComparison.Ordinal)));
		var nextWorldIndex = Math.Clamp(worldIndex + delta, 0, _worlds.Count - 1);
		SelectWorldByIndex(nextWorldIndex, focusButton: true);
		return true;
	}

	private bool MoveScenarioSelection(int delta)
	{
		if (_scenarios.Count == 0)
			return false;

		var currentIndex = Math.Max(0, _scenarios
			.ToList()
			.FindIndex(entry => string.Equals(entry.Id, _selectedScenarioId, StringComparison.Ordinal)));
		var nextIndex = Math.Clamp(currentIndex + delta, 0, _scenarios.Count - 1);
		SelectScenarioByIndex(nextIndex, focusButton: true);
		return true;
	}

	private bool MoveLegacySelection(int delta)
	{
		if (_legacySaves.Count == 0)
			return false;

		var currentIndex = Math.Max(0, _legacySaves
			.ToList()
			.FindIndex(entry => string.Equals(entry.SourcePath, _selectedLegacySavePath, StringComparison.OrdinalIgnoreCase)));
		var nextIndex = Math.Clamp(currentIndex + delta, 0, _legacySaves.Count - 1);
		SelectLegacyByIndex(nextIndex, focusButton: true);
		return true;
	}

	private bool ActivatePrimaryAction()
	{
		switch (_currentTab)
		{
			case WorldLaunchTab.Worlds:
			{
				var selectedWorld = GetSelectedWorld();
				if (selectedWorld == null)
				{
					CreateWorldRequested?.Invoke();
					return true;
				}

				if (selectedWorld.Characters.Count == 0 || string.IsNullOrWhiteSpace(_selectedCharacterId))
				{
					CreateCharacterRequested?.Invoke(selectedWorld.WorldId);
					return true;
				}

				ContinueCharacterRequested?.Invoke(selectedWorld.WorldId, _selectedCharacterId);
				return true;
			}
			case WorldLaunchTab.Scenarios:
			{
				var scenario = _scenarios.FirstOrDefault(entry => string.Equals(entry.Id, _selectedScenarioId, StringComparison.Ordinal));
				if (scenario == null)
					return false;

				ScenarioRequested?.Invoke(scenario.Id);
				return true;
			}
			case WorldLaunchTab.LegacySaves:
			{
				var save = _legacySaves.FirstOrDefault(entry => string.Equals(entry.SourcePath, _selectedLegacySavePath, StringComparison.OrdinalIgnoreCase));
				if (save == null)
					return false;

				LegacySaveRequested?.Invoke(save);
				return true;
			}
			default:
				return false;
		}
	}

	private void RefreshView()
	{
		_worldsTabButton.ButtonPressed = _currentTab == WorldLaunchTab.Worlds;
		_scenariosTabButton.ButtonPressed = _currentTab == WorldLaunchTab.Scenarios;
		_legacyTabButton.ButtonPressed = _currentTab == WorldLaunchTab.LegacySaves;
		_worldsPage.Visible = _currentTab == WorldLaunchTab.Worlds;
		_scenariosPage.Visible = _currentTab == WorldLaunchTab.Scenarios;
		_legacyPage.Visible = _currentTab == WorldLaunchTab.LegacySaves;
		_createWorldButton.Visible = _currentTab == WorldLaunchTab.Worlds;
		_createCharacterButton.Visible = _currentTab == WorldLaunchTab.Worlds;
		_continueCharacterButton.Visible = _currentTab == WorldLaunchTab.Worlds;

		RebuildWorldList();
		RefreshSelectedWorldDetails();
		RebuildScenarioList();
		RebuildLegacyList();
	}

	private void RebuildWorldList()
	{
		_selectedWorldId = ResolveSelectedWorldId(_selectedWorldId);
		ReconcileButtonCount(
			_worldList,
			_worldButtons,
			Math.Max(1, _worlds.Count),
			index => SelectWorldButtonByIndex(index, false),
			index => SelectWorldButtonByIndex(index, false));
		if (_worlds.Count == 0)
		{
			var button = _worldButtons[0];
			button.Disabled = true;
			ConfigureRowButton(button, LocalizationService.T("ui.world_manager.empty_worlds"), string.Empty, selected: false);
			return;
		}

		for (var i = 0; i < _worlds.Count; i++)
		{
			var world = _worlds[i];
			var button = _worldButtons[i];
			button.Disabled = false;
			ConfigureRowButton(
				button,
				world.DisplayName,
				world.Summary,
				string.Equals(world.WorldId, _selectedWorldId, StringComparison.Ordinal));
		}
	}

	private void RefreshSelectedWorldDetails()
	{
		var selectedWorld = GetSelectedWorld();
		if (selectedWorld == null)
		{
			_selectedWorldTitle.Text = LocalizationService.T("ui.world_manager.no_world_selected");
			_selectedWorldSummary.Text = LocalizationService.T("ui.world_manager.no_world_selected_summary");
			ReconcileButtonCount(
				_characterList,
				_characterButtons,
				1,
				index => SelectCharacterButtonByIndex(index, false),
				index => SelectCharacterButtonByIndex(index, false));
			_characterButtons[0].Disabled = true;
			_characterButtons[0].Text = LocalizationService.T("ui.world_manager.empty_characters");
			_createCharacterButton.Disabled = true;
			_continueCharacterButton.Disabled = true;
			_worldsListFocus = WorldsListFocus.Worlds;
			return;
		}

		_selectedWorldTitle.Text = selectedWorld.DisplayName;
		_selectedWorldSummary.Text = selectedWorld.Summary;
		_createCharacterButton.Disabled = false;

		var characters = selectedWorld.Characters;
		_selectedCharacterId = ResolveSelectedCharacterId(_selectedCharacterId);
		ReconcileButtonCount(
			_characterList,
			_characterButtons,
			Math.Max(1, characters.Count),
			index => SelectCharacterButtonByIndex(index, false),
			index => SelectCharacterButtonByIndex(index, false));
		if (characters.Count == 0)
		{
			_characterButtons[0].Disabled = true;
			ConfigureRowButton(_characterButtons[0], LocalizationService.T("ui.world_manager.empty_characters"), string.Empty, selected: false);
			_continueCharacterButton.Disabled = true;
			_worldsListFocus = WorldsListFocus.Worlds;
			return;
		}

		for (var i = 0; i < characters.Count; i++)
		{
			var character = characters[i];
			var button = _characterButtons[i];
			button.Disabled = false;
			ConfigureRowButton(
				button,
				character.CharacterName,
				character.Summary,
				string.Equals(character.CharacterId, _selectedCharacterId, StringComparison.Ordinal));
		}

		_continueCharacterButton.Disabled = string.IsNullOrWhiteSpace(_selectedCharacterId);
	}

	private void RebuildScenarioList()
	{
		_selectedScenarioId = ResolveSelectedScenarioId(_selectedScenarioId);
		ReconcileButtonCount(
			_scenarioList,
			_scenarioButtons,
			Math.Max(1, _scenarios.Count),
			index =>
			{
				SelectScenarioButtonByIndex(index, false);
				if (index >= 0 && index < _scenarios.Count)
					ScenarioRequested?.Invoke(_scenarios[index].Id);
			},
			index => SelectScenarioButtonByIndex(index, false));
		if (_scenarios.Count == 0)
		{
			_scenarioButtons[0].Disabled = true;
			ConfigureRowButton(_scenarioButtons[0], LocalizationService.T("ui.world_manager.empty_scenarios"), string.Empty, selected: false);
			return;
		}

		var tag = LocalizationService.T("ui.world_manager.tag.scenario_readonly");
		for (var i = 0; i < _scenarios.Count; i++)
		{
			var scenario = _scenarios[i];
			var button = _scenarioButtons[i];
			button.Disabled = false;
			ConfigureRowButton(
				button,
				$"[{tag}] {scenario.DisplayName}",
				scenario.Summary,
				string.Equals(scenario.Id, _selectedScenarioId, StringComparison.Ordinal));
		}
	}

	private void RebuildLegacyList()
	{
		_selectedLegacySavePath = ResolveSelectedLegacySavePath(_selectedLegacySavePath);
		ReconcileButtonCount(
			_legacyList,
			_legacyButtons,
			Math.Max(1, _legacySaves.Count),
			index =>
			{
				SelectLegacyButtonByIndex(index, false);
				if (index >= 0 && index < _legacySaves.Count)
					LegacySaveRequested?.Invoke(_legacySaves[index]);
			},
			index => SelectLegacyButtonByIndex(index, false));
		if (_legacySaves.Count == 0)
		{
			_legacyButtons[0].Disabled = true;
			ConfigureRowButton(_legacyButtons[0], LocalizationService.T("ui.world_manager.empty_legacy"), string.Empty, selected: false);
			return;
		}

		var tag = LocalizationService.T("ui.world_manager.tag.legacy_save");
		for (var i = 0; i < _legacySaves.Count; i++)
		{
			var save = _legacySaves[i];
			var button = _legacyButtons[i];
			button.Disabled = false;
			ConfigureRowButton(
				button,
				$"[{tag}] {save.DisplayName}",
				save.Summary,
				string.Equals(save.SourcePath, _selectedLegacySavePath, StringComparison.OrdinalIgnoreCase));
		}
	}

	private void ReconcileButtonCount(
		VBoxContainer root,
		List<Button> buttons,
		int needed,
		Action<int> pressedAction,
		Action<int> focusAction)
	{
		while (buttons.Count > needed)
		{
			buttons[^1].QueueFree();
			buttons.RemoveAt(buttons.Count - 1);
		}

		while (buttons.Count < needed)
		{
			var index = buttons.Count;
			var button = CreateRowButton();
			root.AddChild(button);
			buttons.Add(button);
			button.Pressed += () => pressedAction(index);
			button.FocusEntered += () => focusAction(index);
		}
	}

	private void SelectWorldButtonByIndex(int index, bool focusButton)
	{
		if (index < 0 || index >= _worlds.Count)
			return;

		_selectedWorldId = _worlds[index].WorldId;
		_selectedCharacterId = ResolveSelectedCharacterId(_selectedCharacterId);
		_worldsListFocus = WorldsListFocus.Worlds;
		RefreshView();
		if (focusButton)
			GrabFocus(_worldButtons, index);
	}

	private void SelectWorldByIndex(int index, bool focusButton) =>
		SelectWorldButtonByIndex(index, focusButton);

	private void SelectCharacterButtonByIndex(int index, bool focusButton)
	{
		var selectedWorld = GetSelectedWorld();
		if (selectedWorld == null || index < 0 || index >= selectedWorld.Characters.Count)
			return;

		_selectedCharacterId = selectedWorld.Characters[index].CharacterId;
		_worldsListFocus = WorldsListFocus.Characters;
		RefreshView();
		if (focusButton)
			GrabFocus(_characterButtons, index);
	}

	private void SelectCharacterByIndex(int index, bool focusButton) =>
		SelectCharacterButtonByIndex(index, focusButton);

	private void SelectScenarioButtonByIndex(int index, bool focusButton)
	{
		if (index < 0 || index >= _scenarios.Count)
			return;

		_selectedScenarioId = _scenarios[index].Id;
		if (focusButton)
			GrabFocus(_scenarioButtons, index);
		RefreshView();
	}

	private void SelectScenarioByIndex(int index, bool focusButton) =>
		SelectScenarioButtonByIndex(index, focusButton);

	private void SelectLegacyButtonByIndex(int index, bool focusButton)
	{
		if (index < 0 || index >= _legacySaves.Count)
			return;

		_selectedLegacySavePath = _legacySaves[index].SourcePath;
		if (focusButton)
			GrabFocus(_legacyButtons, index);
		RefreshView();
	}

	private void SelectLegacyByIndex(int index, bool focusButton) =>
		SelectLegacyButtonByIndex(index, focusButton);

	private WorldEntryInfo? GetSelectedWorld() =>
		_worlds.FirstOrDefault(world => string.Equals(world.WorldId, _selectedWorldId, StringComparison.Ordinal));

	private WorldsListFocus ResolveWorldsListFocus()
	{
		var focusOwner = _panel.GetViewport().GuiGetFocusOwner();
		if (focusOwner is Button button)
		{
			if (_characterButtons.Contains(button))
				_worldsListFocus = WorldsListFocus.Characters;
			else if (_worldButtons.Contains(button))
				_worldsListFocus = WorldsListFocus.Worlds;
		}

		var selectedWorld = GetSelectedWorld();
		if (_worldsListFocus == WorldsListFocus.Characters && (selectedWorld == null || selectedWorld.Characters.Count == 0))
			_worldsListFocus = WorldsListFocus.Worlds;

		return _worldsListFocus;
	}

	private void FocusCurrentSelection()
	{
		if (!Visible)
			return;

		switch (_currentTab)
		{
			case WorldLaunchTab.Worlds:
				if (ResolveWorldsListFocus() == WorldsListFocus.Characters)
				{
					var selectedWorld = GetSelectedWorld();
					var characterIndex = selectedWorld?.Characters
						.ToList()
						.FindIndex(entry => string.Equals(entry.CharacterId, _selectedCharacterId, StringComparison.Ordinal)) ?? -1;
					if (characterIndex >= 0)
					{
						GrabFocus(_characterButtons, characterIndex);
						return;
					}
				}

				var worldIndex = _worlds
					.ToList()
					.FindIndex(entry => string.Equals(entry.WorldId, _selectedWorldId, StringComparison.Ordinal));
				if (worldIndex >= 0)
				{
					GrabFocus(_worldButtons, worldIndex);
					return;
				}

				if (_worlds.Count == 0)
					_createWorldButton.GrabFocus();
				break;
			case WorldLaunchTab.Scenarios:
			{
				var scenarioIndex = _scenarios
					.ToList()
					.FindIndex(entry => string.Equals(entry.Id, _selectedScenarioId, StringComparison.Ordinal));
				if (scenarioIndex >= 0)
					GrabFocus(_scenarioButtons, scenarioIndex);
				break;
			}
			case WorldLaunchTab.LegacySaves:
			{
				var legacyIndex = _legacySaves
					.ToList()
					.FindIndex(entry => string.Equals(entry.SourcePath, _selectedLegacySavePath, StringComparison.OrdinalIgnoreCase));
				if (legacyIndex >= 0)
					GrabFocus(_legacyButtons, legacyIndex);
				break;
			}
		}
	}

	private string? ResolveSelectedWorldId(string? preferredWorldId)
	{
		if (!string.IsNullOrWhiteSpace(preferredWorldId)
			&& _worlds.Any(world => string.Equals(world.WorldId, preferredWorldId, StringComparison.Ordinal)))
		{
			return preferredWorldId;
		}

		return _worlds.FirstOrDefault()?.WorldId;
	}

	private string? ResolveSelectedCharacterId(string? preferredCharacterId)
	{
		var selectedWorld = GetSelectedWorld();
		if (selectedWorld == null || selectedWorld.Characters.Count == 0)
			return null;

		if (!string.IsNullOrWhiteSpace(preferredCharacterId)
			&& selectedWorld.Characters.Any(character => string.Equals(character.CharacterId, preferredCharacterId, StringComparison.Ordinal)))
		{
			return preferredCharacterId;
		}

		if (!string.IsNullOrWhiteSpace(selectedWorld.LastPlayedCharacterId)
			&& selectedWorld.Characters.Any(character => string.Equals(character.CharacterId, selectedWorld.LastPlayedCharacterId, StringComparison.Ordinal)))
		{
			return selectedWorld.LastPlayedCharacterId;
		}

		return selectedWorld.Characters[0].CharacterId;
	}

	private string? ResolveSelectedScenarioId(string? preferredScenarioId)
	{
		if (!string.IsNullOrWhiteSpace(preferredScenarioId)
			&& _scenarios.Any(entry => string.Equals(entry.Id, preferredScenarioId, StringComparison.Ordinal)))
		{
			return preferredScenarioId;
		}

		return _scenarios.FirstOrDefault()?.Id;
	}

	private string? ResolveSelectedLegacySavePath(string? preferredPath)
	{
		if (!string.IsNullOrWhiteSpace(preferredPath)
			&& _legacySaves.Any(entry => string.Equals(entry.SourcePath, preferredPath, StringComparison.OrdinalIgnoreCase)))
		{
			return preferredPath;
		}

		return _legacySaves.FirstOrDefault()?.SourcePath;
	}

	private static void GrabFocus(IReadOnlyList<Button> buttons, int index)
	{
		if (index >= 0 && index < buttons.Count && !buttons[index].Disabled)
			buttons[index].GrabFocus();
	}

	private static void ConfigureRowButton(Button button, string title, string summary, bool selected)
	{
		button.Text = FormatSelectedRowText(title, summary, selected);
		ApplyRowButtonTheme(button, selected, button.Disabled);
	}

	private static string FormatSelectedRowText(string title, string summary, bool selected)
	{
		var header = selected ? $"> {title}" : title;
		return string.IsNullOrWhiteSpace(summary)
			? header
			: $"{header}\n{summary}";
	}

	private static void ApplyRowButtonTheme(Button button, bool selected, bool disabled)
	{
		var normalBackground = disabled
			? RowDisabledBackground
			: selected
				? RowSelectedBackground
				: RowNormalBackground;
		var hoverBackground = disabled
			? RowDisabledBackground
			: selected
				? RowSelectedHoverBackground
				: RowHoverBackground;
		var borderColor = disabled
			? RowDisabledBorderColor
			: selected
				? RowSelectedBorderColor
				: RowBorderColor;
		var focusBorderColor = disabled
			? RowDisabledBorderColor
			: selected
				? Colors.White
				: RowSelectedBorderColor;
		var fontColor = disabled ? RowDisabledTextColor : RowTextColor;

		button.AddThemeStyleboxOverride("normal", CreateRowStyle(normalBackground, borderColor, 2));
		button.AddThemeStyleboxOverride("hover", CreateRowStyle(hoverBackground, borderColor, 2));
		button.AddThemeStyleboxOverride("pressed", CreateRowStyle(hoverBackground, focusBorderColor, 3));
		button.AddThemeStyleboxOverride("focus", CreateRowStyle(hoverBackground, focusBorderColor, 3));
		button.AddThemeStyleboxOverride("disabled", CreateRowStyle(RowDisabledBackground, RowDisabledBorderColor, 2));
		button.AddThemeColorOverride("font_color", fontColor);
		button.AddThemeColorOverride("font_hover_color", fontColor);
		button.AddThemeColorOverride("font_pressed_color", fontColor);
		button.AddThemeColorOverride("font_focus_color", fontColor);
		button.AddThemeColorOverride("font_disabled_color", RowDisabledTextColor);
	}

	private static StyleBoxFlat CreateRowStyle(Color background, Color border, int borderWidth)
	{
		return new StyleBoxFlat
		{
			BgColor = background,
			BorderColor = border,
			BorderWidthTop = borderWidth,
			BorderWidthRight = borderWidth,
			BorderWidthBottom = borderWidth,
			BorderWidthLeft = borderWidth,
			ContentMarginTop = 8,
			ContentMarginRight = 12,
			ContentMarginBottom = 8,
			ContentMarginLeft = 14,
			CornerRadiusTopLeft = 6,
			CornerRadiusTopRight = 6,
			CornerRadiusBottomRight = 6,
			CornerRadiusBottomLeft = 6,
		};
	}

	private static Button CreateRowButton() => new()
	{
		Flat = false,
		Alignment = HorizontalAlignment.Left,
		FocusMode = Control.FocusModeEnum.All,
		SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		CustomMinimumSize = new Vector2(0, 72),
		TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
		ClipText = false,
	};
}
