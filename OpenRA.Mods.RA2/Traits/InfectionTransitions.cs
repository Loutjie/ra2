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
	/// <summary>What happens to an attached infector after an event on its host.</summary>
	public enum InfectorOutcome
	{
		/// <summary>The infector stays attached (or there was none).</summary>
		None,

		/// <summary>The infector is put back into the world alive.</summary>
		Ejected,

		/// <summary>The infector is put back into the world and killed.</summary>
		InfectorKilled
	}

	/// <summary>Pure state transitions for <see cref="Infectable"/>, kept free of actor state so they can be unit tested.</summary>
	public static class InfectionTransitions
	{
		/// <summary>The host was teleported (Chronosphere, chrono miner): the infector is left behind at the origin.</summary>
		public static InfectorOutcome OnChronoshift(bool infected)
		{
			return infected ? InfectorOutcome.Ejected : InfectorOutcome.None;
		}

		/// <summary>The host became invulnerable (Iron Curtain): the infector is destroyed.</summary>
		public static InfectorOutcome OnIronCurtain(bool infected)
		{
			return infected ? InfectorOutcome.InfectorKilled : InfectorOutcome.None;
		}

		/// <summary>The host took damage. Kill damage types (e.g. repair) win over remove damage types.</summary>
		public static InfectorOutcome OnDamaged(bool infected, bool killInfectorType, bool removeInfectorType)
		{
			if (!infected)
				return InfectorOutcome.None;

			if (killInfectorType)
				return InfectorOutcome.InfectorKilled;

			return removeInfectorType ? InfectorOutcome.Ejected : InfectorOutcome.None;
		}

		/// <summary>The host died. The infector survives only if the killing damage type allows it.</summary>
		public static InfectorOutcome OnHostKilled(bool infected, bool infectorSurvives)
		{
			if (!infected)
				return InfectorOutcome.None;

			return infectorSurvives ? InfectorOutcome.Ejected : InfectorOutcome.InfectorKilled;
		}

		/// <summary>The host was disposed without being killed (e.g. transformed): the infector is put back alive.</summary>
		public static InfectorOutcome OnHostDisposed(bool infected)
		{
			return infected ? InfectorOutcome.Ejected : InfectorOutcome.None;
		}

		/// <summary>The attached infector died while out of the world: the host must forget it and become targetable again.</summary>
		public static bool InfectorLost(bool infected, bool infectorDead)
		{
			return infected && infectorDead;
		}

		/// <summary>
		/// Teleport activities bump <see cref="Actor.Generation"/>, but so does an owner change.
		/// The host teleported if the generation advanced by more than the owner changes account for.
		/// </summary>
		public static bool HostTeleported(int observedGeneration, int currentGeneration, int ownerChanges)
		{
			return currentGeneration - observedGeneration > ownerChanges;
		}
	}
}
