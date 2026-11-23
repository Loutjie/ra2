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

using System;

namespace OpenRA.Mods.RA2.Traits
{
	/// <summary>
	/// Integer timing for the Lightning Storm's build-up (RA2's [General] LightningDeferment):
	/// how strongly the sky is tinted and how often gathering clouds appear. Integer-only so the
	/// cloud schedule stays sync-safe.
	/// </summary>
	public static class LightningStormBuildUp
	{
		public const int FullStrength = 1000;

		/// <summary>
		/// Tint strength in per-mille at <paramref name="elapsed"/> ticks into an effect that lasts
		/// <paramref name="total"/> ticks: it rises linearly from 0 over the first <paramref name="rampIn"/>
		/// ticks and falls linearly to 0 over the last <paramref name="fadeOut"/> ticks.
		/// </summary>
		public static int TintPerMille(int elapsed, int rampIn, int total, int fadeOut)
		{
			if (elapsed < 0 || elapsed >= total)
				return 0;

			var strength = FullStrength;
			if (rampIn > 0 && elapsed < rampIn)
				strength = Math.Min(strength, FullStrength * elapsed / rampIn);

			var remaining = total - elapsed;
			if (fadeOut > 0 && remaining < fadeOut)
				strength = Math.Min(strength, FullStrength * remaining / fadeOut);

			return strength;
		}

		/// <summary>
		/// The first tick of a new ramp of <paramref name="rampIn"/> ticks that already has
		/// <paramref name="currentPerMille"/> strength, so a storm launched while the sky is still
		/// tinted darkens on from where it is instead of flashing back to clear.
		/// </summary>
		public static int RampStartFor(int currentPerMille, int rampIn)
		{
			if (rampIn <= 0 || currentPerMille <= 0)
				return 0;

			if (currentPerMille >= FullStrength)
				return rampIn;

			// Round up so the ramp never starts below the current strength.
			return (currentPerMille * rampIn + FullStrength - 1) / FullStrength;
		}

		/// <summary>
		/// Ticks until the next gathering cloud, shrinking linearly from <paramref name="startInterval"/>
		/// at the start of a <paramref name="buildUp"/>-tick build-up to <paramref name="endInterval"/> at its
		/// end, so the clouds thicken as the first strike approaches. Never below 1.
		/// </summary>
		public static int CloudInterval(int elapsed, int buildUp, int startInterval, int endInterval)
		{
			if (buildUp <= 0)
				return Math.Max(1, endInterval);

			var t = Math.Clamp(elapsed, 0, buildUp);
			return Math.Max(1, startInterval + (endInterval - startInterval) * t / buildUp);
		}
	}
}
