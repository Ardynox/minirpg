using Godot;

namespace MiniRPG.Module.Panel;

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
	/// 处理键盘命令字符串。返回 true 表示已消费，false 表示未处理。
	/// 通用命令由 PanelManager 预处理（W/S→"up"/"down"，Esc→"close"，数字→"1"-"9"），
	/// 面板只需处理自己关心的命令。
	/// </summary>
	bool HandleCommand(string cmd);

	/// <summary>面板获得焦点时调用。</summary>
	void OnFocus() { }

	/// <summary>面板失去焦点时调用。</summary>
	void OnBlur() { }
}
