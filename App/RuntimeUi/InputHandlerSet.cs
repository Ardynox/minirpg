using System;
using Godot;

namespace MiniRPG;

internal sealed record InputHandlerSet(
	Func<InputEvent, bool> HandlePanelChromeInput,
	Func<InputEvent, bool> HandlePanelDragInput,
	Func<InputEventKey, bool> HandleLayoutEditKeyInput,
	Func<InputEvent, bool> HandleLayoutEditInput,
	Func<InputEventKey, bool> HandleMapEditorKeyInput,
	Func<InputEvent, bool> HandleMapEditorInput,
	Func<InputEventKey, bool> HandleInspectModeKeyInput,
	Func<InputEventKey, bool> HandlePanelManagerKeyInput,
	Func<InputEventKey, bool> HandleInputModuleKeyInput,
	Func<InputEvent, RuntimeUiModeSnapshot, bool> HandleGameplayMouseInput);
