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
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.RA2.Traits
{
	[Desc("Renders an animation on the host while an infector is attached (e.g. the Giant Squid's grapple).",
		"The animation faces the way the infector faced when it latched on and uses the infector owner's palette.")]
	public class WithInfectorOverlayInfo : ConditionalTraitInfo, Requires<InfectableInfo>
	{
		[FieldLoader.Require]
		[Desc("Image holding the sequences.")]
		public readonly string Image = null;

		[SequenceReference(nameof(Image), allowNullImage: true)]
		[Desc("Sequence played once when the infector latches on. Optional.")]
		public readonly string StartSequence = null;

		[FieldLoader.Require]
		[SequenceReference(nameof(Image))]
		[Desc("Sequence looped while the infector is attached.")]
		public readonly string Sequence = null;

		[Desc("Position relative to the host's centre.")]
		public readonly WVec Offset = WVec.Zero;

		[PaletteReference(nameof(IsPlayerPalette))]
		public readonly string Palette = "player";

		[Desc("Palette is a player palette BaseName, taken for the infector's owner.")]
		public readonly bool IsPlayerPalette = true;

		public override object Create(ActorInitializer init) { return new WithInfectorOverlay(init.Self, this); }
	}

	public class WithInfectorOverlay : ConditionalTrait<WithInfectorOverlayInfo>, ITick, IRender
	{
		readonly Infectable infectable;
		readonly Animation anim;

		Actor shownInfector;
		WAngle facing;

		public WithInfectorOverlay(Actor self, WithInfectorOverlayInfo info)
			: base(info)
		{
			infectable = self.Trait<Infectable>();
			anim = new Animation(self.World, info.Image, () => facing);
		}

		void ITick.Tick(Actor self)
		{
			var infector = infectable.Infector;
			if (infector != shownInfector)
			{
				shownInfector = infector;
				if (infector != null)
				{
					facing = infector.TraitOrDefault<IFacing>()?.Facing ?? WAngle.Zero;
					if (Info.StartSequence != null)
						anim.PlayThen(Info.StartSequence, () => anim.PlayRepeating(Info.Sequence));
					else
						anim.PlayRepeating(Info.Sequence);
				}
			}

			if (shownInfector != null)
				anim.Tick();
		}

		bool Visible(Actor self)
		{
			return !IsTraitDisabled && shownInfector != null && !shownInfector.Disposed
				&& self.IsInWorld && !self.IsDead && !self.World.FogObscures(self);
		}

		IEnumerable<IRenderable> IRender.Render(Actor self, WorldRenderer wr)
		{
			if (!Visible(self))
				return [];

			var palette = wr.Palette(Info.IsPlayerPalette ? Info.Palette + shownInfector.Owner.InternalName : Info.Palette);
			return anim.Render(self.CenterPosition, Info.Offset, 0, palette);
		}

		IEnumerable<Rectangle> IRender.ScreenBounds(Actor self, WorldRenderer wr)
		{
			if (!Visible(self))
				return [];

			return [anim.ScreenBounds(wr, self.CenterPosition, Info.Offset)];
		}
	}
}
