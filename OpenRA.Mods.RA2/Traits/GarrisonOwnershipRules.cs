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

namespace OpenRA.Mods.RA2.Traits
{
	/// <summary>Pure decision logic for GarrisonOwnership, kept free of engine state so it can be unit tested.</summary>
	public static class GarrisonOwnershipRules
	{
		/// <summary>
		/// Anyone may enter an empty building; an occupied one only accepts infantry of its occupants' owner.
		/// Allies may not mix: the building takes a single owner, and re-owning it would re-own their infantry.
		/// </summary>
		public static bool CanEnter(bool isEmpty, bool sameOwnerAsOccupants)
		{
			return isEmpty || sameOwnerAsOccupants;
		}

		/// <summary>
		/// Whether every current occupant has the entering owner. Checked against the occupants rather than the
		/// building, so a second infantry entering in the same tick as the first (before the deferred owner
		/// change) is still accepted.
		/// </summary>
		public static bool SameOwnerAsOccupants<T>(T enteringOwner, IEnumerable<T> occupantOwners)
			where T : class
		{
			return occupantOwners.All(o => o == enteringOwner);
		}

		/// <summary>Whether a passenger may target (and keep targeting) a garrison building.</summary>
		public static bool CanTarget(bool isGarrison, bool cargoTypeAccepted, bool hasSpace, bool isEmpty, bool sameOwnerAsOccupants)
		{
			return isGarrison && cargoTypeAccepted && hasSpace && CanEnter(isEmpty, sameOwnerAsOccupants);
		}

		/// <summary>
		/// GarrisonOwnership reverts an emptied building to its owner at creation, which is only right for buildings
		/// that nobody can capture.
		/// </summary>
		public static void EnsureNotCapturable(string actorName, bool hasCapturable)
		{
			if (hasCapturable)
				throw new YamlException($"Actor type `{actorName}` has GarrisonOwnership and Capturable: garrison buildings "
					+ "must not be capturable, because they revert to their original owner when emptied.");
		}

		/// <summary>
		/// Owner the building should have after its occupant count changed.
		/// Empty: back to the original owner. First occupant: the entering owner. Otherwise unchanged.
		/// </summary>
		public static T NextOwner<T>(T original, T current, int occupantsAfterChange, T enteringOwner)
			where T : class
		{
			if (occupantsAfterChange <= 0)
				return original;

			return current == original ? enteringOwner : current;
		}
	}
}
