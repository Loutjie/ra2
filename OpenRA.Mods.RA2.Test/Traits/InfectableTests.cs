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

		[TestCase(TestName = "A host disposed without dying puts its infector back alive")]
		public void HostDisposedEjectsInfector()
		{
			Assert.That(InfectionTransitions.OnHostDisposed(infected: true), Is.EqualTo(InfectorOutcome.Ejected));
			Assert.That(InfectionTransitions.OnHostDisposed(infected: false), Is.EqualTo(InfectorOutcome.None));
		}

		[TestCase(true, true, ExpectedResult = true, TestName = "A dead attached infector is cleared from its host")]
		[TestCase(true, false, ExpectedResult = false, TestName = "A living attached infector is kept")]
		[TestCase(false, false, ExpectedResult = false, TestName = "A clean host has nothing to clear")]
		public bool InfectorLostDetection(bool infected, bool infectorDead)
		{
			return InfectionTransitions.InfectorLost(infected, infectorDead);
		}

		[TestCase(5, 5, 0, ExpectedResult = false, TestName = "Unchanged generation is not a teleport")]
		[TestCase(5, 6, 0, ExpectedResult = true, TestName = "Generation bump is a teleport")]
		[TestCase(5, 6, 1, ExpectedResult = false, TestName = "Owner change alone is not a teleport")]
		[TestCase(5, 7, 1, ExpectedResult = true, TestName = "Teleport and owner change in one tick is still a teleport")]
		public bool TeleportDetection(int observed, int current, int ownerChanges)
		{
			return InfectionTransitions.HostTeleported(observed, current, ownerChanges);
		}

		[TestCase(true, false, ExpectedResult = true, TestName = "An always-surviving infector (Giant Squid) lets go alive whatever killed the host")]
		[TestCase(false, true, ExpectedResult = true, TestName = "A survive damage type lets the infector live")]
		[TestCase(false, false, ExpectedResult = false, TestName = "Otherwise the infector dies with its host")]
		public bool SurvivesHostDeath(bool always, bool surviveType)
		{
			return InfectionTransitions.InfectorSurvivesHostDeath(always, surviveType);
		}

		[TestCase(50, 200, 25, ExpectedResult = true, TestName = "Culling kills a host at exactly 25%")]
		[TestCase(51, 200, 25, ExpectedResult = false, TestName = "Culling spares a host above 25%")]
		[TestCase(1, 600, 25, ExpectedResult = true, TestName = "Culling kills a host deep in the red")]
		[TestCase(1, 600, 0, ExpectedResult = false, TestName = "A percentage of 0 disables culling")]
		[TestCase(0, 600, 25, ExpectedResult = false, TestName = "A dead host is not culled again")]
		public bool Culling(int hp, int maxHp, int percent)
		{
			return InfectionTransitions.ShouldCull(hp, maxHp, percent);
		}

		[TestCase(false, false, true, ExpectedResult = true, TestName = "Without infect armaments every attack latches on (Terror Drone)")]
		[TestCase(true, true, false, ExpectedResult = true, TestName = "A target valid for an infect armament is latched onto (squid vs ship)")]
		[TestCase(true, true, true, ExpectedResult = true, TestName = "Latching wins when both kinds of armament are valid")]
		[TestCase(true, false, true, ExpectedResult = false, TestName = "Only other armaments valid: attack normally (squid vs dolphin)")]
		[TestCase(true, false, false, ExpectedResult = true, TestName = "Nothing valid keeps the infect activity, which cancels itself")]
		public bool AttackKind(bool restricts, bool infectValid, bool otherValid)
		{
			return InfectionTransitions.UsesInfectAttack(restricts, infectValid, otherValid);
		}
	}
}
