namespace MiniRPG.Core.Debug;

/// <summary>
/// 草地 overlay 调试用强制模式。
/// Auto = 走 chunk.GrassCover 数据；On = 全部视为 cover=255；Off = 完全跳过 overlay。
/// 由 <see cref="DebugModule.ForceGrassOverlay"/> 控制；Wave 2.2 接 OverlayPass 时读取。
/// </summary>
public enum GrassOverlayForceMode
{
	Auto = 0,
	On = 1,
	Off = 2,
}
