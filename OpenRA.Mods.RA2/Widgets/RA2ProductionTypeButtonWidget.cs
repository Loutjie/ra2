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
using OpenRA.Widgets;

namespace OpenRA.Mods.RA2.Widgets
{
	/// <summary>
	/// A production tab that keeps the RA2 art for selected and unselected tabs and, while the tab has a finished item
	/// such as a building ready to place, draws a blinking glow that hugs the outline of the tab's button face.
	/// The tab art has the icon baked in, so the stock OpenRA icon colour change cannot be used.
	/// The glow colour comes from the chrome metric "ProductionTabGlow-&lt;faction suffix&gt;".
	/// </summary>
	public class RA2ProductionTypeButtonWidget : ProductionTypeButtonWidget
	{
		const string ReadySuffix = "-alert";
		const string GlowMetric = "ProductionTabGlow-";

		[Desc("Glow colour, including its strength as alpha, when the faction has no ProductionTabGlow metric.")]
		public readonly Color ReadyGlowColor = Color.FromArgb(200, 255, 240, 40);

		[Desc("How far in pixels the glow reaches past the edge of the button face.")]
		public readonly int ReadyGlowRadius = 3;

		[Desc("The glow blinks on and off every this many milliseconds. 0 keeps it steady.")]
		public readonly int ReadyGlowBlinkMs = 400;

		readonly Dictionary<(string Background, Color Color), (Sheet Sheet, Sprite Sprite)> glows = [];
		ImageWidget icon;

		[ObjectCreator.UseCtor]
		public RA2ProductionTypeButtonWidget(ModData modData, World world)
			: base(modData, world) { }

		protected RA2ProductionTypeButtonWidget(RA2ProductionTypeButtonWidget other)
			: base(other)
		{
			ReadyGlowColor = other.ReadyGlowColor;
			ReadyGlowRadius = other.ReadyGlowRadius;
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

		// AddFactionSuffixLogic appends "-<faction>" to the Background.
		Color GlowColor()
		{
			var suffix = Background[(Background.LastIndexOf('-') + 1)..];
			return ChromeMetrics.TryGet(GlowMetric + suffix, out Color color) ? color : ReadyGlowColor;
		}

		public override void Draw()
		{
			base.Draw();
			if (IsDisabled() || !IsReady() || (ReadyGlowBlinkMs > 0 && Game.RunTime / ReadyGlowBlinkMs % 2 == 1))
				return;

			var glow = Glow(Background, GlowColor());
			if (glow == null)
				return;

			// The glow sprite is the tab art plus a margin; scale it like the art is scaled into the tab.
			var rb = RenderBounds;
			var art = glow.Bounds.Size - new Size(2 * ReadyGlowRadius, 2 * ReadyGlowRadius);
			var sx = (float)rb.Width / art.Width;
			var sy = (float)rb.Height / art.Height;
			WidgetUtils.DrawSprite(glow,
				new Vector2(rb.X - ReadyGlowRadius * sx, rb.Y - ReadyGlowRadius * sy),
				new Vector2(glow.Bounds.Width * sx, glow.Bounds.Height * sy));
		}

		Sprite Glow(string background, Color color)
		{
			if (glows.TryGetValue((background, color), out var cached))
				return cached.Sprite;

			var art = ChromeProvider.TryGetPanelImages(background)?[4];
			if (art == null)
			{
				glows[(background, color)] = (null, null);
				return null;
			}

			var w = art.Bounds.Width;
			var h = art.Bounds.Height;
			var face = FaceMask(Pixels(art), w, h);
			var pixels = GlowPixels(face, w, h, ReadyGlowRadius, color);
			var gw = w + 2 * ReadyGlowRadius;
			var gh = h + 2 * ReadyGlowRadius;

			// Textures must have power-of-two sides, so the glow sits in the corner of a larger sheet.
			var sheet = new Sheet(SheetType.BGRA, new Size(gw, gh).NextPowerOf2());
			var data = sheet.GetData();
			for (var y = 0; y < gh; y++)
				Array.Copy(pixels, y * gw * 4, data, y * sheet.Size.Width * 4, gw * 4);

			sheet.CommitBufferedData();
			var sprite = new Sprite(sheet, new Rectangle(0, 0, gw, gh), TextureChannel.RGBA);
			glows[(background, color)] = (sheet, sprite);
			return sprite;
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

		/// <summary>
		/// The button face: the largest connected area of opaque, not-dark pixels, with its holes (the icon) filled.
		/// Dark frame pixels around the face, as in the Soviet art, are left out so the glow can show over them.
		/// </summary>
		public static bool[] FaceMask(byte[] bgra, int width, int height)
		{
			var lit = new bool[width * height];
			for (var i = 0; i < lit.Length; i++)
			{
				var p = i * 4;
				var luma = (11 * bgra[p] + 59 * bgra[p + 1] + 30 * bgra[p + 2]) / 100;
				lit[i] = bgra[p + 3] > 128 && luma > 60;
			}

			// Keep the largest connected area.
			var label = new int[lit.Length];
			var bestLabel = 0;
			var bestSize = 0;
			var stack = new Stack<int>();
			for (int start = 0, next = 1; start < lit.Length; start++)
			{
				if (!lit[start] || label[start] != 0)
					continue;

				var size = 0;
				label[start] = next;
				stack.Push(start);
				while (stack.Count > 0)
				{
					var i = stack.Pop();
					size++;
					foreach (var n in Neighbours(i, width, height))
					{
						if (lit[n] && label[n] == 0)
						{
							label[n] = next;
							stack.Push(n);
						}
					}
				}

				if (size > bestSize)
					(bestSize, bestLabel) = (size, next);

				next++;
			}

			// Everything not reachable from the border without crossing the face is inside it.
			var outside = new bool[lit.Length];
			for (var x = 0; x < width; x++)
			{
				stack.Push(x);
				stack.Push((height - 1) * width + x);
			}

			for (var y = 0; y < height; y++)
			{
				stack.Push(y * width);
				stack.Push(y * width + width - 1);
			}

			while (stack.Count > 0)
			{
				var i = stack.Pop();
				if (outside[i] || (bestLabel != 0 && label[i] == bestLabel))
					continue;

				outside[i] = true;
				foreach (var n in Neighbours(i, width, height))
					stack.Push(n);
			}

			var face = new bool[lit.Length];
			for (var i = 0; i < face.Length; i++)
				face[i] = bestLabel != 0 && !outside[i];

			return face;
		}

		static IEnumerable<int> Neighbours(int i, int width, int height)
		{
			var x = i % width;
			var y = i / width;
			if (x > 0)
				yield return i - 1;
			if (x < width - 1)
				yield return i + 1;
			if (y > 0)
				yield return i - width;
			if (y < height - 1)
				yield return i + width;
		}

		/// <summary>
		/// A glow around the face, <paramref name="radius"/> pixels wide, fading with distance from the face.
		/// The face itself stays clear. The result is <paramref name="radius"/> pixels larger on every side.
		/// Premultiplied BGRA.
		/// </summary>
		public static byte[] GlowPixels(bool[] face, int width, int height, int radius, Color color)
		{
			var gw = width + 2 * radius;
			var gh = height + 2 * radius;
			var pixels = new byte[gw * gh * 4];

			for (var y = -radius; y < height + radius; y++)
			{
				for (var x = -radius; x < width + radius; x++)
				{
					if (x >= 0 && y >= 0 && x < width && y < height && face[y * width + x])
						continue;

					var nearest = float.MaxValue;
					for (var fy = Math.Max(y - radius - 1, 0); fy <= Math.Min(y + radius + 1, height - 1); fy++)
						for (var fx = Math.Max(x - radius - 1, 0); fx <= Math.Min(x + radius + 1, width - 1); fx++)
							if (face[fy * width + fx])
								nearest = Math.Min(nearest, (fx - x) * (fx - x) + (fy - y) * (fy - y));

					var strength = MathF.Max(1 - MathF.Sqrt(nearest) / (radius + 1), 0);
					if (strength <= 0)
						continue;

					var a = (int)(color.A * MathF.Pow(strength, 1.5f));
					var i = ((y + radius) * gw + x + radius) * 4;
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
				sheet?.Dispose();

			glows.Clear();
		}
	}
}
