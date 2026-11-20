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

using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.RA2.Traits;

namespace OpenRA.Mods.RA2.Test
{
	[TestFixture]
	public class PrismSupportNetworkTests
	{
		static readonly WDist Range = WDist.FromCells(8);

		static WPos Cell(int x, int y) => new(x * 1024, y * 1024, 0);

		[Test]
		public void NoCandidatesGivesNoSupport()
		{
			var links = PrismSupportNetwork.Select(Cell(0, 0), [], Range, 8, true);
			Assert.That(links, Is.Empty);
		}

		[Test]
		public void TowersInRangeSupportTheFiringTower()
		{
			var links = PrismSupportNetwork.Select(Cell(0, 0), [Cell(3, 0), Cell(0, 8)], Range, 8, true);
			Assert.That(links.Select(l => l.Supporter), Is.EquivalentTo(new[] { 0, 1 }));
			Assert.That(links.All(l => l.Receiver == PrismSupportNetwork.FiringTower), Is.True);
		}

		[Test]
		public void TowersOutOfRangeDoNotSupport()
		{
			var links = PrismSupportNetwork.Select(Cell(0, 0), [Cell(9, 0)], Range, 8, false);
			Assert.That(links, Is.Empty);
		}

		[Test]
		public void SupportIsCappedAtMax()
		{
			var candidates = Enumerable.Range(1, 12).Select(i => Cell(i % 4, i / 4)).ToArray();
			var links = PrismSupportNetwork.Select(Cell(0, 0), candidates, Range, 8, true);
			Assert.That(links, Has.Count.EqualTo(8));
			Assert.That(links.Select(l => l.Supporter).Distinct().Count(), Is.EqualTo(8));
		}

		[Test]
		public void NearestTowersAreChosenFirst()
		{
			var links = PrismSupportNetwork.Select(Cell(0, 0), [Cell(7, 0), Cell(2, 0), Cell(5, 0)], Range, 2, false);
			Assert.That(links.Select(l => l.Supporter), Is.EqualTo(new[] { 1, 2 }));
		}

		[Test]
		public void ChainsRelayThroughSupportingTowers()
		{
			// 0 is in range of the firing tower; 1 is only in range of 0; 2 is only in range of 1.
			var links = PrismSupportNetwork.Select(Cell(0, 0), [Cell(6, 0), Cell(12, 0), Cell(18, 0)], Range, 8, true);
			Assert.That(links, Has.Count.EqualTo(3));
			Assert.That(links[0], Is.EqualTo(new PrismSupportLink(0, PrismSupportNetwork.FiringTower)));
			Assert.That(links[1], Is.EqualTo(new PrismSupportLink(1, 0)));
			Assert.That(links[2], Is.EqualTo(new PrismSupportLink(2, 1)));
		}

		[Test]
		public void WithoutChainingOnlyDirectNeighboursSupport()
		{
			var links = PrismSupportNetwork.Select(Cell(0, 0), [Cell(6, 0), Cell(12, 0)], Range, 8, false);
			Assert.That(links.Select(l => l.Supporter), Is.EqualTo(new[] { 0 }));
		}

		[Test]
		public void ChainCountsTowardsMax()
		{
			var links = PrismSupportNetwork.Select(Cell(0, 0), [Cell(6, 0), Cell(12, 0), Cell(18, 0)], Range, 2, true);
			Assert.That(links.Select(l => l.Supporter), Is.EqualTo(new[] { 0, 1 }));
		}

		[Test]
		public void SelectionIsDeterministicForEqualDistances()
		{
			var candidates = new[] { Cell(0, 4), Cell(4, 0), Cell(-4, 0), Cell(0, -4) };
			var first = PrismSupportNetwork.Select(Cell(0, 0), candidates, Range, 2, false);
			var second = PrismSupportNetwork.Select(Cell(0, 0), candidates, Range, 2, false);
			Assert.That(first, Is.EqualTo(second));
			Assert.That(first.Select(l => l.Supporter), Is.EqualTo(new[] { 0, 1 }));
		}

		[TestCase(0, 100)]
		[TestCase(1, 250)]
		[TestCase(8, 1300)]
		public void DamageGrowsByTheModifierPerSupportBeam(int beams, int expected)
		{
			Assert.That(PrismSupportNetwork.DamagePercent(beams, 150), Is.EqualTo(expected));
		}
	}
}
