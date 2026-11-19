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
using OpenRA.Graphics;
using OpenRA.Mods.Cnc.Traits;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.RA2.Traits
{
	/// <summary>One support beam: <see cref="Supporter"/> beams into <see cref="Receiver"/>.</summary>
	/// <remarks>Both are indices into the candidate list, or <see cref="PrismSupportNetwork.FiringTower"/>.</remarks>
	public readonly record struct PrismSupportLink(int Supporter, int Receiver);

	public static class PrismSupportNetwork
	{
		public const int FiringTower = -1;

		/// <summary>
		/// Picks the towers that support a shot. Breadth first from the firing tower: each receiver takes the
		/// nearest unused candidates within <paramref name="range"/> until <paramref name="max"/> beams are found.
		/// With <paramref name="chain"/>, supporters receive from towers further out in turn (RA2 prism chains).
		/// Ties are broken by candidate index, so the result is deterministic.
		/// </summary>
		public static List<PrismSupportLink> Select(WPos firingTower, IReadOnlyList<WPos> candidates, WDist range, int max, bool chain)
		{
			var links = new List<PrismSupportLink>();
			if (max <= 0 || candidates.Count == 0)
				return links;

			var used = new bool[candidates.Count];
			var receivers = new Queue<int>();
			receivers.Enqueue(FiringTower);
			var rangeSquared = range.LengthSquared;

			while (receivers.Count > 0 && links.Count < max)
			{
				var receiver = receivers.Dequeue();
				var receiverPos = receiver == FiringTower ? firingTower : candidates[receiver];

				var inRange = Enumerable.Range(0, candidates.Count)
					.Where(i => !used[i])
					.Select(i => (Index: i, DistanceSquared: (candidates[i] - receiverPos).HorizontalLengthSquared))
					.Where(c => c.DistanceSquared <= rangeSquared)
					.OrderBy(c => c.DistanceSquared)
					.ThenBy(c => c.Index);

				foreach (var c in inRange)
				{
					if (links.Count >= max)
						break;

					used[c.Index] = true;
					links.Add(new PrismSupportLink(c.Index, receiver));
					if (chain)
						receivers.Enqueue(c.Index);
				}
			}

			return links;
		}

		/// <summary>Firepower of a shot backed by <paramref name="beams"/> support beams, as a percentage.</summary>
		public static int DamagePercent(int beams, int modifierPerBeam) => 100 + beams * modifierPerBeam;
	}

	[Desc("Prism Tower support (RA2 PrismSupport*): when this tower charges a shot, idle towers of the same type",
		"and owner within range of the support weapon beam into it, and each beam adds firepower to the shot.",
		"Supporting takes a tower offline for a while. Needs AttackTesla (charge start) and an Armament.")]
	public class PrismTowerInfo : PausableConditionalTraitInfo, IPlaceBuildingDecorationInfo, Requires<ArmamentInfo>
	{
		[Desc("Towers support each other only when their Type matches.")]
		public readonly string Type = "prism";

		[WeaponReference]
		[FieldLoader.Require]
		[Desc("Support beam weapon (RA2 Secondary=PrismSupport). Its range is the support range;",
			"it is drawn from the supporter's muzzle to the receiver's muzzle and its warheads are not applied.")]
		public readonly string SupportWeapon = null;

		[Desc("Firepower added to the shot per support beam, in percent (RA2 PrismSupportModifier).")]
		public readonly int SupportModifier = 150;

		[Desc("Most support beams for one shot (RA2 PrismSupportMax).")]
		public readonly int SupportMax = 8;

		[Desc("Ticks a tower stays offline after supporting (RA2 PrismSupportDelay).")]
		public readonly int SupportDelay = 50;

		[Desc("Supporting towers receive support from towers further out (RA2 prism chains).")]
		public readonly bool Chain = true;

		[GrantedConditionReference]
		[Desc("Condition granted while the tower is offline after supporting. Pause the attack on it.")]
		public readonly string SupportingCondition = null;

		[Desc("Draw links to towers in support range while placing this building.")]
		public readonly bool ShowPlacementLinks = true;

		[Desc("Alpha of the placement links, drawn in the placing player's colour.")]
		public readonly int PlacementLinkAlpha = 128;

		public WeaponInfo SupportWeaponInfo { get; private set; }

		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			var weaponToLower = SupportWeapon.ToLowerInvariant();
			if (!rules.Weapons.TryGetValue(weaponToLower, out var weapon))
				throw new YamlException($"Weapons Ruleset does not contain an entry '{weaponToLower}'");

			SupportWeaponInfo = weapon;
			base.RulesetLoaded(rules, ai);
		}

		IEnumerable<IRenderable> IPlaceBuildingDecorationInfo.RenderAnnotations(WorldRenderer wr, World w, ActorInfo ai, WPos centerPosition)
		{
			if (!ShowPlacementLinks || !EnabledByDefault || w.LocalPlayer == null)
				yield break;

			var color = Color.FromArgb(PlacementLinkAlpha, w.LocalPlayer.Color);
			var rangeSquared = SupportWeaponInfo.Range.LengthSquared;
			foreach (var tower in w.ActorsWithTrait<PrismTower>())
			{
				var a = tower.Actor;
				if (a.Owner != w.LocalPlayer || !a.IsInWorld || tower.Trait.Info.Type != Type)
					continue;

				if ((a.CenterPosition - centerPosition).HorizontalLengthSquared <= rangeSquared)
					yield return new LineAnnotationRenderable(centerPosition, a.CenterPosition, 1, color);
			}
		}

		public override object Create(ActorInitializer init) { return new PrismTower(init.Self, this); }
	}

	public class PrismTower : PausableConditionalTrait<PrismTowerInfo>, ITick, INotifyTeslaCharging, INotifyAttack, IFirepowerModifier, ISync
	{
		readonly Actor self;
		Armament[] armaments;
		AttackBase[] attacks;

		[VerifySync]
		int supportBeams;

		[VerifySync]
		int offlineTicks;

		int supportingToken = Actor.InvalidConditionToken;

		public PrismTower(Actor self, PrismTowerInfo info)
			: base(info)
		{
			this.self = self;
		}

		protected override void Created(Actor self)
		{
			armaments = self.TraitsImplementing<Armament>().ToArray();
			attacks = self.TraitsImplementing<AttackBase>().ToArray();
			base.Created(self);
		}

		/// <summary>Idle, powered, and not offline from an earlier support beam.</summary>
		bool CanSupport =>
			!IsTraitDisabled && !IsTraitPaused && offlineTicks <= 0 && self.IsInWorld && !self.IsDead && self.IsIdle
			&& attacks.Any(a => !a.IsTraitDisabled && !a.IsTraitPaused)
			&& !armaments.Any(a => !a.IsTraitDisabled && a.IsReloading);

		WPos Muzzle
		{
			get
			{
				var armament = armaments.FirstOrDefault(a => !a.IsTraitDisabled) ?? armaments[0];
				return self.CenterPosition + armament.MuzzleOffset(self, armament.Barrels[0]);
			}
		}

		void ITick.Tick(Actor self)
		{
			if (offlineTicks > 0 && --offlineTicks == 0 && supportingToken != Actor.InvalidConditionToken)
				supportingToken = self.RevokeCondition(supportingToken);
		}

		void INotifyTeslaCharging.Charging(Actor self, in Target target)
		{
			supportBeams = 0;
			if (IsTraitDisabled || IsTraitPaused)
				return;

			var candidates = self.World.ActorsWithTrait<PrismTower>()
				.Where(t => t.Actor != self && t.Actor.Owner == self.Owner && t.Trait.Info.Type == Info.Type && t.Trait.CanSupport)
				.Select(t => t.Trait)
				.ToList();

			var links = PrismSupportNetwork.Select(
				self.CenterPosition, candidates.Select(c => c.self.CenterPosition).ToList(),
				Info.SupportWeaponInfo.Range, Info.SupportMax, Info.Chain);

			foreach (var link in links)
			{
				var receiver = link.Receiver == PrismSupportNetwork.FiringTower ? this : candidates[link.Receiver];
				candidates[link.Supporter].Support(receiver);
			}

			supportBeams = links.Count;
		}

		void Support(PrismTower receiver)
		{
			offlineTicks = Info.SupportDelay;
			if (supportingToken == Actor.InvalidConditionToken && !string.IsNullOrEmpty(Info.SupportingCondition))
				supportingToken = self.GrantCondition(Info.SupportingCondition);

			var weapon = Info.SupportWeaponInfo;
			if (weapon.Projectile == null)
				return;

			var source = Muzzle;
			var target = receiver.Muzzle;
			var args = new ProjectileArgs
			{
				Weapon = weapon,
				Facing = (target - source).Yaw,
				CurrentMuzzleFacing = () => (target - source).Yaw,
				DamageModifiers = [],
				InaccuracyModifiers = [],
				RangeModifiers = [],
				Source = source,
				CurrentSource = () => source,
				SourceActor = self,
				PassiveTarget = target,
				GuidedTarget = Target.FromPos(target)
			};

			var projectile = weapon.Projectile.Create(args);
			if (projectile != null)
				self.World.AddFrameEndTask(w => w.Add(projectile));

			if (weapon.Report != null && weapon.Report.Length > 0)
				Game.Sound.Play(SoundType.World, weapon.Report, self.World, source);
		}

		void INotifyAttack.PreparingAttack(Actor self, in Target target, Armament a, Barrel barrel) { }

		void INotifyAttack.Attacking(Actor self, in Target target, Armament a, Barrel barrel)
		{
			// The firepower modifiers were read when the shot was prepared, so the support is spent now.
			supportBeams = 0;
		}

		int IFirepowerModifier.GetFirepowerModifier()
		{
			return PrismSupportNetwork.DamagePercent(supportBeams, Info.SupportModifier);
		}
	}
}
