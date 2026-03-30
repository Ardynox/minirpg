using System.Collections.Generic;

namespace MiniRPG.Core;

/// <summary>
/// Actor 模板入口：委托给 PresetDB，保持调用方兼容。
/// </summary>
public static class ActorTemplates
{
	public static Actor Spawn(string templateId, string instanceId) =>
		PresetDB.SpawnActor(templateId, instanceId);

	public static IReadOnlyCollection<string> MonsterIds =>
		PresetDB.MonsterIds;
}
