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
using NUnit.Framework;
using OpenRA.Mods.RA2.Traits;

namespace OpenRA.Mods.RA2.Test
{
	[TestFixture]
	public class ChronoResourceDeliveryTests
	{
		// enabled, hostReserved, movingToDock, landDockFound
		[TestCase(true, false, true, false, ExpectedResult = true, TestName = "Delivering, no land dock: search")]
		[TestCase(true, false, true, true, ExpectedResult = false, TestName = "Land dock found: leave to MovingToDock")]
		[TestCase(true, false, false, false, ExpectedResult = false, TestName = "Not moving to a dock: no search")]
		[TestCase(true, true, true, false, ExpectedResult = false, TestName = "Host already reserved: no search")]
		[TestCase(false, false, true, false, ExpectedResult = false, TestName = "Dock client disabled: no search")]
		public bool ShouldSearchUnreachableDockTruthTable(bool enabled, bool hostReserved, bool movingToDock, bool landDockFound)
		{
			return ChronoResourceDelivery.ShouldSearchUnreachableDock(enabled, hostReserved, movingToDock, landDockFound);
		}

		[TestCase(TestName = "Closest dock is chosen by straight-line distance, not by land path")]
		public void ClosestDockCellIgnoresTerrain()
		{
			var from = new CPos(10, 10);
			var far = new CPos(40, 10);
			var nearAcrossWater = new CPos(14, 10);

			Assert.That(ChronoResourceDelivery.ClosestDockCell(from, [far, nearAcrossWater]), Is.EqualTo(nearAcrossWater));
		}

		[TestCase(TestName = "Equidistant docks: first one wins")]
		public void ClosestDockCellTieKeepsFirst()
		{
			var from = new CPos(10, 10);
			var first = new CPos(14, 10);
			var second = new CPos(6, 10);

			Assert.That(ChronoResourceDelivery.ClosestDockCell(from, [first, second]), Is.EqualTo(first));
			Assert.That(ChronoResourceDelivery.ClosestDockCell(from, [second, first]), Is.EqualTo(second));
		}

		[TestCase(TestName = "No docks: no destination")]
		public void ClosestDockCellWithoutDocks()
		{
			Assert.That(ChronoResourceDelivery.ClosestDockCell(new CPos(1, 1), Array.Empty<CPos>()), Is.Null);
		}
	}
}
