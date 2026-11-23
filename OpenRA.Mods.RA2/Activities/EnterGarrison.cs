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
using OpenRA.Activities;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.RA2.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.RA2.Activities
{
	/// <summary>
	/// Walks into a garrisonable building. When the first occupant enters, the building changes owner, which bumps its
	/// actor generation and invalidates every other walker's target; the engine's Enter would then give up, so only the
	/// first soldier of a group got in. This activity re-targets the building and carries on while it can still be entered.
	/// </summary>
	sealed class EnterGarrison : Activity
	{
		readonly Actor building;
		readonly Color? targetLineColor;
		readonly Passenger passenger;
		EnterGarrisonStep step;
		int generation;

		public EnterGarrison(Actor self, in Target target, Color? targetLineColor)
		{
			building = target.Type == TargetType.Actor ? target.Actor : null;
			this.targetLineColor = targetLineColor;
			passenger = self.Trait<Passenger>();
		}

		protected override void OnFirstRun(Actor self)
		{
			if (building != null)
				QueueStep(self);
		}

		void QueueStep(Actor self)
		{
			generation = building.Generation;
			step = new EnterGarrisonStep(self, Target.FromActor(building), targetLineColor);
			QueueChild(step);
		}

		public override bool Tick(Actor self)
		{
			if (IsCanceling || step == null || step.Entered)
				return true;

			// Retry only when the building changed hands while we walked (the generation bump ended the step).
			if (building.IsDead || !building.IsInWorld || building.Generation == generation)
				return true;

			var cargo = building.TraitOrDefault<Cargo>();
			if (cargo == null || !EntersGarrisons.CanEnter(self, building, cargo, passenger))
				return true;

			QueueStep(self);
			return false;
		}

		public override IEnumerable<TargetLineNode> TargetLineNodes(Actor self)
		{
			if (step != null)
				return step.TargetLineNodes(self);

			return building != null && targetLineColor != null ? [new TargetLineNode(Target.FromActor(building), targetLineColor.Value)] : [];
		}
	}

	/// <summary>Mod-side equivalent of the engine's (internal) RideTransport, for buildings with GarrisonOwnership.</summary>
	sealed class EnterGarrisonStep : Enter
	{
		readonly Passenger passenger;

		Actor enterActor;
		Cargo enterCargo;

		public EnterGarrisonStep(Actor self, in Target target, Color? targetLineColor)
			: base(self, target, targetLineColor)
		{
			passenger = self.Trait<Passenger>();
		}

		public bool Entered { get; private set; }

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
				Entered = true;
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
