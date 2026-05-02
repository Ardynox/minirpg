namespace MiniRPG.Core.Debug;

/// <summary>
/// 草地渲染模式：控制 overlay/shader 选路。
///
/// <see cref="Shader3D"/>（默认）：使用 GPU MultiMesh + 程序化 shader 绘制真实立体草叶（顶点风摆 + 片段色彩）。
/// <see cref="Legacy"/>：使用 CPU 预烤 atlas overlay（历史 Tier 1 路径，保留作低端兜底与 A/B 对照）。
/// <see cref="Off"/>：完全不画草（仅裸 dirt）。
///
/// 进程级 visual flag，不入存档、不走多人同步。
/// </summary>
public enum GrassRenderMode
{
	Shader3D = 0,
	Legacy = 1,
	Off = 2,
}
