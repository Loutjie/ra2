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
using OpenRA.Graphics;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Primitives;

namespace OpenRA.Mods.RA2.Widgets
{
	/// <summary>
	/// A main menu button drawn with RA2's animated shell button art: whenever the button (re)appears the
	/// slot's cover plate slides away and the button slides in, and hovering lights it red, orange then yellow.
	/// </summary>
	public class RA2ShellButtonWidget : ButtonWidget
	{
		public readonly string Image = "ra2shell";
		public readonly string Sequence = "button";
		public readonly string Palette = "sdbtnanm";

		[Desc("Frames played when the button appears: cover plate slides out, then the button slides in.")]
		public readonly int[] OpenFrames = [0, 11, 12, 13, 14, 15, 16, 9, 8, 7, 6, 5, 1];

		[Desc("Frame shown while the button is idle.")]
		public readonly int IdleFrame = 1;

		[Desc("Frames stepped through, in order, while the mouse is over the button.")]
		public readonly int[] GlowFrames = [2, 3, 4];

		[Desc("Frame shown while the button is held down.")]
		public readonly int PressedFrame = 4;

		[Desc("Milliseconds per frame of the open and glow animations.")]
		public readonly int FrameTime = 35;

		[Desc("Milliseconds to wait before opening, so a column of buttons opens top to bottom.")]
		public readonly int OpenDelay = 0;

		readonly RA2ShellArt art;

		// Rendered frame of the last draw: a skipped frame means the button was hidden, so it opens again.
		int lastDrawnFrame = int.MinValue;
		long openStart;
		long lastGlowStep;
		int glow;
		int openFrame;
		bool opening;

		[ObjectCreator.UseCtor]
		public RA2ShellButtonWidget(ModData modData, WorldRenderer worldRenderer)
			: base(modData)
		{
			art = new RA2ShellArt(worldRenderer, Image);
		}

		protected RA2ShellButtonWidget(RA2ShellButtonWidget other)
			: base(other)
		{
			Image = other.Image;
			Sequence = other.Sequence;
			Palette = other.Palette;
			OpenFrames = other.OpenFrames;
			IdleFrame = other.IdleFrame;
			GlowFrames = other.GlowFrames;
			PressedFrame = other.PressedFrame;
			FrameTime = other.FrameTime;
			OpenDelay = other.OpenDelay;
			art = other.art;
		}

		public override RA2ShellButtonWidget Clone() { return new RA2ShellButtonWidget(this); }

		public override void Draw()
		{
			var now = Game.RunTime;
			if (lastDrawnFrame < Game.RenderFrame - 1)
			{
				openStart = now + OpenDelay;
				glow = 0;
			}

			lastDrawnFrame = Game.RenderFrame;

			openFrame = (int)Math.Max(0, (now - openStart) / Math.Max(1, FrameTime));
			opening = openFrame < OpenFrames.Length;

			// The label only appears once the button has slid fully into place.
			if (opening)
				DrawBackground(RenderBounds, IsDisabled(), false, false, false);
			else
				base.Draw();
		}

		public override bool HandleMouseInput(MouseInput mi)
		{
			// The button is not there to be clicked until it has slid in.
			return !opening && base.HandleMouseInput(mi);
		}

		public override void DrawBackground(Rectangle rect, bool disabled, bool pressed, bool hover, bool highlighted)
		{
			var sequence = art.Sequence(Sequence);
			if (sequence == null)
				return;

			var now = Game.RunTime;
			int frame;
			if (opening)
				frame = OpenFrames[openFrame];
			else
			{
				// Step the glow one frame at a time towards lit (hover) or unlit.
				var target = !disabled && (hover || highlighted) ? GlowFrames.Length : 0;
				if (glow != target && now - lastGlowStep >= FrameTime)
				{
					glow += Math.Sign(target - glow);
					lastGlowStep = now;
				}

				if (pressed && !disabled)
					frame = PressedFrame;
				else
					frame = glow > 0 ? GlowFrames[glow - 1] : IdleFrame;
			}

			art.Draw(sequence, frame, Palette, rect.Location);
		}
	}
}
