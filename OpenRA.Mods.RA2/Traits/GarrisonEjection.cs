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
using System.Collections.Generic;
using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.RA2.Traits
{
	/// <summary>
	/// Pure placement logic for occupants leaving a destroyed garrison, kept free of engine state so it can be
	/// unit tested. The engine's Cargo.EjectOnDeath drops every passenger into the building's origin cell, which
	/// only holds as many infantry as there are sub-cells; the rest die.
	/// </summary>
	public static class GarrisonEjection
	{
		/// <summary>
		/// Candidate cells in a fixed order: the footprint first, then the ring of cells around it (diagonals
		/// included). Each group is sorted by row, then column, so the result does not depend on input order.
		/// </summary>
		public static IReadOnlyList<IReadOnlyList<CPos>> CandidateCells(IEnumerable<CPos> footprint)
		{
			var inner = footprint.Distinct().OrderBy(c => c.Y).ThenBy(c => c.X).ToList();
			var ring = inner
				.SelectMany(c => new[]
				{
					new CPos(c.X - 1, c.Y - 1), new CPos(c.X, c.Y - 1), new CPos(c.X + 1, c.Y - 1),
					new CPos(c.X - 1, c.Y), new CPos(c.X + 1, c.Y),
					new CPos(c.X - 1, c.Y + 1), new CPos(c.X, c.Y + 1), new CPos(c.X + 1, c.Y + 1),
				})
				.Distinct()
				.Except(inner)
				.OrderBy(c => c.Y).ThenBy(c => c.X)
				.ToList();

			return new[] { inner, ring };
		}

		/// <summary>
		/// Assigns a distinct (cell, sub-cell) slot to each of up to <paramref name="count"/> occupants. Tiers are
		/// used in order; within a tier the first free sub-cell of every cell is used before any cell gets a second
		/// occupant, so the occupants spread out. Returns fewer slots than <paramref name="count"/> when there is
		/// not enough room: the occupants without a slot cannot be placed.
		/// </summary>
		public static List<(T Cell, SubCell SubCell)> AssignSlots<T>(int count, IEnumerable<IReadOnlyList<T>> tiers,
			Func<T, IReadOnlyList<SubCell>> freeSubCells)
		{
			var slots = new List<(T, SubCell)>();
			foreach (var tier in tiers)
			{
				var free = tier.Select(freeSubCells).ToList();
				var maxDepth = free.Count == 0 ? 0 : free.Max(f => f.Count);
				for (var depth = 0; depth < maxDepth; depth++)
				{
					for (var i = 0; i < tier.Count; i++)
					{
						if (slots.Count >= count)
							return slots;

						if (depth < free[i].Count)
							slots.Add((tier[i], free[i][depth]));
					}
				}
			}

			return slots;
		}
	}
}
