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
using System.Collections;
using System.Collections.Generic;
using OpenRA.Mods.Common.Traits;
using OpenRA.Support;
using OpenRA.Traits;

namespace OpenRA.Mods.RA2.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("Makes a resource type grow denser and spread to adjacent cells over time, as ore does in RA2. Attach to the world actor, once per resource type.")]
	public class ResourceGrowthInfo : TraitInfo, Requires<IResourceLayerInfo>, IRulesetLoaded
	{
		[FieldLoader.Require]
		[Desc("Resource type (a key of the ResourceLayer's ResourceTypes) this instance grows.")]
		public readonly string ResourceType = null;

		[Desc("Ticks in which every cell holding the resource is visited once. RA2: GrowthRate of 5 minutes = 7500 ticks.")]
		public readonly int Interval = 7500;

		[Desc("Chance in 1/1000 per visit that a cell below maximum density gains one density.")]
		public readonly int GrowthChance = 0;

		[Desc("Chance in 1/1000 per visit that a cell adds one density to a random adjacent cell that accepts it.")]
		public readonly int SpreadChance = 0;

		void IRulesetLoaded<ActorInfo>.RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			var layerInfo = ai.TraitInfo<IResourceLayerInfo>();
			if (!layerInfo.TryGetResourceIndex(ResourceType, out _))
				throw new YamlException($"ResourceGrowth: unknown resource type '{ResourceType}'; it must be a key of the ResourceLayer's ResourceTypes.");
		}

		public override object Create(ActorInitializer init) { return new ResourceGrowth(init.Self, this); }
	}

	public class ResourceGrowth : ITick
	{
		// Adjacent cells are the 8 neighbours in CVec.Directions order (fixed, so identical on all clients).
		// ModEnc does not state the neighbourhood used by RA2's spread; 8 matches the engine's own density rule.
		readonly ResourceGrowthInfo info;
		readonly IResourceLayer layer;
		readonly string resourceType;
		readonly Grid grid;
		readonly IGrowthRandom random;

		// Tracked cells in insertion order. Removal swaps the last cell into the hole, which can make a cell be
		// visited twice or skipped within one cycle: deterministic and harmless. CellChanged is raised
		// deterministically from synced simulation code, so the order stays identical on every client.
		readonly List<CPos> cells = [];
		readonly Dictionary<CPos, int> indexOf = [];
		readonly List<CPos> batch = [];
		readonly bool enabled;
		bool scanned;
		int cursor;
		int budget;

		public ResourceGrowth(Actor self, ResourceGrowthInfo info)
		{
			this.info = info;
			resourceType = info.ResourceType;
			layer = self.Trait<IResourceLayer>();

			// Idle (no tracking, no scan, no tick work) when the instance can never do anything.
			enabled = info.GrowthChance > 0 || info.SpreadChance > 0;
			if (!enabled)
				return;

			grid = new Grid(layer, resourceType);
			random = new SharedRandomAdapter(self.World.SharedRandom);
			layer.CellChanged += OnCellChanged;
		}

		void OnCellChanged(CPos cell, string type)
		{
			if (type == resourceType)
				Track(cell);
			else
				Untrack(cell);
		}

		void Track(CPos cell)
		{
			if (indexOf.ContainsKey(cell))
				return;

			indexOf[cell] = cells.Count;
			cells.Add(cell);
		}

		void Untrack(CPos cell)
		{
			if (!indexOf.Remove(cell, out var index))
				return;

			var last = cells.Count - 1;
			if (index != last)
			{
				var moved = cells[last];
				cells[index] = moved;
				indexOf[moved] = index;
			}

			cells.RemoveAt(last);
		}

		void ITick.Tick(Actor self)
		{
			if (!enabled)
				return;

			// The resource layer fills itself in WorldLoaded, so scan on the first tick (fixed map order).
			if (!scanned)
			{
				scanned = true;
				if (!layer.IsEmpty)
					foreach (var cell in self.World.Map.AllCells)
						if (layer.GetResource(cell).Type == resourceType)
							Track(cell);
			}

			var count = cells.Count;
			var size = Math.Min(ResourceGrowthBatching.NextBatch(ref budget, count, info.Interval), count);
			if (size == 0)
				return;

			if (cursor >= count)
				cursor = 0;

			// Snapshot the batch: applying the step adds cells to the tracked list.
			batch.Clear();
			for (var i = 0; i < size; i++)
				batch.Add(cells[(cursor + i) % count]);

			cursor = (cursor + size) % count;
			OreGrowthStep.Apply(grid, batch, info.GrowthChance, info.SpreadChance, random);
		}

		sealed class SharedRandomAdapter(MersenneTwister mt) : IGrowthRandom
		{
			public int Next(int maxExclusive) => mt.Next(maxExclusive);
		}

		/// <summary>The growth grid for one resource type; cells holding another type are not this type's cells.</summary>
		sealed class Grid(IResourceLayer layer, string type) : IGrowthGrid, IEnumerable<CPos>, IEnumerator<CPos>
		{
			readonly byte maxDensity = layer.GetMaxDensity(type);
			CPos center;
			int next;

			public int MaxDensity => maxDensity;

			public int Density(CPos c)
			{
				var content = layer.GetResource(c);
				return content.Type == type ? content.Density : 0;
			}

			public bool CanAdd(CPos c) => layer.CanAddResource(type, c);

			public void Add(CPos c) => layer.AddResource(type, c);

			// This adapter does not allocate: the object is its own enumerable and enumerator (enumerations never nest).
			// The engine's CanAddResource may allocate; that is outside this class's control.
			public IEnumerable<CPos> Neighbours(CPos c)
			{
				center = c;
				return this;
			}

			public IEnumerator<CPos> GetEnumerator()
			{
				next = 0;
				return this;
			}

			IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

			public CPos Current => center + CVec.Directions[next - 1];
			object IEnumerator.Current => Current;

			public bool MoveNext() => next++ < CVec.Directions.Length;
			public void Reset() => next = 0;
			public void Dispose() { }
		}
	}
}
