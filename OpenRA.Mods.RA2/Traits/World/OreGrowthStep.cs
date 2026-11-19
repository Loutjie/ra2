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
	/// <summary>The cell grid the growth step reads and mutates.</summary>
	public interface IGrowthGrid
	{
		/// <summary>Current density of the cell; 0 (or less) means no ore.</summary>
		int Density(CPos c);
		int MaxDensity { get; }

		/// <summary>True if one more unit of density may be added to the cell (terrain allows ore, not full).</summary>
		bool CanAdd(CPos c);

		/// <summary>Adds one unit of density to the cell, creating ore if it was empty.</summary>
		void Add(CPos c);

		/// <summary>
		/// The cells adjacent to <paramref name="c"/> that spread may target. The enumeration order must be
		/// fixed and identical on every client (the step picks the n-th candidate with a shared random number),
		/// and two consecutive enumerations must yield the same sequence.
		/// </summary>
		IEnumerable<CPos> Neighbours(CPos c);
	}

	public interface IGrowthRandom
	{
		/// <summary>Returns a value in [0, maxExclusive).</summary>
		int Next(int maxExclusive);
	}

	/// <summary>
	/// One pass of RA2-style ore growth over a batch of cells. Pure: all state is behind
	/// <see cref="IGrowthGrid"/> and <see cref="IGrowthRandom"/>, so the result is a function of the
	/// grid and the random sequence.
	/// <para>
	/// RA2 reference (ModEnc): rules.ini [Tiberiums] Growth/Spread are intervals in frames after which each
	/// ore overlay gets a GrowthPercentage / SpreadPercentage chance to grow or spread; both only act when
	/// the map has TiberiumGrows / TiberiumSpreads enabled and a percentage of 0 disables the behaviour.
	/// See https://modenc.renegadeprojects.com/GrowthPercentage ,
	/// https://modenc.renegadeprojects.com/SpreadPercentage ,
	/// https://modenc.renegadeprojects.com/Growth and https://modenc.renegadeprojects.com/Spread .
	/// The mod does not use the per-type Growth/Spread frame values (e.g. 2200 frames for [Riparius]): the
	/// [General] GrowthRate (5 minutes = 7500 ticks) sets the interval of one pass over every ore cell
	/// (ResourceGrowthInfo.Interval), and the per-type percentages are the per-pass chances applied here.
	/// The per-type frame values are unused because the mod keeps one pass interval for all ore types, the
	/// way the [General] GrowthRate setting does in RA2.
	/// Scheduling is the caller's concern; this class only applies the per-cell rolls.
	/// </para>
	/// <para>
	/// Per non-empty cell, two independent rolls: growth (only below max density) adds 1 density;
	/// spread (at any density, because ModEnc says every overlay instance rolls) adds 1 density to one
	/// randomly chosen neighbour that <see cref="IGrowthGrid.CanAdd"/>. Empty cells never act, so an
	/// emptied cell is refilled only by a neighbour's spread.
	/// </para>
	/// </summary>
	public static class OreGrowthStep
	{
		public static void Apply(IGrowthGrid grid, IReadOnlyList<CPos> batch, int growthChancePerMille, int spreadChancePerMille, IGrowthRandom random)
		{
			for (var i = 0; i < batch.Count; i++)
			{
				var cell = batch[i];
				if (grid.Density(cell) <= 0)
					continue;

				if (growthChancePerMille > 0 && grid.Density(cell) < grid.MaxDensity && random.Next(1000) < growthChancePerMille)
					grid.Add(cell);

				if (spreadChancePerMille > 0 && random.Next(1000) < spreadChancePerMille)
					Spread(grid, cell, random);
			}
		}

		static void Spread(IGrowthGrid grid, CPos cell, IGrowthRandom random)
		{
			// Two passes over the neighbours instead of collecting candidates, so the step itself allocates nothing
			// (the grid's CanAdd/Neighbours implementations may).
			var candidates = 0;
			foreach (var n in grid.Neighbours(cell))
				if (grid.CanAdd(n))
					candidates++;

			if (candidates == 0)
				return;

			var pick = random.Next(candidates);
			foreach (var n in grid.Neighbours(cell))
			{
				if (!grid.CanAdd(n))
					continue;

				if (pick-- == 0)
				{
					grid.Add(n);
					return;
				}
			}
		}
	}
}
