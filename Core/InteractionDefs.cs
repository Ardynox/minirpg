using System.Collections.Generic;

namespace MiniRPG.Core;

/// <summary>
/// 交互定义注册表：从 PresetDB 读取，不再硬编码。
/// </summary>
public static class InteractionDefs
{
	public static List<InteractionDef> All => PresetDB.Interactions;
}
