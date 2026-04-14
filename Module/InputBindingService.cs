using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;
using MiniRPG.Core.Config;

namespace MiniRPG.Module;

public enum InputBindingContext
{
	Action,
	Typing,
	Selection,
	Direction,
}

public enum InputGestureKind
{
	None,
	Key,
	MouseWheel,
}

public readonly record struct InputGesture(
	InputGestureKind Kind,
	Key Keycode = Key.None,
	MouseButton MouseButtonCode = 0,
	bool Ctrl = false,
	bool Alt = false,
	bool Shift = false)
{
	public bool IsEmpty => Kind switch
	{
		InputGestureKind.Key => Keycode == Key.None,
		InputGestureKind.MouseWheel => MouseButtonCode is not MouseButton.WheelUp and not MouseButton.WheelDown,
		_ => true,
	};

	public static InputGesture FromKey(Key keycode, bool ctrl = false, bool alt = false, bool shift = false) =>
		new(InputGestureKind.Key, keycode, 0, ctrl, alt, shift);

	public static InputGesture FromMouseWheel(MouseButton button, bool ctrl = false, bool alt = false, bool shift = false) =>
		new(InputGestureKind.MouseWheel, Key.None, button, ctrl, alt, shift);

	public bool Matches(InputEventKey key) =>
		Kind == InputGestureKind.Key
		&& !IsEmpty
		&& key.Pressed
		&& key.Keycode == Keycode
		&& key.CtrlPressed == Ctrl
		&& key.AltPressed == Alt
		&& key.ShiftPressed == Shift;

	public bool Matches(InputEventMouseButton button) =>
		Kind == InputGestureKind.MouseWheel
		&& !IsEmpty
		&& button.Pressed
		&& button.ButtonIndex == MouseButtonCode
		&& button.CtrlPressed == Ctrl
		&& button.AltPressed == Alt
		&& button.ShiftPressed == Shift;

	public string ToDisplayString()
	{
		if (IsEmpty)
			return LocalizationService.T("ui.key_bindings.unbound");

		var parts = new List<string>();
		if (Ctrl) parts.Add("Ctrl");
		if (Alt) parts.Add("Alt");
		if (Shift) parts.Add("Shift");

		var inputName = Kind switch
		{
			InputGestureKind.Key => FormatKeycode(Keycode),
			InputGestureKind.MouseWheel => MouseButtonCode switch
			{
				MouseButton.WheelUp => "WheelUp",
				MouseButton.WheelDown => "WheelDown",
				_ => MouseButtonCode.ToString(),
			},
			_ => "None",
		};
		parts.Add(inputName);
		return string.Join("+", parts);
	}

	private static string FormatKeycode(Key keycode)
	{
		var name = keycode.ToString();
		if (name.StartsWith("Key", StringComparison.Ordinal) && name.Length == 4 && char.IsDigit(name[3]))
			return name[3].ToString();

		return keycode switch
		{
			Key.None => "None",
			Key.Space => "Space",
			Key.Enter => "Enter",
			Key.Escape => "Escape",
			Key.Up => "Up",
			Key.Down => "Down",
			Key.Left => "Left",
			Key.Right => "Right",
			_ => name,
		};
	}

	public static bool TryFromEvent(InputEventKey key, out InputGesture gesture)
	{
		gesture = default;
		if (!key.Pressed) return false;
		if (key.Keycode is Key.None or Key.Ctrl or Key.Alt or Key.Shift or Key.Meta)
			return false;

		gesture = FromKey(key.Keycode, key.CtrlPressed, key.AltPressed, key.ShiftPressed);
		return true;
	}

	public static bool TryFromEvent(InputEventMouseButton button, out InputGesture gesture)
	{
		gesture = default;
		if (!button.Pressed) return false;
		if (button.ButtonIndex is not MouseButton.WheelUp and not MouseButton.WheelDown)
			return false;

		gesture = FromMouseWheel(button.ButtonIndex, button.CtrlPressed, button.AltPressed, button.ShiftPressed);
		return true;
	}
}

public sealed class BindingActionDef(
	string id,
	string labelKey,
	InputBindingContext context,
	string? command,
	InputGesture primaryDefault,
	InputGesture secondaryDefault)
{
	public string Id { get; } = id;
	public string LabelKey { get; } = labelKey;
	public InputBindingContext Context { get; } = context;
	public string? Command { get; } = command;
	public InputGesture PrimaryDefault { get; } = primaryDefault;
	public InputGesture SecondaryDefault { get; } = secondaryDefault;
}

public readonly record struct BindingActionView(
	string Id,
	string Label,
	InputGesture Primary,
	InputGesture Secondary,
	string? Command);

public sealed class InputBindingService
{
	private const string BindingFileName = "keybindings.json";
	private const int BindingSchemaVersion = 3;

	private readonly Dictionary<InputBindingContext, List<BindingActionState>> _byContext = [];
	private readonly Dictionary<string, BindingActionState> _byId = [];
	private readonly string _savePath;

	public event Action? Changed;

	private string SavePath => _savePath;

	public InputBindingService(string? savePath = null)
	{
		_savePath = string.IsNullOrWhiteSpace(savePath)
			? ResolveFallbackSavePath()
			: Path.GetFullPath(savePath);

		foreach (InputBindingContext ctx in Enum.GetValues(typeof(InputBindingContext)))
			_byContext[ctx] = [];

		foreach (var def in BuildDefaults())
		{
			var state = new BindingActionState(def);
			_byContext[def.Context].Add(state);
			_byId[def.Id] = state;
		}

		ResetAllNoSave();
		LoadOrInitFromDisk();
	}

	public IReadOnlyList<BindingActionView> GetActions(InputBindingContext context)
	{
		var list = _byContext[context];
		var result = new List<BindingActionView>(list.Count);
		foreach (var state in list)
		{
			result.Add(new BindingActionView(
				state.Def.Id,
				LocalizationService.T(state.Def.LabelKey),
				state.Primary,
				state.Secondary,
				state.Def.Command));
		}
		return result;
	}

	public bool Resolve(InputBindingContext context, InputEventKey key, out string actionId, out string? command)
	{
		actionId = "";
		command = null;
		foreach (var state in _byContext[context])
		{
			if (!state.Primary.Matches(key) && !state.Secondary.Matches(key))
				continue;
			actionId = state.Def.Id;
			command = state.Def.Command;
			return true;
		}
		return false;
	}

	public bool Resolve(InputBindingContext context, InputEventMouseButton button, out string actionId, out string? command)
	{
		actionId = "";
		command = null;
		foreach (var state in _byContext[context])
		{
			if (!state.Primary.Matches(button) && !state.Secondary.Matches(button))
				continue;
			actionId = state.Def.Id;
			command = state.Def.Command;
			return true;
		}
		return false;
	}

	public bool Rebind(InputBindingContext context, string actionId, int slot, InputGesture gesture)
	{
		if (slot is < 0 or > 1) return false;
		if (gesture.IsEmpty) return false;
		if (!_byId.TryGetValue(actionId, out var current)) return false;
		if (current.Def.Context != context) return false;

		var old = GetSlot(current, slot);
		if (old == gesture) return true;

		if (TryFindOccupied(context, gesture, actionId, slot, out var occupiedState, out var occupiedSlot))
			SetSlot(occupiedState!, occupiedSlot, old);

		SetSlot(current, slot, gesture);
		SaveToDisk();
		Changed?.Invoke();
		return true;
	}

	public void ResetContext(InputBindingContext context)
	{
		foreach (var state in _byContext[context])
		{
			state.Primary = state.Def.PrimaryDefault;
			state.Secondary = state.Def.SecondaryDefault;
		}
		SaveToDisk();
		Changed?.Invoke();
	}

	public void ResetAll()
	{
		ResetAllNoSave();
		SaveToDisk();
		Changed?.Invoke();
	}

	private void ResetAllNoSave()
	{
		foreach (var (_, state) in _byId)
		{
			state.Primary = state.Def.PrimaryDefault;
			state.Secondary = state.Def.SecondaryDefault;
		}
	}

	private void LoadOrInitFromDisk()
	{
		if (!File.Exists(SavePath))
		{
			SaveToDisk();
			return;
		}

		try
		{
			var json = File.ReadAllText(SavePath);
			var store = JsonSerializer.Deserialize<BindingStore>(json, new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true,
			});
			if (store == null || store.Version != BindingSchemaVersion || store.Bindings == null || store.Bindings.Count == 0)
			{
				ResetAllNoSave();
				SaveToDisk();
				return;
			}

			ResetAllNoSave();
			foreach (var row in store.Bindings)
			{
				if (!_byId.TryGetValue(row.ActionId, out var state)) continue;
				if (!Enum.TryParse<InputBindingContext>(row.Context, ignoreCase: true, out var context)) continue;
				if (state.Def.Context != context) continue;
				if (row.Slot is < 0 or > 1) continue;

				var gesture = ReadGesture(row);
				if (gesture.IsEmpty) continue;
				SetSlot(state, row.Slot, gesture);
			}
		}
		catch (Exception)
		{
			ResetAllNoSave();
			SaveToDisk();
		}
	}

	private void SaveToDisk()
	{
		var dir = Path.GetDirectoryName(SavePath);
		if (!string.IsNullOrEmpty(dir))
			Directory.CreateDirectory(dir);

		var store = new BindingStore { Version = BindingSchemaVersion, Bindings = [] };
		foreach (var (context, list) in _byContext)
		{
			foreach (var state in list)
			{
				WriteRow(store, context, state.Def.Id, 0, state.Primary);
				WriteRow(store, context, state.Def.Id, 1, state.Secondary);
			}
		}

		var json = JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true });
		File.WriteAllText(SavePath, json);
	}

	private static InputGesture ReadGesture(BindingStoreRow row)
	{
		if (string.IsNullOrWhiteSpace(row.Kind))
			return InputGesture.FromKey((Key)row.Keycode, row.Ctrl, row.Alt, row.Shift);

		if (!Enum.TryParse<InputGestureKind>(row.Kind, ignoreCase: true, out var kind))
			return default;

		return kind switch
		{
			InputGestureKind.Key => InputGesture.FromKey((Key)row.Keycode, row.Ctrl, row.Alt, row.Shift),
			InputGestureKind.MouseWheel => InputGesture.FromMouseWheel((MouseButton)row.MouseButton, row.Ctrl, row.Alt, row.Shift),
			_ => default,
		};
	}

	private static void WriteRow(BindingStore store, InputBindingContext context, string actionId, int slot, InputGesture gesture)
	{
		store.Bindings.Add(new BindingStoreRow
		{
			Context = context.ToString(),
			ActionId = actionId,
			Slot = slot,
			Kind = gesture.Kind.ToString(),
			Keycode = gesture.Kind == InputGestureKind.Key ? (long)gesture.Keycode : 0,
			MouseButton = gesture.Kind == InputGestureKind.MouseWheel ? (int)gesture.MouseButtonCode : 0,
			Ctrl = gesture.Ctrl,
			Alt = gesture.Alt,
			Shift = gesture.Shift,
		});
	}

	private bool TryFindOccupied(InputBindingContext context, InputGesture gesture, string skipActionId, int skipSlot, out BindingActionState? state, out int slot)
	{
		foreach (var item in _byContext[context])
		{
			if (!(item.Primary == gesture) && !(item.Secondary == gesture))
				continue;

			if (item.Def.Id == skipActionId && ((item.Primary == gesture && skipSlot == 0) || (item.Secondary == gesture && skipSlot == 1)))
				continue;

			state = item;
			slot = item.Primary == gesture ? 0 : 1;
			return true;
		}

		state = null;
		slot = -1;
		return false;
	}

	private static InputGesture GetSlot(BindingActionState state, int slot) => slot == 0 ? state.Primary : state.Secondary;

	private static void SetSlot(BindingActionState state, int slot, InputGesture gesture)
	{
		if (slot == 0) state.Primary = gesture;
		else state.Secondary = gesture;
	}

	private static string ResolveFallbackSavePath()
	{
		var baseDir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
		if (string.IsNullOrWhiteSpace(baseDir))
			baseDir = AppContext.BaseDirectory;

		return Path.Combine(baseDir, "MiniRPG", BindingFileName);
	}

	private static List<BindingActionDef> BuildDefaults()
	{
		var defs = new List<BindingActionDef>
		{
			new("move_north", "input.action.move_north", InputBindingContext.Action, "w", InputGesture.FromKey(Key.W), InputGesture.FromKey(Key.Up)),
			new("move_south", "input.action.move_south", InputBindingContext.Action, "s", InputGesture.FromKey(Key.S), InputGesture.FromKey(Key.Down)),
			new("move_west", "input.action.move_west", InputBindingContext.Action, "a", InputGesture.FromKey(Key.A), InputGesture.FromKey(Key.Left)),
			new("move_east", "input.action.move_east", InputBindingContext.Action, "d", InputGesture.FromKey(Key.D), InputGesture.FromKey(Key.Right)),
			new("look", "input.action.look", InputBindingContext.Action, "look", InputGesture.FromKey(Key.L), default),
			new("toggle_render", "input.action.toggle_render", InputBindingContext.Action, ":render", InputGesture.FromKey(Key.R, ctrl: true), default),
			new("interact", "input.action.interact", InputBindingContext.Action, ":interact", InputGesture.FromKey(Key.F), InputGesture.FromKey(Key.O)),
			new("inventory", "input.action.inventory", InputBindingContext.Action, ":inventory", InputGesture.FromKey(Key.I), default),
			new("rest", "input.action.rest", InputBindingContext.Action, ":rest", InputGesture.FromKey(Key.Y), default),
			new("dig", "input.action.dig", InputBindingContext.Action, ":dig", InputGesture.FromKey(Key.G), default),
			new("skills", "input.action.skills", InputBindingContext.Action, ":skills", InputGesture.FromKey(Key.K), default),
			new("skillbar", "input.action.skillbar", InputBindingContext.Action, ":skillbar", InputGesture.FromKey(Key.B), default),
			new("debug_panel", "input.action.debug_panel", InputBindingContext.Action, ":debug_panel", InputGesture.FromKey(Key.F12), default),
			new("skill_prev", "input.action.skill_prev", InputBindingContext.Action, ":skill_prev", InputGesture.FromMouseWheel(MouseButton.WheelUp), default),
			new("skill_next", "input.action.skill_next", InputBindingContext.Action, ":skill_next", InputGesture.FromMouseWheel(MouseButton.WheelDown), default),
			new("toggle_status", "input.action.toggle_status", InputBindingContext.Action, ":toggle_status", InputGesture.FromKey(Key.H), default),
			new("cycle_party", "input.action.cycle_party", InputBindingContext.Action, ":cycle_party", InputGesture.FromKey(Key.P), default),
			new("quests", "input.action.quests", InputBindingContext.Action, ":quests", InputGesture.FromKey(Key.J), default),
			new("camera_toggle_mode", "input.action.camera_toggle_mode", InputBindingContext.Action, ":camera_toggle_mode", InputGesture.FromKey(Key.V), default),
			new("minimap", "input.action.minimap", InputBindingContext.Action, ":minimap", InputGesture.FromKey(Key.Tab), default),
			new("fogmap", "input.action.fogmap", InputBindingContext.Action, ":fogmap", InputGesture.FromKey(Key.M), default),
			new("fogmap_center", "input.action.fogmap_center", InputBindingContext.Action, ":fogmap_center", InputGesture.FromKey(Key.C), default),
			new("enter_stairs", "input.action.enter_stairs", InputBindingContext.Action, "enter", InputGesture.FromKey(Key.Space), default),
			new("open_settings", "input.action.open_settings", InputBindingContext.Action, ":settings", InputGesture.FromKey(Key.Escape), default),
			new("open_typing", "input.action.open_typing", InputBindingContext.Action, ":typing", InputGesture.FromKey(Key.Enter), InputGesture.FromKey(Key.T)),
			new("quicksave", "input.action.quicksave", InputBindingContext.Action, ":quicksave", InputGesture.FromKey(Key.F5), default),
			new("quickload", "input.action.quickload", InputBindingContext.Action, ":quickload", InputGesture.FromKey(Key.F9), default),
			new("status_prev", "input.action.status_prev", InputBindingContext.Action, ":status_prev", InputGesture.FromKey(Key.Comma), default),
			new("status_next", "input.action.status_next", InputBindingContext.Action, ":status_next", InputGesture.FromKey(Key.Period), default),

			new("typing_cancel", "input.typing.cancel", InputBindingContext.Typing, null, InputGesture.FromKey(Key.Escape), default),

			new("selection_cancel", "input.selection.cancel", InputBindingContext.Selection, null, InputGesture.FromKey(Key.Escape), default),
			new("selection_0", "input.selection.0", InputBindingContext.Selection, null, InputGesture.FromKey(Key.Key0), default),
			new("selection_1", "input.selection.1", InputBindingContext.Selection, null, InputGesture.FromKey(Key.Key1), default),
			new("selection_2", "input.selection.2", InputBindingContext.Selection, null, InputGesture.FromKey(Key.Key2), default),
			new("selection_3", "input.selection.3", InputBindingContext.Selection, null, InputGesture.FromKey(Key.Key3), default),
			new("selection_4", "input.selection.4", InputBindingContext.Selection, null, InputGesture.FromKey(Key.Key4), default),
			new("selection_5", "input.selection.5", InputBindingContext.Selection, null, InputGesture.FromKey(Key.Key5), default),
			new("selection_6", "input.selection.6", InputBindingContext.Selection, null, InputGesture.FromKey(Key.Key6), default),
			new("selection_7", "input.selection.7", InputBindingContext.Selection, null, InputGesture.FromKey(Key.Key7), default),
			new("selection_8", "input.selection.8", InputBindingContext.Selection, null, InputGesture.FromKey(Key.Key8), default),
			new("selection_9", "input.selection.9", InputBindingContext.Selection, null, InputGesture.FromKey(Key.Key9), default),

			new("direction_cancel", "input.direction.cancel", InputBindingContext.Direction, null, InputGesture.FromKey(Key.Escape), default),
			new("direction_n", "input.direction.north", InputBindingContext.Direction, null, InputGesture.FromKey(Key.W), InputGesture.FromKey(Key.Up)),
			new("direction_s", "input.direction.south", InputBindingContext.Direction, null, InputGesture.FromKey(Key.S), InputGesture.FromKey(Key.Down)),
			new("direction_w", "input.direction.west", InputBindingContext.Direction, null, InputGesture.FromKey(Key.A), InputGesture.FromKey(Key.Left)),
			new("direction_e", "input.direction.east", InputBindingContext.Direction, null, InputGesture.FromKey(Key.D), InputGesture.FromKey(Key.Right)),
		};

		return defs;
	}

	private sealed class BindingActionState(BindingActionDef def)
	{
		public BindingActionDef Def { get; } = def;
		public InputGesture Primary { get; set; }
		public InputGesture Secondary { get; set; }
	}

	private sealed class BindingStore
	{
		public int Version { get; set; }
		public List<BindingStoreRow> Bindings { get; set; } = [];
	}

	private sealed class BindingStoreRow
	{
		public string Context { get; set; } = "";
		public string ActionId { get; set; } = "";
		public int Slot { get; set; }
		public string Kind { get; set; } = "";
		public long Keycode { get; set; }
		public int MouseButton { get; set; }
		public bool Ctrl { get; set; }
		public bool Alt { get; set; }
		public bool Shift { get; set; }
	}
}
