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
	public class ChronoErasureTests
	{
		[Test]
		public void TotalIsStrengthTimesTen()
		{
			Assert.That(ChronoErasure.Total(125, 10), Is.EqualTo(1250));
		}

		[Test]
		public void EveryBeamAddsItsRate()
		{
			Assert.That(ChronoErasure.Step(100, [10, 19]), Is.EqualTo(129));
		}

		[Test]
		public void NoBeamResetsTheWarp()
		{
			Assert.That(ChronoErasure.Step(900, []), Is.Zero);
		}

		[Test]
		public void OneBeamErasesAGiInOneHundredTwentyFiveTicks()
		{
			var warp = 0;
			var ticks = 0;
			while (warp < ChronoErasure.Total(125, 10))
			{
				warp = ChronoErasure.Step(warp, [10]);
				ticks++;
			}

			Assert.That(ticks, Is.EqualTo(125));
		}
	}
}
