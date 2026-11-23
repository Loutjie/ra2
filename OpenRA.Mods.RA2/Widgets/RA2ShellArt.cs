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
using System.Numerics;
using OpenRA.Graphics;

namespace OpenRA.Mods.RA2.Widgets
{
	/// <summary>
	/// Draws frames of the shell (main menu) sprite sequences in screen space.
	/// RA2's shell art is palette-indexed SHP, which the chrome image collections cannot load,
	/// so the shell widgets take their sprites from the menu world's sequences and palettes.
	/// </summary>
	public sealed class RA2ShellArt
	{
		readonly WorldRenderer worldRenderer;
		readonly string image;
		readonly Dictionary<string, PaletteReference> palettes = [];

		public RA2ShellArt(WorldRenderer worldRenderer, string image)
		{
			this.worldRenderer = worldRenderer;
			this.image = image;
		}

		public ISpriteSequence Sequence(string sequence)
		{
			var sequences = worldRenderer?.World?.Map?.Sequences;
			if (sequences == null || string.IsNullOrEmpty(sequence) || !sequences.HasSequence(image, sequence))
				return null;

			return sequences.GetSequence(image, sequence);
		}

		PaletteReference Palette(string palette)
		{
			if (!palettes.TryGetValue(palette, out var pr))
			{
				pr = worldRenderer.Palette(palette);
				palettes.Add(palette, pr);
			}

			return pr;
		}

		/// <summary>Draws a frame so the top-left of the sequence's full canvas lands on <paramref name="topLeft"/>.</summary>
		public void Draw(ISpriteSequence sequence, int frame, string palette, int2 topLeft)
		{
			if (sequence == null || sequence.Length == 0)
				return;

			var sprite = sequence.GetSprite(frame % sequence.Length);
			if (sprite == null || sprite.Size.X == 0 || sprite.Size.Y == 0)
				return;

			// Sequence sprites are centred on their canvas; Bounds spans the whole canvas around that centre.
			var bounds = sequence.Bounds;
			var center = new Vector2(topLeft.X - bounds.X, topLeft.Y - bounds.Y);
			var location = new Vector3((int)(center.X - 0.5f * sprite.Size.X), (int)(center.Y - 0.5f * sprite.Size.Y), 0);
			Game.Renderer.SpriteRenderer.DrawSprite(sprite, Palette(palette), location);
		}
	}
}
