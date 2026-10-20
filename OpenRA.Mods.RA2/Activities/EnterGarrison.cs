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

using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.RA2.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.RA2.Activities
{
	/// <summary>Mod-side equivalent of the engine's (internal) RideTransport, for buildings with GarrisonOwnership.</summary>
	sealed class EnterGarrison : Enter
	{
		readonly Passenger passenger;

		Actor enterActor;
		Cargo enterCargo;

		public EnterGarrison(Actor self, in Target target, Color? targetLineColor)
			: base(self, target, targetLineColor)
		{
			passenger = self.Trait<Passenger>();
		}

		protected override bool TryStartEnter(Actor self, Actor targetActor)
		{
			enterActor = targetActor;
			enterCargo = targetActor.TraitOrDefault<Cargo>();

			// Re-check on arrival: the owner may have changed while we were walking.
			if (enterCargo == null || enterCargo.IsTraitDisabled || !EntersGarrisons.CanEnter(self, targetActor, enterCargo, passenger)
				|| !passenger.Reserve(self, enterCargo))
			{
				Cancel(self, true);
				return false;
			}

			return true;
		}

		protected override void TickInner(Actor self, in Target target, bool targetIsDeadOrHiddenActor)
		{
			if (enterCargo != null && enterCargo.IsTraitDisabled)
				Cancel(self, true);
		}

		protected override void OnEnterComplete(Actor self, Actor targetActor)
		{
			self.World.AddFrameEndTask(w =>
			{
				if (self.IsDead || targetActor != enterActor)
					return;

				// Ownership may have flipped to an enemy of ours in the meantime.
				if (!enterCargo.CanLoad(self) || !EntersGarrisons.CanEnter(self, targetActor, enterCargo, passenger))
					return;

				foreach (var inl in targetActor.TraitsImplementing<INotifyLoadCargo>())
					inl.Loading(self);

				enterCargo.Load(enterActor, self);
				w.Remove(self);
			});
		}

		protected override void OnLastRun(Actor self)
		{
			passenger.Unreserve(self);
		}

		public override void Cancel(Actor self, bool keepQueue = false)
		{
			passenger.Unreserve(self);
			base.Cancel(self, keepQueue);
		}
	}
}
