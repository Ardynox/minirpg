namespace MiniRPG.Module.Panel;

/// <summary>
/// 让面板/HUD 模块声明"我有 hover tooltip 要挂"，由 <c>Main</c> 在 <c>RichTooltipLayer</c> 创建后
/// 统一调用 <see cref="RegisterTooltips"/> 把 layer 注入。
///
/// <para>
/// 实现方约定：
/// <list type="bullet">
///   <item>方法内只持有 layer 引用（如 <c>_tooltipLayer = layer</c>），不立即 Attach。</item>
///   <item>真正 <c>Attach</c> 在 Refresh / RebuildRows / ApplyRowContent 等渲染落点调用。</item>
///   <item>factory 闭包必须捕获 index 或 InstanceId 而不是 Item / 行引用，避免行池复用 + 排序变更后取错数据。</item>
///   <item>factory 内部对 GameState / Actor / Item 必须 null check（联机断线 / 切换会话瞬间数据可能为 null）。</item>
/// </list>
/// </para>
///
/// 详细规范见 <c>Docs/界面与面板.md</c> 第 9 节。
/// </summary>
public interface ITooltipRegistrar
{
	void RegisterTooltips(RichTooltipLayer layer);
}
