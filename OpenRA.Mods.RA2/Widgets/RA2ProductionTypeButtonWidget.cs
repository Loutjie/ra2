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
using OpenRA.Primitives;

namespace OpenRA.Mods.RA2.Widgets
{
	/// <summary>
	/// A production tab that keeps the RA2 art for selected and unselected tabs and draws a line along its bottom edge
	/// while the tab has a finished item, such as a building ready to place.
	/// The tab art has the icon baked in, so the stock OpenRA icon colour change cannot be used.
	/// </summary>
	public class RA2ProductionTypeButtonWidget : ProductionTypeButtonWidget
	{
		const string ReadySuffix = "-alert";

		[Desc("Colour of the line shown while the tab has a finished item.")]
		public readonly Color ReadyLineColor = Color.FromArgb(64, 255, 64);

		[Desc("Colour of the outline around the ready line.")]
		public readonly Color ReadyLineOutlineColor = Color.Black;

		[Desc("Height of the ready line in pixels, without the outline.")]
		public readonly int ReadyLineHeight = 2;

		[Desc("Distance in pixels from the bottom of the tab to the bottom of the ready line.")]
		public readonly int ReadyLineBottom = 3;

		[Desc("Distance in pixels from the left and right sides of the tab to the ready line.")]
		public readonly int ReadyLineInset = 5;

		ImageWidget icon;

		[ObjectCreator.UseCtor]
		public RA2ProductionTypeButtonWidget(ModData modData, World world)
			: base(modData, world) { }

		protected RA2ProductionTypeButtonWidget(RA2ProductionTypeButtonWidget other)
			: base(other)
		{
			ReadyLineColor = other.ReadyLineColor;
			ReadyLineOutlineColor = other.ReadyLineOutlineColor;
			ReadyLineHeight = other.ReadyLineHeight;
			ReadyLineBottom = other.ReadyLineBottom;
			ReadyLineInset = other.ReadyLineInset;
		}

		public override WorldButtonWidget Clone() { return new RA2ProductionTypeButtonWidget(this); }

		// ClassicProductionLogic names the icon "<name>-alert" while the queue has a finished item.
		bool IsReady()
		{
			icon ??= GetOrNull<ImageWidget>("ICON");
			var name = icon?.GetImageName();
			return name != null && name.EndsWith(ReadySuffix, StringComparison.Ordinal);
		}

		public override void Draw()
		{
			base.Draw();
			if (IsDisabled() || !IsReady())
				return;

			var rb = RenderBounds;
			var line = new Rectangle(
				rb.X + ReadyLineInset,
				rb.Bottom - ReadyLineBottom - ReadyLineHeight,
				rb.Width - 2 * ReadyLineInset,
				ReadyLineHeight);

			WidgetUtils.FillRectWithColor(new Rectangle(line.X - 1, line.Y - 1, line.Width + 2, line.Height + 2), ReadyLineOutlineColor);
			WidgetUtils.FillRectWithColor(line, ReadyLineColor);
		}
	}
}
