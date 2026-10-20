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
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.RA2.Activities;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.RA2.Traits
{
	[Desc("Lets this infantry enter buildings with GarrisonOwnership, including empty neutral ones (the stock Passenger order is allied-only).")]
	public class EntersGarrisonsInfo : TraitInfo, Requires<PassengerInfo>
	{
		[VoiceReference]
		public readonly string Voice = "Action";

		[Desc("Color to use for the target line.")]
		public readonly Color TargetLineColor = Color.Green;

		[CursorReference]
		[Desc("Cursor to display when able to enter target actor.")]
		public readonly string EnterCursor = "enter";

		[CursorReference]
		[Desc("Cursor to display when unable to enter target actor.")]
		public readonly string EnterBlockedCursor = "enter-blocked";

		public override object Create(ActorInitializer init) { return new EntersGarrisons(this); }
	}

	public class EntersGarrisons : IIssueOrder, IResolveOrder, IOrderVoice
	{
		const string OrderName = "EnterGarrison";

		readonly EntersGarrisonsInfo info;

		public EntersGarrisons(EntersGarrisonsInfo info)
		{
			this.info = info;
		}

		public static bool CanEnter(Actor self, Actor building, Cargo cargo, Passenger passenger)
		{
			// Ownership flips in a deferred task, so a teammate that entered this tick may not have changed
			// the building's owner yet: also accept allies of any current occupant.
			var allied = GarrisonOwnershipRules.AlliedToGarrison(
				self.Owner.IsAlliedWith(building.Owner),
				cargo.Passengers.Any(p => self.Owner.IsAlliedWith(p.Owner)));

			return GarrisonOwnershipRules.CanTarget(
				building.Info.HasTraitInfo<GarrisonOwnershipInfo>(),
				cargo.Info.Types.Contains(passenger.Info.CargoType),
				!cargo.IsTraitDisabled && (cargo.CanLoad(self) || cargo.HasSpace(passenger.Info.Weight)),
				cargo.IsEmpty(),
				allied);
		}

		bool CanEnter(Actor self, Actor building)
		{
			var cargo = building.TraitOrDefault<Cargo>();
			return cargo != null && CanEnter(self, building, cargo, self.Trait<Passenger>());
		}

		IEnumerable<IOrderTargeter> IIssueOrder.Orders
		{
			get { yield return new GarrisonTargeter(info, CanEnter); }
		}

		Order IIssueOrder.IssueOrder(Actor self, IOrderTargeter order, in Target target, bool queued)
		{
			return order.OrderID == OrderName ? new Order(order.OrderID, self, target, queued) : null;
		}

		string IOrderVoice.VoicePhraseForOrder(Actor self, Order order)
		{
			if (order.OrderString != OrderName || order.Target.Type != TargetType.Actor || !CanEnter(self, order.Target.Actor))
				return null;

			return info.Voice;
		}

		void IResolveOrder.ResolveOrder(Actor self, Order order)
		{
			if (order.OrderString != OrderName || order.Target.Type != TargetType.Actor)
				return;

			if (!CanEnter(self, order.Target.Actor))
				return;

			self.QueueActivity(order.Queued, new EnterGarrison(self, order.Target, info.TargetLineColor));
			self.ShowTargetLines();
		}

		sealed class GarrisonTargeter : UnitOrderTargeter
		{
			readonly EntersGarrisonsInfo info;
			readonly System.Func<Actor, Actor, bool> canEnter;

			public GarrisonTargeter(EntersGarrisonsInfo info, System.Func<Actor, Actor, bool> canEnter)
				: base(OrderName, 6, info.EnterCursor, false, true)
			{
				// Ctrl (force attack) must fall through to the Attack order.
				ForceAttack = false;
				this.info = info;
				this.canEnter = canEnter;
			}

			public override bool CanTargetActor(Actor self, Actor target, TargetModifiers modifiers, ref string cursor)
			{
				if (!target.Info.HasTraitInfo<GarrisonOwnershipInfo>() || !target.Info.HasTraitInfo<CargoInfo>())
					return false;

				// Only claim the target when entry is possible, so we never shadow Attack with a blocked cursor.
				if (!canEnter(self, target))
					return false;

				cursor = info.EnterCursor;
				return true;
			}

			public override bool CanTargetFrozenActor(Actor self, FrozenActor target, TargetModifiers modifiers, ref string cursor)
			{
				// Fogged (frozen) garrison buildings are deliberately not targetable.
				return false;
			}
		}
	}
}
