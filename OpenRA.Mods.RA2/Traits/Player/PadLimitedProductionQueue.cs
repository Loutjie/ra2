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
	[TraitLocation(SystemActors.Player)]
	[Desc("A `" + nameof(BalanceProductionQueue) + "` that limits `" + nameof(PadBound) + "` actors to the number of",
		"`" + nameof(AircraftPads) + "` pads the player owns: they can only be queued while the pads outnumber",
		"the owned and queued ones, and a finished one waits until one of the owned ones frees its pad.")]
	public class PadLimitedProductionQueueInfo : BalanceProductionQueueInfo
	{
		public override object Create(ActorInitializer init) { return new PadLimitedProductionQueue(init, this); }
	}

	public class PadLimitedProductionQueue : BalanceProductionQueue
	{
		readonly Actor self;

		public PadLimitedProductionQueue(ActorInitializer init, PadLimitedProductionQueueInfo info)
			: base(init, info)
		{
			self = init.Self;
		}

		public static bool IsPadBound(ActorInfo actor) { return actor.HasTraitInfo<PadBoundInfo>(); }

		// Pure decisions, kept free of world state so they can be unit tested and reused by other queue types.
		public static bool CanQueue(bool allTech, ActorInfo item, int pads, int ownedPadBound, IEnumerable<ActorInfo> queued)
		{
			return allTech || !IsPadBound(item) || ownedPadBound + queued.Count(IsPadBound) < pads;
		}

		public static bool CanDeliver(bool allTech, ActorInfo item, int pads, int ownedPadBound)
		{
			return allTech || !IsPadBound(item) || ownedPadBound < pads;
		}

		// Items already in the queue stay buildable so that they wait for a pad instead of being cancelled.
		public static bool IsBuildable(bool allTech, ActorInfo item, int pads, int ownedPadBound, bool inQueue)
		{
			return inQueue || CanDeliver(allTech, item, pads, ownedPadBound);
		}

		// Pads count from placement, including while the building is still under construction.
		int OwnedPads()
		{
			return self.World.ActorsWithTrait<AircraftPads>()
				.Where(p => p.Actor.Owner == self.Owner && !p.Actor.IsDead)
				.Sum(p => p.Trait.Info.Count);
		}

		int OwnedPadBound()
		{
			return self.World.ActorsHavingTrait<PadBound>()
				.Count(a => a.Owner == self.Owner && !a.IsDead);
		}

		public override IEnumerable<ActorInfo> BuildableItems()
		{
			var items = base.BuildableItems();
			if (developerMode.AllTech)
				return items;

			var pads = OwnedPads();
			var owned = OwnedPadBound();
			return items.Where(a => IsBuildable(false, a, pads, owned, IsInQueue(a)));
		}

		protected override void BeginProduction(ProductionItem item, bool hasPriority)
		{
			var actors = self.World.Map.Rules.Actors;
			if (!CanQueue(developerMode.AllTech, actors[item.Item], OwnedPads(), OwnedPadBound(), Queue.Select(i => actors[i.Item])))
				return;

			base.BeginProduction(item, hasPriority);
		}

		protected override bool BuildUnit(ActorInfo unit)
		{
			if (!CanDeliver(developerMode.AllTech, unit, OwnedPads(), OwnedPadBound()))
				return false;

			return base.BuildUnit(unit);
		}
	}
}
