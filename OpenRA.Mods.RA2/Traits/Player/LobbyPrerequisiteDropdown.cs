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

using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.RA2.Traits
{
	[TraitLocation(SystemActors.Player)]
	[Desc("Grants the prerequisites mapped to the value selected in a lobby dropdown to all players.")]
	public class LobbyPrerequisiteDropdownInfo : TraitInfo, ILobbyOptions, ITechTreePrerequisiteInfo, IRulesetLoaded
	{
		[FieldLoader.Require]
		[Desc("Internal id for this dropdown.")]
		public readonly string ID = null;

		[FluentReference]
		[FieldLoader.Require]
		[Desc("Display name for this dropdown.")]
		public readonly string Label = null;

		[FluentReference]
		[Desc("Description of this dropdown.")]
		public readonly string Description = null;

		[FluentReference(dictionaryReference: LintDictionaryReference.Values)]
		[FieldLoader.Require]
		[Desc("Selectable values: value id to Fluent label key.")]
		public readonly FrozenDictionary<string, string> Values = FrozenDictionary<string, string>.Empty;

		[FieldLoader.Require]
		[Desc("Prerequisites to grant for each value id.")]
		public readonly FrozenDictionary<string, ImmutableArray<string>> Prerequisites = FrozenDictionary<string, ImmutableArray<string>>.Empty;

		[FieldLoader.Require]
		[Desc("Default value id in the lobby; also used when the selected value is unknown.")]
		public readonly string Default = null;

		[Desc("Prevent the dropdown from being changed from its default value.")]
		public readonly bool Locked = false;

		[Desc("Display the dropdown in the lobby.")]
		public readonly bool Visible = true;

		[Desc("Display order for the dropdown in the lobby.")]
		public readonly int DisplayOrder = 0;

		IEnumerable<string> ITechTreePrerequisiteInfo.Prerequisites(ActorInfo info)
		{
			return Prerequisites.Values.SelectMany(p => p);
		}

		IEnumerable<LobbyOption> ILobbyOptions.LobbyOptions(MapPreview map)
		{
			yield return new LobbyOption(map, ID, Label, Description, Visible, DisplayOrder, Values, Default, Locked);
		}

		void IRulesetLoaded<ActorInfo>.RulesetLoaded(Ruleset rules, ActorInfo info)
		{
			var error = LobbyDropdownPrerequisites.Validate(Default, Values.Keys, Prerequisites.Keys);
			if (error != null)
				throw new YamlException($"{nameof(LobbyPrerequisiteDropdown)} '{ID}' on actor '{info.Name}': {error}");
		}

		public override object Create(ActorInitializer init) { return new LobbyPrerequisiteDropdown(this); }
	}

	public class LobbyPrerequisiteDropdown : INotifyCreated, ITechTreePrerequisite
	{
		readonly LobbyPrerequisiteDropdownInfo info;
		ImmutableArray<string> prerequisites = [];

		public LobbyPrerequisiteDropdown(LobbyPrerequisiteDropdownInfo info)
		{
			this.info = info;
		}

		void INotifyCreated.Created(Actor self)
		{
			var selected = self.World.LobbyInfo.GlobalSettings.OptionOrDefault(info.ID, info.Default);
			prerequisites = LobbyDropdownPrerequisites.Resolve(selected, info.Default, info.Prerequisites);
		}

		IEnumerable<string> ITechTreePrerequisite.ProvidesPrerequisites => prerequisites;
	}
}
