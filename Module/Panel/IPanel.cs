using Godot;

namespace MiniRPG.Module.Panel;

/// <summary>
/// 列表型面板的公共契约：提供"当前选中"和"上下移动"语义，
/// 让外层可以做统一的键盘导航（PageUp / PageDown / Home / End）而无需知道具体面板实现。
/// 默认由 <see cref="ListPanelBase"/> 实现；不继承 ListPanelBase 的列表面板
/// （例如 SaveBrowserModule / WorldManagerModule）可选择直接实现此接口来纳入统一导航。
/// </summary>
public interface IFocusableListPanel : IPanel
{
	/// <summary>当前数据行数（不含"空列表提示"占位行）。</summary>
	int RowCount { get; }

	/// <summary>当前选中索引，0 表示第一行，-1 表示无选中。</summary>
	int SelectedIndex { get; }

	/// <summary>相对移动光标；实现需自行 clamp 到 [0, RowCount-1]。</summary>
	void MoveSelection(int delta);

	/// <summary>激活当前选中行（等价于 Enter 键）。</summary>
	void ActivateSelection();
}

/// <summary>
/// 统一面板接口。所有可聚焦的面板（背包、宝箱、对话、状态……）实现此接口，
/// PanelManager 通过它管理焦点切换、边框刷新、键盘路由、鼠标点击。
/// </summary>
public interface IPanel
{
	/// <summary>面板的唯一标识字符串，用于 PanelManager 注册和查找。</summary>
	string PanelId { get; }

	/// <summary>对应的 Godot PanelContainer 节点。</summary>
	PanelContainer PanelNode { get; }

	/// <summary>面板当前是否可见。</summary>
	bool Visible { get; set; }

	/// <summary>
	/// 面板是否能获取焦点。
	/// 返回 false 的面板（如 SkillPanel、GroundPanel）只参与边框刷新，不参与焦点切换和键盘路由。
	/// </summary>
	bool CanFocus => true;

	/// <summary>
	/// 当面板有焦点时，是否吞掉未处理按键。
	/// 默认 false：未处理按键继续回落给 InputModule，让全局快捷键持续生效。
	/// </summary>
	bool ConsumeUnhandledKeys => false;

	bool AllowGlobalClose => true;

	/// <summary>
	/// 处理键盘命令字符串。返回 true 表示已消费，false 表示未处理。
	/// 通用命令由 PanelManager 预处理（W/S→"up"/"down"，Esc→"close"，数字→"1"-"9"），
	/// 面板只需处理自己关心的命令。
	/// </summary>
	bool HandleCommand(string cmd);

	/// <summary>面板获得焦点时调用。</summary>
	void OnFocus() { }

	/// <summary>面板失去焦点时调用。</summary>
	void OnBlur() { }

	/// <summary>脏标记：数据已变化，下次 _Process 时需要刷新 UI。</summary>
	bool Dirty { get; set; }

	/// <summary>若 Dirty 为 true，执行刷新并清除标记。</summary>
	void FlushIfDirty() { }
}
