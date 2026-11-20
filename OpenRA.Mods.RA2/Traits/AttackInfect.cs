#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System.Collections.Generic;
using System.Linq;
using OpenRA.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.RA2.Activities;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.RA2.Traits
{
	[Desc("Move onto the target then execute the attack.")]
	public class AttackInfectInfo : AttackFrontalInfo, Requires<MobileInfo>
	{
		[Desc("Range of the final jump of the infector.")]
		public readonly WDist JumpRange = WDist.Zero;

		[Desc("Conditions that last from start of the jump until the attack.")]
		[GrantedConditionReference]
		public readonly string JumpCondition = "jumping";

		[FieldLoader.Require]
		[Desc("How much damage to deal.")]
		public readonly int Damage = 0;

		[FieldLoader.Require]
		[Desc("How often to deal the damage.")]
		public readonly int DamageInterval = 0;

		[Desc("Damage types for the infection damage.")]
		public readonly BitSet<DamageType> DamageTypes = default;

		[Desc("Damage types which allows the infector survive when it's host dies.")]
		public readonly BitSet<DamageType> SurviveHostDamageTypes = default;

		[Desc("The infector always survives its host's death, whatever killed it (RA2's Giant Squid).")]
		public readonly bool AlwaysSurviveHostDeath = false;

		[Desc("Kill the host outright on an infection damage tick once its health is at or below this percentage",
			"of its maximum (RA2's Culling warheads, which kill a victim in the red). 0 disables it.")]
		public readonly int CullHealthPercent = 0;

		[Desc("Armaments (by name) that latch on. A target none of them is valid for is attacked normally",
			"with the other armaments, if one of those is valid. Empty means every attack latches on.")]
		public readonly HashSet<string> InfectArmaments = [];

		public override object Create(ActorInitializer init) { return new AttackInfect(init.Self, this); }
	}

	public class AttackInfect : AttackFrontal
	{
		public readonly AttackInfectInfo InfectInfo;

		int jumpToken = Actor.InvalidConditionToken;

		public AttackInfect(Actor self, AttackInfectInfo info)
			: base(self, info)
		{
			InfectInfo = info;
		}

		protected override bool CanAttack(Actor self, in Target target)
		{
			if (target.Type != TargetType.Actor)
				return false;

			if (self.Location == target.Actor.Location && HasAnyValidWeapons(target))
				return true;

			return base.CanAttack(self, target);
		}

		public void GrantJumpCondition(Actor self)
		{
			if (!string.IsNullOrEmpty(InfectInfo.JumpCondition))
				jumpToken = self.GrantCondition(InfectInfo.JumpCondition);
		}

		public void RevokeJumpCondition(Actor self)
		{
			if (jumpToken != Actor.InvalidConditionToken)
				jumpToken = self.RevokeCondition(jumpToken);
		}

		bool IsInfectArmament(Armament a)
		{
			return InfectInfo.InfectArmaments.Count == 0 || InfectInfo.InfectArmaments.Contains(a.Info.Name);
		}

		bool UsesInfectAttack(Actor self, in Target target)
		{
			var restricts = InfectInfo.InfectArmaments.Count > 0;
			if (!restricts)
				return true;

			var infectValid = false;
			var otherValid = false;
			foreach (var a in Armaments)
			{
				if (a.IsTraitDisabled || !a.Weapon.IsValidAgainst(target, self.World, self))
					continue;

				if (IsInfectArmament(a))
					infectValid = true;
				else
					otherValid = true;
			}

			return InfectionTransitions.UsesInfectAttack(restricts, infectValid, otherValid);
		}

		/// <summary>Whether every armament that latches on is reloading.</summary>
		public bool InfectArmamentsReloading()
		{
			return Armaments.Where(IsInfectArmament).All(a => a.IsReloading);
		}

		public override Activity GetAttackActivity(Actor self, AttackSource source, in Target newTarget, bool allowMove, bool forceAttack, Color? targetLineColor)
		{
			if (UsesInfectAttack(self, newTarget))
				return new Infect(self, newTarget, this, targetLineColor);

			return base.GetAttackActivity(self, source, newTarget, allowMove, forceAttack, targetLineColor);
		}
	}
}
