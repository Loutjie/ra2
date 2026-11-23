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
using System.Linq;
using System.Reflection;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.RA2.Traits
{
	[Desc("Lets the owner and its allies see through enemy disguises (Spy, Mirage Tank) near this actor",
		"(RA2 DetectDisguise, DetectDisguiseRange). Only changes what those players see, not how anything behaves.")]
	public class DisguiseDetectorInfo : ConditionalTraitInfo
	{
		[Desc("Disguised enemies within this distance of the actor are shown as what they really are.")]
		public readonly WDist Range = WDist.FromCells(15);

		public override object Create(ActorInitializer init) { return new DisguiseDetector(this); }
	}

	public class DisguiseDetector : ConditionalTrait<DisguiseDetectorInfo>
	{
		public DisguiseDetector(DisguiseDetectorInfo info)
			: base(info) { }

		public bool Reveals(Actor self, Actor target, Player viewer)
		{
			return !IsTraitDisabled && self.IsInWorld && !self.IsDead && self.Owner.IsAlliedWith(viewer)
				&& DisguiseDetection.InRange(self.CenterPosition, target.CenterPosition, Info.Range);
		}
	}

	public static class DisguiseDetection
	{
		/// <summary>
		/// Whether the local render player sees through <paramref name="target"/>'s disguise.
		/// Render-side only: the answer depends on <see cref="World.RenderPlayer"/>, so it must never feed game logic.
		/// </summary>
		public static bool IsRevealed(Actor target)
		{
			var viewer = target.World.RenderPlayer;

			// Observers and allies already get the engine's default view of the disguise.
			if (viewer == null || target.Owner.IsAlliedWith(viewer))
				return false;

			foreach (var d in target.World.ActorsWithTrait<DisguiseDetector>())
				if (d.Trait.Reveals(d.Actor, target, viewer))
					return true;

			return false;
		}

		public static bool InRange(WPos detector, WPos target, WDist range)
		{
			return (target - detector).HorizontalLengthSquared <= range.LengthSquared;
		}
	}

	/// <summary>
	/// Read access to the engine's Disguise trait (OpenRA.Mods.Cnc), which is internal to its assembly.
	/// The disguised player is public through <see cref="IEffectiveOwner"/>; the disguised actor type and
	/// tooltip are read through reflection on the trait's public properties.
	/// </summary>
	public static class EngineDisguise
	{
		public static readonly Type TraitType = typeof(OpenRA.Mods.Cnc.Traits.Render.WithVoxelBody).Assembly
			.GetType("OpenRA.Mods.Cnc.Traits.Disguise", true);

		static readonly Type InfoType = TraitType.Assembly.GetType("OpenRA.Mods.Cnc.Traits.DisguiseInfo", true);
		static readonly PropertyInfo AsActorProperty = GetProperty("AsActor", typeof(ActorInfo));
		static readonly PropertyInfo AsTooltipInfoProperty = GetProperty("AsTooltipInfo", typeof(ITooltipInfo));

		static PropertyInfo GetProperty(string name, Type type)
		{
			var property = TraitType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
			if (property == null || property.PropertyType != type)
				throw new InvalidOperationException($"Engine trait {TraitType.Name} no longer has a {type.Name} {name} property.");

			return property;
		}

		public static bool IsDeclaredOn(ActorInfo ai)
		{
			return ai.TraitInfos<TraitInfo>().Any(InfoType.IsInstanceOfType);
		}

		public static IEffectiveOwner Get(Actor self)
		{
			return self.TraitsImplementing<IEffectiveOwner>().First(TraitType.IsInstanceOfType);
		}

		public static ActorInfo AsActor(IEffectiveOwner disguise) { return (ActorInfo)AsActorProperty.GetValue(disguise); }

		public static ITooltipInfo AsTooltipInfo(IEffectiveOwner disguise) { return (ITooltipInfo)AsTooltipInfoProperty.GetValue(disguise); }

		public static void RequireOn(ActorInfo ai, string traitName)
		{
			if (!IsDeclaredOn(ai))
				throw new YamlException($"Actor type `{ai.Name}` uses {traitName}, which requires the Disguise trait.");
		}
	}

	[Desc("Overrides the default Tooltip when this actor is disguised. Unlike DisguiseTooltip, enemies with",
		"a DisguiseDetector in range see the real actor and owner.")]
	sealed class DetectableDisguiseTooltipInfo : TooltipInfo
	{
		public override object Create(ActorInitializer init) { return new DetectableDisguiseTooltip(init.Self, this); }

		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			base.RulesetLoaded(rules, ai);
			EngineDisguise.RequireOn(ai, nameof(DetectableDisguiseTooltip));
		}
	}

	sealed class DetectableDisguiseTooltip : ConditionalTrait<DetectableDisguiseTooltipInfo>, ITooltip
	{
		readonly Actor self;
		IEffectiveOwner disguise;

		public DetectableDisguiseTooltip(Actor self, DetectableDisguiseTooltipInfo info)
			: base(info)
		{
			this.self = self;
		}

		protected override void Created(Actor self)
		{
			disguise = EngineDisguise.Get(self);
			base.Created(self);
		}

		bool ShowsDisguise => disguise.Disguised && !DisguiseDetection.IsRevealed(self);

		public ITooltipInfo TooltipInfo => ShowsDisguise ? EngineDisguise.AsTooltipInfo(disguise) : Info;

		public Player Owner => !ShowsDisguise || self.Owner.IsAlliedWith(self.World.RenderPlayer) ? self.Owner : disguise.Owner;
	}
}
