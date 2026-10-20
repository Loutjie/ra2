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

using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.RA2.Traits
{
	[Desc("Garrisoned buildings take the owner of their first occupant and revert to their original owner when emptied.")]
	public class GarrisonOwnershipInfo : TraitInfo, Requires<CargoInfo>
	{
		public override object Create(ActorInitializer init) { return new GarrisonOwnership(init.Self); }
	}

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
			Update(self, passenger.Owner);
		}

		void INotifyPassengerExited.OnPassengerExited(Actor self, Actor passenger)
		{
			Update(self, self.Owner);
		}

		void Update(Actor self, Player enteringOwner)
		{
			// Cargo has already updated its passenger list when these notifications fire.
			var next = GarrisonOwnershipRules.NextOwner(originalOwner, self.Owner, cargo.PassengerCount, enteringOwner);
			if (next == self.Owner)
				return;

			// Changing owner re-owns the passengers (Cargo.OnOwnerChanged), so defer to frame end.
			self.World.AddFrameEndTask(_ =>
			{
				if (!self.IsDead && !self.Disposed)
					self.ChangeOwner(next);
			});
		}
	}
}
