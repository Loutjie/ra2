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

using NUnit.Framework;
using OpenRA.Mods.RA2.Traits;

namespace OpenRA.Mods.RA2.Test
{
	[TestFixture]
	public class InfectableTests
	{
		[TestCase(TestName = "Chronoshifting an infected host leaves the infector behind")]
		public void ChronoshiftEjectsInfector()
		{
			Assert.That(InfectionTransitions.OnChronoshift(infected: true), Is.EqualTo(InfectorOutcome.Ejected));
		}

		[TestCase(TestName = "Chronoshifting a clean host does nothing")]
		public void ChronoshiftWithoutInfectorDoesNothing()
		{
			Assert.That(InfectionTransitions.OnChronoshift(infected: false), Is.EqualTo(InfectorOutcome.None));
		}

		[TestCase(TestName = "The Iron Curtain kills an attached infector")]
		public void IronCurtainKillsInfector()
		{
			Assert.That(InfectionTransitions.OnIronCurtain(infected: true), Is.EqualTo(InfectorOutcome.InfectorKilled));
		}

		[TestCase(TestName = "The Iron Curtain on a clean host does nothing")]
		public void IronCurtainWithoutInfectorDoesNothing()
		{
			Assert.That(InfectionTransitions.OnIronCurtain(infected: false), Is.EqualTo(InfectorOutcome.None));
		}

		[TestCase(true, false, ExpectedResult = InfectorOutcome.InfectorKilled, TestName = "Repair (kill damage type) kills the infector")]
		[TestCase(false, true, ExpectedResult = InfectorOutcome.Ejected, TestName = "Remove damage type ejects the infector")]
		[TestCase(true, true, ExpectedResult = InfectorOutcome.InfectorKilled, TestName = "Kill damage type wins over remove damage type")]
		[TestCase(false, false, ExpectedResult = InfectorOutcome.None, TestName = "Other damage leaves the infector attached")]
		public InfectorOutcome DamageOutcome(bool killType, bool removeType)
		{
			return InfectionTransitions.OnDamaged(infected: true, killType, removeType);
		}

		[TestCase(TestName = "Damage to a clean host does nothing")]
		public void DamageWithoutInfectorDoesNothing()
		{
			Assert.That(InfectionTransitions.OnDamaged(infected: false, killInfectorType: true, removeInfectorType: true), Is.EqualTo(InfectorOutcome.None));
		}

		[TestCase(true, ExpectedResult = InfectorOutcome.Ejected, TestName = "Infector survives a host killed by a survive damage type")]
		[TestCase(false, ExpectedResult = InfectorOutcome.InfectorKilled, TestName = "Infector dies with its host otherwise")]
		public InfectorOutcome HostKilledOutcome(bool survivable)
		{
			return InfectionTransitions.OnHostKilled(infected: true, survivable);
		}

		[TestCase(5, 5, 0, ExpectedResult = false, TestName = "Unchanged generation is not a teleport")]
		[TestCase(5, 6, 0, ExpectedResult = true, TestName = "Generation bump is a teleport")]
		[TestCase(5, 6, 1, ExpectedResult = false, TestName = "Owner change alone is not a teleport")]
		[TestCase(5, 7, 1, ExpectedResult = true, TestName = "Teleport and owner change in one tick is still a teleport")]
		public bool TeleportDetection(int observed, int current, int ownerChanges)
		{
			return InfectionTransitions.HostTeleported(observed, current, ownerChanges);
		}
	}
}
