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
using OpenRA.Traits;

namespace OpenRA.Mods.RA2.Test
{
	[TestFixture]
	public class DisguiseDetectorTests
	{
		[TestCase(0, 0, ExpectedResult = true)]
		[TestCase(15, 0, ExpectedResult = true)]
		[TestCase(16, 0, ExpectedResult = false)]
		[TestCase(10, 11, ExpectedResult = true)]
		[TestCase(11, 11, ExpectedResult = false)]
		public bool DisguiseDetector_InRange_UsesHorizontalCellDistance(int dx, int dy)
		{
			var detector = new WPos(0, 0, 0);
			var target = new WPos(dx * 1024, dy * 1024, 2048);
			return DisguiseDetection.InRange(detector, target, WDist.FromCells(15));
		}

		[Test]
		public void DisguiseDetector_DefaultRange_IsFifteenCells()
		{
			Assert.That(new DisguiseDetectorInfo().Range, Is.EqualTo(WDist.FromCells(15)));
		}

		[Test]
		public void EngineDisguise_ResolvesInternalEngineTrait()
		{
			// The engine's Disguise trait is internal to OpenRA.Mods.Cnc and read through reflection;
			// this fails loudly if an engine bump renames it or its properties.
			Assert.That(EngineDisguise.TraitType.Name, Is.EqualTo("Disguise"));
			Assert.That(typeof(IEffectiveOwner).IsAssignableFrom(EngineDisguise.TraitType), Is.True);
			Assert.DoesNotThrow(() => System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(EngineDisguise).TypeHandle));
		}
	}
}
