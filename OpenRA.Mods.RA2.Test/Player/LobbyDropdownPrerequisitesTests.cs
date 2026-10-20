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

		static readonly string[] ValueIds = ["reimagined", "exact"];

		[Test]
		public void Validate_ConsistentDefinition_ReturnsNull()
		{
			Assert.That(LobbyDropdownPrerequisites.Validate("reimagined", ValueIds, Map.Keys), Is.Null);
		}

		[Test]
		public void Validate_DefaultNotAValue_ReturnsError()
		{
			Assert.That(LobbyDropdownPrerequisites.Validate("modern", ValueIds, Map.Keys), Does.Contain("Default 'modern'"));
		}

		[Test]
		public void Validate_ValueWithoutPrerequisites_ReturnsError()
		{
			Assert.That(LobbyDropdownPrerequisites.Validate("reimagined", ["reimagined", "exact", "modern"], Map.Keys),
				Does.Contain("modern"));
		}

		[Test]
		public void Validate_PrerequisitesWithoutValue_ReturnsError()
		{
			Assert.That(LobbyDropdownPrerequisites.Validate("reimagined", ["reimagined"], Map.Keys), Does.Contain("exact"));
		}
	}
}
