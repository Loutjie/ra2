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
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Traits;
using Color = OpenRA.Primitives.Color;

namespace OpenRA.Mods.RA2.Traits
{
	using Util = OpenRA.Graphics.Util;

	[Desc("Global palette effect with a fixed color that can ramp in (a storm's build-up) and fade out.")]
	public class WeatherPaletteEffectInfo : TraitInfo
	{
		public readonly string[] ExcludePalette = { "cursor", "chrome", "colorpicker", "fog", "shroud", "effect" };

		[Desc("Used to pre-multiply colors.")]
		public readonly float Ratio = 0.6f;

		[Desc("How long the tint lasts at full strength, in ticks. A build-up ramp is added in front of it.")]
		public readonly int Length = 40;

		[Desc("Ticks at the end of Length over which the tint fades back to clear. 0 ends it at once.")]
		public readonly int FadeOut = 0;

		public readonly Color Color = Color.LightGray;

		[Desc("Set this when using multiple independent flash effects.")]
		public readonly string Type = null;

		public override object Create(ActorInitializer init) { return new WeatherPaletteEffect(this); }
	}

	public class WeatherPaletteEffect : IPaletteModifier, ITick
	{
		public readonly WeatherPaletteEffectInfo Info;

		int elapsed;
		int total;
		int rampIn;

		public WeatherPaletteEffect(WeatherPaletteEffectInfo info)
		{
			Info = info;
		}

		/// <summary>Tint strength right now, in per-mille of Ratio.</summary>
		public int StrengthPerMille => LightningStormBuildUp.TintPerMille(elapsed, rampIn, total, Info.FadeOut);

		/// <summary>Tint at full strength for <paramref name="ticks"/> ticks, or Length when -1.</summary>
		public void Enable(int ticks)
		{
			elapsed = 0;
			rampIn = 0;
			total = ticks == -1 ? Info.Length : ticks;
		}

		/// <summary>
		/// Darken over <paramref name="rampTicks"/> ticks, then hold for Length. Continues from the
		/// current strength if the tint is already showing.
		/// </summary>
		public void BuildUp(int rampTicks)
		{
			var current = StrengthPerMille;
			rampIn = Math.Max(0, rampTicks);
			elapsed = LightningStormBuildUp.RampStartFor(current, rampIn);
			total = rampIn + Info.Length;
		}

		void ITick.Tick(Actor self)
		{
			if (elapsed < total)
				elapsed++;
		}

		void IPaletteModifier.AdjustPalette(IReadOnlyDictionary<string, MutablePalette> palettes)
		{
			var strength = StrengthPerMille;
			if (strength <= 0)
				return;

			var ratio = Info.Ratio * strength / LightningStormBuildUp.FullStrength;

			foreach (var palette in palettes)
			{
				if (Info.ExcludePalette.Contains(palette.Key))
					continue;

				for (var x = 0; x < Palette.Size; x++)
				{
					var orig = palette.Value.GetColor(x);
					var c = Info.Color;
					var color = Color.FromArgb(orig.A, ((int)c.R).Clamp(0, 255), ((int)c.G).Clamp(0, 255), ((int)c.B).Clamp(0, 255));
					var final = Util.PremultipliedColorLerp(ratio, orig, Util.PremultiplyAlpha(Color.FromArgb(orig.A, color)));
					palette.Value.SetColor(x, final);
				}
			}
		}
	}
}
