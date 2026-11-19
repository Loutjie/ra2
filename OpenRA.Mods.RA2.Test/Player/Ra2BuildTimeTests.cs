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
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.RA2.Traits;

namespace OpenRA.Mods.RA2.Test
{
	[TestFixture]
	public class Ra2BuildTimeTests
	{
		// RA2 [General]: MultipleFactory=0.8, MinLowPowerProductionSpeed=.5, MaxLowPowerProductionSpeed=.8, LowPowerPenaltyModifier=1.
		const int MultipleFactory = 80;
		const int MinSpeed = 50;
		const int MaxSpeed = 80;
		const int Penalty = 100;

		[Test]
		public void Compute_SingleFactoryFullPower_ReturnsBase()
		{
			Assert.That(Ra2BuildTime.Compute(1000, 1, MultipleFactory, 200, 100, MinSpeed, MaxSpeed, Penalty), Is.EqualTo(1000));
			Assert.That(Ra2BuildTime.Compute(1000, 1, MultipleFactory, 100, 100, MinSpeed, MaxSpeed, Penalty), Is.EqualTo(1000));
		}

		[Test]
		public void Compute_NoPowerDrain_ReturnsBase()
		{
			Assert.That(Ra2BuildTime.Compute(1000, 1, MultipleFactory, 0, 0, MinSpeed, MaxSpeed, Penalty), Is.EqualTo(1000));
		}

		[Test]
		public void Compute_TwoFactories_AppliesCumulativeMultiplier()
		{
			Assert.That(Ra2BuildTime.Compute(1000, 2, MultipleFactory, 100, 100, MinSpeed, MaxSpeed, Penalty), Is.EqualTo(800));
			Assert.That(Ra2BuildTime.Compute(1000, 3, MultipleFactory, 100, 100, MinSpeed, MaxSpeed, Penalty), Is.EqualTo(640));
			Assert.That(Ra2BuildTime.Compute(1000, 4, MultipleFactory, 100, 100, MinSpeed, MaxSpeed, Penalty), Is.EqualTo(512));
		}

		[Test]
		public void Compute_NoFactory_TreatedAsOne()
		{
			Assert.That(Ra2BuildTime.Compute(1000, 0, MultipleFactory, 100, 100, MinSpeed, MaxSpeed, Penalty), Is.EqualTo(1000));
		}

		[Test]
		public void Compute_LowPower_ClampsBetweenMinAndMax()
		{
			// 99% power: speed 99% is capped at MaxLowPowerProductionSpeed (80%), so the time is 100 / 80 of base.
			Assert.That(Ra2BuildTime.Compute(1000, 1, MultipleFactory, 99, 100, MinSpeed, MaxSpeed, Penalty), Is.EqualTo(1250));

			// 10% power: speed 10% is raised to MinLowPowerProductionSpeed (50%), so the time doubles.
			Assert.That(Ra2BuildTime.Compute(1000, 1, MultipleFactory, 10, 100, MinSpeed, MaxSpeed, Penalty), Is.EqualTo(2000));

			// 60% power: speed 60% lies between the bounds.
			Assert.That(Ra2BuildTime.Compute(600, 1, MultipleFactory, 60, 100, MinSpeed, MaxSpeed, Penalty), Is.EqualTo(1000));
		}

		[Test]
		public void Compute_LowPower_PenaltyModifierScalesShortfall()
		{
			// 20% short with a 50% penalty modifier: 10% penalty, speed 90% capped at the 80% maximum.
			Assert.That(Ra2BuildTime.LowPowerSpeedPercent(80, 100, MinSpeed, MaxSpeed, 50), Is.EqualTo(80));

			// 30% short with a 200% penalty modifier: 60% penalty, speed 40% raised to the 50% minimum.
			Assert.That(Ra2BuildTime.LowPowerSpeedPercent(70, 100, MinSpeed, MaxSpeed, 200), Is.EqualTo(50));

			// 30% short with a 100% penalty modifier: speed 70%.
			Assert.That(Ra2BuildTime.LowPowerSpeedPercent(70, 100, MinSpeed, MaxSpeed, 100), Is.EqualTo(70));
		}

		[Test]
		public void Compute_FactoriesAndLowPower_Combine()
		{
			Assert.That(Ra2BuildTime.Compute(1000, 2, MultipleFactory, 10, 100, MinSpeed, MaxSpeed, Penalty), Is.EqualTo(1600));
		}
	}

	[TestFixture]
	public class BalanceProductionQueueTests
	{
		static int NotEvaluated() => throw new AssertionException("Exact-only input evaluated without the prerequisite.");

		[TestCase(0)]
		[TestCase(750)]
		public void BalanceProductionQueue_WithoutPrerequisite_BuildTimeEqualsClassic(int classicTime)
		{
			// GetBuildTime routes through SelectBuildTime with base.GetBuildTime (ClassicProductionQueue) as classicTime.
			var info = new BalanceProductionQueueInfo();
			Assert.That(BalanceProductionQueue.SelectBuildTime(false, () => classicTime, NotEvaluated, NotEvaluated, info),
				Is.EqualTo(classicTime));
		}

		[Test]
		public void BalanceProductionQueue_WithoutPrerequisite_LeavesLowPowerToEngine()
		{
			// TickInner and RemainingTimeActual defer to the base class when this is null.
			var info = new BalanceProductionQueueInfo();
			Assert.That(BalanceProductionQueue.SelectLowPowerSpeedPercent(false, 10, 100, info), Is.Null);
		}

		[Test]
		public void BalanceProductionQueue_WithPrerequisite_UsesRa2Rules()
		{
			var info = new BalanceProductionQueueInfo();
			Assert.That(BalanceProductionQueue.SelectBuildTime(true, NotEvaluated, () => 1000, () => 2, info),
				Is.EqualTo(1000 * info.MultipleFactory / 100));
			Assert.That(BalanceProductionQueue.SelectLowPowerSpeedPercent(true, 100, 100, info), Is.EqualTo(100));
			Assert.That(BalanceProductionQueue.SelectLowPowerSpeedPercent(true, 0, 0, info), Is.EqualTo(100));
			Assert.That(BalanceProductionQueue.SelectLowPowerSpeedPercent(true, 10, 100, info), Is.EqualTo(info.MinLowPowerProductionSpeed));
		}

		[Test]
		public void BalanceProductionQueue_IsAnExtendableClassicQueue()
		{
			Assert.That(typeof(ClassicProductionQueueInfo).IsAssignableFrom(typeof(BalanceProductionQueueInfo)), Is.True);
			Assert.That(typeof(ClassicProductionQueue).IsAssignableFrom(typeof(BalanceProductionQueue)), Is.True);
			Assert.That(typeof(BalanceProductionQueueInfo).IsSealed, Is.False);
			Assert.That(typeof(BalanceProductionQueue).IsSealed, Is.False);
		}

		[Test]
		public void BalanceProductionQueue_Defaults_ExactPrerequisite()
		{
			Assert.That(new BalanceProductionQueueInfo().ExactPrerequisite, Is.EqualTo("balance.exact"));
		}
	}
}
