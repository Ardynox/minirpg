using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using MiniRPG.Module.Panel;

namespace MiniRPG.Module;

public sealed class KeyBindingsUIModule : IPanel
{
	private readonly PanelContainer _panel;
	private readonly RichTextLabel _bindingsText;
	private readonly Label _statusLabel;
	private readonly Label _hintLabel;
	private readonly Button _bindPrimaryBtn;
	private readonly Button _bindSecondaryBtn;
	private readonly Button _resetContextBtn;
	private readonly Button _resetAllBtn;
	private readonly Button _closeBtn;
	private readonly Dictionary<InputBindingContext, Button> _tabs = [];
	private readonly InputBindingService _bindings;

	private InputBindingContext _context = InputBindingContext.Action;
	private string? _selectedActionId;
	private int _captureSlot = -1;
	private IReadOnlyList<BindingActionView> _views = Array.Empty<BindingActionView>();

	private static readonly InputBindingContext[] ContextOrder =
	[
		InputBindingContext.Action,
		InputBindingContext.Typing,
		InputBindingContext.Selection,
		InputBindingContext.Direction,
	];

	public string PanelId => "key_bindings";
	public PanelContainer PanelNode => _panel;
	public bool Visible { get => _panel.Visible; set => _panel.Visible = value; }
	public bool ConsumeUnhandledKeys => true;
	public bool Dirty { get; set; }
	public bool IsOpen => _panel.Visible;
	public bool IsCapturing => _captureSlot >= 0;
	public event Action? CloseRequested;

	public KeyBindingsUIModule(Node root, InputBindingService bindings)
	{
		_bindings = bindings;
		_panel = root.GetNode<PanelContainer>("KeyBindingsPanel");
		var vbox = _panel.GetNode<VBoxContainer>("MarginContainer/VBox");
		var contextBar = vbox.GetNode<HBoxContainer>("ContextBar");
		_tabs[InputBindingContext.Action] = contextBar.GetNode<Button>("ActionTab");
		_tabs[InputBindingContext.Typing] = contextBar.GetNode<Button>("TypingTab");
		_tabs[InputBindingContext.Selection] = contextBar.GetNode<Button>("SelectionTab");
		_tabs[InputBindingContext.Direction] = contextBar.GetNode<Button>("DirectionTab");

		_bindingsText = vbox.GetNode<RichTextLabel>("BindingsText");
		_statusLabel = vbox.GetNode<Label>("StatusLabel");
		_hintLabel = vbox.GetNode<Label>("HintLabel");

		var actionBar = vbox.GetNode<HBoxContainer>("ActionBar");
		_bindPrimaryBtn = actionBar.GetNode<Button>("BindPrimaryBtn");
		_bindSecondaryBtn = actionBar.GetNode<Button>("BindSecondaryBtn");
		_resetContextBtn = actionBar.GetNode<Button>("ResetContextBtn");
		_resetAllBtn = actionBar.GetNode<Button>("ResetAllBtn");
		_closeBtn = actionBar.GetNode<Button>("CloseBtn");

		foreach (var (context, btn) in _tabs)
		{
			var captured = context;
			btn.Pressed += () => SetContext(captured);
		}

		_bindingsText.MetaClicked += OnMetaClicked;
		_bindPrimaryBtn.Pressed += () => StartCapture(0);
		_bindSecondaryBtn.Pressed += () => StartCapture(1);
		_resetContextBtn.Pressed += ResetCurrentContext;
		_resetAllBtn.Pressed += ResetAllContexts;
		_closeBtn.Pressed += () => CloseRequested?.Invoke();

		_bindings.Changed += () =>
		{
			if (IsOpen) Refresh();
		};

		_panel.Visible = false;
	}

	public void Open()
	{
		Visible = true;
		_captureSlot = -1;
		_statusLabel.Text = "";
		_hintLabel.Text = "上/下切换条目，左右切换输入态，Enter改主键，Space改备键，滚轮也可绑定，ESC关闭";
		Refresh();
	}

	public void Close()
	{
		Visible = false;
		_captureSlot = -1;
		_statusLabel.Text = "";
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
			case "left":
			case "tab_prev":
				CycleContext(-1);
				return true;
			case "right":
			case "tab_next":
				CycleContext(1);
				return true;
			case "confirm":
				StartCapture(0);
				return true;
			case "close":
				CloseRequested?.Invoke();
				return true;
			default:
				return false;
		}
	}

	public bool HandleKey(InputEventKey key)
	{
		if (!IsOpen || !key.Pressed) return false;

		if (_captureSlot >= 0)
			return HandleCaptureKey(key);

		switch (key.Keycode)
		{
			case Key.Escape:
				CloseRequested?.Invoke();
				return true;
			case Key.W or Key.Up:
				MoveSelection(-1);
				return true;
			case Key.S or Key.Down:
				MoveSelection(1);
				return true;
			case Key.A or Key.Left:
				CycleContext(-1);
				return true;
			case Key.D or Key.Right:
				CycleContext(1);
				return true;
			case Key.Enter:
				StartCapture(0);
				return true;
			case Key.Space:
				StartCapture(1);
				return true;
		}

		return false;
	}

	public bool HandleMouseInput(InputEvent @event)
	{
		if (!IsOpen) return false;
		if (@event is not InputEventMouseButton mb || !mb.Pressed) return false;
		if (_captureSlot >= 0)
			return HandleCaptureMouseButton(mb);
		return false;
	}

	private void SetContext(InputBindingContext context)
	{
		_context = context;
		_selectedActionId = null;
		_captureSlot = -1;
		_statusLabel.Text = "";
		Refresh();
	}

	private void CycleContext(int delta)
	{
		var idx = Array.IndexOf(ContextOrder, _context);
		idx = (idx + delta + ContextOrder.Length) % ContextOrder.Length;
		SetContext(ContextOrder[idx]);
	}

	private void Refresh()
	{
		_views = _bindings.GetActions(_context);
		if (_views.Count == 0)
		{
			_selectedActionId = null;
			_bindingsText.Clear();
			_bindingsText.AppendText("暂无可绑定动作");
			UpdateButtons();
			UpdateTabStates();
			return;
		}

		if (_selectedActionId == null || !ContainsAction(_selectedActionId))
			_selectedActionId = _views[0].Id;

		UpdateTabStates();
		RenderList();
		UpdateButtons();
	}

	private bool ContainsAction(string actionId)
	{
		foreach (var row in _views)
			if (row.Id == actionId) return true;
		return false;
	}

	private void UpdateTabStates()
	{
		foreach (var (context, btn) in _tabs)
			btn.ButtonPressed = context == _context;
	}

	private void RenderList()
	{
		var sb = new StringBuilder();
		sb.AppendLine("[b]动作[/b]                                  [b]主键[/b]               [b]备键[/b]");
		sb.AppendLine("[color=#666666]----------------------------------------------------------------[/color]");
		for (var i = 0; i < _views.Count; i++)
		{
			var row = _views[i];
			var selected = row.Id == _selectedActionId;
			var mark = selected ? "▶ " : "  ";
			var text = $"{mark}{row.Label}".PadRight(34) + $"{row.Primary.ToDisplayString(),-18}{row.Secondary.ToDisplayString()}";
			var color = selected ? "#99ffaa" : "#cccccc";
			sb.AppendLine($"[url={row.Id}][color={color}]{EscapeBbcode(text)}[/color][/url]");
		}
		_bindingsText.Clear();
		_bindingsText.AppendText(sb.ToString());
	}

	private void UpdateButtons()
	{
		var hasSelection = _selectedActionId != null;
		_bindPrimaryBtn.Disabled = !hasSelection;
		_bindSecondaryBtn.Disabled = !hasSelection;
		_resetContextBtn.Disabled = _views.Count == 0;
		_bindPrimaryBtn.Text = _captureSlot == 0 ? "等待按键..." : "改主键";
		_bindSecondaryBtn.Text = _captureSlot == 1 ? "等待按键..." : "改备键";
	}

	private void MoveSelection(int delta)
	{
		if (_views.Count == 0 || _selectedActionId == null) return;
		var idx = -1;
		for (var i = 0; i < _views.Count; i++)
		{
			if (_views[i].Id != _selectedActionId) continue;
			idx = i;
			break;
		}
		if (idx < 0) idx = 0;
		idx = Math.Clamp(idx + delta, 0, _views.Count - 1);
		_selectedActionId = _views[idx].Id;
		RenderList();
	}

	private void StartCapture(int slot)
	{
		if (_selectedActionId == null || _views.Count == 0) return;
		_captureSlot = slot;
		_statusLabel.Text = slot == 0 ? "按下新主键（ESC取消）" : "按下新备键（ESC取消）";
		UpdateButtons();
	}

	private bool HandleCaptureKey(InputEventKey key)
	{
		if (key.Keycode == Key.Escape)
		{
			_captureSlot = -1;
			_statusLabel.Text = "已取消绑定";
			UpdateButtons();
			return true;
		}

		if (!InputGesture.TryFromEvent(key, out var gesture))
			return true;

		if (_selectedActionId == null) return true;
		var slot = _captureSlot;
		_captureSlot = -1;
		if (_bindings.Rebind(_context, _selectedActionId, slot, gesture))
			_statusLabel.Text = $"已绑定: {gesture.ToDisplayString()}";
		else
			_statusLabel.Text = "绑定失败";
		Refresh();
		return true;
	}

	private bool HandleCaptureMouseButton(InputEventMouseButton button)
	{
		if (!InputGesture.TryFromEvent(button, out var gesture))
			return true;

		if (_selectedActionId == null) return true;
		var slot = _captureSlot;
		_captureSlot = -1;
		if (_bindings.Rebind(_context, _selectedActionId, slot, gesture))
			_statusLabel.Text = $"已绑定: {gesture.ToDisplayString()}";
		else
			_statusLabel.Text = "绑定失败";
		Refresh();
		return true;
	}

	private void ResetCurrentContext()
	{
		_bindings.ResetContext(_context);
		_statusLabel.Text = "当前输入态已恢复默认";
		_captureSlot = -1;
		Refresh();
	}

	private void ResetAllContexts()
	{
		_bindings.ResetAll();
		_statusLabel.Text = "全部输入态已恢复默认";
		_captureSlot = -1;
		Refresh();
	}

	private void OnMetaClicked(Variant meta)
	{
		var actionId = meta.AsString();
		if (string.IsNullOrEmpty(actionId)) return;
		if (!ContainsAction(actionId)) return;
		_selectedActionId = actionId;
		RenderList();
		UpdateButtons();
	}

	private static string EscapeBbcode(string text) =>
		text.Replace("[", "\\[").Replace("]", "\\]");
}
