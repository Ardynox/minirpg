using System;
using System.Collections.Generic;
using MiniRPG.Core.AI;

namespace MiniRPG.Module;

/// <summary>
/// 战斗 UI 流程：碰撞战斗、动作选择、肢体瞄准、怪物反击、击杀处理。
/// </summary>
public class CombatUIModule
{
	private readonly IGameUI _ui;

	public CombatUIModule(IGameUI ui) => _ui = ui;

	public void HandleCombatBump(GameEvent e)
	{
		var player = ActorModule.GetPlayer(_ui.State);
		var target = e.TargetId != null ? ActorModule.GetById(_ui.State, e.TargetId) : null;
		if (player == null || target == null) return;

		var actions = CombatModule.GetAttackActions(player);
		if (actions.Count == 0 || target.Limbs.Count == 0)
		{
			_ui.AddLog("你无法攻击！");
			return;
		}

		var rng = new Random(_ui.State.RngSeed + _ui.State.Turn);
		var action = actions[0];
		var limb = target.Limbs[rng.Next(target.Limbs.Count)];

		var combatEvents = CombatModule.Attack(_ui.State, player, target, action, limb);
		_ui.Dispatch(combatEvents);

		if (!combatEvents.Exists(ev => ev.Type is "actor_killed" or "actor_incapacitated"))
			MonsterCounterAttack(target);

		TickAllBuffs();
		_ui.FlushMap();
	}

	public void OpenCombatMenu(GameEvent e)
	{
		var player = ActorModule.GetPlayer(_ui.State);
		var target = e.TargetId != null ? ActorModule.GetById(_ui.State, e.TargetId) : null;
		if (player == null || target == null) return;

		ShowActionSelection(player, target);
	}

	public void HandleActorKilled(GameEvent e)
	{
		_ui.AddLog($"💀 击杀了{e.TargetActorName}！");
		var player = ActorModule.GetPlayer(_ui.State);
		if (player != null)
		{
			var goldDrop = e.Damage > 0 ? e.Damage : 5;
			player.Gold += goldDrop;
			_ui.AddLog($"  💰 获得 {goldDrop}G (总计: {player.Gold}G)");
		}

		GenerateLoot(e);
	}

	/// <summary>怪物死亡时按概率在死亡位置生成掉落物。</summary>
	private void GenerateLoot(GameEvent e)
	{
		var rng = new Random(_ui.State.RngSeed + _ui.State.Turn + (e.TargetId ?? "").GetHashCode());
		if (rng.Next(100) >= 40) return;

		var pool = new List<string>(PresetDB.Items.Keys);
		if (pool.Count == 0) return;

		var itemId = pool[rng.Next(pool.Count)];
		var item = PresetDB.CloneItem(itemId);
		MapModule.PlaceItem(_ui.State, e.TargetX, e.TargetY, item);
		_ui.AddLog($"  📦 {e.TargetActorName}掉落了 {item.Name}");
	}

	private void ShowActionSelection(Actor player, Actor target)
	{
		var actions = CombatModule.GetAttackActions(player);
		var allSkills = SkillQuery.GetAll(player);
		var hasBlock = allSkills.Exists(a => a.EffectType == "block");

		_ui.AddLog($"═══ 攻击 {target.DisplayName} ═══");
		for (var i = 0; i < actions.Count; i++)
		{
			var a = actions[i];
			var estDmg = CombatModule.CalcDamage(player, a, target);
			var dmgLabel = a.DamageType switch
			{
				DamageTypes.Sharp => "锐",
				DamageTypes.Blunt => "钝",
				DamageTypes.Poison => "毒",
				_ => "",
			};
			_ui.AddLog($"  [{i + 1}] {a.Name} ({dmgLabel}伤害:{estDmg})");
		}
		var blockIdx = actions.Count + 1;
		if (hasBlock)
			_ui.AddLog($"  [{blockIdx}] 格挡 (防御+5, 1回合)");
		_ui.AddLog("  [0] 取消");

		_ui.EnterSelection(n =>
		{
			if (n == 0) { _ui.AddLog("取消攻击"); return; }

			if (hasBlock && n == blockIdx)
			{
				var blockDef = allSkills.Find(a => a.EffectType == "block")!;
				var blockEvents = CombatModule.Attack(_ui.State, player, target, blockDef, target.Limbs[0]);
				_ui.Dispatch(blockEvents);
				MonsterCounterAttack(target);
				TickAllBuffs();
				_ui.FlushMap();
				return;
			}

			if (n < 1 || n > actions.Count) { _ui.AddLog("无效选择"); return; }

			var chosen = actions[n - 1];
			ShowLimbTargetSelection(player, target, chosen);
		});
	}

	private void ShowLimbTargetSelection(Actor player, Actor target, InteractionDef action)
	{
		var limbs = target.Limbs;
		if (limbs.Count == 0)
		{
			_ui.AddLog($"{target.DisplayName}已经没有可攻击的肢体了");
			return;
		}

		_ui.AddLog($"选择目标肢体 ({target.DisplayName})：");
		for (var i = 0; i < limbs.Count; i++)
		{
			var l = limbs[i];
			var vital = l.Tags.ContainsKey("要害") ? " [要害]" : "";
			_ui.AddLog($"  [{i + 1}] {l.Name} ({l.Durability}/{l.MaxDurability}){vital}");
		}
		_ui.AddLog("  [0] 返回");

		_ui.EnterSelection(n =>
		{
			if (n == 0) { ShowActionSelection(player, target); return; }
			if (n < 1 || n > limbs.Count) { _ui.AddLog("无效选择"); return; }

			var targetLimb = limbs[n - 1];
			var combatEvents = CombatModule.Attack(_ui.State, player, target, action, targetLimb);
			_ui.Dispatch(combatEvents);

			var eliminated = combatEvents.Exists(ev => ev.Type is "actor_killed" or "actor_incapacitated");
			if (!eliminated)
			{
				var stillAlive = ActorModule.GetById(_ui.State, target.Id);
				if (stillAlive != null)
					MonsterCounterAttack(stillAlive);
			}

			TickAllBuffs();
			_ui.FlushMap();
		});
	}

	public void MonsterCounterAttack(Actor monster)
	{
		var mEvents = AIDispatcher.DecideAndExecuteOne(_ui.State, monster);
		_ui.Dispatch(mEvents);
	}

	private void TickAllBuffs()
	{
		var player = ActorModule.GetPlayer(_ui.State);
		player?.TickBuffs();
	}
}
