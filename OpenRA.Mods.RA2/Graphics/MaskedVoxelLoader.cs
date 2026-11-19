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
using OpenRA.FileSystem;
using OpenRA.Graphics;
using OpenRA.Mods.Cnc.FileFormats;
using OpenRA.Mods.Cnc.Graphics;

namespace OpenRA.Mods.RA2.Graphics
{
	/// <summary>
	/// A region of a voxel model, in voxel-grid coordinates (voxel (i, j, k) has its centre at (i + 0.5, j + 0.5, k + 0.5);
	/// x forward, y across, z up). The shape has its own frame: an axis that starts at Origin and points along +x
	/// pitched up by Pitch degrees; 'side' runs along +y and 'up' is perpendicular to both.
	/// A voxel is inside when its axial position is within Start..End, its distance from the axis is at most Radius
	/// (when Radius is above zero) and its side and up coordinates are within their limits.
	/// Without a radius the shape is an oriented box.
	/// </summary>
	public class VoxelMaskShape
	{
		[Desc("Axis start point (x, y, z) in voxel-grid coordinates.")]
		public readonly ImmutableArray<float> Origin = [0, 0, 0];

		[Desc("Axis pitch in degrees: 0 runs along +x, 90 straight up along +z.")]
		public readonly float Pitch = 0;

		[Desc("Axial range, measured along the axis from Origin.")]
		public readonly float Start = 0;
		public readonly float End = 0;

		[Desc("Maximum distance from the axis. Zero means no radial limit.")]
		public readonly float Radius = 0;

		[Desc("Limits across the axis (along y).")]
		public readonly float MinSide = float.NegativeInfinity;
		public readonly float MaxSide = float.PositiveInfinity;

		[Desc("Limits perpendicular to the axis and the side direction (up when Pitch is 0).")]
		public readonly float MinUp = float.NegativeInfinity;
		public readonly float MaxUp = float.PositiveInfinity;

		public VoxelMaskShape() { }

		public VoxelMaskShape(ImmutableArray<float> origin, float pitch, float start, float end, float radius = 0,
			float minSide = float.NegativeInfinity, float maxSide = float.PositiveInfinity,
			float minUp = float.NegativeInfinity, float maxUp = float.PositiveInfinity)
		{
			Origin = origin;
			Pitch = pitch;
			Start = start;
			End = end;
			Radius = radius;
			MinSide = minSide;
			MaxSide = maxSide;
			MinUp = minUp;
			MaxUp = maxUp;
		}

		internal Func<float, float, float, bool> CreateTest()
		{
			if (Origin.Length != 3)
				throw new InvalidOperationException($"A voxel mask Origin needs three values (x, y, z), not {Origin.Length}.");

			var radians = Pitch * MathF.PI / 180;
			var cos = MathF.Cos(radians);
			var sin = MathF.Sin(radians);
			var (ox, oy, oz) = (Origin[0], Origin[1], Origin[2]);
			var radiusSquared = Radius * Radius;
			return (x, y, z) =>
			{
				var dx = x - ox;
				var dz = z - oz;
				var along = dx * cos + dz * sin;
				var up = dz * cos - dx * sin;
				var side = y - oy;
				return along >= Start && along <= End
					&& side >= MinSide && side <= MaxSide
					&& up >= MinUp && up <= MaxUp
					&& (Radius <= 0 || side * side + up * up <= radiusSquared);
			};
		}
	}

	/// <summary>
	/// Wraps the engine's voxel loader. Models with configured mask shapes are read from the player's own files, every
	/// voxel inside a shape is cleared in memory, and the result is built into a model. Nothing is written to disk.
	/// Unmasked models go straight to the engine's loader.
	/// </summary>
	public sealed class MaskedVoxelLoader : IDisposable
	{
		readonly VoxelLoader loader;
		readonly IReadOnlyFileSystem fileSystem;
		readonly IReadOnlyDictionary<string, ImmutableArray<VoxelMaskShape>> masks;
		readonly Dictionary<(string Vxl, string Hva), IModel> masked = [];

		public MaskedVoxelLoader(IReadOnlyFileSystem fileSystem, int sheetSize, IReadOnlyDictionary<string, ImmutableArray<VoxelMaskShape>> masks)
		{
			this.fileSystem = fileSystem;
			this.masks = masks;
			loader = new VoxelLoader(fileSystem, sheetSize);
		}

		public IModel Load(string vxl, string hva)
		{
			if (!masks.TryGetValue(vxl, out var shapes))
				return loader.Load(vxl, hva);

			if (masked.TryGetValue((vxl, hva), out var model))
				return model;

			// A configured mask must never fall back to the unmasked model silently: let a missing file throw.
			VxlReader vxlReader;
			HvaReader hvaReader;
			using (var s = fileSystem.Open(vxl + ".vxl"))
				vxlReader = new VxlReader(s);

			using (var s = fileSystem.Open(hva + ".hva"))
				hvaReader = new HvaReader(s, hva + ".hva");

			var removed = 0;
			foreach (var limb in vxlReader.Limbs)
				removed += Apply(limb, shapes);

			Log.Write("debug", $"Masked voxel model {vxl}: removed {removed} voxels.");

			model = new Voxel(loader, vxlReader, hvaReader, (vxl, hva));
			masked.Add((vxl, hva), model);
			return model;
		}

		/// <summary>Clears every voxel of the limb that lies inside any of the shapes. Returns the number removed.</summary>
		public static int Apply(VxlLimb limb, IReadOnlyList<VoxelMaskShape> shapes)
		{
			if (shapes.Count == 0)
				return 0;

			var tests = new Func<float, float, float, bool>[shapes.Count];
			for (var i = 0; i < shapes.Count; i++)
				tests[i] = shapes[i].CreateTest();

			var removed = 0;
			var remove = new List<byte>();
			for (var x = 0; x < limb.Size[0]; x++)
			{
				for (var y = 0; y < limb.Size[1]; y++)
				{
					var column = limb.VoxelMap[x, y];
					if (column == null)
						continue;

					remove.Clear();
					foreach (var z in column.Keys)
						foreach (var test in tests)
							if (test(x + 0.5f, y + 0.5f, z + 0.5f))
							{
								remove.Add(z);
								break;
							}

					foreach (var z in remove)
						column.Remove(z);

					removed += remove.Count;
				}
			}

			limb.VoxelCount -= (uint)removed;
			return removed;
		}

		public void RefreshBuffer() { loader.RefreshBuffer(); }
		public void Finish() { loader.Finish(); }
		public IVertexBuffer<ModelVertex> VertexBuffer => loader.VertexBuffer;
		public void Dispose() { loader.Dispose(); }
	}
}
