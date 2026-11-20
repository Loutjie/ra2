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
using OpenRA.Mods.Common.Widgets;
using OpenRA.Widgets;

namespace OpenRA.Mods.RA2.Widgets.Logic
{
	/// <summary>
	/// Plays RA2's radar shutter (radar.shp: the faction logo at frame 0, opening to an empty screen at the last frame)
	/// over the minimap while the radar comes online or goes offline. Pair with IngameRadarDisplayLogic, without a
	/// RADAR_FADETOBLACK widget (that would take the radar's animation callback), and give the minimap an
	/// AnimationLength of one less than the shutter's frame count so every frame is shown once.
	/// </summary>
	public class RA2RadarArtLogic : ChromeLogic
	{
		[ObjectCreator.UseCtor]
		public RA2RadarArtLogic(Widget widget)
		{
			var radar = widget.Get<RadarWidget>("RADAR_MINIMAP");
			var shutter = widget.Get<RA2ArtWidget>("RADAR_SHUTTER");

			// RadarWidget reports 0 while offline and 1 once fully online; it only calls back on change.
			var progress = 0f;
			radar.Animating = p => progress = p;
			shutter.GetFrame = () => (int)MathF.Round(progress * (shutter.Frames.Length - 1));
			shutter.IsVisible = () => progress < 1;
		}
	}
}
