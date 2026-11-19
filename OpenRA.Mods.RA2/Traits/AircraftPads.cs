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

using OpenRA.Traits;

namespace OpenRA.Mods.RA2.Traits
{
	[Desc("Landing pads for actors with the `" + nameof(PadBound) + "` trait.",
		"Each pad lets the owner have one more such actor. Used by `" + nameof(PadLimitedProductionQueue) + "`.")]
	public class AircraftPadsInfo : TraitInfo
	{
		[Desc("Number of pads.")]
		public readonly int Count = 1;

		public override object Create(ActorInitializer init) { return new AircraftPads(this); }
	}

	public class AircraftPads
	{
		public readonly AircraftPadsInfo Info;

		public AircraftPads(AircraftPadsInfo info)
		{
			Info = info;
		}
	}

	[Desc("This actor takes one `" + nameof(AircraftPads) + "` pad, so a `" + nameof(PadLimitedProductionQueue) + "`",
		"only builds it while its owner has a free pad.")]
	public class PadBoundInfo : TraitInfo<PadBound> { }

	public class PadBound { }
}
