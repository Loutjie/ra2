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
using OpenRA.Graphics;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Primitives;

namespace OpenRA.Mods.RA2.Widgets
{
	/// <summary>
	/// A production tab that separates "selected" from "has a finished item", like the stock OpenRA sidebar does
	/// with background and icon colour. The RA2 tab art has the icon baked in, so here:
	/// the icon colour (Soviet red/yellow, Allied dark/bright) shows whether something is ready,
	/// and the selected tab is drawn pushed in using the art's pressed state.
	/// The pushed-in tabs the art lacks are generated once, on first draw (see <see cref="PushedIn"/>).
	/// </summary>
	public class RA2ProductionTypeButtonWidget : ProductionTypeButtonWidget
	{
		const string ReadySuffix = "-alert";
		const int PushDepth = 2;

		readonly Dictionary<string, (Sheet Sheet, Sprite[] Panel)> generated = [];
		ImageWidget icon;

		[ObjectCreator.UseCtor]
		public RA2ProductionTypeButtonWidget(ModData modData, World world)
			: base(modData, world) { }

		protected RA2ProductionTypeButtonWidget(RA2ProductionTypeButtonWidget other)
			: base(other) { }

		public override WorldButtonWidget Clone() { return new RA2ProductionTypeButtonWidget(this); }

		bool IsReady()
		{
			icon ??= GetOrNull<ImageWidget>("ICON");
			var name = icon?.GetImageName();
			return name != null && name.EndsWith(ReadySuffix, StringComparison.Ordinal);
		}

		// ClassicProductionLogic highlights the tab of the open production queue.
		public override void DrawBackground(Rectangle rect, bool disabled, bool pressed, bool hover, bool highlighted)
		{
			var ready = !disabled && IsReady();
			if (disabled || !highlighted)
			{
				// Raised tab: the highlighted (bright) art means something is ready.
				DrawBackground(Background, rect, disabled, pressed, hover, ready);
				return;
			}

			WidgetUtils.DrawPanel(rect, PushedIn(Background, ready));
		}

		/// <summary>
		/// The selected tab, pushed in, with a bright icon when ready.
		/// Soviet art has a real pushed-in state with a yellow icon: used as is when ready, recoloured red otherwise.
		/// Allied art has no pushed-in state (its pressed art is only a lighter shade), so one is drawn from the raised art.
		/// </summary>
		Sprite[] PushedIn(string baseName, bool ready)
		{
			var key = ready ? baseName + "-ready" : baseName;
			if (generated.TryGetValue(key, out var cached))
				return cached.Panel;

			var normal = ChromeProvider.GetPanelImages(baseName)[4];
			var bright = ChromeProvider.GetPanelImages(baseName + "-highlighted")[4];
			var palette = LearnPalette(Pixels(bright), Pixels(normal));

			byte[] pixels;
			Sprite source;
			if (palette.Count > 0)
			{
				if (ready)
				{
					var art = ChromeProvider.GetPanelImages(baseName + "-highlighted-pressed");
					generated[key] = (null, art);
					return art;
				}

				source = ChromeProvider.GetPanelImages(baseName + "-pressed")[4];
				pixels = Pixels(source);
				RecolourIcon(pixels, palette);
			}
			else
			{
				source = ready ? bright : normal;
				pixels = PushIn(Pixels(source), source.Bounds.Width, source.Bounds.Height, ready ? 85 : 70);
			}

			var size = source.Bounds.Size;
			// Textures must have power-of-two sides, so the tab sits in the corner of a larger sheet.
			var sheet = new Sheet(SheetType.BGRA, size.NextPowerOf2());
			var data = sheet.GetData();
			for (var y = 0; y < size.Height; y++)
				Array.Copy(pixels, y * size.Width * 4, data, y * sheet.Size.Width * 4, size.Width * 4);

			sheet.CommitBufferedData();

			var scale = source.Size.X / source.Bounds.Width;
			var panel = new Sprite[9];
			panel[4] = new Sprite(sheet, new Rectangle(0, 0, size.Width, size.Height), TextureChannel.RGBA, scale);
			generated[key] = (sheet, panel);
			return panel;
		}

		/// <summary>
		/// Draws raised tab art pushed in: the face moves down and right by <see cref="PushDepth"/> pixels and darkens
		/// to <paramref name="brightnessPercent"/>, and a shadow runs along the inside of the top and left edges.
		/// The outline keeps the original shape. Pixels are premultiplied BGRA.
		/// </summary>
		public static byte[] PushIn(byte[] src, int width, int height, int brightnessPercent)
		{
			var dst = new byte[src.Length];
			byte Alpha(int x, int y) => x < 0 || y < 0 ? (byte)0 : src[(y * width + x) * 4 + 3];

			for (var y = 0; y < height; y++)
			{
				for (var x = 0; x < width; x++)
				{
					var i = (y * width + x) * 4;
					var a = src[i + 3];
					if (a == 0)
						continue;

					// Take the face from up-left, but only where both pixels are opaque, so the edges keep their own colour.
					var j = i;
					if (a == 255 && Alpha(x - PushDepth, y - PushDepth) == 255)
						j = ((y - PushDepth) * width + x - PushDepth) * 4;

					// Distance to the top or left outline, for the inner shadow.
					var edge = PushDepth + 1;
					for (var k = 1; k <= PushDepth; k++)
					{
						if (Alpha(x, y - k) == 0 || Alpha(x - k, y) == 0)
						{
							edge = k;
							break;
						}
					}

					var shade = brightnessPercent * (edge <= PushDepth ? 25 + 25 * (edge - 1) : 100) / 100;
					for (var c = 0; c < 3; c++)
						dst[i + c] = (byte)Math.Min(src[j + c] * shade / 100, a);

					dst[i + 3] = a;
				}
			}

			return dst;
		}

		/// <summary>Copies a BGRA sprite's pixels out of its sheet.</summary>
		static byte[] Pixels(Sprite sprite)
		{
			var data = sprite.Sheet.GetData();
			var stride = sprite.Sheet.Size.Width * 4;
			var b = sprite.Bounds;
			var pixels = new byte[b.Width * b.Height * 4];
			for (var y = 0; y < b.Height; y++)
				Array.Copy(data, (b.Y + y) * stride + b.X * 4, pixels, y * b.Width * 4, b.Width * 4);

			return pixels;
		}

		/// <summary>The yellow (ready) icon colour in the Soviet tab art, in BGRA order.</summary>
		public static bool IsIconYellow(byte b, byte g, byte r)
		{
			return r > 120 && g > 120 && b < 90 && Math.Abs(r - g) < 70;
		}

		static int ColourKey(byte b, byte g, byte r) { return (r >> 3) << 10 | (g >> 3) << 5 | b >> 3; }

		/// <summary>
		/// Maps each yellow colour in the ready art to the average colour at the same pixels in the normal art.
		/// Both are the same raised tab, so they line up pixel for pixel.
		/// </summary>
		public static Dictionary<int, (byte B, byte G, byte R)> LearnPalette(byte[] ready, byte[] normal)
		{
			var sums = new Dictionary<int, (int B, int G, int R, int Count)>();
			for (var i = 0; i + 3 < ready.Length && i + 3 < normal.Length; i += 4)
			{
				if (!IsIconYellow(ready[i], ready[i + 1], ready[i + 2]))
					continue;

				var key = ColourKey(ready[i], ready[i + 1], ready[i + 2]);
				sums.TryGetValue(key, out var s);
				sums[key] = (s.B + normal[i], s.G + normal[i + 1], s.R + normal[i + 2], s.Count + 1);
			}

			var palette = new Dictionary<int, (byte B, byte G, byte R)>(sums.Count);
			foreach (var (key, s) in sums)
				palette[key] = ((byte)(s.B / s.Count), (byte)(s.G / s.Count), (byte)(s.R / s.Count));

			return palette;
		}

		/// <summary>Turns yellow icon pixels red. Does nothing when the palette is empty (art without a yellow icon).</summary>
		public static void RecolourIcon(byte[] pixels, Dictionary<int, (byte B, byte G, byte R)> palette)
		{
			if (palette.Count == 0)
				return;

			for (var i = 0; i + 3 < pixels.Length; i += 4)
			{
				var (b, g, r) = (pixels[i], pixels[i + 1], pixels[i + 2]);
				if (!IsIconYellow(b, g, r))
					continue;

				if (palette.TryGetValue(ColourKey(b, g, r), out var c))
					(pixels[i], pixels[i + 1], pixels[i + 2]) = (c.B, c.G, c.R);
				else
				{
					var l = (r + g) / 2;
					(pixels[i], pixels[i + 1], pixels[i + 2]) = ((byte)(l / 20), (byte)(l / 20), (byte)(l * 17 / 20));
				}
			}
		}

		public override void Removed()
		{
			base.Removed();
			foreach (var (sheet, _) in generated.Values)
				sheet?.Dispose();

			generated.Clear();
		}
	}
}
