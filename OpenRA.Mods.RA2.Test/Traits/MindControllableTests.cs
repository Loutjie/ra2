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
	public class MindControllableTests
	{
		sealed class P { }

		readonly P yuri = new();
		readonly P allies = new();

		[TestCase(TestName = "MindControl_SameOwnerReassignment_KeepsControl")]
		public void SameOwnerReassignment_KeepsControl()
		{
			// A garrison flipping to the occupant's owner re-owns the (controlled) occupant to the same player.
			Assert.That(MindControllable.LosesControlOnOwnerChange(false, yuri, yuri), Is.False);
		}

		[TestCase(TestName = "MindControl_RealOwnerChange_LosesControl")]
		public void RealOwnerChange_LosesControl()
		{
			Assert.That(MindControllable.LosesControlOnOwnerChange(false, yuri, allies), Is.True);
		}

		[TestCase(TestName = "MindControl_ControlTransfer_KeepsControl")]
		public void ControlTransfer_KeepsControl()
		{
			Assert.That(MindControllable.LosesControlOnOwnerChange(true, allies, yuri), Is.False);
		}
	}
}
