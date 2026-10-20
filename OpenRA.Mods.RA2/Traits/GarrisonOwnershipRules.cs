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

namespace OpenRA.Mods.RA2.Traits
{
	/// <summary>Pure decision logic for GarrisonOwnership, kept free of engine state so it can be unit tested.</summary>
	public static class GarrisonOwnershipRules
	{
		/// <summary>Anyone may enter an empty building; an occupied one only accepts the current owner and allies.</summary>
		public static bool CanEnter(bool isEmpty, bool sameOrAlliedOwner)
		{
			return isEmpty || sameOrAlliedOwner;
		}

		/// <summary>Whether a passenger may target (and keep targeting) a garrison building.</summary>
		public static bool CanTarget(bool isGarrison, bool cargoTypeAccepted, bool hasSpace, bool isEmpty, bool sameOrAlliedOwner)
		{
			return isGarrison && cargoTypeAccepted && hasSpace && CanEnter(isEmpty, sameOrAlliedOwner);
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
