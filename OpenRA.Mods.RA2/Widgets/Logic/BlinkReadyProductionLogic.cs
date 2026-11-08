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
	/// Blinks a production tab while it has a finished item, such as a building ready to place: every ready tab
	/// alternates between its normal and highlighted art, and the selected one blinks twice as fast to stand out.
	/// Must be listed after ClassicProductionLogic, which sets the button and icon state this reads.
	/// The tab art is baked into the button backgrounds, because the mod's icon glyphs are deliberately empty.
	/// </summary>
	public class BlinkReadyProductionLogic : ChromeLogic
	{
		const string Alert = "-alert";
		const int HalfPeriodMs = 400;
		const int SelectedHalfPeriodMs = 200;

		[ObjectCreator.UseCtor]
		public BlinkReadyProductionLogic(Widget widget)
		{
			Wrap(widget);
		}

		static void Wrap(Widget widget)
		{
			foreach (var child in widget.Children.ToArray())
			{
				if (child is ProductionTypeButtonWidget button && button.GetOrNull<ImageWidget>("ICON") is ImageWidget icon)
					Blink(button, icon);

				Wrap(child);
			}
		}

		static void Blink(ProductionTypeButtonWidget button, ImageWidget icon)
		{
			var selected = button.IsHighlighted;
			button.IsHighlighted = () =>
			{
				var isSelected = selected();
				if (!icon.GetImageName().EndsWith(Alert, StringComparison.Ordinal))
					return isSelected;

				return Game.RunTime / (isSelected ? SelectedHalfPeriodMs : HalfPeriodMs) % 2 == 0;
			};
		}
	}
}
