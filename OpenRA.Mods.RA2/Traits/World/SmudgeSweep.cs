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

using System.Collections.Generic;

namespace OpenRA.Mods.RA2.Traits
{
	/// <summary>
	/// Visits every cell index once per sweep, a few per tick, in a scattered order: a fixed stride
	/// coprime with the cell count jumps around the map instead of walking along a row.
	/// </summary>
	public sealed class SmudgeSweep
	{
		readonly int count;
		readonly int stride;
		readonly int perTick;
		int visited;

		public SmudgeSweep(int count, int duration)
		{
			this.count = count;
			perTick = count <= 0 ? 0 : (count + duration - 1) / duration;
			TicksPerSweep = perTick == 0 ? 0 : (count + perTick - 1) / perTick;

			// Golden-ratio step, nudged until it shares no factor with the count so it reaches every cell.
			stride = count <= 1 ? 1 : count * 618 / 1000;
			while (stride > 1 && Gcd(stride, count) != 1)
				stride++;
		}

		public int TicksPerSweep { get; }

		/// <summary>The cell indices to visit this tick.</summary>
		public IEnumerable<int> Next()
		{
			for (var i = 0; i < perTick && visited < count; i++)
			{
				yield return (int)((long)visited * stride % count);
				visited++;
			}

			if (visited >= count)
				visited = 0;
		}

		static int Gcd(int a, int b)
		{
			while (b != 0)
				(a, b) = (b, a % b);

			return a;
		}
	}
}
