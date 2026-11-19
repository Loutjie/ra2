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
using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.RA2.Traits
{
	[Desc("Garrisoned buildings take the owner of their first occupant and revert to their original owner when emptied.",
		"When destroyed, the occupants leave alive over the footprint and the cells around it (set Cargo EjectOnDeath: false).")]
	public class GarrisonOwnershipInfo : TraitInfo, Requires<CargoInfo>, IRulesetLoaded
	{
		public override object Create(ActorInitializer init) { return new GarrisonOwnership(init.Self); }

		void IRulesetLoaded<ActorInfo>.RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			GarrisonOwnershipRules.EnsureNotCapturable(ai.Name, ai.HasTraitInfo<CapturableInfo>());
		}
	}

	// The original owner is the owner at creation: garrison buildings must be neutral-owned and not Capturable
	// (Capturable is rejected when the ruleset loads), otherwise "revert when empty" would hand them back to the
	// wrong player.
	public class GarrisonOwnership : INotifyPassengerEntered, INotifyPassengerExited, INotifyDamage
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
			// Changing owner re-owns the passengers (Cargo.OnOwnerChanged), so defer to frame end, where the
			// owner must change synchronously (ChangeOwner would queue yet another frame-end task).
			// The target owner is derived from the cargo state when the task runs, not when it was queued,
			// so enter+exit within one drain cannot leave an empty building owned by an occupant.
			self.World.AddFrameEndTask(_ =>
			{
				if (self.IsDead || self.Disposed)
					return;

				var first = cargo.Passengers.FirstOrDefault();
				var next = GarrisonOwnershipRules.NextOwner(originalOwner, self.Owner, cargo.PassengerCount, first?.Owner);
				if (next != null && next != self.Owner)
					self.ChangeOwnerSync(next);
			});
		}

		void INotifyDamage.Damaged(Actor self, AttackInfo e)
		{
			// INotifyDamage runs before INotifyKilled, so the occupants are out before Cargo.Killed would kill them.
			if (e.DamageState != DamageState.Dead || cargo.IsEmpty())
				return;

			var passengers = new List<Actor>();
			while (!cargo.IsEmpty())
				passengers.Add(cargo.Unload(self));

			var footprint = self.Info.TraitInfoOrDefault<BuildingInfo>()?.Tiles(self.Location) ?? new[] { self.Location };
			var tiers = GarrisonEjection.CandidateCells(footprint);
			var attacker = e.Attacker;

			self.World.AddFrameEndTask(w =>
			{
				var positionable = passengers[0].Trait<IPositionable>();
				var slots = GarrisonEjection.AssignSlots(passengers.Count, tiers, c => FreeSubCells(self, positionable, c));

				for (var i = 0; i < passengers.Count; i++)
				{
					var passenger = passengers[i];
					var pos = passenger.Trait<IPositionable>();

					// Slots were found with the first occupant's locomotor; re-check for this one.
					if (i >= slots.Count || pos.GetAvailableSubCell(slots[i].Cell, slots[i].SubCell, self) != slots[i].SubCell)
					{
						passenger.Kill(attacker);
						continue;
					}

					pos.SetPosition(passenger, slots[i].Cell, slots[i].SubCell);
					w.Add(passenger);

					foreach (var nbm in passenger.TraitsImplementing<INotifyBlockingMove>())
						nbm.OnNotifyBlockingMove(passenger, passenger);

					passenger.Trait<Passenger>().OnEjectedFromKilledCargo(passenger);
				}
			});
		}

		static IReadOnlyList<SubCell> FreeSubCells(Actor building, IPositionable positionable, CPos cell)
		{
			var map = building.World.Map;
			if (!map.Contains(cell))
				return Array.Empty<SubCell>();

			var free = new List<SubCell>();
			for (var i = (int)SubCell.First; i < map.Grid.SubCellOffsets.Length; i++)
			{
				var subCell = (SubCell)i;
				var available = positionable.GetAvailableSubCell(cell, subCell, building);
				if (available == SubCell.Invalid)
					break;

				// Actors that do not share cells take the whole cell.
				if (available == SubCell.FullCell)
					return new[] { SubCell.FullCell };

				if (available == subCell)
					free.Add(subCell);
			}

			return free;
		}
	}
}
