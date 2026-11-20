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
using System.Linq;
using OpenRA.GameRules;
using OpenRA.Mods.Common.Warheads;
using OpenRA.Traits;

namespace OpenRA.Mods.RA2.Warheads
{
	public static class FragmentTargeting
	{
		/// <summary>
		/// Picks up to <paramref name="count"/> distinct candidates within <paramref name="range"/> of the impact,
		/// nearest first, ties broken by candidate index so the result is deterministic.
		/// </summary>
		public static List<int> Select(WPos impact, IReadOnlyList<WPos> candidates, WDist range, int count)
		{
			if (count <= 0)
				return [];

			var rangeSquared = range.LengthSquared;
			return Enumerable.Range(0, candidates.Count)
				.Select(i => (Index: i, DistanceSquared: (candidates[i] - impact).HorizontalLengthSquared))
				.Where(c => c.DistanceSquared <= rangeSquared)
				.OrderBy(c => c.DistanceSquared)
				.ThenBy(c => c.Index)
				.Take(count)
				.Select(c => c.Index)
				.ToList();
		}
	}

	[Desc("Splits on impact into fragment beams (RA2 ShrapnelWeapon/ShrapnelCount): fires Weapon from the impact",
		"point at up to Count different valid targets within the fragment weapon's range, one fragment each.")]
	public class FireFragmentsWarhead : Warhead, IRulesetLoaded<WeaponInfo>
	{
		[WeaponReference]
		[FieldLoader.Require]
		[Desc("Fragment weapon. Its range is the search radius around the impact.")]
		public readonly string Weapon = null;

		[Desc("Most fragments per impact (RA2 ShrapnelCount).")]
		public readonly int Count = 5;

		[Desc("Relationships of the actors fragments may target.")]
		public readonly PlayerRelationship FragmentRelationships = PlayerRelationship.Enemy;

		[Desc("Do not send a fragment at the actor the main shot was aimed at.")]
		public readonly bool ExcludePrimaryTarget = true;

		WeaponInfo weapon;

		public void RulesetLoaded(Ruleset rules, WeaponInfo info)
		{
			if (!rules.Weapons.TryGetValue(Weapon.ToLowerInvariant(), out weapon))
				throw new YamlException($"Weapons Ruleset does not contain an entry '{Weapon.ToLowerInvariant()}'");
		}

		public override void DoImpact(in Target target, WarheadArgs args)
		{
			var firedBy = args.SourceActor;
			if (target.Type == TargetType.Invalid || firedBy == null || weapon.Projectile == null)
				return;

			var world = firedBy.World;
			var impact = target.CenterPosition;
			var primary = args.WeaponTarget.Type == TargetType.Actor ? args.WeaponTarget.Actor : null;

			// FindActorsInCircle can return actors in any order: sort by ID so every client picks the same targets.
			var candidates = world.FindActorsInCircle(impact, weapon.Range)
				.Where(a => a != primary && !a.IsDead && a.IsInWorld
					&& FragmentRelationships.HasRelationship(firedBy.Owner.RelationshipWith(a.Owner))
					&& a.CanBeViewedByPlayer(firedBy.Owner)
					&& weapon.IsValidAgainst(Target.FromActor(a), world, firedBy))
				.OrderBy(a => a.ActorID)
				.ToList();

			var picked = FragmentTargeting.Select(impact, candidates.Select(a => a.CenterPosition).ToList(), weapon.Range, Count);
			foreach (var i in picked)
				Fire(firedBy, impact, candidates[i], args);
		}

		void Fire(Actor firedBy, WPos impact, Actor victim, WarheadArgs args)
		{
			var victimTarget = Target.FromActor(victim);
			var aim = victim.CenterPosition;
			var projectileArgs = new ProjectileArgs
			{
				Weapon = weapon,
				Facing = (aim - impact).Yaw,
				CurrentMuzzleFacing = () => (aim - impact).Yaw,
				DamageModifiers = args.DamageModifiers,
				InaccuracyModifiers = [],
				RangeModifiers = [],
				Source = impact,
				CurrentSource = () => impact,
				SourceActor = firedBy,
				PassiveTarget = aim,
				GuidedTarget = victimTarget
			};

			var projectile = weapon.Projectile.Create(projectileArgs);
			if (projectile != null)
				firedBy.World.AddFrameEndTask(w => w.Add(projectile));

			if (weapon.Report != null && weapon.Report.Length > 0)
				Game.Sound.Play(SoundType.World, weapon.Report, firedBy.World, impact);
		}
	}
}
