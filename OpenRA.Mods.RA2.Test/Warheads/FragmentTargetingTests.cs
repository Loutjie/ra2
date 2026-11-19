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
using OpenRA.Mods.RA2.Warheads;

namespace OpenRA.Mods.RA2.Test
{
	[TestFixture]
	public class FragmentTargetingTests
	{
		static readonly WDist Range = WDist.FromCells(3);

		static WPos Cell(int x, int y) => new(x * 1024, y * 1024, 0);

		[Test]
		public void NoCandidatesGivesNoFragments()
		{
			Assert.That(FragmentTargeting.Select(Cell(0, 0), [], Range, 5), Is.Empty);
		}

		[Test]
		public void CandidatesOutOfRangeAreIgnored()
		{
			var picked = FragmentTargeting.Select(Cell(0, 0), [Cell(4, 0), Cell(1, 0)], Range, 5);
			Assert.That(picked, Is.EqualTo(new[] { 1 }));
		}

		[Test]
		public void OneFragmentPerTargetUpToCount()
		{
			var candidates = new[] { Cell(1, 0), Cell(0, 1), Cell(-1, 0), Cell(0, -1), Cell(1, 1), Cell(2, 0), Cell(2, 2) };
			var picked = FragmentTargeting.Select(Cell(0, 0), candidates, Range, 5);
			Assert.That(picked, Has.Count.EqualTo(5));
			Assert.That(picked, Is.Unique);
		}

		[Test]
		public void NearestTargetsAreChosenFirstWithIndexTieBreak()
		{
			var picked = FragmentTargeting.Select(Cell(0, 0), [Cell(2, 0), Cell(0, 1), Cell(1, 0), Cell(3, 0)], Range, 3);
			Assert.That(picked, Is.EqualTo(new[] { 1, 2, 0 }));
		}

		[Test]
		public void ZeroCountGivesNoFragments()
		{
			Assert.That(FragmentTargeting.Select(Cell(0, 0), [Cell(1, 0)], Range, 0), Is.Empty);
		}
	}
}
