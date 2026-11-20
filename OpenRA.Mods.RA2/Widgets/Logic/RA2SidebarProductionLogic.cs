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
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Widgets;

namespace OpenRA.Mods.RA2.Widgets.Logic
{
	/// <summary>
	/// Lays out RA2's full-height sidebar and drives its production tabs. The cameo strip gets as many rows as fit
	/// between the top of the production area and the bottom of the window (less the bottom cap), like RA2 adds strip
	/// rows on taller screens; whatever is left under the cap is covered by the filler art. Each tab selects a
	/// production group, and <see cref="GroupedProductionPaletteWidget"/> shows every queue in it.
	/// Expects PRODUCTION_PALETTE, PRODUCTION_TYPES, PRODUCTION_TICKER and PALETTE_BACKGROUND with ROW_TEMPLATE,
	/// BOTTOM_CAP and FILLER; SCROLL_UP_BUTTON and SCROLL_DOWN_BUTTON are optional.
	/// </summary>
	public class RA2SidebarProductionLogic : ChromeLogic
	{
		readonly GroupedProductionPaletteWidget palette;
		readonly World world;
		readonly WorldRenderer worldRenderer;

		[ObjectCreator.UseCtor]
		public RA2SidebarProductionLogic(Widget widget, World world, WorldRenderer worldRenderer)
		{
			this.world = world;
			this.worldRenderer = worldRenderer;
			palette = widget.Get<GroupedProductionPaletteWidget>("PRODUCTION_PALETTE");

			var background = widget.Get("PALETTE_BACKGROUND");
			var rowTemplate = background.Get("ROW_TEMPLATE");
			var bottomCap = background.Get("BOTTOM_CAP");
			var filler = background.Get("FILLER");

			var rowHeight = rowTemplate.Bounds.Height;

			// The production container sits directly in the full-screen root, so its Y is its screen position.
			var top = widget.Bounds.Y;
			var rows = (Game.Renderer.Resolution.Height - top - bottomCap.Bounds.Height) / rowHeight;
			rows = Math.Min(Math.Max(rows, palette.MinimumRows), palette.MaximumRows);
			palette.MaxIconRowOffset = rows;

			background.RemoveChildren();
			for (var i = 0; i < rows; i++)
			{
				var row = rowTemplate.Clone();
				row.Bounds.Y = i * rowHeight;
				background.AddChild(row);
			}

			bottomCap.Bounds.Y = rows * rowHeight;
			background.AddChild(bottomCap);

			filler.Bounds.Y = bottomCap.Bounds.Y + bottomCap.Bounds.Height;
			filler.Bounds.Height = Math.Max(0, Game.Renderer.Resolution.Height - top - filler.Bounds.Y);
			background.AddChild(filler);

			var typesContainer = widget.Get("PRODUCTION_TYPES");
			foreach (var button in typesContainer.Children.OfType<ProductionTypeButtonWidget>())
				SetupTab(button);

			widget.Get<LogicTickerWidget>("PRODUCTION_TICKER").OnTick = () =>
			{
				if (palette.CurrentQueue != null && palette.GroupIconCount > 0)
					return;

				// Select the first tab with something to build
				var first = typesContainer.Children.OfType<ProductionTypeButtonWidget>().FirstOrDefault(b => !b.IsDisabled());
				first?.OnClick();
			};

			var scrollDown = widget.GetOrNull<ButtonWidget>("SCROLL_DOWN_BUTTON");
			if (scrollDown != null)
			{
				scrollDown.OnClick = palette.ScrollDown;
				scrollDown.IsDisabled = () => !palette.CanScrollDown;
			}

			var scrollUp = widget.GetOrNull<ButtonWidget>("SCROLL_UP_BUTTON");
			if (scrollUp != null)
			{
				scrollUp.OnClick = palette.ScrollUp;
				scrollUp.IsDisabled = () => !palette.CanScrollUp;
			}
		}

		void SetupTab(ProductionTypeButtonWidget button)
		{
			// Classic production queues are created at game start and never change.
			var queues = world.LocalPlayer.PlayerActor.TraitsImplementing<ProductionQueue>()
				.Where(q => GroupedProductionPaletteWidget.GroupOf(q) == button.ProductionGroup)
				.ToArray();

			void SelectTab()
			{
				palette.CurrentQueue = queues.FirstOrDefault(q => q.Enabled) ?? queues.FirstOrDefault();

				// The row offset may be invalid for the new tab
				palette.ScrollToTop();

				// Pick up a completed building, if there is one, so it can be placed
				var (queue, item) = queues.Select(q => (q, q.AllQueued().FirstOrDefault(i => i.Done))).FirstOrDefault(d => d.Item2 != null);
				if (item != null && world.Map.Rules.Actors[item.Item].HasTraitInfo<BuildingInfo>())
					world.OrderGenerator = new PlaceBuildingOrderGenerator(queue, item.Item, worldRenderer);
			}

			bool Ready() => queues.Any(q => q.AllQueued().Any(i => i.Done));

			button.IsDisabled = () => !queues.Any(q => q.AnyItemsToBuild());
			button.OnMouseUp = _ => SelectTab();
			button.OnKeyPress = _ => SelectTab();
			button.OnClick = SelectTab;

			// RA2 flashes the tab while it holds a finished building.
			button.IsHighlighted = () => queues.Contains(palette.CurrentQueue) ^ (Ready() && Game.RunTime / 400 % 2 == 0);
		}
	}
}
