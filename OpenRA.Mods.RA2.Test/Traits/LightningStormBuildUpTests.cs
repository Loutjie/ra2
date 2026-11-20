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
	public class LightningStormBuildUpTests
	{
		const int Full = LightningStormBuildUp.FullStrength;

		[Test]
		public void TintRampsInFromClearToFull()
		{
			Assert.That(LightningStormBuildUp.TintPerMille(0, 200, 500, 0), Is.EqualTo(0));
			Assert.That(LightningStormBuildUp.TintPerMille(50, 200, 500, 0), Is.EqualTo(250));
			Assert.That(LightningStormBuildUp.TintPerMille(100, 200, 500, 0), Is.EqualTo(500));
			Assert.That(LightningStormBuildUp.TintPerMille(200, 200, 500, 0), Is.EqualTo(Full));
		}

		[Test]
		public void TintRampNeverDecreases()
		{
			var last = -1;
			for (var t = 0; t < 208; t++)
			{
				var s = LightningStormBuildUp.TintPerMille(t, 208, 508, 50);
				Assert.That(s, Is.GreaterThanOrEqualTo(last));
				last = s;
			}
		}

		[Test]
		public void TintHoldsThenFadesOutAndEnds()
		{
			Assert.That(LightningStormBuildUp.TintPerMille(449, 200, 500, 50), Is.EqualTo(Full));
			Assert.That(LightningStormBuildUp.TintPerMille(475, 200, 500, 50), Is.EqualTo(500));
			Assert.That(LightningStormBuildUp.TintPerMille(499, 200, 500, 50), Is.EqualTo(20));
			Assert.That(LightningStormBuildUp.TintPerMille(500, 200, 500, 50), Is.EqualTo(0));
		}

		[Test]
		public void NoRampOrFadeIsAFlatTint()
		{
			Assert.That(LightningStormBuildUp.TintPerMille(0, 0, 300, 0), Is.EqualTo(Full));
			Assert.That(LightningStormBuildUp.TintPerMille(299, 0, 300, 0), Is.EqualTo(Full));
			Assert.That(LightningStormBuildUp.TintPerMille(300, 0, 300, 0), Is.EqualTo(0));
			Assert.That(LightningStormBuildUp.TintPerMille(-1, 0, 300, 0), Is.EqualTo(0));
		}

		[Test]
		public void InactiveEffectHasNoTint()
		{
			Assert.That(LightningStormBuildUp.TintPerMille(0, 0, 0, 0), Is.EqualTo(0));
		}

		[TestCase(0, 200, 0)]
		[TestCase(Full, 200, 200)]
		[TestCase(500, 200, 100)]
		[TestCase(333, 200, 67)]
		[TestCase(400, 0, 0)]
		public void RampStartMatchesCurrentStrength(int current, int rampIn, int expected)
		{
			Assert.That(LightningStormBuildUp.RampStartFor(current, rampIn), Is.EqualTo(expected));
		}

		[Test]
		public void RampRestartNeverLightensTheSky()
		{
			for (var current = 0; current <= Full; current += 7)
			{
				var start = LightningStormBuildUp.RampStartFor(current, 208);
				Assert.That(LightningStormBuildUp.TintPerMille(start, 208, 208 + 300, 0), Is.GreaterThanOrEqualTo(current));
			}
		}

		[Test]
		public void CloudsThickenTowardsTheFirstStrike()
		{
			Assert.That(LightningStormBuildUp.CloudInterval(0, 200, 20, 5), Is.EqualTo(20));
			Assert.That(LightningStormBuildUp.CloudInterval(100, 200, 20, 5), Is.EqualTo(13));
			Assert.That(LightningStormBuildUp.CloudInterval(200, 200, 20, 5), Is.EqualTo(5));
			Assert.That(LightningStormBuildUp.CloudInterval(500, 200, 20, 5), Is.EqualTo(5));
		}

		[Test]
		public void CloudIntervalIsAtLeastOneTick()
		{
			Assert.That(LightningStormBuildUp.CloudInterval(200, 200, 0, 0), Is.EqualTo(1));
			Assert.That(LightningStormBuildUp.CloudInterval(0, 0, 20, 0), Is.EqualTo(1));
		}
	}
}
