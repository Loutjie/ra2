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
using System.Globalization;
using System.Linq;
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Orders;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Mods.Common.Widgets;
using OpenRA.Network;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.RA2.Widgets
{
	/// <summary>
	/// A production palette that shows every queue in the current queue's production group at once, as RA2's
	/// fourth tab shows vehicles, aircraft and ships together while each keeps its own factory and build queue.
	/// <see cref="ProductionPaletteWidget.CurrentQueue"/> picks the group (so ProductionQueueFromSelection still
	/// works); each icon builds from its own queue. Bulk (Chrome Supply Drop style) queues are not supported.
	/// </summary>
	public class GroupedProductionPaletteWidget : ProductionPaletteWidget
	{
		readonly ModData modData;
		readonly OrderManager orderManager;
		readonly WorldRenderer worldRenderer;

		Dictionary<Rectangle, ProductionIcon> groupIcons = [];
		Rectangle groupEventBounds = Rectangle.Empty;
		HotkeyReference[] groupHotkeys;
		Animation clock;
		Animation cantBuild;
		SpriteFont overlayFont;
		SpriteFont symbolFont;
		Vector2 iconOffset, holdOffset, readyOffset, timeOffset, infiniteOffset;
		ProductionQueue[] playerQueues = [];
		Player cachedOwner;
		IProductionIconOverlay[] overlays = [];

		/// <summary>Icons for every buildable item in the group, before scrolling.</summary>
		public int GroupIconCount { get; private set; }

		public new ProductionIcon TooltipIcon { get; private set; }

		[ObjectCreator.UseCtor]
		public GroupedProductionPaletteWidget(ModData modData, OrderManager orderManager, World world, WorldRenderer worldRenderer)
			: base(modData, orderManager, world, worldRenderer)
		{
			this.modData = modData;
			this.orderManager = orderManager;
			this.worldRenderer = worldRenderer;
			GetTooltipIcon = () => TooltipIcon;
		}

		public override void Initialize(WidgetArgs args)
		{
			base.Initialize(args);

			clock = new Animation(World, ClockAnimation);
			cantBuild = new Animation(World, NotBuildableAnimation);
			cantBuild.PlayFetchIndex(NotBuildableSequence, () => 0);
			groupHotkeys = Exts.MakeArray(HotkeyCount,
				i => modData.Hotkeys[HotkeyPrefix + (i + 1).ToStringInvariant("D2")]);

			overlayFont = Game.Renderer.Fonts[OverlayFont];
			Game.Renderer.Fonts.TryGetValue(SymbolsFont, out symbolFont);

			// The base class has already translated ReadyText and HoldText.
			iconOffset = 0.5f * IconSize.ToVector2() + IconSpriteOffset.ToVector2();
			holdOffset = iconOffset - overlayFont.Measure(HoldText).ToVector2() / 2;
			readyOffset = iconOffset - overlayFont.Measure(ReadyText).ToVector2() / 2;

			if (ChromeMetrics.TryGet("InfiniteOffset", out infiniteOffset))
				infiniteOffset += QueuedOffset;
			else
				infiniteOffset = QueuedOffset;
		}

		public static string GroupOf(ProductionQueue queue) => queue.Info.Group ?? queue.Info.Type;

		/// <summary>The queues shown: those in the current queue's group that have a factory, in display order.</summary>
		public IEnumerable<ProductionQueue> GroupQueues
		{
			get
			{
				if (CurrentQueue == null)
					return [];

				var group = GroupOf(CurrentQueue);
				return playerQueues
					.Where(q => GroupOf(q) == group && q.Actor.IsInWorld && q.MostLikelyProducer().Trait != null)
					.OrderBy(q => q.Info.DisplayOrder);
			}
		}

		IEnumerable<(ProductionQueue Queue, ActorInfo Item)> GroupBuildables =>
			GroupQueues.SelectMany(q => q.AllItems()
				.OrderBy(a => a.TraitInfo<BuildableInfo>().BuildPaletteOrder)
				.Select(a => (q, a)));

		public new bool CanScrollDown => IconRowOffset < (GroupIconCount + Columns - 1) / Columns - MaxIconRowOffset;

		public new void ScrollDown()
		{
			if (CanScrollDown)
				IconRowOffset++;
		}

		public new void ScrollUp()
		{
			if (CanScrollUp)
				IconRowOffset--;
		}

		public override Rectangle EventBounds => groupEventBounds;

		public override void Tick()
		{
			if (CurrentQueue != null && !CurrentQueue.Actor.IsInWorld)
				CurrentQueue = null;

			var owner = CurrentQueue?.Actor.Owner ?? World.LocalPlayer;
			if (owner != cachedOwner)
			{
				cachedOwner = owner;
				playerQueues = owner?.PlayerActor.TraitsImplementing<ProductionQueue>().ToArray() ?? [];
				overlays = owner?.PlayerActor.TraitsImplementing<IProductionIconOverlay>().ToArray() ?? [];
			}

			GroupIconCount = GroupBuildables.Count();
			IconRowOffset = IconRowOffset.Clamp(0, System.Math.Max(0, (GroupIconCount + Columns - 1) / Columns - MaxIconRowOffset));
			RefreshGroupIcons();
		}

		void RefreshGroupIcons()
		{
			groupIcons = [];
			var displayed = 0;
			var rb = RenderBounds;

			foreach (var (queue, item) in GroupBuildables.Skip(IconRowOffset * Columns).Take(MaxIconRowOffset * Columns))
			{
				var producer = queue.MostLikelyProducer();
				var x = displayed % Columns;
				var y = displayed / Columns;
				var rect = new Rectangle(rb.X + x * (IconSize.X + IconMargin.X), rb.Y + y * (IconSize.Y + IconMargin.Y), IconSize.X, IconSize.Y);

				var rsi = item.TraitInfo<RenderSpritesInfo>();
				var icon = new Animation(World, rsi.GetImage(item, producer.Trait.Faction));
				var bi = item.TraitInfo<BuildableInfo>();
				icon.Play(bi.Icon);

				var palette = bi.IconPaletteIsPlayerPalette ? bi.IconPalette + producer.Actor.Owner.InternalName : bi.IconPalette;

				groupIcons.Add(rect, new ProductionIcon()
				{
					Actor = item,
					Name = item.Name,
					Hotkey = displayed < HotkeyCount ? groupHotkeys[displayed] : null,
					Sprite = icon.Image,
					Palette = worldRenderer.Palette(palette),
					IconClockPalette = worldRenderer.Palette(ClockPalette),
					IconDarkenPalette = worldRenderer.Palette(NotBuildablePalette),
					Pos = rect.Location.ToVector2(),
					Queued = queue.AllQueued().Where(a => a.Item == item.Name).ToList(),
					ProductionQueue = queue
				});

				displayed++;
			}

			groupEventBounds = groupIcons.Keys.Union();
		}

		ProductionIcon IconAt(int2 location) => groupIcons.Where(i => i.Key.Contains(location)).Select(i => i.Value).FirstOrDefault();

		void PlaySound(string sound) => Game.Sound.PlayNotification(World.Map.Rules, World.LocalPlayer, "Sounds", sound, null);

		void Notify(string speech, string text)
		{
			Game.Sound.PlayNotification(World.Map.Rules, World.LocalPlayer, "Speech", speech, World.LocalPlayer.Faction.InternalName);
			TextNotificationsManager.AddTransientLine(World.LocalPlayer, text);
		}

		public override bool HandleMouseInput(MouseInput mi)
		{
			var icon = IconAt(mi.Location);

			if (mi.Event == MouseInputEvent.Move)
				TooltipIcon = icon;

			if (mi.Event == MouseInputEvent.Scroll)
			{
				if (mi.Delta.Y < 0 && CanScrollDown)
				{
					ScrollDown();
					Ui.ResetTooltips();
					PlaySound(ClickSound);
				}
				else if (mi.Delta.Y > 0 && CanScrollUp)
				{
					ScrollUp();
					Ui.ResetTooltips();
					PlaySound(ClickSound);
				}
			}

			if (icon == null)
				return false;

			// Eat mouse-up events
			if (mi.Event != MouseInputEvent.Down)
				return true;

			return HandleEvent(icon, mi.Button, mi.Modifiers);
		}

		bool PickUpCompletedBuilding(ProductionQueue queue, ProductionItem item)
		{
			if (item == null || !item.Done || !World.Map.Rules.Actors[item.Item].HasTraitInfo<BuildingInfo>())
				return false;

			World.OrderGenerator = new PlaceBuildingOrderGenerator(queue, item.Item, worldRenderer);
			return true;
		}

		bool HandleLeftClick(ProductionItem item, ProductionIcon icon, int handleCount, Modifiers modifiers)
		{
			var queue = icon.ProductionQueue;
			if (PickUpCompletedBuilding(queue, item))
			{
				PlaySound(ClickSound);
				return true;
			}

			if (item != null && item.Paused)
			{
				// Resume a paused item
				PlaySound(ClickSound);
				Notify(queue.Info.QueuedAudio, queue.Info.QueuedTextNotification);
				World.IssueOrder(Order.PauseProduction(queue.Actor, icon.Name, false));
				return true;
			}

			var buildable = queue.BuildableItems().FirstOrDefault(a => a.Name == icon.Name);
			if (buildable == null)
				return false;

			if (queue.Info.PayUpFront &&
				queue.GetProductionCost(buildable) > queue.Actor.Owner.PlayerActor.Trait<PlayerResources>().GetCashAndResources())
				return false;

			PlaySound(ClickSound);

			// Queue a new item
			var canQueue = queue.CanQueue(buildable, out var notification, out var textNotification);
			if (!queue.AllQueued().Any())
				Notify(notification, textNotification);

			if (!canQueue)
				return false;

			World.IssueOrder(Order.StartProduction(queue.Actor, icon.Name, handleCount, !modifiers.HasModifier(Modifiers.Ctrl)));
			return true;
		}

		bool HandleRightClick(ProductionItem item, ProductionIcon icon, int handleCount)
		{
			if (item == null)
				return false;

			var queue = icon.ProductionQueue;
			PlaySound(ClickSound);

			if (queue.Info.DisallowPaused || item.Paused || item.Done || item.TotalCost == item.RemainingCost || !item.Started)
			{
				// Instantly cancel items that haven't started, have finished, or if the queue doesn't support pausing
				Notify(queue.Info.CancelledAudio, queue.Info.CancelledTextNotification);
				World.IssueOrder(Order.CancelProduction(queue.Actor, icon.Name, handleCount));
			}
			else
			{
				// Pause an existing item
				Notify(queue.Info.OnHoldAudio, queue.Info.OnHoldTextNotification);
				World.IssueOrder(Order.PauseProduction(queue.Actor, icon.Name, true));
			}

			return true;
		}

		bool HandleMiddleClick(ProductionItem item, ProductionIcon icon, int handleCount)
		{
			if (item == null)
				return false;

			// Directly cancel, skipping "on-hold"
			PlaySound(ClickSound);
			Notify(icon.ProductionQueue.Info.CancelledAudio, icon.ProductionQueue.Info.CancelledTextNotification);
			World.IssueOrder(Order.CancelProduction(icon.ProductionQueue.Actor, icon.Name, handleCount));
			return true;
		}

		bool HandleEvent(ProductionIcon icon, MouseButton btn, Modifiers modifiers)
		{
			var startCount = modifiers.HasModifier(Modifiers.Shift) ? 5 : 1;
			var cancelCount = modifiers.HasModifier(Modifiers.Ctrl) ? icon.ProductionQueue.AllQueued().Count() : startCount;
			var item = icon.Queued.FirstOrDefault();
			var handled = btn == MouseButton.Left ? HandleLeftClick(item, icon, startCount, modifiers)
				: btn == MouseButton.Right ? HandleRightClick(item, icon, cancelCount)
				: btn == MouseButton.Middle && HandleMiddleClick(item, icon, cancelCount);

			if (!handled)
				PlaySound(ClickDisabledSound);

			return true;
		}

		public override bool HandleKeyPress(KeyInput e)
		{
			if (e.Event == KeyInputEvent.Up || CurrentQueue == null)
				return false;

			if (SelectProductionBuildingHotkey.IsActivatedBy(e))
				return SelectProductionBuilding();

			var batchModifiers = e.Modifiers.HasModifier(Modifiers.Shift) ? Modifiers.Shift : Modifiers.None;

			// HACK: enable production if the shift key is pressed
			e.Modifiers &= ~Modifiers.Shift;
			var toBuild = groupIcons.Values.FirstOrDefault(i => i.Hotkey != null && i.Hotkey.IsActivatedBy(e));
			return toBuild != null && HandleEvent(toBuild, MouseButton.Left, batchModifiers);
		}

		bool SelectProductionBuilding()
		{
			var facility = CurrentQueue.MostLikelyProducer().Actor;
			if (facility == null || facility.OccupiesSpace == null)
				return true;

			var selection = World.Selection;
			if (selection.Actors.Count == 1 && selection.Contains(facility))
				worldRenderer.Viewport.Center(selection.Actors);
			else
				selection.Combine(World, [facility], false, true);

			Game.Sound.PlayNotification(World.Map.Rules, null, "Sounds", ClickSound, null);
			return true;
		}

		public override void Draw()
		{
			timeOffset = iconOffset - overlayFont.Measure(WidgetUtils.FormatTime(0, World.Timestep)).ToVector2() / 2;

			// Icons
			Game.Renderer.EnableAntialiasingFilter();
			foreach (var icon in groupIcons.Values)
			{
				WidgetUtils.DrawSpriteCentered(icon.Sprite, icon.Palette, icon.Pos + iconOffset);

				foreach (var pio in overlays.Where(p => p.IsOverlayActive(icon.Actor)))
					WidgetUtils.DrawSpriteCentered(pio.Sprite, worldRenderer.Palette(pio.Palette), icon.Pos + iconOffset + pio.Offset(IconSize.ToVector2()));

				// Build progress
				if (icon.Queued.Count > 0)
				{
					var first = icon.Queued[0];
					clock.PlayFetchIndex(ClockSequence,
						() => (first.TotalTime - first.RemainingTime)
							* (clock.CurrentSequence.Length - 1) / first.TotalTime);
					clock.Tick();

					WidgetUtils.DrawSpriteCentered(clock.Image, icon.IconClockPalette, icon.Pos + iconOffset);
				}
				else if (!icon.ProductionQueue.BuildableItems().Any(a => a.Name == icon.Name))
					WidgetUtils.DrawSpriteCentered(cantBuild.Image, icon.IconDarkenPalette, icon.Pos + iconOffset);
			}

			Game.Renderer.DisableAntialiasingFilter();

			// Overlays
			foreach (var icon in groupIcons.Values)
			{
				var total = icon.Queued.Count;
				if (total == 0)
					continue;

				var first = icon.Queued[0];
				var waiting = !icon.ProductionQueue.IsProducing(first) && !first.Done;
				if (first.Done)
				{
					if (ReadyTextStyle == ReadyTextStyleOptions.Solid || orderManager.LocalFrameNumber * World.Timestep / 360 % 2 == 0)
						overlayFont.DrawTextWithContrast(ReadyText, icon.Pos + readyOffset, TextColor, Color.Black, 1);
					else if (ReadyTextStyle == ReadyTextStyleOptions.AlternatingColor)
						overlayFont.DrawTextWithContrast(ReadyText, icon.Pos + readyOffset, ReadyTextAltColor, Color.Black, 1);
				}
				else if (first.Paused)
					overlayFont.DrawTextWithContrast(HoldText, icon.Pos + holdOffset, TextColor, Color.Black, 1);
				else if (!waiting && DrawTime)
					overlayFont.DrawTextWithContrast(WidgetUtils.FormatTime(first.Queue.RemainingTimeActual(first), World.Timestep),
						icon.Pos + timeOffset, TextColor, Color.Black, 1);

				if (first.Infinite && symbolFont != null)
					symbolFont.DrawTextWithContrast(InfiniteSymbol, icon.Pos + infiniteOffset, TextColor, Color.Black, 1);
				else if (total > 1 || waiting)
				{
					var text = total.ToString(NumberFormatInfo.CurrentInfo);
					var pos = QueuedOffset;
					if (QueuedTextAlign != TextAlign.Left)
					{
						var size = overlayFont.Measure(text);
						pos = QueuedTextAlign == TextAlign.Center ?
							new Vector2(QueuedOffset.X - size.X / 2, QueuedOffset.Y) :
							new Vector2(QueuedOffset.X - size.X, QueuedOffset.Y);
					}

					overlayFont.DrawTextWithContrast(text, icon.Pos + pos, TextColor, Color.Black, 1);
				}
			}
		}

		public override string GetCursor(int2 pos)
		{
			// The base class only knows its own (unused) icons, so take the default cursor directly.
			return IconAt(pos) != null ? ChromeMetrics.Get<string>("DefaultCursor") : null;
		}
	}
}
