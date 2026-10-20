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

using System;
using System.Linq;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.RA2.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.RA2.Activities
{
	sealed class EnterCarrierParent : Enter
	{
		readonly Actor parent;
		readonly CarrierParent spawnerParent;
		readonly WDist landingAbortDistance;

		WPos lastParentPosition;
		WPos? landingStartPosition;

		public EnterCarrierParent(Actor self, Actor parent, CarrierParent spawnerParent, WDist landingAbortDistance)
			: base(self, Target.FromActor(parent))
		{
			this.parent = parent;
			this.spawnerParent = spawnerParent;
			this.landingAbortDistance = landingAbortDistance;
			lastParentPosition = parent.CenterPosition;
		}

		protected override bool TryStartEnter(Actor self, Actor targetActor)
		{
			// Remember where the parent was when we committed to landing on it.
			landingStartPosition = targetActor.CenterPosition;
			return true;
		}

		protected override void TickInner(Actor self, in Target target, bool targetIsDeadOrHiddenActor)
		{
			if (parent.IsDead)
				return;

			var parentPosition = parent.CenterPosition;
			var parentMoved = parentPosition != lastParentPosition;
			lastParentPosition = parentPosition;

			if (IsCanceling || landingStartPosition == null)
				return;

			var parentDrift = new WDist((parentPosition - landingStartPosition.Value).HorizontalLength);
			if (!CarrierChild.ShouldCircle(parentMoved, parentDrift, landingAbortDistance))
				return;

			// The parent moved away from under us: abort the landing (which takes off again)
			// and fly a full circle before trying again, instead of landing on a stale position.
			Cancel(self, true);
			var aircraft = self.TraitOrDefault<Aircraft>();
			if (aircraft != null)
				Queue(new FlyIdle(self, FullCircleTicks(aircraft.TurnSpeed), false));
		}

		static int FullCircleTicks(WAngle turnSpeed)
		{
			return 1024 / Math.Max(1, turnSpeed.Angle);
		}

		protected override void OnEnterComplete(Actor self, Actor targetActor)
		{
			if (parent.IsDead)
				return;

			self.World.AddFrameEndTask(w =>
			{
				if (self.IsDead || parent.IsDead)
					return;

				spawnerParent.PickupChild(parent, self);
				w.Remove(self);

				// Delayed launching is handled at spawner.
				var ammoPools = self.TraitsImplementing<AmmoPool>().ToArray();
				if (ammoPools != null)
					foreach (var pool in ammoPools)
						pool.GiveAmmo(self, pool.Info.Ammo);
			});
		}
	}
}
