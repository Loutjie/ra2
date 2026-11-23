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
using OpenRA.Mods.RA2.Graphics;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.RA2.Widgets.Logic
{
	/// <summary>
	/// Moves and resizes a widget for the local faction, because the Allied and Soviet sidebar art place their
	/// buttons differently. Each argument is a faction suffix with "X, Y, Width, Height" relative to the parent:
	///   Logic: FactionBoundsLogic
	///     soviets: 33, 7, 52, 32
	/// Factions without an entry keep the bounds from the layout.
	/// </summary>
	public class FactionBoundsLogic : ChromeLogic
	{
		[ObjectCreator.UseCtor]
		public FactionBoundsLogic(Widget widget, World world, Dictionary<string, MiniYaml> logicArgs)
		{
			var suffix = RA2ChromeArt.FactionSuffix(world);
			if (suffix == null || !logicArgs.TryGetValue(suffix, out var yaml))
				return;

			var r = FieldLoader.GetValue<Rectangle>(suffix, yaml.Value);
			widget.Bounds = new WidgetBounds(r.X, r.Y, r.Width, r.Height);
		}
	}
}
