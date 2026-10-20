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
	public static class ResourceGrowthBatching
	{
		/// <summary>
		/// Cells to process this tick so that, on average, every cell is visited once per <paramref name="interval"/> ticks
		/// for any cell count: a fractional budget carries the remainder between ticks. Deterministic and allocation-free.
		/// </summary>
		public static int NextBatch(ref int budget, int cellCount, int interval)
		{
			if (cellCount <= 0)
			{
				budget = 0;
				return 0;
			}

			interval = Math.Max(interval, 1);
			budget += cellCount;
			var size = budget / interval;
			budget %= interval;
			return size;
		}
	}
}
