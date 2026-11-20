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
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Mods.RA2.Graphics;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.RA2.Widgets
{
	/// <summary>
	/// Draws a frame of an SHP from the local faction's sidebar art package (sidec01.mix for the Allies,
	/// sidec02.mix for the Soviets, chosen by the "SidebarArtPackage-&lt;faction suffix&gt;" chrome metric).
	/// Placed as a child of a button with an empty Background, it draws the frame matching the button's state.
	/// </summary>
	public class RA2ArtWidget : Widget
	{
		[Desc("SHP file in the faction's sidebar art package, or \"package|file\".")]
		[FieldLoader.Require]
		public string Image = null;

		[Desc("Frame to draw.")]
		public readonly int Frame = 0;

		[Desc("Frame drawn while the parent button is hovered. -1 uses Frame.")]
		public readonly int HoverFrame = -1;

		[Desc("Frame drawn while the parent button is pressed. -1 uses HighlightedFrame, then Frame.")]
		public readonly int PressedFrame = -1;

		[Desc("Frame drawn while the parent button is highlighted (selected tab, active mode). -1 uses Frame.")]
		public readonly int HighlightedFrame = -1;

		[Desc("Frame drawn while the parent button is disabled. -1 uses Frame.")]
		public readonly int DisabledFrame = -1;

		[Desc("Repeat the frame across the widget's width.")]
		public readonly bool TileHorizontally = false;

		[Desc("Repeat the frame down the widget's height.")]
		public readonly bool TileVertically = false;

		[Desc("Let mouse clicks on the art reach whatever is beneath it (the world). Set false for sidebar backgrounds.")]
		public readonly bool ClickThrough = true;

		public Func<int> GetFrame;

		readonly World world;
		string package;
		Sprite[] frames;

		[ObjectCreator.UseCtor]
		public RA2ArtWidget(World world)
		{
			this.world = world;
			IgnoreMouseOver = true;
			GetFrame = DefaultFrame;
		}

		protected RA2ArtWidget(RA2ArtWidget other)
			: base(other)
		{
			world = other.world;
			Image = other.Image;
			Frame = other.Frame;
			HoverFrame = other.HoverFrame;
			PressedFrame = other.PressedFrame;
			HighlightedFrame = other.HighlightedFrame;
			DisabledFrame = other.DisabledFrame;
			TileHorizontally = other.TileHorizontally;
			TileVertically = other.TileVertically;
			ClickThrough = other.ClickThrough;
			package = other.package;
			frames = other.frames;
			GetFrame = DefaultFrame;
		}

		public override Widget Clone() { return new RA2ArtWidget(this); }

		public override void Initialize(WidgetArgs args)
		{
			base.Initialize(args);
			package = RA2ChromeArt.Package(RA2ChromeArt.FactionSuffix(world));

			// Without a size in the layout, take the art's, so it can block clicks and be stacked by height.
			if (Bounds.Width == 0 && Bounds.Height == 0 && Image != null)
			{
				Bounds.Width = FrameSize.X;
				Bounds.Height = FrameSize.Y;
			}
		}

		public override bool HandleMouseInput(MouseInput mi)
		{
			return !ClickThrough && RenderBounds.Contains(mi.Location);
		}

		public Sprite[] Frames => frames ??= RA2ChromeArt.Frames(package, Image);

		/// <summary>The frame's size, for widgets that size themselves to their art.</summary>
		public int2 FrameSize
		{
			get
			{
				var s = Frames[0].Bounds.Size;
				return new int2(s.Width, s.Height);
			}
		}

		int DefaultFrame()
		{
			if (Parent is not ButtonWidget button)
				return Frame;

			if (DisabledFrame >= 0 && button.IsDisabled())
				return DisabledFrame;

			if (button.Depressed && PressedFrame >= 0)
				return PressedFrame;

			if ((button.Depressed || button.IsHighlighted()) && HighlightedFrame >= 0)
				return HighlightedFrame;

			if (HoverFrame >= 0 && Ui.MouseOverWidget == button)
				return HoverFrame;

			return Frame;
		}

		public override void Draw()
		{
			var sprite = RA2ChromeArt.Frame(Frames, GetFrame(), Image);
			var size = sprite.Bounds.Size;
			if (size.Width == 0 || size.Height == 0)
				return;

			var origin = RenderOrigin;
			var w = TileHorizontally ? Math.Max(Bounds.Width, size.Width) : size.Width;
			var h = TileVertically ? Math.Max(Bounds.Height, size.Height) : size.Height;

			// Tiles that run past the widget are cut off by the scissor, so a partial last tile is drawn cleanly.
			var clip = TileHorizontally || TileVertically;
			if (clip)
				Game.Renderer.EnableScissor(new Rectangle(origin.X, origin.Y, w, h));

			for (var y = 0; y < h; y += size.Height)
				for (var x = 0; x < w; x += size.Width)
					WidgetUtils.DrawSprite(sprite, new Vector2(origin.X + x, origin.Y + y));

			if (clip)
				Game.Renderer.DisableScissor();
		}
	}
}
