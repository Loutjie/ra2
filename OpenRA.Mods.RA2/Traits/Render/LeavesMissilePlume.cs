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

using System.Collections.Immutable;
using OpenRA.Mods.Common.Effects;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.RA2.Traits
{
	[Desc("Leaves an exhaust plume behind a moving missile, like LeavesTrails with Type: CenterPosition,",
		"but each puff is drawn closer to the camera in depth by its height above the ground (see MissileActor.DepthFromAltitude),",
		"so buildings the missile flies over do not hide the plume. Puffs stay where they are on screen.")]
	public class LeavesMissilePlumeInfo : ConditionalTraitInfo
	{
		[FieldLoader.Require]
		public readonly string Image = null;

		[SequenceReference(nameof(Image))]
		public readonly ImmutableArray<string> Sequences = ["idle"];

		[PaletteReference]
		public readonly string Palette = "effect";

		[Desc("Ticks between puffs while moving.")]
		public readonly int MovingInterval = 0;

		[Desc("Extra depth towards the camera on top of the puff's height above the ground.")]
		public readonly WDist DepthOffset = WDist.Zero;

		public override object Create(ActorInitializer init) { return new LeavesMissilePlume(this); }
	}

	public class LeavesMissilePlume : ConditionalTrait<LeavesMissilePlumeInfo>, ITick
	{
		IFacing facing;
		WPos lastPosition;
		WAngle lastFacing;
		int ticks;

		public LeavesMissilePlume(LeavesMissilePlumeInfo info)
			: base(info) { }

		protected override void Created(Actor self)
		{
			facing = self.TraitOrDefault<IFacing>();
			lastPosition = self.CenterPosition;
			lastFacing = facing?.Facing ?? WAngle.Zero;
			base.Created(self);
		}

		protected override void TraitEnabled(Actor self)
		{
			lastPosition = self.CenterPosition;
		}

		void ITick.Tick(Actor self)
		{
			if (!self.IsInWorld || IsTraitDisabled)
				return;

			if (self.CenterPosition == lastPosition)
			{
				ticks = 0;
				return;
			}

			if (++ticks < Info.MovingInterval)
				return;

			ticks = 0;
			var map = self.World.Map;
			if (map.Contains(map.CellContaining(lastPosition)))
			{
				// Moving south and up by the same amount leaves the screen position alone and brings it closer in depth.
				var depth = Info.DepthOffset.Length + map.DistanceAboveTerrain(lastPosition).Length;
				var pos = lastPosition + new WVec(0, depth, depth);
				var spawnFacing = lastFacing;
				var sequence = Info.Sequences.Random(Game.CosmeticRandom);
				self.World.AddFrameEndTask(w => w.Add(new SpriteEffect(pos, spawnFacing, w, Info.Image, sequence, Info.Palette)));
			}

			lastPosition = self.CenterPosition;
			lastFacing = facing?.Facing ?? WAngle.Zero;
		}
	}
}
