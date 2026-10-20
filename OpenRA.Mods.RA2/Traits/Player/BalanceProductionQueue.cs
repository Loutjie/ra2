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

using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.RA2.Traits
{
	[TraitLocation(SystemActors.Player)]
	[Desc("A ClassicProductionQueue that applies Red Alert 2's production speed rules when the owner has ExactPrerequisite.",
		"With it, SpeedUp/BuildTimeSpeedReduction and LowPowerModifier are replaced by RA2's MultipleFactory and low-power rules",
		"(see Ra2BuildTime). Without it, the queue behaves exactly like ClassicProductionQueue.",
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

			if (MinLowPowerProductionSpeed <= 0 || MinLowPowerProductionSpeed > MaxLowPowerProductionSpeed)
				throw new YamlException($"{nameof(BalanceProductionQueue)} on actor '{info.Name}': "
					+ "MinLowPowerProductionSpeed must be at least 1 and at most MaxLowPowerProductionSpeed.");
		}

		public override object Create(ActorInitializer init) { return new BalanceProductionQueue(init, this); }
	}

	public class BalanceProductionQueue : ClassicProductionQueue
	{
		readonly BalanceProductionQueueInfo info;
		bool? isExact;

		// Exact replaces the engine's per-item low-power slowdown, which only reads the shared, non-virtual
		// ProductionQueueInfo.LowPowerModifier. Items are created with the protected playerPower field, so in Exact
		// it is moved here (leaving null) before any item can be created, and TickInner gates progress instead.
		PowerManager exactPower;
		ProductionItem lowPowerItem;
		int lowPowerProgress;

		public BalanceProductionQueue(ActorInitializer init, BalanceProductionQueueInfo info)
			: base(init, info)
		{
			this.info = info;
		}

		/// <summary>Build time without prerequisite: the classic one. With it: RA2's multiple-factory rule on the raw time.</summary>
		public static int SelectBuildTime(bool exact, int classicTime, int rawTime, int factoryCount, BalanceProductionQueueInfo info)
		{
			return exact ? Ra2BuildTime.MultipleFactoryTicks(rawTime, factoryCount, info.MultipleFactory) : classicTime;
		}

		/// <summary>Production speed percent under RA2's low-power rule, or null to leave low power to the engine.</summary>
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

		/// <summary>Build time before any factory or power rule, as in ProductionQueue.GetBuildTime.</summary>
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
			if (!IsExact)
				return base.GetBuildTime(unit, bi);

			if (developerMode.FastBuild)
				return 0;

			return SelectBuildTime(true, 0, RawBuildTime(unit, bi), FactoryCount(bi), info);
		}

		/// <summary>Current production speed percent, or null when the engine's LowPowerModifier applies.</summary>
		protected virtual int? LowPowerSpeedPercent()
		{
			if (!IsExact)
				return null;

			return exactPower == null ? 100 : SelectLowPowerSpeedPercent(true, exactPower.PowerProvided, exactPower.PowerDrained, info);
		}

		void DetachEnginePowerGate()
		{
			// Also catches the PowerManager the base class re-reads on owner change.
			if (IsExact && playerPower != null)
			{
				exactPower = playerPower;
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
