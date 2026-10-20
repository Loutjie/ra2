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
	[Desc("A `" + nameof(ClassicProductionQueue) + "` that limits `" + nameof(PadBound) + "` actors to the number of",
		"`" + nameof(AircraftPads) + "` pads the player owns: they can only be queued while the pads outnumber",
		"the owned and queued ones, and a finished one waits until one of the owned ones frees its pad.")]
	public class PadLimitedProductionQueueInfo : ClassicProductionQueueInfo
	{
		public override object Create(ActorInitializer init) { return new PadLimitedProductionQueue(init, this); }
	}

	public class PadLimitedProductionQueue : ClassicProductionQueue
	{
		readonly Actor self;

		public PadLimitedProductionQueue(ActorInitializer init, PadLimitedProductionQueueInfo info)
			: base(init, info)
		{
			self = init.Self;
		}

		public static bool HasPadForQueuing(int pads, int owned, int queued) { return owned + queued < pads; }

		public static bool HasPadForDelivery(int pads, int owned) { return owned < pads; }

		static bool IsPadBound(ActorInfo actor) { return actor.HasTraitInfo<PadBoundInfo>(); }

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

		int QueuedPadBound()
		{
			var actors = self.World.Map.Rules.Actors;
			return Queue.Count(i => IsPadBound(actors[i.Item]));
		}

		public override IEnumerable<ActorInfo> BuildableItems()
		{
			var items = base.BuildableItems();
			if (developerMode.AllTech || HasPadForDelivery(OwnedPads(), OwnedPadBound()))
				return items;

			// Keep queued items buildable so that they wait for a pad instead of being cancelled.
			return items.Where(a => !IsPadBound(a) || IsInQueue(a));
		}

		protected override void BeginProduction(ProductionItem item, bool hasPriority)
		{
			if (!developerMode.AllTech && IsPadBound(self.World.Map.Rules.Actors[item.Item])
				&& !HasPadForQueuing(OwnedPads(), OwnedPadBound(), QueuedPadBound()))
				return;

			base.BeginProduction(item, hasPriority);
		}

		protected override bool BuildUnit(ActorInfo unit)
		{
			if (!developerMode.AllTech && IsPadBound(unit) && !HasPadForDelivery(OwnedPads(), OwnedPadBound()))
				return false;

			return base.BuildUnit(unit);
		}
	}
}
