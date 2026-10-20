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
	public class PadLimitedProductionQueueTests
	{
		static readonly ActorInfo Harrier = new("orca", new PadBoundInfo());
		static readonly ActorInfo Kirov = new("zep");

		static ActorInfo[] Queued(int padBound, int other = 0)
		{
			return Enumerable.Repeat(Harrier, padBound).Concat(Enumerable.Repeat(Kirov, other)).ToArray();
		}

		[TestCase(4, 0, 0, ExpectedResult = true)]
		[TestCase(4, 0, 3, ExpectedResult = true)]
		[TestCase(4, 0, 4, ExpectedResult = false, TestName = "One Air Force Command: the fifth Harrier cannot be queued")]
		[TestCase(4, 2, 2, ExpectedResult = false, TestName = "Queued and owned Harriers both take pads")]
		[TestCase(4, 3, 0, ExpectedResult = true)]
		[TestCase(8, 4, 3, ExpectedResult = true, TestName = "Two Air Force Commands give eight pads")]
		[TestCase(0, 0, 0, ExpectedResult = false, TestName = "No pads, no Harriers")]
		public bool CanQueueHarrier(int pads, int owned, int queued)
		{
			return PadLimitedProductionQueue.CanQueue(false, Harrier, pads, owned, Queued(queued));
		}

		[TestCase(TestName = "Queued actors without PadBound take no pad")]
		public void OtherQueuedActorsTakeNoPad()
		{
			Assert.That(PadLimitedProductionQueue.CanQueue(false, Harrier, 4, 1, Queued(2, 5)), Is.True);
		}

		[TestCase(TestName = "Actors without PadBound are never limited")]
		public void OtherActorsAreNotLimited()
		{
			Assert.That(PadLimitedProductionQueue.CanQueue(false, Kirov, 0, 9, Queued(9)), Is.True);
			Assert.That(PadLimitedProductionQueue.CanDeliver(false, Kirov, 0, 9), Is.True);
			Assert.That(PadLimitedProductionQueue.IsBuildable(false, Kirov, 0, 9, false), Is.True);
		}

		[TestCase(TestName = "All tech bypasses the pad limit")]
		public void AllTechBypassesLimit()
		{
			Assert.That(PadLimitedProductionQueue.CanQueue(true, Harrier, 4, 4, Queued(4)), Is.True);
			Assert.That(PadLimitedProductionQueue.CanDeliver(true, Harrier, 4, 4), Is.True);
			Assert.That(PadLimitedProductionQueue.IsBuildable(true, Harrier, 4, 4, false), Is.True);
		}

		[TestCase(4, 3, ExpectedResult = true)]
		[TestCase(4, 4, ExpectedResult = false, TestName = "A finished Harrier waits while every pad is taken")]
		[TestCase(4, 5, ExpectedResult = false, TestName = "Losing an Air Force Command never frees a pad")]
		public bool CanDeliverHarrier(int pads, int owned)
		{
			return PadLimitedProductionQueue.CanDeliver(false, Harrier, pads, owned);
		}

		[TestCase(4, 3, false, ExpectedResult = true)]
		[TestCase(4, 4, false, ExpectedResult = false, TestName = "The Harrier cameo greys once every pad is owned")]
		[TestCase(4, 6, true, ExpectedResult = true, TestName = "Queued Harriers stay buildable so they are not cancelled")]
		public bool HarrierIsBuildable(int pads, int owned, bool inQueue)
		{
			return PadLimitedProductionQueue.IsBuildable(false, Harrier, pads, owned, inQueue);
		}
	}
}
