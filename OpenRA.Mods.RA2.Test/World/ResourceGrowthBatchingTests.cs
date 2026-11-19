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
	public class ResourceGrowthBatchingTests
	{
		static int Total(int cells, int interval, int ticks, out int maxPerTick)
		{
			var budget = 0;
			var total = 0;
			maxPerTick = 0;
			for (var i = 0; i < ticks; i++)
			{
				var n = ResourceGrowthBatching.NextBatch(ref budget, cells, interval);
				total += n;
				maxPerTick = System.Math.Max(maxPerTick, n);
			}

			return total;
		}

		[Test]
		public void NextBatch_VisitsEveryCellOncePerInterval()
		{
			Assert.That(Total(7500, 7500, 7500, out var max), Is.EqualTo(7500));
			Assert.That(max, Is.EqualTo(1));
			Assert.That(Total(30000, 7500, 7500, out max), Is.EqualTo(30000));
			Assert.That(max, Is.EqualTo(4));
			Assert.That(Total(2000, 7500, 7500, out _), Is.EqualTo(2000));
		}

		[Test]
		public void NextBatch_FewCells_WaitsThenVisits()
		{
			var budget = 0;
			for (var i = 0; i < 749; i++)
				Assert.That(ResourceGrowthBatching.NextBatch(ref budget, 10, 7500), Is.EqualTo(0));

			Assert.That(ResourceGrowthBatching.NextBatch(ref budget, 10, 7500), Is.EqualTo(1));
			Assert.That(Total(10, 7500, 7500, out _), Is.EqualTo(10));
		}

		[Test]
		public void NextBatch_ZeroCells_ZeroAndResetsBudget()
		{
			var budget = 123;
			Assert.That(ResourceGrowthBatching.NextBatch(ref budget, 0, 7500), Is.EqualTo(0));
			Assert.That(budget, Is.EqualTo(0));
		}
	}
}
