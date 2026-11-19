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
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Cnc.Traits.Render;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.RA2.Traits
{
	/// <summary>
	/// Whether one launcher slot holds its missile. The slot empties when its barrel fires and refills when the
	/// armament has finished reloading. Visual state only: nothing in the simulation reads it.
	/// </summary>
	public sealed class LauncherSlot
	{
		public bool IsLoaded { get; private set; } = true;

		public void Fired() { IsLoaded = false; }

		/// <summary>A burst in progress counts as reloading: IsReloading drops to false between its shots.</summary>
		public void Tick(bool armamentReloading, int burstRemaining, int weaponBurst)
		{
			if (!armamentReloading && burstRemaining == weaponBurst)
				IsLoaded = true;
		}
	}

	[Desc("Draws one loaded missile on a launcher, at the muzzle offset of one armament barrel.",
		"The missile disappears when that barrel fires and returns when the armament has reloaded. Visual only.")]
	public class WithLauncherMissileInfo : ConditionalTraitInfo, IRenderActorPreviewVoxelsInfo,
		Requires<RenderVoxelsInfo>, Requires<ArmamentInfo>, Requires<BodyOrientationInfo>
	{
		[FieldLoader.Require]
		[Desc("Voxel image of the missile.")]
		public readonly string Image = null;

		[Desc("Voxel sequence of the missile.")]
		public readonly string Sequence = "idle";

		[Desc("Name of the armament that launches this missile.")]
		public readonly string Armament = "primary";

		[Desc("Index into the armament's LocalOffset list. The missile is drawn at that offset, where the launched actor spawns.")]
		public readonly int Barrel = 0;

		[Desc("Nose-up pitch of the missile in its slot.")]
		public readonly WAngle Pitch = WAngle.Zero;

		[Desc("Defines if the missile should have a shadow.")]
		public readonly bool ShowShadow = true;

		public override object Create(ActorInitializer init) { return new WithLauncherMissile(init.Self, this); }

		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			var armament = ai.TraitInfos<ArmamentInfo>().FirstOrDefault(a => a.Name == Armament)
				?? throw new YamlException($"Actor `{ai.Name}` has no armament `{Armament}` for {nameof(WithLauncherMissile)}.");

			if (Barrel < 0 || Barrel >= armament.LocalOffset.Length)
				throw new YamlException($"Actor `{ai.Name}`: {nameof(WithLauncherMissile)} barrel {Barrel} is not in armament `{Armament}`'s LocalOffset list.");

			base.RulesetLoaded(rules, ai);
		}

		public IEnumerable<ModelAnimation> RenderPreviewVoxels(IModelCache cache,
			ActorPreviewInitializer init, RenderVoxelsInfo rv, string image, Func<WRot> orientation, int facings, PaletteReference p)
		{
			if (!EnabledByDefault)
				yield break;

			var body = init.Actor.TraitInfo<BodyOrientationInfo>();
			var armament = init.Actor.TraitInfos<ArmamentInfo>().First(a => a.Name == Armament);
			var offset = armament.LocalOffset[Barrel];
			var localPitch = new WRot(WAngle.Zero, Pitch, WAngle.Zero);

			WRot BodyOrientation() => body.QuantizeOrientation(orientation(), facings);

			yield return new ModelAnimation(cache.GetModelSequence(Image, Sequence),
				() => body.LocalToWorld(offset.Rotate(BodyOrientation())),
				() => localPitch.Rotate(BodyOrientation()),
				() => false, () => 0, ShowShadow);
		}
	}

	public class WithLauncherMissile : ConditionalTrait<WithLauncherMissileInfo>, INotifyAttack, ITick, ILauncherMissileSlot
	{
		readonly Armament armament;
		readonly Barrel barrel;
		readonly LauncherSlot slot = new();

		// The missile this slot launched, drawn here until it flies.
		Actor lifting;
		MissileActor liftingMissile;

		public WithLauncherMissile(Actor self, WithLauncherMissileInfo info)
			: base(info)
		{
			armament = self.TraitsImplementing<Armament>().First(a => a.Info.Name == info.Armament);
			barrel = armament.Barrels[info.Barrel];

			var body = self.Trait<BodyOrientation>();
			var rv = self.Trait<RenderVoxels>();
			var localPitch = new WRot(WAngle.Zero, info.Pitch, WAngle.Zero);

			rv.Add(new ModelAnimation(rv.Renderer.ModelCache.GetModelSequence(info.Image, info.Sequence),
				() => Lifting ? liftingMissile.CenterPosition - self.CenterPosition : armament.MuzzleOffset(self, barrel),
				() => Lifting ? liftingMissile.Orientation : localPitch.Rotate(body.QuantizeOrientation(self.Orientation)),
				() => IsTraitDisabled || (!slot.IsLoaded && !Lifting),
				() => 0, info.ShowShadow));
		}

		// Not IsInWorld: the launched missile is only added to the world at the end of the tick it was fired in,
		// and this slot must keep drawing it in the meantime.
		bool Lifting => liftingMissile != null && liftingMissile.IsDrawnByLauncher && !lifting.IsDead;

		bool ILauncherMissileSlot.TryTake(Actor launcher, MissileActor missile)
		{
			if (IsTraitDisabled || Lifting)
				return false;

			// The missile spawns at the muzzle of the barrel that fired it.
			var muzzle = launcher.CenterPosition + armament.MuzzleOffset(launcher, barrel);
			if ((missile.CenterPosition - muzzle).LengthSquared > 16 * 16)
				return false;

			lifting = missile.Self;
			liftingMissile = missile;
			return true;
		}

		void INotifyAttack.PreparingAttack(Actor self, in Target target, Armament a, Barrel b) { }

		void INotifyAttack.Attacking(Actor self, in Target target, Armament a, Barrel b)
		{
			if (a == armament && b == barrel)
				slot.Fired();
		}

		void ITick.Tick(Actor self)
		{
			if (liftingMissile != null && !Lifting)
				(lifting, liftingMissile) = (null, null);

			slot.Tick(armament.IsReloading, armament.Burst, armament.Weapon.Burst);
		}
	}
}
