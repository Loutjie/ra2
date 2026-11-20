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
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.RA2.Traits
{
	public static class DistributedFire
	{
		/// <summary>
		/// The actor ID the given shot goes at: shot 0 at the primary target, then the other candidates in
		/// ascending ID order, then round again. Deterministic, so it is the same on every client.
		/// </summary>
		public static uint Pick(uint primary, IEnumerable<uint> candidates, int shot)
		{
			var order = candidates.Where(id => id != primary).Distinct().Order().Prepend(primary).ToList();
			return order[shot % order.Count];
		}
	}

	[Desc("Armament that spreads its shots over every valid enemy in range (RA2 DistributedFire):",
		"the first shot goes at the attacked actor, each next one at the next enemy, by actor ID.")]
	public class DistributedArmamentInfo : ArmamentInfo
	{
		public override object Create(ActorInitializer init) { return new DistributedArmament(init.Self, this); }
	}

	public class DistributedArmament : Armament
	{
		uint primary;
		int shot;

		public DistributedArmament(Actor self, DistributedArmamentInfo info)
			: base(self, info) { }

		protected override void FireBarrel(Actor self, IFacing facing, in Target target, Barrel barrel)
		{
			base.FireBarrel(self, facing, Distribute(self, target), barrel);
		}

		Target Distribute(Actor self, in Target target)
		{
			if (target.Type != TargetType.Actor || self.Owner.RelationshipWith(target.Actor.Owner) != PlayerRelationship.Enemy)
				return target;

			if (target.Actor.ActorID != primary)
			{
				primary = target.Actor.ActorID;
				shot = 0;
			}

			var range = MaxRange();
			var candidates = self.World.FindActorsInCircle(self.CenterPosition, range)
				.Where(a => IsCandidate(self, a, range))
				.ToDictionary(a => a.ActorID);

			var id = DistributedFire.Pick(primary, candidates.Keys, shot++);
			return candidates.TryGetValue(id, out var picked) ? Target.FromActor(picked) : target;
		}

		bool IsCandidate(Actor self, Actor a, WDist range)
		{
			if (a.IsDead || !a.IsInWorld || self.Owner.RelationshipWith(a.Owner) != PlayerRelationship.Enemy)
				return false;

			if (!a.CanBeViewedByPlayer(self.Owner))
				return false;

			var t = Target.FromActor(a);
			if (!t.IsInRange(self.CenterPosition, range))
				return false;

			if (Weapon.MinRange != WDist.Zero && t.IsInRange(self.CenterPosition, Weapon.MinRange))
				return false;

			return Weapon.IsValidAgainst(t, self.World, self);
		}
	}
}
