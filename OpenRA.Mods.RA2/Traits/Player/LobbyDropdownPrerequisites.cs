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
using System.Collections.Immutable;

namespace OpenRA.Mods.RA2.Traits
{
	public static class LobbyDropdownPrerequisites
	{
		/// <summary>
		/// Maps a lobby dropdown selection to the prerequisites it grants. A null or unknown
		/// selection (e.g. from a replay or server of a newer build) falls back to the default value.
		/// </summary>
		public static ImmutableArray<string> Resolve(string selected, string defaultValue,
			IReadOnlyDictionary<string, ImmutableArray<string>> prerequisites)
		{
			if (selected != null && prerequisites.TryGetValue(selected, out var result))
				return result;

			return prerequisites.TryGetValue(defaultValue, out result) ? result : [];
		}
	}
}
