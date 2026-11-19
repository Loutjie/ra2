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
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.RA2.Traits;

namespace OpenRA.Mods.RA2.Test
{
	[TestFixture]
	public class OreGrowthStepTests
	{
		sealed class FakeGrid : IGrowthGrid
		{
			public readonly Dictionary<CPos, int> Cells = [];
			public readonly HashSet<CPos> Blocked = [];
			public int Width = 5;
			public int Height = 5;
			public int MaxDensity { get; set; } = 3;

			public int Density(CPos c) => Cells.TryGetValue(c, out var d) ? d : 0;
			public bool CanAdd(CPos c) => c.X >= 0 && c.Y >= 0 && c.X < Width && c.Y < Height && !Blocked.Contains(c) && Density(c) < MaxDensity;
			public void Add(CPos c) => Cells[c] = Density(c) + 1;

			public IEnumerable<CPos> Neighbours(CPos c)
			{
				for (var dy = -1; dy <= 1; dy++)
					for (var dx = -1; dx <= 1; dx++)
						if (dx != 0 || dy != 0)
							yield return new CPos(c.X + dx, c.Y + dy);
			}
		}

		sealed class Scripted : IGrowthRandom
		{
			readonly Queue<int> values;
			public Scripted(params int[] v) { values = new Queue<int>(v); }
			public int Next(int maxExclusive) => values.Dequeue() % maxExclusive;
		}

		sealed class Seeded : IGrowthRandom
		{
			readonly System.Random rng;
			public Seeded(int seed) { rng = new System.Random(seed); }
			public int Next(int maxExclusive) => rng.Next(maxExclusive);
		}

		static readonly CPos Centre = new(2, 2);

		[Test]
		public void Step_GrowsBelowMaxDensity()
		{
			var grid = new FakeGrid();
			grid.Cells[Centre] = 1;
			OreGrowthStep.Apply(grid, new[] { Centre }, 60, 0, new Scripted(59));
			Assert.That(grid.Density(Centre), Is.EqualTo(2));
		}

		[Test]
		public void Step_GrowthRollMissed_NoChange()
		{
			var grid = new FakeGrid();
			grid.Cells[Centre] = 1;
			OreGrowthStep.Apply(grid, new[] { Centre }, 60, 0, new Scripted(60));
			Assert.That(grid.Density(Centre), Is.EqualTo(1));
		}

		[Test]
		public void Step_DoesNotGrowAtMax()
		{
			var grid = new FakeGrid();
			grid.Cells[Centre] = 3;

			// Only one value is scripted: a growth roll at max would dequeue past it and throw.
			OreGrowthStep.Apply(grid, new[] { Centre }, 1000, 0, new Scripted(0));
			Assert.That(grid.Density(Centre), Is.EqualTo(3));
		}

		[Test]
		public void Step_SpreadsFromFullCells()
		{
			var grid = new FakeGrid();
			grid.Cells[Centre] = 3;

			// Spread roll succeeds, then pick candidate index 0 (neighbours enumerate (1,1) first).
			OreGrowthStep.Apply(grid, new[] { Centre }, 1000, 1000, new Scripted(0, 0));
			Assert.That(grid.Density(Centre), Is.EqualTo(3));
			Assert.That(grid.Density(new CPos(1, 1)), Is.EqualTo(1));
			Assert.That(grid.Cells.Values.Sum(), Is.EqualTo(4));
		}

		[Test]
		public void Step_SpreadsFromNonFullCells()
		{
			// ModEnc: every overlay instance rolls to spread, not only full ones.
			var grid = new FakeGrid();
			grid.Cells[Centre] = 1;
			OreGrowthStep.Apply(grid, new[] { Centre }, 0, 1000, new Scripted(0, 7));
			Assert.That(grid.Cells.Values.Sum(), Is.EqualTo(2));
			Assert.That(grid.Density(new CPos(3, 3)), Is.EqualTo(1));
		}

		[Test]
		public void Step_DoesNotSpreadWhereCannotAdd()
		{
			var grid = new FakeGrid();
			grid.Cells[Centre] = 3;
			foreach (var n in grid.Neighbours(Centre))
				if (n != new CPos(3, 3))
					grid.Blocked.Add(n);

			for (var pick = 0; pick < 8; pick++)
			{
				grid.Cells.Remove(new CPos(3, 3));
				OreGrowthStep.Apply(grid, new[] { Centre }, 0, 1000, new Scripted(0, pick));
				Assert.That(grid.Density(new CPos(3, 3)), Is.EqualTo(1), "only open neighbour must receive it, pick " + pick);
				Assert.That(grid.Cells.Count, Is.EqualTo(2));
			}
		}

		[Test]
		public void Step_NoOpenNeighbour_SpreadDoesNothing()
		{
			var grid = new FakeGrid();
			grid.Cells[Centre] = 3;
			foreach (var n in grid.Neighbours(Centre))
				grid.Blocked.Add(n);

			OreGrowthStep.Apply(grid, new[] { Centre }, 0, 1000, new Scripted(0));
			Assert.That(grid.Cells.Count, Is.EqualTo(1));
		}

		[Test]
		public void Step_EmptyField_NoChanges()
		{
			var grid = new FakeGrid();
			var batch = new[] { Centre, new CPos(0, 0), new CPos(4, 4) };

			// No values scripted: an empty cell must not consume any randomness.
			OreGrowthStep.Apply(grid, batch, 1000, 1000, new Scripted());
			Assert.That(grid.Cells, Is.Empty);
		}

		[Test]
		public void Step_EmptiedCellOnlyRefilledBySpread()
		{
			var grid = new FakeGrid();
			grid.Cells[Centre] = 0; // harvested out

			OreGrowthStep.Apply(grid, new[] { Centre }, 1000, 0, new Scripted());
			Assert.That(grid.Density(Centre), Is.EqualTo(0), "growth must not revive an empty cell");

			// A full neighbour spreads into the empty cell (the only open neighbour).
			grid.Cells[new CPos(3, 3)] = 3;
			foreach (var n in grid.Neighbours(new CPos(3, 3)))
				if (n != Centre)
					grid.Blocked.Add(n);

			OreGrowthStep.Apply(grid, new[] { new CPos(3, 3) }, 0, 1000, new Scripted(0, 0));
			Assert.That(grid.Density(Centre), Is.EqualTo(1));
		}

		[Test]
		public void Step_SameSeed_SameResult()
		{
			static Dictionary<CPos, int> Run(int seed)
			{
				var grid = new FakeGrid { Width = 12, Height = 12 };
				for (var i = 4; i < 8; i++)
					for (var j = 4; j < 8; j++)
						grid.Cells[new CPos(i, j)] = 1 + (i + j) % 3;

				var batch = Enumerable.Range(0, 144).Select(n => new CPos(n % 12, n / 12)).ToList();
				var rng = new Seeded(seed);
				for (var pass = 0; pass < 20; pass++)
					OreGrowthStep.Apply(grid, batch, 300, 300, rng);

				return grid.Cells;
			}

			var a = Run(42);
			var b = Run(42);
			Assert.That(a.OrderBy(kv => kv.Key.Y).ThenBy(kv => kv.Key.X), Is.EqualTo(b.OrderBy(kv => kv.Key.Y).ThenBy(kv => kv.Key.X)));
			Assert.That(a.Count, Is.GreaterThan(16), "the field should have spread");
			Assert.That(Run(43).Count == a.Count && Run(43).All(kv => a.TryGetValue(kv.Key, out var d) && d == kv.Value), Is.False, "different seed should differ");
		}

		[Test]
		public void Step_ZeroChance_NeverChanges()
		{
			var grid = new FakeGrid();
			grid.Cells[Centre] = 1;
			grid.Cells[new CPos(1, 1)] = 3;
			var before = grid.Cells.ToDictionary(kv => kv.Key, kv => kv.Value);

			OreGrowthStep.Apply(grid, new[] { Centre, new CPos(1, 1) }, 0, 0, new Scripted());
			Assert.That(grid.Cells, Is.EqualTo(before));
		}
	}
}
