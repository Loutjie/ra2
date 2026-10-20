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
using NUnit.Framework;
using OpenRA.Mods.RA2.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.RA2.Test
{
	[TestFixture]
	public class GarrisonEjectionTests
	{
		static readonly SubCell[] ThreeSubCells = { (SubCell)1, (SubCell)2, (SubCell)3 };

		static readonly CPos[] Footprint2X2 = { new(10, 10), new(11, 10), new(10, 11), new(11, 11) };

		static IReadOnlyList<SubCell> AllFree(CPos cell) => ThreeSubCells;

		[TestCase(TestName = "Eject_CandidateCells_FootprintThenSortedRing")]
		public void CandidateCells_FootprintThenSortedRing()
		{
			var tiers = GarrisonEjection.CandidateCells(new[] { new CPos(5, 5) });

			Assert.That(tiers[0], Is.EqualTo(new[] { new CPos(5, 5) }));
			Assert.That(tiers[1], Is.EqualTo(new[]
			{
				new CPos(4, 4), new CPos(5, 4), new CPos(6, 4),
				new CPos(4, 5), new CPos(6, 5),
				new CPos(4, 6), new CPos(5, 6), new CPos(6, 6),
			}));
		}

		[TestCase(TestName = "Eject_CandidateCells_IndependentOfInputOrder")]
		public void CandidateCells_IndependentOfInputOrder()
		{
			var a = GarrisonEjection.CandidateCells(Footprint2X2);
			var b = GarrisonEjection.CandidateCells(Footprint2X2.Reverse());

			Assert.That(b[0], Is.EqualTo(a[0]));
			Assert.That(b[1], Is.EqualTo(a[1]));
			Assert.That(a[1], Has.Count.EqualTo(12));
		}

		[TestCase(TestName = "Eject_FullTenManBuilding_AllPlacedInDistinctSlots")]
		public void FullTenManBuilding_AllPlacedInDistinctSlots()
		{
			var slots = GarrisonEjection.AssignSlots(10, GarrisonEjection.CandidateCells(Footprint2X2), AllFree);

			Assert.That(slots, Has.Count.EqualTo(10));
			Assert.That(slots.Distinct().Count(), Is.EqualTo(10));
			Assert.That(slots.All(s => Footprint2X2.Contains(s.Cell)), Is.True);
		}

		[TestCase(TestName = "Eject_SpreadsOverCellsBeforeDoublingUp")]
		public void SpreadsOverCellsBeforeDoublingUp()
		{
			var slots = GarrisonEjection.AssignSlots(5, GarrisonEjection.CandidateCells(Footprint2X2), AllFree);

			Assert.That(slots.Take(4).Select(s => s.Cell), Is.EqualTo(new[] { new CPos(10, 10), new CPos(11, 10), new CPos(10, 11), new CPos(11, 11) }));
			Assert.That(slots.Take(4).All(s => s.SubCell == (SubCell)1), Is.True);
			Assert.That(slots[4], Is.EqualTo((new CPos(10, 10), (SubCell)2)));
		}

		[TestCase(TestName = "Eject_BlockedFootprint_UsesSurroundingCells")]
		public void BlockedFootprint_UsesSurroundingCells()
		{
			static IReadOnlyList<SubCell> Free(CPos c) => Footprint2X2.Contains(c) ? Array.Empty<SubCell>() : ThreeSubCells;

			var slots = GarrisonEjection.AssignSlots(10, GarrisonEjection.CandidateCells(Footprint2X2), Free);

			Assert.That(slots, Has.Count.EqualTo(10));
			Assert.That(slots.Any(s => Footprint2X2.Contains(s.Cell)), Is.False);
			Assert.That(slots[0], Is.EqualTo((new CPos(9, 9), (SubCell)1)));
		}

		[TestCase(TestName = "Eject_UsesOnlyFreeSubCells")]
		public void UsesOnlyFreeSubCells()
		{
			static IReadOnlyList<SubCell> Free(CPos c) => c == new CPos(10, 10) ? new[] { (SubCell)3 } : Array.Empty<SubCell>();

			var slots = GarrisonEjection.AssignSlots(3, GarrisonEjection.CandidateCells(new[] { new CPos(10, 10) }), Free);

			Assert.That(slots, Is.EqualTo(new[] { (new CPos(10, 10), (SubCell)3) }));
		}

		[TestCase(TestName = "Eject_NotEnoughRoom_ReturnsFewerSlots")]
		public void NotEnoughRoom_ReturnsFewerSlots()
		{
			// Only the origin cell, as the engine's EjectOnDeath uses: three of ten survive.
			var slots = GarrisonEjection.AssignSlots(10, new[] { new[] { new CPos(10, 10) } }, AllFree);

			Assert.That(slots, Has.Count.EqualTo(3));
		}

		[TestCase(TestName = "Eject_NoOccupants_NoSlots")]
		public void NoOccupants_NoSlots()
		{
			Assert.That(GarrisonEjection.AssignSlots(0, GarrisonEjection.CandidateCells(Footprint2X2), AllFree), Is.Empty);
		}
	}
}
