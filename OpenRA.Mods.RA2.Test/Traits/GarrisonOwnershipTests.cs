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
	public class GarrisonOwnershipTests
	{
		sealed class P { }

		readonly P neutral = new();
		readonly P gdi = new();
		readonly P gdiTeammate = new();

		[TestCase(TestName = "Owner_ChangesToFirstOccupant")]
		public void Owner_ChangesToFirstOccupant()
		{
			Assert.That(GarrisonOwnershipRules.NextOwner(neutral, neutral, 1, gdi), Is.SameAs(gdi));
		}

		[TestCase(TestName = "Owner_UnchangedForLaterOccupants")]
		public void Owner_UnchangedForLaterOccupants()
		{
			Assert.That(GarrisonOwnershipRules.NextOwner(neutral, gdi, 2, gdi), Is.SameAs(gdi));
		}

		[TestCase(TestName = "Owner_RevertsWhenEmpty")]
		public void Owner_RevertsWhenEmpty()
		{
			Assert.That(GarrisonOwnershipRules.NextOwner(neutral, gdi, 0, gdi), Is.SameAs(neutral));
		}

		[TestCase(TestName = "CanEnter_EnemyWhenOccupied_False")]
		public void CanEnter_EnemyWhenOccupied_False()
		{
			Assert.That(GarrisonOwnershipRules.CanEnter(false, false), Is.False);
		}

		[TestCase(TestName = "CanEnter_SameOwnerWhenOccupied_True")]
		public void CanEnter_SameOwnerWhenOccupied_True()
		{
			Assert.That(GarrisonOwnershipRules.CanEnter(false, GarrisonOwnershipRules.SameOwnerAsOccupants(gdi, new[] { gdi, gdi })), Is.True);
		}

		[TestCase(TestName = "CanEnter_AllyWhenOccupied_False")]
		public void CanEnter_AllyWhenOccupied_False()
		{
			// No allied mixing: the building would re-own the teammate's infantry to the first occupant's owner.
			Assert.That(GarrisonOwnershipRules.CanEnter(false, GarrisonOwnershipRules.SameOwnerAsOccupants(gdiTeammate, new[] { gdi })), Is.False);
		}

		[TestCase(TestName = "CanEnter_AnyoneWhenEmpty_True")]
		public void CanEnter_AnyoneWhenEmpty_True()
		{
			Assert.That(GarrisonOwnershipRules.CanEnter(true, false), Is.True);
		}

		[TestCase(true, true, true, true, false, ExpectedResult = true, TestName = "CanTarget_EmptyNeutral")]
		[TestCase(true, true, true, false, false, ExpectedResult = false, TestName = "CanTarget_OccupiedEnemy")]
		[TestCase(true, true, true, false, true, ExpectedResult = true, TestName = "CanTarget_OccupiedSameOwner")]
		[TestCase(false, true, true, true, true, ExpectedResult = false, TestName = "CanTarget_NotGarrison")]
		[TestCase(true, false, true, true, true, ExpectedResult = false, TestName = "CanTarget_WrongCargoType")]
		[TestCase(true, true, false, true, true, ExpectedResult = false, TestName = "CanTarget_Full")]
		public bool CanTarget(bool garrison, bool type, bool space, bool empty, bool sameOwner)
		{
			return GarrisonOwnershipRules.CanTarget(garrison, type, space, empty, sameOwner);
		}

		[TestCase(TestName = "CanEnter_SameTickSameOwner_True")]
		public void CanEnter_SameTickSameOwner_True()
		{
			// Building still owned by neutral, the first GI of the same player is already inside.
			var sameOwner = GarrisonOwnershipRules.SameOwnerAsOccupants(gdi, new[] { gdi });
			Assert.That(GarrisonOwnershipRules.CanEnter(false, sameOwner), Is.True);
		}

		[TestCase(TestName = "CanEnter_SameTickTeammate_False")]
		public void CanEnter_SameTickTeammate_False()
		{
			var sameOwner = GarrisonOwnershipRules.SameOwnerAsOccupants(gdiTeammate, new[] { gdi });
			Assert.That(GarrisonOwnershipRules.CanEnter(false, sameOwner), Is.False);
		}

		[TestCase(TestName = "CanEnter_SameTickEnemy_False")]
		public void CanEnter_SameTickEnemy_False()
		{
			var sameOwner = GarrisonOwnershipRules.SameOwnerAsOccupants(neutral, new[] { gdi });
			Assert.That(GarrisonOwnershipRules.CanEnter(false, sameOwner), Is.False);
		}

		[TestCase(TestName = "GarrisonOwnership_RejectsCapturable")]
		public void RejectsCapturable()
		{
			Assert.Throws<YamlException>(() => GarrisonOwnershipRules.EnsureNotCapturable("cafake01", true));
			Assert.DoesNotThrow(() => GarrisonOwnershipRules.EnsureNotCapturable("cafake01", false));
		}
	}
}
