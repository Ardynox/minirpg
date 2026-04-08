using System.Collections.Generic;
using System.Text;
using Godot;
using MiniRPG.Core.Config;

namespace MiniRPG.Module;

public sealed class KeyBindingsView
{
	private readonly RichTextLabel _bindingsText;
	private readonly Label _statusLabel;
	private readonly Label _hintLabel;
	private readonly Button _bindPrimaryBtn;
	private readonly Button _bindSecondaryBtn;
	private readonly Button _resetContextBtn;
	private readonly Button _resetAllBtn;
	private readonly Dictionary<InputBindingContext, Button> _tabs = [];
	private readonly KeyBindingsController _controller;

	public InputBindingContext CurrentContext => _controller.CurrentContext;
	public string? SelectedActionId => _controller.SelectedActionId;
	public bool IsCapturing => _controller.IsCapturing;

	public KeyBindingsView(Control root, InputBindingService bindings)
	{
		_controller = new KeyBindingsController(bindings);

		var contextBar = root.GetNode<HBoxContainer>("ContextBar");
		_tabs[InputBindingContext.Action] = contextBar.GetNode<Button>("ActionTab");
		_tabs[InputBindingContext.Typing] = contextBar.GetNode<Button>("TypingTab");
		_tabs[InputBindingContext.Selection] = contextBar.GetNode<Button>("SelectionTab");
		_tabs[InputBindingContext.Direction] = contextBar.GetNode<Button>("DirectionTab");

		_bindingsText = root.GetNode<RichTextLabel>("BindingsText");
		_statusLabel = root.GetNode<Label>("StatusLabel");
		_hintLabel = root.GetNode<Label>("HintLabel");

		var actionBar = root.GetNode<HBoxContainer>("ActionBar");
		_bindPrimaryBtn = actionBar.GetNode<Button>("BindPrimaryBtn");
		_bindSecondaryBtn = actionBar.GetNode<Button>("BindSecondaryBtn");
		_resetContextBtn = actionBar.GetNode<Button>("ResetContextBtn");
		_resetAllBtn = actionBar.GetNode<Button>("ResetAllBtn");

		foreach (var (context, btn) in _tabs)
		{
			var captured = context;
			btn.Pressed += () =>
			{
				_controller.SetContext(captured);
				UpdateUi();
			};
		}

		_bindingsText.MetaClicked += OnMetaClicked;
		_bindPrimaryBtn.Pressed += () =>
		{
			_controller.StartCapture(0);
			UpdateUi();
		};
		_bindSecondaryBtn.Pressed += () =>
		{
			_controller.StartCapture(1);
			UpdateUi();
		};
		_resetContextBtn.Pressed += () =>
		{
			_controller.ResetCurrentContext();
			UpdateUi();
		};
		_resetAllBtn.Pressed += () =>
		{
			_controller.ResetAllContexts();
			UpdateUi();
		};

		RefreshTexts();
		Refresh();
	}

	public void RefreshTexts() => UpdateUi();

	public void Refresh()
	{
		_controller.Refresh();
		UpdateUi();
	}

	public bool HandleCommand(string cmd)
	{
		var handled = _controller.HandleCommand(cmd);
		if (handled)
			UpdateUi();
		return handled;
	}

	public bool HandleKey(InputEventKey key)
	{
		if (!key.Pressed)
			return false;

		var handled = _controller.HandleKey(key.Keycode, key.CtrlPressed, key.AltPressed, key.ShiftPressed);
		if (handled)
			UpdateUi();
		return handled;
	}

	public bool HandleMouseInput(InputEvent @event)
	{
		if (@event is not InputEventMouseButton mb || !mb.Pressed)
			return false;

		var handled = _controller.HandleMouseWheel(mb.ButtonIndex, mb.CtrlPressed, mb.AltPressed, mb.ShiftPressed);
		if (handled)
			UpdateUi();
		return handled;
	}

	private void UpdateUi()
	{
		_hintLabel.Text = LocalizationService.T("ui.key_bindings.hint");
		_statusLabel.Text = _controller.StatusText;
		UpdateTabStates();
		RenderList();
		UpdateButtons();
	}

	private void UpdateTabStates()
	{
		foreach (var (context, btn) in _tabs)
		{
			btn.Text = LocalizationService.T(ContextToKey(context));
			btn.ButtonPressed = context == _controller.CurrentContext;
		}
	}

	private void RenderList()
	{
		var views = _controller.CurrentActions;
		if (views.Count == 0)
		{
			_bindingsText.Clear();
			_bindingsText.AppendText(LocalizationService.T("ui.key_bindings.empty"));
			return;
		}

		var sb = new StringBuilder();
		sb.AppendLine($"[b]{LocalizationService.T("ui.key_bindings.column.action")}[/b]                                  [b]{LocalizationService.T("ui.key_bindings.column.primary")}[/b]               [b]{LocalizationService.T("ui.key_bindings.column.secondary")}[/b]");
		sb.AppendLine("[color=#666666]----------------------------------------------------------------[/color]");
		for (var i = 0; i < views.Count; i++)
		{
			var row = views[i];
			var selected = row.Id == _controller.SelectedActionId;
			var mark = selected ? "> " : "  ";
			var text = $"{mark}{row.Label}".PadRight(34) + $"{row.Primary.ToDisplayString(),-18}{row.Secondary.ToDisplayString()}";
			var color = selected ? "#99ffaa" : "#cccccc";
			sb.AppendLine($"[url={row.Id}][color={color}]{EscapeBbcode(text)}[/color][/url]");
		}

		_bindingsText.Clear();
		_bindingsText.AppendText(sb.ToString());
	}

	private void UpdateButtons()
	{
		var hasSelection = _controller.SelectedActionId != null;
		_bindPrimaryBtn.Disabled = !hasSelection;
		_bindSecondaryBtn.Disabled = !hasSelection;
		_resetContextBtn.Disabled = _controller.CurrentActions.Count == 0;
		_bindPrimaryBtn.Text = _controller.CaptureSlot == 0
			? LocalizationService.T("ui.key_bindings.button.waiting")
			: LocalizationService.T("ui.key_bindings.button.bind_primary");
		_bindSecondaryBtn.Text = _controller.CaptureSlot == 1
			? LocalizationService.T("ui.key_bindings.button.waiting")
			: LocalizationService.T("ui.key_bindings.button.bind_secondary");
		_resetContextBtn.Text = LocalizationService.T("ui.key_bindings.button.reset_context");
		_resetAllBtn.Text = LocalizationService.T("ui.key_bindings.button.reset_all");
	}

	private void OnMetaClicked(Variant meta)
	{
		if (_controller.SelectAction(meta.AsString()))
			UpdateUi();
	}

	private static string EscapeBbcode(string text) =>
		text.Replace("[", "\\[").Replace("]", "\\]");

	private static string ContextToKey(InputBindingContext context) => context switch
	{
		InputBindingContext.Action => "ui.key_bindings.context.action",
		InputBindingContext.Typing => "ui.key_bindings.context.typing",
		InputBindingContext.Selection => "ui.key_bindings.context.selection",
		InputBindingContext.Direction => "ui.key_bindings.context.direction",
		_ => context.ToString(),
	};
}
