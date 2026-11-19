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
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Primitives;

namespace OpenRA.Mods.RA2.Widgets
{
	/// <summary>
	/// A production tab that keeps the RA2 art for selected and unselected tabs and, while the tab has a finished item
	/// such as a building ready to place, draws a blinking coloured glow behind it that shows around and below the tab.
	/// The tab art has the icon baked in, so the stock OpenRA icon colour change cannot be used.
	/// </summary>
	public class RA2ProductionTypeButtonWidget : ProductionTypeButtonWidget
	{
		const string ReadySuffix = "-alert";

		[Desc("Glow colour, including its strength as alpha, by the faction suffix of the tab's Background.",
			"Tabs whose suffix is not listed use ReadyGlowColor.")]
		public readonly Dictionary<string, Color> ReadyGlowColors = [];

		[Desc("Glow colour for tabs not listed in ReadyGlowColors.")]
		public readonly Color ReadyGlowColor = Color.FromArgb(200, 255, 240, 40);

		[Desc("How far in pixels the glow reaches past the edge of the tab.")]
		public readonly int ReadyGlowRadius = 7;

		[Desc("How far in pixels the glow is moved down, so it is strongest below the tab.")]
		public readonly int ReadyGlowDrop = 2;

		[Desc("Corner radius in pixels of the glow's shape.")]
		public readonly int ReadyGlowCornerRadius = 6;

		[Desc("The glow blinks on and off every this many milliseconds. 0 keeps it steady.")]
		public readonly int ReadyGlowBlinkMs = 400;

		readonly Dictionary<(Color Color, Size Size), (Sheet Sheet, Sprite Sprite)> glows = [];
		ImageWidget icon;

		[ObjectCreator.UseCtor]
		public RA2ProductionTypeButtonWidget(ModData modData, World world)
			: base(modData, world) { }

		protected RA2ProductionTypeButtonWidget(RA2ProductionTypeButtonWidget other)
			: base(other)
		{
			ReadyGlowColors = other.ReadyGlowColors;
			ReadyGlowColor = other.ReadyGlowColor;
			ReadyGlowRadius = other.ReadyGlowRadius;
			ReadyGlowDrop = other.ReadyGlowDrop;
			ReadyGlowCornerRadius = other.ReadyGlowCornerRadius;
			ReadyGlowBlinkMs = other.ReadyGlowBlinkMs;
		}

		public override WorldButtonWidget Clone() { return new RA2ProductionTypeButtonWidget(this); }

		// ClassicProductionLogic names the icon "<name>-alert" while the queue has a finished item.
		bool IsReady()
		{
			icon ??= GetOrNull<ImageWidget>("ICON");
			var name = icon?.GetImageName();
			return name != null && name.EndsWith(ReadySuffix, StringComparison.Ordinal);
		}

		Color GlowColor()
		{
			foreach (var (suffix, color) in ReadyGlowColors)
				if (Background.EndsWith("-" + suffix, StringComparison.Ordinal))
					return color;

			return ReadyGlowColor;
		}

		public override void Draw()
		{
			// The glow goes behind the tab, so only the part around and below it shows.
			if (!IsDisabled() && IsReady() && (ReadyGlowBlinkMs <= 0 || Game.RunTime / ReadyGlowBlinkMs % 2 == 0))
			{
				var rb = RenderBounds;
				var glow = Glow(GlowColor(), rb.Size);
				WidgetUtils.DrawSprite(glow, new Vector2(rb.X - ReadyGlowRadius, rb.Y - ReadyGlowRadius + ReadyGlowDrop));
			}

			base.Draw();
		}

		Sprite Glow(Color color, Size size)
		{
			if (glows.TryGetValue((color, size), out var cached))
				return cached.Sprite;

			var width = size.Width + 2 * ReadyGlowRadius;
			var height = size.Height + 2 * ReadyGlowRadius;
			var pixels = GlowPixels(color, size.Width, size.Height, ReadyGlowRadius, ReadyGlowCornerRadius);

			// Textures must have power-of-two sides, so the glow sits in the corner of a larger sheet.
			var sheet = new Sheet(SheetType.BGRA, new Size(width, height).NextPowerOf2());
			var data = sheet.GetData();
			for (var y = 0; y < height; y++)
				Array.Copy(pixels, y * width * 4, data, y * sheet.Size.Width * 4, width * 4);

			sheet.CommitBufferedData();
			var sprite = new Sprite(sheet, new Rectangle(0, 0, width, height), TextureChannel.RGBA);
			glows[(color, size)] = (sheet, sprite);
			return sprite;
		}

		/// <summary>
		/// A rounded rectangle of <paramref name="width"/> by <paramref name="height"/> filled with the colour,
		/// fading out linearly over <paramref name="radius"/> pixels around it. Premultiplied BGRA.
		/// </summary>
		public static byte[] GlowPixels(Color color, int width, int height, int radius, int cornerRadius)
		{
			var w = width + 2 * radius;
			var h = height + 2 * radius;
			var pixels = new byte[w * h * 4];
			var corner = Math.Min(cornerRadius, Math.Min(width, height) / 2);

			for (var y = 0; y < h; y++)
			{
				for (var x = 0; x < w; x++)
				{
					// Distance from the pixel centre to the rounded rectangle (0 inside).
					var dx = Math.Max(Math.Abs(x + 0.5f - w / 2f) - (width / 2f - corner), 0);
					var dy = Math.Max(Math.Abs(y + 0.5f - h / 2f) - (height / 2f - corner), 0);
					var distance = Math.Max(MathF.Sqrt(dx * dx + dy * dy) - corner, 0);
					var strength = Math.Max(1 - distance / (radius + 1), 0);

					var a = (int)(color.A * strength);
					var i = (y * w + x) * 4;
					pixels[i] = (byte)(color.B * a / 255);
					pixels[i + 1] = (byte)(color.G * a / 255);
					pixels[i + 2] = (byte)(color.R * a / 255);
					pixels[i + 3] = (byte)a;
				}
			}

			return pixels;
		}

		public override void Removed()
		{
			base.Removed();
			foreach (var (sheet, _) in glows.Values)
				sheet.Dispose();

			glows.Clear();
		}
	}
}
