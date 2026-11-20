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
	public class DistributedFireTests
	{
		[Test]
		public void NoOtherTargetsKeepsThePrimary()
		{
			Assert.That(DistributedFire.Pick(7, [], 0), Is.EqualTo(7));
			Assert.That(DistributedFire.Pick(7, [7], 3), Is.EqualTo(7));
		}

		[Test]
		public void FirstShotGoesAtThePrimary()
		{
			Assert.That(DistributedFire.Pick(7, [3, 7, 9], 0), Is.EqualTo(7));
		}

		[Test]
		public void LaterShotsCycleThroughTheOthersInIdOrder()
		{
			var shots = Enumerable.Range(0, 6).Select(i => DistributedFire.Pick(7, [9, 3, 7], i));
			Assert.That(shots, Is.EqualTo(new uint[] { 7, 3, 9, 7, 3, 9 }));
		}

		[Test]
		public void DuplicateCandidatesCountOnce()
		{
			var shots = Enumerable.Range(0, 3).Select(i => DistributedFire.Pick(1, [2, 2, 1], i));
			Assert.That(shots, Is.EqualTo(new uint[] { 1, 2, 1 }));
		}
	}
}
