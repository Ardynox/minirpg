using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;

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
		if (IsEmpty) return "未绑定";

		var parts = new List<string>();
		if (Ctrl) parts.Add("Ctrl");
		if (Alt) parts.Add("Alt");
		if (Shift) parts.Add("Shift");

		var inputName = Kind switch
		{
			InputGestureKind.Key => string.IsNullOrWhiteSpace(OS.GetKeycodeString(Keycode))
				? Keycode.ToString()
				: OS.GetKeycodeString(Keycode),
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
	string label,
	InputBindingContext context,
	string? command,
	InputGesture primaryDefault,
	InputGesture secondaryDefault)
{
	public string Id { get; } = id;
	public string Label { get; } = label;
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
	private const int BindingSchemaVersion = 2;

	private readonly Dictionary<InputBindingContext, List<BindingActionState>> _byContext = [];
	private readonly Dictionary<string, BindingActionState> _byId = [];

	public event Action? Changed;

	private string SavePath => ProjectSettings.GlobalizePath($"user://{BindingFileName}");

	public InputBindingService()
	{
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
			result.Add(new BindingActionView(state.Def.Id, state.Def.Label, state.Primary, state.Secondary, state.Def.Command));
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
			var store = JsonSerializer.Deserialize<BindingStore>(json);
			if (store == null || store.Bindings == null || store.Bindings.Count == 0)
			{
				ResetAllNoSave();
				SaveToDisk();
				return;
			}

			ResetAllNoSave();
			foreach (var row in store.Bindings)
			{
				if (!_byId.TryGetValue(row.ActionId, out var state)) continue;
				if (!Enum.TryParse<InputBindingContext>(row.Context, out var context)) continue;
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

		if (!Enum.TryParse<InputGestureKind>(row.Kind, out var kind))
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

	private static List<BindingActionDef> BuildDefaults()
	{
		var defs = new List<BindingActionDef>
		{
			new("move_north", "向上移动", InputBindingContext.Action, "w", InputGesture.FromKey(Key.W), InputGesture.FromKey(Key.Up)),
			new("move_south", "向下移动", InputBindingContext.Action, "s", InputGesture.FromKey(Key.S), InputGesture.FromKey(Key.Down)),
			new("move_west", "向左移动", InputBindingContext.Action, "a", InputGesture.FromKey(Key.A), InputGesture.FromKey(Key.Left)),
			new("move_east", "向右移动", InputBindingContext.Action, "d", InputGesture.FromKey(Key.D), InputGesture.FromKey(Key.Right)),
			new("look", "查看周围", InputBindingContext.Action, "look", InputGesture.FromKey(Key.L), default),
			new("toggle_render", "切换渲染", InputBindingContext.Action, ":render", InputGesture.FromKey(Key.R), default),
			new("interact", "交互", InputBindingContext.Action, ":interact", InputGesture.FromKey(Key.F), InputGesture.FromKey(Key.O)),
			new("inventory", "背包", InputBindingContext.Action, ":inventory", InputGesture.FromKey(Key.I), default),
			new("dig", "挖掘", InputBindingContext.Action, ":dig", InputGesture.FromKey(Key.G), default),
			new("skills", "技能管理", InputBindingContext.Action, ":skills", InputGesture.FromKey(Key.K), default),
			new("skillbar", "技能栏", InputBindingContext.Action, ":skillbar", InputGesture.FromKey(Key.B), default),
			new("skill_prev", "上一技能", InputBindingContext.Action, ":skill_prev", InputGesture.FromMouseWheel(MouseButton.WheelUp), default),
			new("skill_next", "下一技能", InputBindingContext.Action, ":skill_next", InputGesture.FromMouseWheel(MouseButton.WheelDown), default),
			new("toggle_status", "状态面板", InputBindingContext.Action, ":toggle_status", InputGesture.FromKey(Key.H), default),
			new("quests", "任务面板", InputBindingContext.Action, ":quests", InputGesture.FromKey(Key.J), default),
			new("minimap", "小地图", InputBindingContext.Action, ":minimap", InputGesture.FromKey(Key.Tab), default),
			new("fogmap", "大地图", InputBindingContext.Action, ":fogmap", InputGesture.FromKey(Key.M), default),
			new("fogmap_center", "大地图居中", InputBindingContext.Action, ":fogmap_center", InputGesture.FromKey(Key.C), default),
			new("enter_stairs", "上下楼梯", InputBindingContext.Action, "enter", InputGesture.FromKey(Key.Space), default),
			new("open_settings", "设置", InputBindingContext.Action, ":settings", InputGesture.FromKey(Key.Escape), default),
			new("open_typing", "输入命令", InputBindingContext.Action, ":typing", InputGesture.FromKey(Key.Enter), InputGesture.FromKey(Key.T)),
			new("quicksave", "快速存档", InputBindingContext.Action, ":quicksave", InputGesture.FromKey(Key.F5), default),
			new("quickload", "快速读档", InputBindingContext.Action, ":quickload", InputGesture.FromKey(Key.F9), default),
			new("status_prev", "状态上一页", InputBindingContext.Action, ":status_prev", InputGesture.FromKey(Key.Comma), default),
			new("status_next", "状态下一页", InputBindingContext.Action, ":status_next", InputGesture.FromKey(Key.Period), default),

			new("typing_cancel", "取消输入", InputBindingContext.Typing, null, InputGesture.FromKey(Key.Escape), default),

			new("selection_cancel", "取消选择", InputBindingContext.Selection, null, InputGesture.FromKey(Key.Escape), default),
			new("selection_0", "选择 0", InputBindingContext.Selection, null, InputGesture.FromKey(Key.Key0), default),
			new("selection_1", "选择 1", InputBindingContext.Selection, null, InputGesture.FromKey(Key.Key1), default),
			new("selection_2", "选择 2", InputBindingContext.Selection, null, InputGesture.FromKey(Key.Key2), default),
			new("selection_3", "选择 3", InputBindingContext.Selection, null, InputGesture.FromKey(Key.Key3), default),
			new("selection_4", "选择 4", InputBindingContext.Selection, null, InputGesture.FromKey(Key.Key4), default),
			new("selection_5", "选择 5", InputBindingContext.Selection, null, InputGesture.FromKey(Key.Key5), default),
			new("selection_6", "选择 6", InputBindingContext.Selection, null, InputGesture.FromKey(Key.Key6), default),
			new("selection_7", "选择 7", InputBindingContext.Selection, null, InputGesture.FromKey(Key.Key7), default),
			new("selection_8", "选择 8", InputBindingContext.Selection, null, InputGesture.FromKey(Key.Key8), default),
			new("selection_9", "选择 9", InputBindingContext.Selection, null, InputGesture.FromKey(Key.Key9), default),

			new("direction_cancel", "取消方向选择", InputBindingContext.Direction, null, InputGesture.FromKey(Key.Escape), default),
			new("direction_n", "方向 北", InputBindingContext.Direction, null, InputGesture.FromKey(Key.W), InputGesture.FromKey(Key.Up)),
			new("direction_s", "方向 南", InputBindingContext.Direction, null, InputGesture.FromKey(Key.S), InputGesture.FromKey(Key.Down)),
			new("direction_w", "方向 西", InputBindingContext.Direction, null, InputGesture.FromKey(Key.A), InputGesture.FromKey(Key.Left)),
			new("direction_e", "方向 东", InputBindingContext.Direction, null, InputGesture.FromKey(Key.D), InputGesture.FromKey(Key.Right)),
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
