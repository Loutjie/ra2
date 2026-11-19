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
using NUnit.Framework;
using OpenRA.Mods.Cnc.FileFormats;
using OpenRA.Mods.RA2.Graphics;

namespace OpenRA.Mods.RA2.Test
{
	[TestFixture]
	public class MaskedVoxelLoaderTests
	{
		// A synthetic, fully solid 8x8x8 limb. Each voxel's colour encodes its position so changes are detectable.
		static VxlLimb SolidCube()
		{
			var limb = new VxlLimb
			{
				Name = "test",
				Scale = 1 / 12f,
				Bounds = [0, 0, 0, 8, 8, 8],
				Size = [8, 8, 8],
				Type = NormalType.RedAlert2,
				VoxelMap = new Dictionary<byte, VxlElement>[8, 8],
			};

			for (var x = 0; x < 8; x++)
			{
				for (var y = 0; y < 8; y++)
				{
					var column = new Dictionary<byte, VxlElement>();
					for (var z = 0; z < 8; z++)
						column.Add((byte)z, new VxlElement((byte)(x * 8 + y), (byte)z));

					limb.VoxelMap[x, y] = column;
				}
			}

			limb.VoxelCount = 512;
			return limb;
		}

		static int Count(VxlLimb limb)
		{
			var n = 0;
			foreach (var column in limb.VoxelMap)
				n += column?.Count ?? 0;

			return n;
		}

		[Test]
		public void Mask_RemovesOnlyVoxelsInsideShapes()
		{
			var limb = SolidCube();

			// Axis along +x through the grid point (y, z) = (4, 4); voxel centres are at index + 0.5.
			// Centre offsets from the axis are +-0.5 or +-1.5 in y and z: squared radii 0.5 (4 voxels),
			// 2.5 (8 voxels) and 4.5 (4 voxels). Radius 2 keeps the first two groups: 12 per x slice, 96 in all.
			var shape = new VoxelMaskShape(origin: [0, 4, 4], pitch: 0, start: 0, end: 8, radius: 2);

			var removed = MaskedVoxelLoader.Apply(limb, [shape]);

			Assert.That(removed, Is.EqualTo(96));
			Assert.That(Count(limb), Is.EqualTo(512 - 96));
			Assert.That(limb.VoxelCount, Is.EqualTo(512 - 96));

			for (var x = 0; x < 8; x++)
			{
				for (var y = 0; y < 8; y++)
				{
					for (var z = 0; z < 8; z++)
					{
						var dy = y + 0.5f - 4;
						var dz = z + 0.5f - 4;
						var inside = dy * dy + dz * dz <= 4;
						var present = limb.VoxelMap[x, y].TryGetValue((byte)z, out var element);
						Assert.That(present, Is.EqualTo(!inside), $"voxel {x},{y},{z}");
						if (present)
							Assert.That(element, Is.EqualTo(new VxlElement((byte)(x * 8 + y), (byte)z)));
					}
				}
			}
		}

		[Test]
		public void Mask_PitchedCylinder_FollowsItsAxis()
		{
			var limb = SolidCube();

			// Pitch 90 degrees turns the axis from +x to +z: the same 96 voxels, in columns this time.
			var shape = new VoxelMaskShape(origin: [4, 4, 0], pitch: 90, start: 0, end: 8, radius: 2);

			Assert.That(MaskedVoxelLoader.Apply(limb, [shape]), Is.EqualTo(96));
			Assert.That(limb.VoxelMap[3, 3], Is.Empty);
			Assert.That(limb.VoxelMap[1, 3], Has.Count.EqualTo(8));
		}

		[Test]
		public void Mask_SideAndUpLimitsClipTheShape()
		{
			var limb = SolidCube();

			// Only the upper half (up >= 0) of the 96-voxel cylinder.
			var shape = new VoxelMaskShape(origin: [0, 4, 4], pitch: 0, start: 0, end: 8, radius: 2, minUp: 0);

			Assert.That(MaskedVoxelLoader.Apply(limb, [shape]), Is.EqualTo(48));
		}

		[Test]
		public void Mask_EmptyShapes_ReturnsModelUnchanged()
		{
			var limb = SolidCube();

			Assert.That(MaskedVoxelLoader.Apply(limb, []), Is.Zero);
			Assert.That(Count(limb), Is.EqualTo(512));
			Assert.That(limb.VoxelCount, Is.EqualTo(512));
		}
	}
}
