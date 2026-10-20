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
	public class CarrierReturnTests
	{
		static readonly WDist Threshold = new(512);

		[TestCase(TestName = "Circle when the carrier is moving and has drifted beyond the threshold")]
		public void MovingParentBeyondThresholdIsStale()
		{
			Assert.That(CarrierChild.ShouldCircle(parentMoved: true, new WDist(513), Threshold), Is.True);
		}

		[TestCase(TestName = "Keep landing while the carrier is moving but still within the threshold")]
		public void MovingParentWithinThresholdIsNotStale()
		{
			Assert.That(CarrierChild.ShouldCircle(parentMoved: true, new WDist(512), Threshold), Is.False);
			Assert.That(CarrierChild.ShouldCircle(parentMoved: true, WDist.Zero, Threshold), Is.False);
		}

		[TestCase(TestName = "Keep landing once the carrier has stopped, however far it drifted")]
		public void StoppedParentIsNotStale()
		{
			Assert.That(CarrierChild.ShouldCircle(parentMoved: false, new WDist(4096), Threshold), Is.False);
		}
	}
}
