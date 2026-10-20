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
using OpenRA.Mods.Common;
using OpenRA.Mods.RA2.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.RA2.Projectiles
{
	[Desc("SPIKE: instead of flying itself, spawns a MissileActor that carries this weapon's warheads to the target.")]
	public class LaunchMissileActorInfo : IProjectileInfo
	{
		[ActorReference(typeof(MissileActorInfo))]
		[FieldLoader.Require]
		[Desc("The missile actor to spawn.")]
		public readonly string Actor = null;

		public IProjectile Create(ProjectileArgs args) { return new LaunchMissileActor(args, this); }
	}

	public class LaunchMissileActor : IProjectile
	{
		bool launched;

		public LaunchMissileActor(ProjectileArgs args, LaunchMissileActorInfo info)
		{
			var world = args.SourceActor.World;
			var owner = args.SourceActor.Owner;

			// Actors cannot be created mid-tick; the projectile is constructed from Armament.Tick.
			world.AddFrameEndTask(w =>
			{
				var missile = w.CreateActor(false, info.Actor,
				[
					new OwnerInit(owner),
					new CenterPositionInit(args.Source),
					new FacingInit(args.Facing),
				]);

				missile.Trait<MissileActor>().Launch(args);
				w.Add(missile);
			});
		}

		public void Tick(World world)
		{
			if (launched)
				return;

			launched = true;
			world.AddFrameEndTask(w => w.Remove(this));
		}

		public IEnumerable<IRenderable> Render(WorldRenderer wr) { return []; }
	}
}
