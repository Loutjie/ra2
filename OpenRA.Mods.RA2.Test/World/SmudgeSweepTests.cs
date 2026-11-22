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
using NUnit.Framework;
using OpenRA.Mods.RA2.Traits;

namespace OpenRA.Mods.RA2.Test
{
	[TestFixture]
	public class SmudgeSweepTests
	{
		static List<int> Sweep(SmudgeSweep sweep, int ticks)
		{
			var visited = new List<int>();
			for (var t = 0; t < ticks; t++)
				visited.AddRange(sweep.Next());

			return visited;
		}

		[TestCase(100, 10)]
		[TestCase(101, 10)]
		[TestCase(20000, 1500)]
		[TestCase(7, 50)]
		public void EveryCellIsVisitedExactlyOncePerSweep(int cells, int duration)
		{
			var sweep = new SmudgeSweep(cells, duration);
			var visited = Sweep(sweep, sweep.TicksPerSweep);
			Assert.That(visited, Is.EquivalentTo(Enumerable.Range(0, cells)));
		}

		[Test]
		public void ASweepTakesTheDurationOrLessButNeverLonger()
		{
			Assert.That(new SmudgeSweep(20000, 1500).TicksPerSweep, Is.LessThanOrEqualTo(1500));
			Assert.That(new SmudgeSweep(20000, 1500).TicksPerSweep, Is.GreaterThan(1400));

			// Fewer cells than ticks: one cell per tick
			Assert.That(new SmudgeSweep(7, 50).TicksPerSweep, Is.EqualTo(7));
		}

		[Test]
		public void TheNextSweepRepeatsTheSameOrder()
		{
			var sweep = new SmudgeSweep(101, 10);
			var first = Sweep(sweep, sweep.TicksPerSweep);
			var second = Sweep(sweep, sweep.TicksPerSweep);
			Assert.That(second, Is.EqualTo(first));
		}

		[Test]
		public void ConsecutiveCellsAreScatteredNotNeighbours()
		{
			// Clearing neighbouring cells one after another would wipe a crater field in a visible line.
			var visited = Sweep(new SmudgeSweep(20000, 1500), 3);
			for (var i = 1; i < visited.Count; i++)
				Assert.That(System.Math.Abs(visited[i] - visited[i - 1]), Is.GreaterThan(100));
		}

		[TestCase(0)]
		[TestCase(1)]
		public void TinyMapsDoNotBreak(int cells)
		{
			var sweep = new SmudgeSweep(cells, 1500);
			Assert.That(Sweep(sweep, 5), Is.All.InRange(0, System.Math.Max(0, cells - 1)));
		}
	}
}
