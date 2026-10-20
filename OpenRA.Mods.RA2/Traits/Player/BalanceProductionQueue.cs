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
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.RA2.Traits
{
	[TraitLocation(SystemActors.Player)]
	[Desc("A ClassicProductionQueue that applies Red Alert 2's production speed rules when the owner has ExactPrerequisite.",
		"With it, SpeedUp/BuildTimeSpeedReduction and LowPowerModifier are replaced by RA2's MultipleFactory and low-power rules",
		"(see Ra2BuildTime). Without it, the queue behaves exactly like ClassicProductionQueue.",
		"As with SpeedUp, the factory count is taken when an item starts; low power is applied every tick.",
		"The prerequisite is queried once and cached, so it must not change during a game (e.g. LobbyPrerequisiteDropdown).")]
	public class BalanceProductionQueueInfo : ClassicProductionQueueInfo, IRulesetLoaded
	{
		[Desc("Owner prerequisite that switches this queue to Red Alert 2's production speed rules.")]
		public readonly string ExactPrerequisite = "balance.exact";

		[Desc("RA2 MultipleFactory as a percentage: the build time is multiplied by this once per extra factory of the same type.")]
		public readonly int MultipleFactory = 100;

		[Desc("RA2 MinLowPowerProductionSpeed as a percentage: the slowest production speed on low power.")]
		public readonly int MinLowPowerProductionSpeed = 50;

		[Desc("RA2 MaxLowPowerProductionSpeed as a percentage: the fastest production speed on low power.")]
		public readonly int MaxLowPowerProductionSpeed = 100;

		[Desc("RA2 LowPowerPenaltyModifier as a percentage: multiplies the power shortfall to give the speed penalty.")]
		public readonly int LowPowerPenaltyModifier = 100;

		void IRulesetLoaded<ActorInfo>.RulesetLoaded(Ruleset rules, ActorInfo info)
		{
			// Re-implementing the interface replaces ProductionQueueInfo's mapping, so keep its validation.
			RulesetLoaded(rules, info);

			if (MultipleFactory <= 0)
				throw new YamlException($"{nameof(BalanceProductionQueue)} on actor '{info.Name}': MultipleFactory must be at least 1.");

			if (MinLowPowerProductionSpeed <= 0 || MinLowPowerProductionSpeed > MaxLowPowerProductionSpeed || MaxLowPowerProductionSpeed > 100)
				throw new YamlException($"{nameof(BalanceProductionQueue)} on actor '{info.Name}': "
					+ "MinLowPowerProductionSpeed must be at least 1 and at most MaxLowPowerProductionSpeed, which must be at most 100.");

			if (LowPowerPenaltyModifier < 0)
				throw new YamlException($"{nameof(BalanceProductionQueue)} on actor '{info.Name}': LowPowerPenaltyModifier must not be negative.");
		}

		public override object Create(ActorInitializer init) { return new BalanceProductionQueue(init, this); }
	}

	public class BalanceProductionQueue : ClassicProductionQueue
	{
		readonly BalanceProductionQueueInfo info;
		bool? isExact;

		// Exact replaces the engine's per-item low-power slowdown, which only reads the shared, non-virtual
		// ProductionQueueInfo.LowPowerModifier. Items are created with the protected playerPower field, so in Exact
		// it is moved to ExactPower (leaving null) before any item can be created, and TickInner gates progress instead.
		protected PowerManager ExactPower { get; private set; }

		ProductionItem lowPowerItem;
		int lowPowerProgress;

		public BalanceProductionQueue(ActorInitializer init, BalanceProductionQueueInfo info)
			: base(init, info)
		{
			this.info = info;
		}

		/// <summary>
		/// The single build-time decision used by <see cref="GetBuildTime"/>. Without the prerequisite: the classic time,
		/// without evaluating the others. With it: RA2's multiple-factory rule on the raw time.
		/// </summary>
		public static int SelectBuildTime(bool exact, Func<int> classicTime, Func<int> rawTime, Func<int> factoryCount,
			BalanceProductionQueueInfo info)
		{
			return exact ? Ra2BuildTime.MultipleFactoryTicks(rawTime(), factoryCount(), info.MultipleFactory) : classicTime();
		}

		/// <summary>
		/// The single low-power decision used by <see cref="LowPowerSpeedPercent()"/>: production speed percent under
		/// RA2's low-power rule, or null to leave low power to the engine.
		/// </summary>
		public static int? SelectLowPowerSpeedPercent(bool exact, int powerProvided, int powerDrained, BalanceProductionQueueInfo info)
		{
			if (!exact)
				return null;

			return Ra2BuildTime.LowPowerSpeedPercent(powerProvided, powerDrained,
				info.MinLowPowerProductionSpeed, info.MaxLowPowerProductionSpeed, info.LowPowerPenaltyModifier);
		}

		protected virtual bool IsExact
		{
			get
			{
				if (isExact.HasValue)
					return isExact.Value;

				var exact = techTree != null && techTree.HasPrerequisites([info.ExactPrerequisite]);

				// TechTree only sees prerequisites of in-world actors, so do not cache an answer from world setup.
				if (Actor.IsInWorld)
					isExact = exact;

				return exact;
			}
		}

		/// <summary>Number of active factories that count towards RA2's MultipleFactory bonus for this item.</summary>
		protected virtual int FactoryCount(BuildableInfo bi)
		{
			var type = bi.BuildAtProductionType ?? Info.Type;
			return Actor.World.ActorsWithTrait<Production>()
				.Count(p => !p.Trait.IsTraitDisabled && !p.Trait.IsTraitPaused
					&& p.Actor.Owner == Actor.Owner && p.Trait.Info.Produces.Contains(type));
		}

		/// <summary>
		/// Build time before any factory or power rule. Copied from ProductionQueue.GetBuildTime (which ClassicProductionQueue
		/// overrides, so it cannot be called directly); re-check it against that method on engine bumps.
		/// </summary>
		protected virtual int RawBuildTime(ActorInfo unit, BuildableInfo bi)
		{
			var time = bi.BuildDuration;
			if (time == -1)
				time = GetProductionCost(unit);

			var modifiers = unit.TraitInfos<IProductionTimeModifierInfo>()
				.Select(t => t.GetProductionTimeModifier(techTree, Info.Type))
				.Append(bi.BuildDurationModifier)
				.Append(Info.BuildDurationModifier);

			return Common.Util.ApplyPercentageModifiers(time, modifiers);
		}

		public override int GetBuildTime(ActorInfo unit, BuildableInfo bi)
		{
			return SelectBuildTime(IsExact,
				() => base.GetBuildTime(unit, bi),
				() => developerMode.FastBuild ? 0 : RawBuildTime(unit, bi),
				() => FactoryCount(bi),
				info);
		}

		/// <summary>Current production speed percent, or null when the engine's LowPowerModifier applies.</summary>
		protected virtual int? LowPowerSpeedPercent()
		{
			// Without a PowerManager, 0 provided and 0 drained means full speed.
			return SelectLowPowerSpeedPercent(IsExact, ExactPower?.PowerProvided ?? 0, ExactPower?.PowerDrained ?? 0, info);
		}

		/// <summary>
		/// In Exact, moves playerPower to <see cref="ExactPower"/> so new items skip the engine's low-power gate.
		/// Subclasses that create ProductionItems outside ResolveOrder/Tick must call this first.
		/// </summary>
		protected void DetachEnginePowerGate()
		{
			// Also catches the PowerManager the base class re-reads on owner change.
			if (IsExact && playerPower != null)
			{
				ExactPower = playerPower;
				playerPower = null;
			}
		}

		public override void ResolveOrder(Actor self, Order order)
		{
			DetachEnginePowerGate();
			base.ResolveOrder(self, order);
		}

		protected override void Tick(Actor self)
		{
			DetachEnginePowerGate();
			base.Tick(self);
		}

		protected override void TickInner(Actor self, bool allProductionPaused)
		{
			// Forget an item that was finished or cancelled since the last tick.
			if (lowPowerItem != null && (Queue.Count == 0 || Queue[0] != lowPowerItem))
			{
				lowPowerItem = null;
				lowPowerProgress = 0;
			}

			var speed = LowPowerSpeedPercent();
			if (speed == null || Queue.Count == 0 || allProductionPaused)
			{
				base.TickInner(self, allProductionPaused);
				return;
			}

			var item = Queue[0];
			if (item != lowPowerItem)
			{
				lowPowerItem = item;
				lowPowerProgress = 0;
			}

			// Like the engine's slowdown, only a started, running item is held back; completion is never delayed.
			if (speed < 100 && item.Started && !item.Done && !item.Paused)
			{
				lowPowerProgress += speed.Value;
				if (lowPowerProgress < 100)
				{
					CancelUnbuildableItems();
					return;
				}

				lowPowerProgress -= 100;
			}

			base.TickInner(self, allProductionPaused);
		}

		public override int RemainingTimeActual(ProductionItem item)
		{
			var speed = LowPowerSpeedPercent();
			return speed == null ? base.RemainingTimeActual(item) : item.RemainingTime * 100 / speed.Value;
		}
	}
}
