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
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.RA2.Activities;
using OpenRA.Traits;

namespace OpenRA.Mods.RA2.Traits
{
	[Desc("When returning to a refinery to deliver resources, this actor will teleport if possible.")]
	public class ChronoResourceDeliveryInfo : TraitInfo, Requires<HarvesterInfo>
	{
		[Desc("The number of ticks between each check to see if we can teleport to the refinery.")]
		public readonly int CheckTeleportDelay = 10;

		[Desc("Image used for the teleport effects. Defaults to the actor's type.")]
		public readonly string Image = null;

		[Desc("Sequence used for the effect played where the harvester jumped from.")]
		[SequenceReference("Image", allowNullImage: true)]
		public readonly string WarpInSequence = null;

		[Desc("Sequence used for the effect played where the harvester jumped to.")]
		[SequenceReference("Image", allowNullImage: true)]
		public readonly string WarpOutSequence = null;

		[Desc("Palette to render the warp in/out sprites in.")]
		[PaletteReference]
		public readonly string Palette = "effect";

		[Desc("Sound played where the harvester jumped from.")]
		public readonly string WarpInSound = null;

		[Desc("Sound where the harvester jumped to.")]
		public readonly string WarpOutSound = null;

		public override object Create(ActorInitializer init) { return new ChronoResourceDelivery(this); }
	}

	public class ChronoResourceDelivery : INotifyHarvestAction, INotifyDockClientMoving, ITick
	{
		readonly ChronoResourceDeliveryInfo info;

		CPos? destination;
		int ticksTillCheck;
		int ticksTillDockSearch;

		// TODO: Rewrite this entire thing, possible to be a subclass of harvester
		// and make it work properly with activities
		public ChronoResourceDelivery(ChronoResourceDeliveryInfo info)
		{
			this.info = info;
		}

		void ITick.Tick(Actor self)
		{
			if (!destination.HasValue)
			{
				// Movement to a dock normally announces the destination (MovingToDock). It never does when no dock
				// is reachable over land (e.g. refinery across water), so look for one ourselves.
				if (ticksTillDockSearch <= 0)
				{
					ticksTillDockSearch = info.CheckTeleportDelay;
					TeleportToUnreachableDock(self);
				}
				else
					ticksTillDockSearch--;

				return;
			}

			if (ticksTillCheck <= 0)
			{
				ticksTillCheck = info.CheckTeleportDelay;

				TeleportIfPossible(self);
			}
			else
				ticksTillCheck--;
		}

		void INotifyHarvestAction.MovingToResources(Actor self, CPos targetCell)
		{
			Reset();
		}

		void INotifyDockClientMoving.MovingToDock(Actor self, Actor hostActor, IDockHost host)
		{
			var targetCell = self.World.Map.CellContaining(host.DockPosition);
			if (destination != null && destination.Value != targetCell)
				ticksTillCheck = 0;

			destination = targetCell;
		}

		void INotifyHarvestAction.MovementCancelled(Actor self)
		{
			Reset();
		}

		void INotifyDockClientMoving.MovementCancelled(Actor self)
		{
			Reset();
		}

		void INotifyHarvestAction.Harvested(Actor self, string resourceType) { }

		/// <summary>
		/// Whether to look for a dock the harvester cannot reach over land: only while it is trying to deliver
		/// (moving to a dock, none reserved yet) and land pathfinding found no dock at all.
		/// </summary>
		public static bool ShouldSearchUnreachableDock(bool enabled, bool hostReserved, bool movingToDock, bool landDockFound)
		{
			return enabled && !hostReserved && movingToDock && !landDockFound;
		}

		/// <summary>Picks the dock cell closest to <paramref name="from"/> in a straight line, ignoring terrain.</summary>
		public static CPos? ClosestDockCell(CPos from, IEnumerable<CPos> dockCells)
		{
			CPos? best = null;
			var bestDistance = int.MaxValue;
			foreach (var cell in dockCells)
			{
				var distance = (cell - from).LengthSquared;
				if (distance < bestDistance)
				{
					bestDistance = distance;
					best = cell;
				}
			}

			return best;
		}

		static bool IsMovingToDock(Actor self)
		{
			// Name match: MoveToDock raises no notify on the no-dock path and ChildActivity is protected. An engine hook
			// would be cleaner; revisit if MoveToDock is renamed or subclassed.
			return self.CurrentActivity != null && self.CurrentActivity.DebugLabelComponents().Contains(nameof(MoveToDock));
		}

		void TeleportToUnreachableDock(Actor self)
		{
			var dockClient = self.TraitOrDefault<DockClientManager>();
			if (dockClient == null)
				return;

			var reserved = dockClient.ReservedHost != null;
			var moving = IsMovingToDock(self);

			// Land pathfinding is only worth running while the harvester is delivering with nothing reserved.
			var landDockFound = !reserved && moving && dockClient.ClosestDock(null) != null;
			if (!ShouldSearchUnreachableDock(!dockClient.IsTraitDisabled, reserved, moving, landDockFound))
				return;

			var cells = self.World.ActorsWithTrait<IDockHost>()
				.Select(h => h.Actor)
				.Distinct()
				.SelectMany(a => dockClient.AvailableDockHosts(a))
				.Select(h => self.World.Map.CellContaining(h.Trait.DockPosition));

			var dest = ClosestDockCell(self.Location, cells);
			if (dest.HasValue)
				TeleportIfPossible(self, dest.Value);
		}

		void TeleportIfPossible(Actor self)
		{
			TeleportIfPossible(self, destination.Value);
		}

		void TeleportIfPossible(Actor self, CPos dest)
		{
			// HACK: Cancelling the current activity will call Reset, so the destination is passed in.
			var pos = self.Trait<IPositionable>();

			// We're already here; no need to interfere.
			if (self.Location == dest)
			{
				Reset();
				return;
			}

			if (!pos.CanEnterCell(dest))
				return;

			self.CancelActivity();
			self.QueueActivity(new ChronoResourceTeleport(dest, info));

			// HACK: Manually queue a delivery and new find since we just cancelled all activities.
			// No explicit host, so MoveToDock picks the closest available dock (normally the one we just teleported to).
			self.QueueActivity(new MoveToDock(self, dockLineColor: self.Trait<DockClientManager>().DockLineColor));
			self.QueueActivity(new FindAndDeliverResources(self));
			Reset();
		}

		void Reset()
		{
			ticksTillCheck = 0;
			destination = null;
		}
	}
}
