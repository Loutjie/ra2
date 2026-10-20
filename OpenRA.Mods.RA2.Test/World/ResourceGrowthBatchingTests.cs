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
		[Test]
		public void BatchSize_VisitsEveryCellOncePerInterval()
		{
			Assert.That(ResourceGrowthBatching.BatchSize(7500, 7500), Is.EqualTo(1));
			Assert.That(ResourceGrowthBatching.BatchSize(10, 7500), Is.EqualTo(1));
			Assert.That(ResourceGrowthBatching.BatchSize(30000, 7500), Is.EqualTo(4));
			Assert.That(ResourceGrowthBatching.BatchSize(7501, 7500), Is.EqualTo(2));
		}

		[Test]
		public void BatchSize_ZeroCells_Zero()
		{
			Assert.That(ResourceGrowthBatching.BatchSize(0, 7500), Is.EqualTo(0));
		}

		[Test]
		public void BatchSize_NonPositiveInterval_ClampedToOne()
		{
			Assert.That(ResourceGrowthBatching.BatchSize(5, 0), Is.EqualTo(5));
		}
	}
}
