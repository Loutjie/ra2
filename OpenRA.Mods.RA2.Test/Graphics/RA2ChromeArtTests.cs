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

using System.Numerics;
using NUnit.Framework;
using OpenRA.Mods.RA2.Graphics;
using OpenRA.Primitives;

namespace OpenRA.Mods.RA2.Test
{
	[TestFixture]
	public class RA2ChromeArtTests
	{
		static uint[] Colors()
		{
			var colors = new uint[256];
			colors[5] = 0xFF112233;
			colors[7] = 0xFF445566;
			return colors;
		}

		// The TS SHP loader reports Offset = position + (size - frameSize) / 2 with integer division.
		static Vector2 LoaderOffset(int x, int y, Size size, Size frame) =>
			new(x + (size.Width - frame.Width) / 2, y + (size.Height - frame.Height) / 2);

		static byte[] Pixel(byte[] bgra, int x, int y, int width) =>
			[bgra[(y * width + x) * 4], bgra[(y * width + x) * 4 + 1], bgra[(y * width + x) * 4 + 2], bgra[(y * width + x) * 4 + 3]];

		[TestCase(1, 1, 2, 1, TestName = "Compose places even-sized data at its position in the frame")]
		[TestCase(0, 2, 3, 1, TestName = "Compose places odd-sized data at its position in the frame")]
		public void ComposePlacesData(int x, int y, int w, int h)
		{
			var frame = new Size(4, 3);
			var size = new Size(w, h);
			var data = new byte[w * h];
			data[0] = 5;
			data[w * h - 1] = 7;

			var bgra = RA2ChromeArt.Compose(data, size, frame, LoaderOffset(x, y, size, frame), Colors());

			Assert.That(bgra, Has.Length.EqualTo(4 * 3 * 4));
			Assert.That(Pixel(bgra, x, y, 4), Is.EqualTo(new byte[] { 0x33, 0x22, 0x11, 0xFF }));
			Assert.That(Pixel(bgra, x + w - 1, y + h - 1, 4), Is.EqualTo(new byte[] { 0x66, 0x55, 0x44, 0xFF }));
		}

		[TestCase(TestName = "Compose leaves index 0 and the rest of the frame transparent")]
		public void ComposeLeavesTransparent()
		{
			var frame = new Size(3, 2);
			var size = new Size(2, 1);
			var bgra = RA2ChromeArt.Compose([0, 5], size, frame, LoaderOffset(0, 0, size, frame), Colors());

			Assert.That(Pixel(bgra, 0, 0, 3), Is.EqualTo(new byte[4]));
			Assert.That(Pixel(bgra, 1, 0, 3)[3], Is.EqualTo(0xFF));
			for (var i = 2; i < 6; i++)
				Assert.That(Pixel(bgra, i % 3, i / 3, 3), Is.EqualTo(new byte[4]));
		}

		[TestCase(TestName = "Compose gives a transparent frame for an empty one")]
		public void ComposeEmptyFrame()
		{
			var bgra = RA2ChromeArt.Compose([], new Size(0, 0), new Size(2, 2), Vector2.Zero, Colors());
			Assert.That(bgra, Is.EqualTo(new byte[16]));
		}

		[TestCase("sidec01", "side1.shp", ExpectedResult = "sidec01|side1.shp", TestName = "Resolve prefixes the package")]
		[TestCase("sidec01", "sidec02|side1.shp", ExpectedResult = "sidec02|side1.shp", TestName = "Resolve keeps an explicit package")]
		[TestCase(null, "side1.shp", ExpectedResult = "side1.shp", TestName = "Resolve without a package leaves the name")]
		public string Resolve(string package, string file) => RA2ChromeArt.Resolve(package, file);
	}
}
