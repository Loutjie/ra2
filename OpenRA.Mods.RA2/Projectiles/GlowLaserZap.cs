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
using OpenRA.GameRules;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Effects;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Mods.Common.Projectiles;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.RA2.Projectiles
{
	[Desc("LaserZap whose core is always drawn in front of its glow, with a see-through glow (RA2 prism beams).",
		"Takes every LaserZap field.")]
	public class GlowLaserZapInfo : LaserZapInfo, IProjectileInfo
	{
		[Desc("Opacity of the secondary (glow) beam, 0-255. Applied on top of its colour, including the player colour.")]
		public readonly int SecondaryBeamAlpha = 255;

		[Desc("How far the core beam is moved towards the camera, without moving it on screen,",
			"so the depth buffer never lets the glow cover it.")]
		public readonly WDist CoreDepthOffset = new(32);

		IProjectile IProjectileInfo.Create(ProjectileArgs args)
		{
			var c = UsePlayerColor ? args.SourceActor.OwnerColor() : Color;
			return new GlowLaserZap(this, args, c);
		}
	}

	/// <summary>Copy of the engine's LaserZap with a different <see cref="Render"/>.</summary>
	public class GlowLaserZap : IProjectile, ISync
	{
		readonly ProjectileArgs args;
		readonly GlowLaserZapInfo info;
		readonly Animation hitanim;
		readonly Color color;
		readonly Color secondaryColor;
		readonly bool hasLaunchEffect;
		readonly WVec coreDepthOffset;
		int ticks;
		int interval;
		bool showHitAnim;

		[VerifySync]
		WPos target;

		[VerifySync]
		WPos source;

		public GlowLaserZap(GlowLaserZapInfo info, ProjectileArgs args, Color color)
		{
			this.args = args;
			this.info = info;
			this.color = color;
			var glow = info.SecondaryBeamUsePlayerColor ? args.SourceActor.OwnerColor() : info.SecondaryBeamColor;
			secondaryColor = Color.FromArgb(glow.A * info.SecondaryBeamAlpha / 255, glow);

			// Screen y follows world Y - Z and depth follows world Y, so moving +Y and +Z together
			// brings the core nearer the camera without moving it on screen.
			coreDepthOffset = new WVec(0, info.CoreDepthOffset.Length, info.CoreDepthOffset.Length);
			target = args.PassiveTarget;
			source = args.Source;

			if (info.Inaccuracy.Length > 0)
			{
				var maxInaccuracyOffset = Common.Util.GetProjectileInaccuracy(info.Inaccuracy.Length, info.InaccuracyType, args);
				target += WVec.FromPDF(args.SourceActor.World.SharedRandom, 2) * maxInaccuracyOffset / 1024;
			}

			if (!string.IsNullOrEmpty(info.HitAnim))
			{
				hitanim = new Animation(args.SourceActor.World, info.HitAnim);
				showHitAnim = true;
			}

			hasLaunchEffect = !string.IsNullOrEmpty(info.LaunchEffectImage) && !string.IsNullOrEmpty(info.LaunchEffectSequence);
		}

		public void Tick(World world)
		{
			source = args.CurrentSource();

			if (hasLaunchEffect && ticks == 0)
				world.AddFrameEndTask(w => w.Add(new SpriteEffect(args.CurrentSource, args.CurrentMuzzleFacing, world,
					info.LaunchEffectImage, info.LaunchEffectSequence, info.LaunchEffectPalette)));

			if (info.TrackTarget && args.GuidedTarget.IsValidFor(args.SourceActor))
				target = args.Weapon.TargetActorCenter ? args.GuidedTarget.CenterPosition : args.GuidedTarget.Positions.ClosestToIgnoringPath(source);

			if (info.Blockable && BlocksProjectiles.AnyBlockingActorsBetween(world, args.SourceActor.Owner, source, target, info.Width, out var blockedPos))
				target = blockedPos;

			if (ticks < info.DamageDuration && --interval <= 0)
			{
				var warheadArgs = new WarheadArgs(args)
				{
					ImpactOrientation = new WRot(WAngle.Zero, Common.Util.GetVerticalAngle(source, target), args.CurrentMuzzleFacing()),
					ImpactPosition = target,
				};

				args.Weapon.Impact(Target.FromPos(target), warheadArgs);
				interval = info.DamageInterval;
			}

			if (showHitAnim)
			{
				if (ticks == 0)
					hitanim.PlayThen(info.HitAnimSequence, () => showHitAnim = false);

				hitanim.Tick();
			}

			if (++ticks >= info.Duration && !showHitAnim)
				world.AddFrameEndTask(w => w.Remove(this));
		}

		public IEnumerable<IRenderable> Render(WorldRenderer wr)
		{
			if (wr.World.FogObscures(target) && wr.World.FogObscures(source))
				yield break;

			if (ticks < info.Duration)
			{
				if (info.SecondaryBeam)
				{
					var src = Color.FromArgb((info.Duration - ticks) * secondaryColor.A / info.Duration, secondaryColor);
					yield return new BeamRenderable(source, info.SecondaryBeamZOffset, target - source,
						info.SecondaryBeamShape, info.SecondaryBeamWidth, src);
				}

				var rc = Color.FromArgb((info.Duration - ticks) * color.A / info.Duration, color);
				yield return new BeamRenderable(source + coreDepthOffset, info.ZOffset, target - source, info.Shape, info.Width, rc);
			}

			if (showHitAnim)
				foreach (var r in hitanim.Render(target, wr.Palette(info.HitAnimPalette)))
					yield return r;
		}
	}
}
