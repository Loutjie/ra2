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
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.RA2.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("Replaces the actors with " + nameof(ReplaceOnPrerequisite) + " that are spawned while the map loads (map actors, starting units)",
		"as soon as they are all spawned, before the first tick, instead of at the end of the first tick.",
		"This lets first-tick orders, such as the AI deploying its starting MCV, reach the replacement.")]
	public class ReplaceOnPrerequisiteAtLoadInfo : TraitInfo, NotBefore<SpawnMapActorsInfo>, NotBefore<SpawnStartingUnitsInfo>
	{
		public override object Create(ActorInitializer init) { return new ReplaceOnPrerequisiteAtLoad(); }
	}

	public class ReplaceOnPrerequisiteAtLoad : IWorldLoaded
	{
		readonly List<Action<World>> deferred = [];
		bool loaded;

		/// <summary>
		/// Queues a replacement until the map actors and starting units are all spawned.
		/// Returns false once those have been spawned: the caller must then handle the replacement itself.
		/// </summary>
		public bool TryDefer(Action<World> replace)
		{
			if (loaded)
				return false;

			deferred.Add(replace);
			return true;
		}

		void IWorldLoaded.WorldLoaded(World w, WorldRenderer wr)
		{
			// Actors created from here on (including the replacements) use the frame-end path.
			loaded = true;

			// Registration order is creation order, so the replacements are created in a deterministic order.
			foreach (var replace in deferred)
				replace(w);

			deferred.Clear();
		}
	}
}
