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
using OpenRA.Traits;

namespace OpenRA.Mods.RA2.Test
{
	[TestFixture]
	public class ReplaceOnPrerequisiteTests
	{
		[Test]
		public void ReplaceOnPrerequisite_NeutralOwner_DoesNotReplace()
		{
			// Neutral/Creeps-style owners share the Player actor (and so the lobby prerequisite),
			// but their map actors must keep their base type.
			Assert.That(ReplaceDecision.ShouldReplace(ownerIsNonCombatant: true, ownerHasPrerequisite: true), Is.False);
			Assert.That(ReplaceDecision.ShouldReplace(ownerIsNonCombatant: true, ownerHasPrerequisite: false), Is.False);
		}

		[Test]
		public void ReplaceOnPrerequisite_PlayerWithPrerequisite_Replaces()
		{
			Assert.That(ReplaceDecision.ShouldReplace(ownerIsNonCombatant: false, ownerHasPrerequisite: true), Is.True);
		}

		[Test]
		public void ReplaceOnPrerequisite_PlayerWithoutPrerequisite_DoesNotReplace()
		{
			Assert.That(ReplaceDecision.ShouldReplace(ownerIsNonCombatant: false, ownerHasPrerequisite: false), Is.False);
		}

		[Test]
		public void ReplaceOnPrerequisite_OnlyEvaluatedAtCreation()
		{
			// Captured or mind-controlled units must never morph: the trait may not react to owner changes.
			Assert.That(typeof(INotifyOwnerChanged).IsAssignableFrom(typeof(ReplaceOnPrerequisite)), Is.False);
			Assert.That(typeof(INotifyCreated).IsAssignableFrom(typeof(ReplaceOnPrerequisite)), Is.True);
		}

		[TestCase(0, 0, ExpectedResult = true)]
		[TestCase(0, 1, ExpectedResult = true)]
		[TestCase(0, 2, ExpectedResult = false)]
		[TestCase(5, 5, ExpectedResult = true)]
		[TestCase(5, 6, ExpectedResult = false)]
		[TestCase(1, 2, ExpectedResult = false)]
		public bool ReplaceOnPrerequisite_InCreationWindow(int createdTick, int currentTick)
		{
			// Map-load actors (tick 0) may still be replaced in the first tick, where initial cargo is notified;
			// any other actor only in the tick it was created.
			return ReplaceDecision.InCreationWindow(createdTick, currentTick);
		}

		[Test]
		public void ReplaceOnPrerequisite_PassengerReplacedByNonPassenger_IsInvalid()
		{
			Assert.That(ReplaceDecision.ValidateIntoActor("e1", "e1x", baseIsPassenger: true, intoIsPassenger: false),
				Does.Contain("Passenger"));
		}

		[TestCase(true, true)]
		[TestCase(false, false)]
		[TestCase(false, true)]
		public void ReplaceOnPrerequisite_CompatibleIntoActor_IsValid(bool baseIsPassenger, bool intoIsPassenger)
		{
			Assert.That(ReplaceDecision.ValidateIntoActor("e1", "e1x", baseIsPassenger, intoIsPassenger), Is.Null);
		}
	}
}
