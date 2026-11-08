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
	/// Makes a production tab icon blink while that tab has a finished item, such as a building ready to place.
	/// Must be listed after ClassicProductionLogic, which sets the icon's image name this wraps.
	/// </summary>
	public class BlinkReadyProductionLogic : ChromeLogic
	{
		const string Alert = "-alert";
		const int HalfPeriodMs = 400;

		[ObjectCreator.UseCtor]
		public BlinkReadyProductionLogic(Widget widget)
		{
			Wrap(widget);
		}

		static void Wrap(Widget widget)
		{
			foreach (var child in widget.Children)
			{
				if (child is ProductionTypeButtonWidget button && button.GetOrNull<ImageWidget>("ICON") is ImageWidget icon)
				{
					var inner = icon.GetImageName;
					icon.GetImageName = () => Blink(inner(), Game.RunTime);
				}

				Wrap(child);
			}
		}

		// The off phase uses the disabled image: the alert image alone can look like the normal one.
		public static string Blink(string name, long runTimeMs)
		{
			if (!name.EndsWith(Alert, StringComparison.Ordinal) || runTimeMs / HalfPeriodMs % 2 == 0)
				return name;

			return name[..^Alert.Length] + "-disabled";
		}
	}
}
