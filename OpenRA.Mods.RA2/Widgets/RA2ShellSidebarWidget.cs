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
using OpenRA.Widgets;

namespace OpenRA.Mods.RA2.Widgets
{
	/// <summary>
	/// The right-hand panel of RA2's main menu: the top panel with its warning gauge, a column of button
	/// slots (each closed by a cover plate until a <see cref="RA2ShellButtonWidget"/> opens over it) and the
	/// bottom cap. The top panel plays its opening animation whenever the shell reappears.
	/// </summary>
	public class RA2ShellSidebarWidget : Widget
	{
		public readonly string Image = "ra2shell";

		public readonly string TopSequence = "top";
		public readonly string TopPalette = "shell";

		[Desc("Played once over the top panel when the shell appears.")]
		public readonly string IntroSequence = "top-intro";
		public readonly string IntroPalette = "shell";
		public readonly int IntroFrameTime = 70;

		[Desc("Looping gauge animation drawn over the top panel once the intro has finished.")]
		public readonly string GaugeSequence = "gauge";
		public readonly string GaugePalette = "shell2";
		public readonly int2 GaugeOffset = new(38, 48);
		public readonly int GaugeFrameTime = 60;

		public readonly string SlotSequence = "slot";
		public readonly string SlotPalette = "shell2";

		[Desc("Cover plate drawn over every slot; buttons draw over it.")]
		public readonly string CoverSequence = "button";
		public readonly string CoverPalette = "sdbtnanm";
		public readonly int CoverFrame = 0;
		public readonly int2 CoverOffset = new(12, 0);

		public readonly string BottomSequence = "bottom";
		public readonly string BottomPalette = "shell";

		// Shared by every sidebar instance, so switching between submenus (each with its own sidebar)
		// does not replay the intro, but leaving the menu and coming back does. Counted in rendered
		// frames rather than time, so a slow frame does not look like the menu was hidden.
		static int lastDrawnFrame = int.MinValue;
		static long introStart;

		readonly RA2ShellArt art;

		[ObjectCreator.UseCtor]
		public RA2ShellSidebarWidget(WorldRenderer worldRenderer)
		{
			art = new RA2ShellArt(worldRenderer, Image);
		}

		protected RA2ShellSidebarWidget(RA2ShellSidebarWidget other)
			: base(other)
		{
			Image = other.Image;
			TopSequence = other.TopSequence;
			TopPalette = other.TopPalette;
			IntroSequence = other.IntroSequence;
			IntroPalette = other.IntroPalette;
			IntroFrameTime = other.IntroFrameTime;
			GaugeSequence = other.GaugeSequence;
			GaugePalette = other.GaugePalette;
			GaugeOffset = other.GaugeOffset;
			GaugeFrameTime = other.GaugeFrameTime;
			SlotSequence = other.SlotSequence;
			SlotPalette = other.SlotPalette;
			CoverSequence = other.CoverSequence;
			CoverPalette = other.CoverPalette;
			CoverFrame = other.CoverFrame;
			CoverOffset = other.CoverOffset;
			BottomSequence = other.BottomSequence;
			BottomPalette = other.BottomPalette;
			art = other.art;
		}

		public override RA2ShellSidebarWidget Clone() { return new RA2ShellSidebarWidget(this); }

		public override void Draw()
		{
			var now = Game.RunTime;
			if (lastDrawnFrame < Game.RenderFrame - 1)
				introStart = now;

			lastDrawnFrame = Game.RenderFrame;

			var origin = RenderOrigin;
			var top = art.Sequence(TopSequence);
			var slot = art.Sequence(SlotSequence);
			var cover = art.Sequence(CoverSequence);
			var bottom = art.Sequence(BottomSequence);
			if (top == null || slot == null || bottom == null)
				return;

			var topHeight = top.Bounds.Height;
			var slotHeight = slot.Bounds.Height;
			var bottomHeight = bottom.Bounds.Height;

			// Slots first, so the bottom cap overlaps a partial last slot rather than the other way around.
			if (slotHeight > 0)
			{
				for (var y = topHeight; y < Bounds.Height - bottomHeight; y += slotHeight)
				{
					art.Draw(slot, 0, SlotPalette, origin + new int2(0, y));
					art.Draw(cover, CoverFrame, CoverPalette, origin + new int2(0, y) + CoverOffset);
				}
			}

			art.Draw(bottom, 0, BottomPalette, origin + new int2(0, Bounds.Height - bottomHeight));

			var intro = art.Sequence(IntroSequence);
			var introFrame = (int)((now - introStart) / IntroFrameTime);
			if (intro != null && introFrame < intro.Length)
			{
				art.Draw(intro, introFrame, IntroPalette, origin);
				return;
			}

			art.Draw(top, 0, TopPalette, origin);

			var gauge = art.Sequence(GaugeSequence);
			if (gauge != null)
			{
				var gaugeStart = introStart + (intro?.Length ?? 0) * IntroFrameTime;
				art.Draw(gauge, (int)((now - gaugeStart) / GaugeFrameTime), GaugePalette, origin + GaugeOffset);
			}
		}
	}
}
