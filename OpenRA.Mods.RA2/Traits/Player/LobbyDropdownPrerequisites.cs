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
using System.Linq;

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

		/// <summary>
		/// Checks that the default is one of the values and that every value has a prerequisite entry and vice versa.
		/// Returns a description of the first problem found, or null when the definition is consistent.
		/// </summary>
		public static string Validate(string defaultValue, IEnumerable<string> valueIds, IEnumerable<string> prerequisiteIds)
		{
			var values = valueIds.ToHashSet();
			var prerequisites = prerequisiteIds.ToHashSet();
			if (!values.Contains(defaultValue))
				return $"Default '{defaultValue}' is not one of the Values ({string.Join(", ", values.Order())}).";

			var withoutPrerequisites = values.Except(prerequisites).Order().ToList();
			if (withoutPrerequisites.Count > 0)
				return $"Values without a Prerequisites entry: {string.Join(", ", withoutPrerequisites)}.";

			var withoutValue = prerequisites.Except(values).Order().ToList();
			if (withoutValue.Count > 0)
				return $"Prerequisites entries without a matching Value: {string.Join(", ", withoutValue)}.";

			return null;
		}
	}
}
