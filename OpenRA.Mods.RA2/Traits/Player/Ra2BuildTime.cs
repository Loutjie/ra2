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

namespace OpenRA.Mods.RA2.Traits
{
	/// <summary>
	/// Red Alert 2's production speed rules from rules.ini [General], with fractions expressed as integer percents.
	/// <list type="bullet">
	/// <item><c>MultipleFactory</c> is a linear, cumulative multiplier per extra factory of the same type:
	/// at 0.8 the build time is 1, 0.8, 0.64, 0.512, ... for 1, 2, 3, 4 factories
	/// (https://modenc.renegadeprojects.com/MultipleFactory).</item>
	/// <item>On low power the speed penalty is the power shortfall multiplied by <c>LowPowerPenaltyModifier</c>
	/// (2 turns a 30% shortfall into a 60% penalty, 0.5 into 15%;
	/// https://modenc.renegadeprojects.com/LowPowerPenaltyModifier), and the resulting speed is clamped between
	/// <c>MinLowPowerProductionSpeed</c> (applied after the modifier) and <c>MaxLowPowerProductionSpeed</c>
	/// (so 99% power builds at that maximum; https://modenc.renegadeprojects.com/MinLowPowerProductionSpeed,
	/// https://modenc.renegadeprojects.com/MaxLowPowerProductionSpeed).</item>
	/// </list>
	/// </summary>
	public static class Ra2BuildTime
	{
		/// <summary>
		/// Build time in ticks for an item whose single-factory, full-power time is <paramref name="baseTicks"/>.
		/// Fewer than one factory counts as one.
		/// </summary>
		public static int Compute(int baseTicks, int factoryCount, int multipleFactoryPercent,
			int powerProvided, int powerDrained, int minLowPowerPercent, int maxLowPowerPercent, int penaltyPercent)
		{
			var ticks = MultipleFactoryTicks(baseTicks, factoryCount, multipleFactoryPercent);
			var speedPercent = LowPowerSpeedPercent(powerProvided, powerDrained, minLowPowerPercent, maxLowPowerPercent, penaltyPercent);
			return (int)((long)ticks * 100 / speedPercent);
		}

		/// <summary>Applies <c>MultipleFactory</c> once per factory beyond the first.</summary>
		public static int MultipleFactoryTicks(int baseTicks, int factoryCount, int multipleFactoryPercent)
		{
			long ticks = baseTicks;
			for (var i = 1; i < factoryCount; i++)
				ticks = ticks * multipleFactoryPercent / 100;

			return (int)ticks;
		}

		/// <summary>Production speed in percent: 100 with enough power, otherwise RA2's clamped low-power speed.</summary>
		public static int LowPowerSpeedPercent(int powerProvided, int powerDrained,
			int minLowPowerPercent, int maxLowPowerPercent, int penaltyPercent)
		{
			if (powerProvided >= powerDrained)
				return 100;

			// Shortfall in percent of the drain, scaled by the penalty modifier (both percents).
			var shortfallPercent = 100 - (long)powerProvided * 100 / powerDrained;
			var speed = 100 - shortfallPercent * penaltyPercent / 100;
			return (int)long.Clamp(speed, minLowPowerPercent, maxLowPowerPercent);
		}
	}
}
