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

using OpenRA.Graphics;
using OpenRA.Mods.Cnc.Traits.Render;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.RA2.Traits.Render
{
	[Desc("Voxel body shown, while the trait is enabled, only to enemies with a DisguiseDetector in range.",
		"Enable it with the condition that hides the regular body while the mirage is active.")]
	sealed class WithMirageRevealedVoxelBodyInfo : ConditionalTraitInfo, Requires<RenderVoxelsInfo>, Requires<BodyOrientationInfo>
	{
		public readonly string Sequence = "idle";
		public readonly WVec Offset = WVec.Zero;

		[Desc("Defines if the Voxel should have a shadow.")]
		public readonly bool ShowShadow = true;

		public override object Create(ActorInitializer init) { return new WithMirageRevealedVoxelBody(init.Self, this); }
	}

	sealed class WithMirageRevealedVoxelBody : ConditionalTrait<WithMirageRevealedVoxelBodyInfo>
	{
		public WithMirageRevealedVoxelBody(Actor self, WithMirageRevealedVoxelBodyInfo info)
			: base(info)
		{
			var body = self.Trait<BodyOrientation>();
			var rv = self.Trait<RenderVoxels>();

			var model = rv.Renderer.ModelCache.GetModelSequence(rv.Image, info.Sequence);
			rv.Add(new ModelAnimation(model, () => info.Offset,
				() => body.QuantizeOrientation(self.Orientation),
				() => IsTraitDisabled || !DisguiseDetection.IsRevealed(self), () => 0, info.ShowShadow));
		}
	}
}
