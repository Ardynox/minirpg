using System;
using System.Collections.Generic;
using MiniRPG.Core;

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
			MonsterCounterAttack(player, target);

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
	}

	private void ShowActionSelection(Actor player, Actor target)
	{
		var actions = CombatModule.GetAttackActions(player);
		var allActions = ActionQuery.GetAvailable(player, ActionDefs.All);
		var hasBlock = allActions.Exists(a => a.EffectType == "block");

		_ui.AddLog($"═══ 攻击 {target.DisplayName} ═══");
		for (var i = 0; i < actions.Count; i++)
		{
			var a = actions[i];
			var estDmg = CombatModule.CalcDamage(player, a, target);
			_ui.AddLog($"  [{i + 1}] {a.Name} (预估伤害:{estDmg})");
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
				var blockDef = allActions.Find(a => a.EffectType == "block")!;
				var blockEvents = CombatModule.Attack(_ui.State, player, target, blockDef, target.Limbs[0]);
				_ui.Dispatch(blockEvents);
				MonsterCounterAttack(player, target);
				TickAllBuffs();
				_ui.FlushMap();
				return;
			}

			if (n < 1 || n > actions.Count) { _ui.AddLog("无效选择"); return; }

			var chosen = actions[n - 1];
			ShowLimbTargetSelection(player, target, chosen);
		});
	}

	private void ShowLimbTargetSelection(Actor player, Actor target, ActionDef action)
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
					MonsterCounterAttack(player, stillAlive);
			}

			TickAllBuffs();
			_ui.FlushMap();
		});
	}

	public void MonsterCounterAttack(Actor player, Actor monster)
	{
		var choice = CombatModule.MonsterChooseAction(_ui.State, monster, player);
		if (choice == null) return;

		var (mAction, mLimb) = choice.Value;
		var mEvents = CombatModule.Attack(_ui.State, monster, player, mAction, mLimb);

		foreach (var ev in mEvents)
		{
			switch (ev.Type)
			{
				case "combat_attack":
					_ui.AddLog($"🩸 {monster.DisplayName}用{ev.ActionName}攻击了你的{ev.LimbName}，造成{ev.Damage}点伤害");
					var hitLimb = player.Limbs.Find(l => l.Name == ev.LimbName);
					if (hitLimb != null)
						_ui.AddLog($"   {ev.LimbName} ({hitLimb.Durability}/{hitLimb.MaxDurability})");
					break;
				case "limb_destroyed":
					_ui.AddLog($"💥 你的{ev.LimbName}被摧毁了！");
					break;
				case "actor_killed":
					_ui.AddLog("💀 你死了……");
					_ui.AddLog("按任意方向键返回主菜单");
					_ui.PlayerDead = true;
					break;
				case "actor_incapacitated":
					_ui.AddLog("😵 你失去了意识……");
					_ui.AddLog("按任意方向键返回主菜单");
					_ui.PlayerDead = true;
					break;
				default:
					_ui.Dispatch([ev]);
					break;
			}
		}
	}

	private void TickAllBuffs()
	{
		var player = ActorModule.GetPlayer(_ui.State);
		player?.TickBuffs();
	}
}
