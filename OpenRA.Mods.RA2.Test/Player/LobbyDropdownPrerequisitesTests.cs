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
using NUnit.Framework;
using OpenRA.Mods.RA2.Traits;

namespace OpenRA.Mods.RA2.Test
{
	[TestFixture]
	public class LobbyDropdownPrerequisitesTests
	{
		static readonly IReadOnlyDictionary<string, ImmutableArray<string>> Map =
			new Dictionary<string, ImmutableArray<string>>
			{
				["reimagined"] = ["balance.reimagined"],
				["exact"] = ["balance.exact"],
			};

		[Test]
		public void Resolve_SelectedValue_ReturnsItsPrerequisites()
		{
			Assert.That(LobbyDropdownPrerequisites.Resolve("exact", "reimagined", Map), Is.EqualTo(new[] { "balance.exact" }));
		}

		[Test]
		public void Resolve_UnknownValue_FallsBackToDefault()
		{
			Assert.That(LobbyDropdownPrerequisites.Resolve("modern", "reimagined", Map), Is.EqualTo(new[] { "balance.reimagined" }));
		}

		[Test]
		public void Resolve_NullSelected_UsesDefault()
		{
			Assert.That(LobbyDropdownPrerequisites.Resolve(null, "reimagined", Map), Is.EqualTo(new[] { "balance.reimagined" }));
		}
	}
}
