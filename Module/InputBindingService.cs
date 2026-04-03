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

public readonly record struct KeyChord(Key Keycode, bool Ctrl = false, bool Alt = false, bool Shift = false)
{
	public bool IsEmpty => Keycode == Key.None;

	public bool Matches(InputEventKey key) =>
		!IsEmpty
		&& key.Keycode == Keycode
		&& key.CtrlPressed == Ctrl
		&& key.AltPressed == Alt
		&& key.ShiftPressed == Shift;

	public string ToDisplayString()
	{
		if (IsEmpty) return "未绑定";
		var parts = new List<string>();
		if (Ctrl) parts.Add("Ctrl");
		if (Alt) parts.Add("Alt");
		if (Shift) parts.Add("Shift");
		var keyName = OS.GetKeycodeString(Keycode);
		if (string.IsNullOrWhiteSpace(keyName)) keyName = Keycode.ToString();
		parts.Add(keyName);
		return string.Join("+", parts);
	}

	public static bool TryFromEvent(InputEventKey key, out KeyChord chord)
	{
		chord = default;
		if (!key.Pressed) return false;

		if (key.Keycode is Key.None or Key.Ctrl or Key.Alt or Key.Shift or Key.Meta)
			return false;

		chord = new KeyChord(key.Keycode, key.CtrlPressed, key.AltPressed, key.ShiftPressed);
		return true;
	}
}

public sealed class BindingActionDef(
	string id,
	string label,
	InputBindingContext context,
	string? command,
	KeyChord primaryDefault,
	KeyChord secondaryDefault)
{
	public string Id { get; } = id;
	public string Label { get; } = label;
	public InputBindingContext Context { get; } = context;
	public string? Command { get; } = command;
	public KeyChord PrimaryDefault { get; } = primaryDefault;
	public KeyChord SecondaryDefault { get; } = secondaryDefault;
}

public readonly record struct BindingActionView(
	string Id,
	string Label,
	KeyChord Primary,
	KeyChord Secondary,
	string? Command);

public sealed class InputBindingService
{
	private const string BindingFileName = "keybindings.json";
	private const int BindingSchemaVersion = 1;

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

	public bool Rebind(InputBindingContext context, string actionId, int slot, KeyChord chord)
	{
		if (slot is < 0 or > 1) return false;
		if (chord.IsEmpty) return false;
		if (!_byId.TryGetValue(actionId, out var current)) return false;
		if (current.Def.Context != context) return false;

		var old = GetSlot(current, slot);
		if (old == chord) return true;

		if (TryFindOccupied(context, chord, actionId, slot, out var occupiedState, out var occupiedSlot))
		{
			SetSlot(occupiedState!, occupiedSlot, old);
		}

		SetSlot(current, slot, chord);
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
				var chord = new KeyChord((Key)row.Keycode, row.Ctrl, row.Alt, row.Shift);
				if (chord.IsEmpty) continue;
				SetSlot(state, row.Slot, chord);
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

	private static void WriteRow(BindingStore store, InputBindingContext context, string actionId, int slot, KeyChord chord)
	{
		store.Bindings.Add(new BindingStoreRow
		{
			Context = context.ToString(),
			ActionId = actionId,
			Slot = slot,
			Keycode = (long)chord.Keycode,
			Ctrl = chord.Ctrl,
			Alt = chord.Alt,
			Shift = chord.Shift,
		});
	}

	private bool TryFindOccupied(InputBindingContext context, KeyChord chord, string skipActionId, int skipSlot, out BindingActionState? state, out int slot)
	{
		foreach (var item in _byContext[context])
		{
			if (!(item.Primary == chord) && !(item.Secondary == chord))
				continue;

			if (item.Def.Id == skipActionId && ((item.Primary == chord && skipSlot == 0) || (item.Secondary == chord && skipSlot == 1)))
				continue;

			state = item;
			slot = item.Primary == chord ? 0 : 1;
			return true;
		}

		state = null;
		slot = -1;
		return false;
	}

	private static KeyChord GetSlot(BindingActionState state, int slot) => slot == 0 ? state.Primary : state.Secondary;
	private static void SetSlot(BindingActionState state, int slot, KeyChord chord)
	{
		if (slot == 0) state.Primary = chord;
		else state.Secondary = chord;
	}

	private static List<BindingActionDef> BuildDefaults()
	{
		var defs = new List<BindingActionDef>
		{
			new("move_north", "向上移动", InputBindingContext.Action, "w", new KeyChord(Key.W), new KeyChord(Key.Up)),
			new("move_south", "向下移动", InputBindingContext.Action, "s", new KeyChord(Key.S), new KeyChord(Key.Down)),
			new("move_west", "向左移动", InputBindingContext.Action, "a", new KeyChord(Key.A), new KeyChord(Key.Left)),
			new("move_east", "向右移动", InputBindingContext.Action, "d", new KeyChord(Key.D), new KeyChord(Key.Right)),
			new("look", "查看周围", InputBindingContext.Action, "look", new KeyChord(Key.L), default),
			new("toggle_render", "切换渲染", InputBindingContext.Action, ":render", new KeyChord(Key.R), default),
			new("interact", "交互", InputBindingContext.Action, ":interact", new KeyChord(Key.F), new KeyChord(Key.O)),
			new("inventory", "背包", InputBindingContext.Action, ":inventory", new KeyChord(Key.I), default),
			new("dig", "挖掘", InputBindingContext.Action, ":dig", new KeyChord(Key.G), default),
			new("skills", "技能管理", InputBindingContext.Action, ":skills", new KeyChord(Key.K), default),
			new("toggle_status", "状态面板", InputBindingContext.Action, ":toggle_status", new KeyChord(Key.H), default),
			new("quests", "任务面板", InputBindingContext.Action, ":quests", new KeyChord(Key.J), default),
			new("minimap", "小地图", InputBindingContext.Action, ":minimap", new KeyChord(Key.Tab), default),
			new("fogmap", "大地图", InputBindingContext.Action, ":fogmap", new KeyChord(Key.M), default),
			new("fogmap_center", "大地图居中", InputBindingContext.Action, ":fogmap_center", new KeyChord(Key.C), default),
			new("enter_stairs", "上下楼梯", InputBindingContext.Action, "enter", new KeyChord(Key.Space), default),
			new("open_settings", "设置", InputBindingContext.Action, ":settings", new KeyChord(Key.Escape), default),
			new("open_typing", "输入命令", InputBindingContext.Action, ":typing", new KeyChord(Key.Enter), new KeyChord(Key.T)),
			new("quicksave", "快速存档", InputBindingContext.Action, ":quicksave", new KeyChord(Key.F5), default),
			new("quickload", "快速读档", InputBindingContext.Action, ":quickload", new KeyChord(Key.F9), default),
			new("status_prev", "状态上一页", InputBindingContext.Action, ":status_prev", new KeyChord(Key.Comma), default),
			new("status_next", "状态下一页", InputBindingContext.Action, ":status_next", new KeyChord(Key.Period), default),

			new("typing_cancel", "取消输入", InputBindingContext.Typing, null, new KeyChord(Key.Escape), default),

			new("selection_cancel", "取消选择", InputBindingContext.Selection, null, new KeyChord(Key.Escape), default),
			new("selection_0", "选择 0", InputBindingContext.Selection, null, new KeyChord(Key.Key0), default),
			new("selection_1", "选择 1", InputBindingContext.Selection, null, new KeyChord(Key.Key1), default),
			new("selection_2", "选择 2", InputBindingContext.Selection, null, new KeyChord(Key.Key2), default),
			new("selection_3", "选择 3", InputBindingContext.Selection, null, new KeyChord(Key.Key3), default),
			new("selection_4", "选择 4", InputBindingContext.Selection, null, new KeyChord(Key.Key4), default),
			new("selection_5", "选择 5", InputBindingContext.Selection, null, new KeyChord(Key.Key5), default),
			new("selection_6", "选择 6", InputBindingContext.Selection, null, new KeyChord(Key.Key6), default),
			new("selection_7", "选择 7", InputBindingContext.Selection, null, new KeyChord(Key.Key7), default),
			new("selection_8", "选择 8", InputBindingContext.Selection, null, new KeyChord(Key.Key8), default),
			new("selection_9", "选择 9", InputBindingContext.Selection, null, new KeyChord(Key.Key9), default),

			new("direction_cancel", "取消方向选择", InputBindingContext.Direction, null, new KeyChord(Key.Escape), default),
			new("direction_n", "方向 北", InputBindingContext.Direction, null, new KeyChord(Key.W), new KeyChord(Key.Up)),
			new("direction_s", "方向 南", InputBindingContext.Direction, null, new KeyChord(Key.S), new KeyChord(Key.Down)),
			new("direction_w", "方向 西", InputBindingContext.Direction, null, new KeyChord(Key.A), new KeyChord(Key.Left)),
			new("direction_e", "方向 东", InputBindingContext.Direction, null, new KeyChord(Key.D), new KeyChord(Key.Right)),
		};

		return defs;
	}

	private sealed class BindingActionState(BindingActionDef def)
	{
		public BindingActionDef Def { get; } = def;
		public KeyChord Primary { get; set; }
		public KeyChord Secondary { get; set; }
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
		public long Keycode { get; set; }
		public bool Ctrl { get; set; }
		public bool Alt { get; set; }
		public bool Shift { get; set; }
	}
}
