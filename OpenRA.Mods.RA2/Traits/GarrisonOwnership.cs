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

using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.RA2.Traits
{
	[Desc("Garrisoned buildings take the owner of their first occupant and revert to their original owner when emptied.")]
	public class GarrisonOwnershipInfo : TraitInfo, Requires<CargoInfo>
	{
		public override object Create(ActorInitializer init) { return new GarrisonOwnership(init.Self); }
	}

	// The original owner is the owner at creation: garrison buildings must be neutral-owned and not Capturable
	// (enforced by the generated YAML), otherwise "revert when empty" would hand them back to the wrong player.
	public class GarrisonOwnership : INotifyPassengerEntered, INotifyPassengerExited
	{
		readonly Cargo cargo;
		readonly Player originalOwner;

		public GarrisonOwnership(Actor self)
		{
			cargo = self.Trait<Cargo>();
			originalOwner = self.Owner;
		}

		void INotifyPassengerEntered.OnPassengerEntered(Actor self, Actor passenger)
		{
			Update(self);
		}

		void INotifyPassengerExited.OnPassengerExited(Actor self, Actor passenger)
		{
			Update(self);
		}

		void Update(Actor self)
		{
			// Changing owner re-owns the passengers (Cargo.OnOwnerChanged), so defer to frame end.
			// The target owner is derived from the cargo state when the task runs, not when it was queued,
			// so enter+exit within one drain cannot leave an empty building owned by an occupant.
			self.World.AddFrameEndTask(_ =>
			{
				if (self.IsDead || self.Disposed)
					return;

				var first = cargo.Passengers.FirstOrDefault();
				var next = GarrisonOwnershipRules.NextOwner(originalOwner, self.Owner, cargo.PassengerCount, first?.Owner);
				if (next != null && next != self.Owner)
					self.ChangeOwner(next);
			});
		}
	}
}
