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
	public class PadLimitedProductionQueueTests
	{
		[TestCase(4, 0, 0, ExpectedResult = true)]
		[TestCase(4, 0, 3, ExpectedResult = true)]
		[TestCase(4, 0, 4, ExpectedResult = false, TestName = "One Air Force Command: the fifth Harrier cannot be queued")]
		[TestCase(4, 2, 2, ExpectedResult = false)]
		[TestCase(4, 3, 0, ExpectedResult = true)]
		[TestCase(8, 4, 3, ExpectedResult = true, TestName = "Two Air Force Commands give eight pads")]
		[TestCase(0, 0, 0, ExpectedResult = false)]
		public bool HasPadForQueuing(int pads, int owned, int queued)
		{
			return PadLimitedProductionQueue.HasPadForQueuing(pads, owned, queued);
		}

		[TestCase(4, 3, ExpectedResult = true)]
		[TestCase(4, 4, ExpectedResult = false, TestName = "A finished aircraft waits while every pad is taken")]
		[TestCase(4, 5, ExpectedResult = false, TestName = "Losing an Air Force Command never frees a pad")]
		public bool HasPadForDelivery(int pads, int owned)
		{
			return PadLimitedProductionQueue.HasPadForDelivery(pads, owned);
		}
	}
}
