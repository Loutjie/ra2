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

using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.RA2.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("Clears craters and scorch marks over time, so a battlefield that is fought over forever does not fill up.",
		"Every cell is cleared once per Duration, a few cells per tick in a scattered order.")]
	public class SmudgeDecayInfo : TraitInfo, Requires<SmudgeLayerInfo>
	{
		[Desc("Ticks for a full pass over the map: the longest a smudge survives.")]
		public readonly int Duration = 1500;

		public override object Create(ActorInitializer init) { return new SmudgeDecay(this); }
	}

	public class SmudgeDecay : IWorldLoaded, ITick
	{
		readonly SmudgeDecayInfo info;
		SmudgeLayer[] layers;
		CPos[] cells;
		SmudgeSweep sweep;

		public SmudgeDecay(SmudgeDecayInfo info)
		{
			this.info = info;
		}

		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			layers = w.WorldActor.TraitsImplementing<SmudgeLayer>().ToArray();
			cells = w.Map.AllCells.ToArray();
			sweep = new SmudgeSweep(cells.Length, info.Duration);
		}

		void ITick.Tick(Actor self)
		{
			if (sweep == null)
				return;

			foreach (var i in sweep.Next())
				foreach (var layer in layers)
					layer.RemoveSmudge(cells[i]);
		}
	}
}
