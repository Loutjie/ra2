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
using System.Collections.Immutable;
using OpenRA.Graphics;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.RA2.Graphics
{
	/// <summary>
	/// Interface art read straight from the player's RA2 files: indexed SHP frames drawn through a palette into
	/// RGBA sprites the widget renderer can draw. ChromeProvider only reads PNG sheets, and the sidebar art in
	/// sidec01.mix / sidec02.mix must not be copied into the mod, so it is converted here when first drawn.
	/// </summary>
	public static class RA2ChromeArt
	{
		/// <summary>Chrome metric naming the package that holds a faction's sidebar art: "SidebarArtPackage-&lt;suffix&gt;".</summary>
		public const string PackageMetric = "SidebarArtPackage-";

		/// <summary>Chrome metric naming the package used when there is no local faction (spectators, replays).</summary>
		public const string DefaultPackageMetric = "SidebarArtPackage";

		/// <summary>Chrome metric naming the palette file, looked up in the same package as the art.</summary>
		public const string PaletteMetric = "SidebarArtPalette";

		static ModData owner;
		static SheetBuilder sheetBuilder;
		static readonly Dictionary<string, Sprite[]> Sprites = [];

		/// <summary>The faction suffix ("allies", "soviets") AddFactionSuffixLogic would use, or null for spectators.</summary>
		public static string FactionSuffix(World world)
		{
			var player = world?.LocalPlayer;
			if (player == null || player.Spectating)
				return null;

			return ChromeMetrics.TryGet("FactionSuffix-" + player.Faction.InternalName, out string suffix)
				? suffix : player.Faction.InternalName;
		}

		/// <summary>The package prefix ("sidec01") for the faction's art, or null when no metric names one.</summary>
		public static string Package(string factionSuffix)
		{
			if (factionSuffix != null && ChromeMetrics.TryGet(PackageMetric + factionSuffix, out string package))
				return package;

			return ChromeMetrics.TryGet(DefaultPackageMetric, out string fallback) ? fallback : null;
		}

		/// <summary>Prefixes <paramref name="filename"/> with the package unless it already names one.</summary>
		public static string Resolve(string package, string filename)
		{
			if (string.IsNullOrEmpty(package) || filename.Contains('|'))
				return filename;

			return package + "|" + filename;
		}

		/// <summary>All frames of an SHP, drawn with the package's sidebar palette. Cached for the lifetime of the mod.</summary>
		public static Sprite[] Frames(string package, string filename)
		{
			var modData = Game.ModData;
			if (owner != modData)
			{
				sheetBuilder?.Dispose();
				sheetBuilder = null;
				Sprites.Clear();
				owner = modData;
			}

			var path = Resolve(package, filename);
			if (Sprites.TryGetValue(path, out var cached))
				return cached;

			var palettePath = Resolve(package, ChromeMetrics.Get<string>(PaletteMetric));
			ImmutablePalette palette;
			using (var s = modData.DefaultFileSystem.Open(palettePath))
				palette = new ImmutablePalette(s, [0], ImmutableArray<int>.Empty);

			var colors = new uint[Palette.Size];
			for (var i = 0; i < Palette.Size; i++)
				colors[i] = palette[i];

			var frames = FrameLoader.GetFrames(modData.DefaultFileSystem, path, modData.SpriteLoaders, out _);
			sheetBuilder ??= new SheetBuilder(SheetType.BGRA, 1024);

			var sprites = new Sprite[frames.Length];
			for (var i = 0; i < frames.Length; i++)
			{
				var f = frames[i];
				var bgra = Compose(f.Data, f.Size, f.FrameSize, f.Offset, colors);
				sprites[i] = sheetBuilder.Add(bgra, SpriteFrameType.Bgra32, f.FrameSize);
			}

			Sprites[path] = sprites;
			return sprites;
		}

		/// <summary>
		/// Places an indexed frame's data in its full frame and looks every pixel up in <paramref name="colors"/>
		/// (ARGB, as <see cref="ImmutablePalette"/> stores them), giving BGRA bytes. Index 0 is transparent.
		/// <paramref name="offset"/> is the data's offset as the TS SHP loader reports it: the distance between the
		/// centre of the data and the centre of the frame.
		/// </summary>
		public static byte[] Compose(byte[] data, Size size, Size frameSize, System.Numerics.Vector2 offset, uint[] colors)
		{
			var bgra = new byte[frameSize.Width * frameSize.Height * 4];
			if (data == null || size.Width == 0 || size.Height == 0)
				return bgra;

			// Undo the loader's centring: Offset = position + (size - frameSize) / 2, with integer division.
			var x0 = (int)offset.X - (size.Width - frameSize.Width) / 2;
			var y0 = (int)offset.Y - (size.Height - frameSize.Height) / 2;

			for (var y = 0; y < size.Height; y++)
			{
				var fy = y0 + y;
				if (fy < 0 || fy >= frameSize.Height)
					continue;

				for (var x = 0; x < size.Width; x++)
				{
					var fx = x0 + x;
					if (fx < 0 || fx >= frameSize.Width)
						continue;

					var index = data[y * size.Width + x];
					if (index == 0)
						continue;

					var c = colors[index];
					var o = (fy * frameSize.Width + fx) * 4;
					bgra[o] = (byte)c;
					bgra[o + 1] = (byte)(c >> 8);
					bgra[o + 2] = (byte)(c >> 16);
					bgra[o + 3] = (byte)(c >> 24);
				}
			}

			return bgra;
		}

		/// <summary>Throws if an index is outside a frame list, naming the file.</summary>
		public static Sprite Frame(Sprite[] frames, int index, string filename)
		{
			if (index < 0 || index >= frames.Length)
				throw new ArgumentOutOfRangeException(nameof(index), $"{filename} has {frames.Length} frames, not frame {index}.");

			return frames[index];
		}
	}
}
