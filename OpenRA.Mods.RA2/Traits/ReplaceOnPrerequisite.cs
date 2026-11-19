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
using System.Linq;
using System.Runtime.CompilerServices;
using OpenRA.Mods.Common;
using OpenRA.Mods.Common.Activities;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.RA2.Traits
{
	public static class ReplaceDecision
	{
		/// <summary>
		/// Every player, including non-combatant owners such as Neutral, has a Player actor with the lobby dropdown
		/// and so receives its prerequisite: this non-combatant guard is what keeps neutral actors (e.g. tech
		/// buildings) at their base type.
		/// </summary>
		public static bool ShouldReplace(bool ownerIsNonCombatant, bool ownerHasPrerequisite)
		{
			return !ownerIsNonCombatant && ownerHasPrerequisite;
		}

		/// <summary>
		/// Replacement is only allowed while the actor is still being created: during its creation tick,
		/// or for actors spawned while the map loads (tick 0), also during the first tick, whose frame-end runs the
		/// load-time tasks (e.g. initial cargo notifications). This keeps actors that are stored for later use
		/// (spawner children) and actors that change owner or leave a transport much later untouched.
		/// </summary>
		public static bool InCreationWindow(int createdTick, int currentTick)
		{
			return currentTick == createdTick || (createdTick == 0 && currentTick == 1);
		}

		/// <summary>
		/// A passenger can only be replaced inside its transport by another passenger.
		/// Returns a description of the problem, or null when the replacement is valid.
		/// </summary>
		public static string ValidateIntoActor(string actor, string intoActor, bool baseIsPassenger, bool intoIsPassenger)
		{
			if (baseIsPassenger && !intoIsPassenger)
				return $"'{actor}' has Passenger but its replacement '{intoActor}' does not, so it could not be replaced inside a transport.";

			return null;
		}
	}

	[Desc("Replaces this actor with another actor type when it is created for an owner that has the given prerequisite.",
		"Covers actors spawned outside the production queue: map actors, starting units, crates, paradrops and initial cargo.",
		"Evaluated only while the actor is being created: later owner changes (capture, mind control) never replace it.",
		"The prerequisite is queried once per owner and cached, so it must not change during a game (e.g. LobbyPrerequisiteDropdown).",
		"Do not add this to actors that their creator configures after creation (e.g. support power aircraft or spawner children).",
		"Actors spawned while the map loads are replaced before the first tick when the world has ReplaceOnPrerequisiteAtLoad.")]
	public class ReplaceOnPrerequisiteInfo : TraitInfo, IRulesetLoaded
	{
		[FieldLoader.Require]
		[Desc("Prerequisite the owner must have for the actor to be replaced.")]
		public readonly string Prerequisite = null;

		[ActorReference]
		[FieldLoader.Require]
		[Desc("Actor type to replace this actor with.")]
		public readonly string IntoActor = null;

		// TraitInfos are shared between games; Player instances are not, so this cannot leak between worlds.
		readonly ConditionalWeakTable<Player, StrongBox<bool>> ownerHasPrerequisite = [];

		public bool OwnerHasPrerequisite(Player owner)
		{
			if (!ownerHasPrerequisite.TryGetValue(owner, out var result))
			{
				var techTree = owner.PlayerActor.TraitOrDefault<TechTree>();
				result = new StrongBox<bool>(techTree != null && techTree.HasPrerequisites([Prerequisite]));
				ownerHasPrerequisite.AddOrUpdate(owner, result);
			}

			return result.Value;
		}

		void IRulesetLoaded<ActorInfo>.RulesetLoaded(Ruleset rules, ActorInfo info)
		{
			if (!rules.Actors.TryGetValue(IntoActor, out var intoInfo))
				throw new YamlException($"{nameof(ReplaceOnPrerequisite)} on actor '{info.Name}': IntoActor '{IntoActor}' is not defined.");

			var error = ReplaceDecision.ValidateIntoActor(info.Name, IntoActor,
				info.HasTraitInfo<PassengerInfo>(), intoInfo.HasTraitInfo<PassengerInfo>());

			if (error != null)
				throw new YamlException($"{nameof(ReplaceOnPrerequisite)} on actor '{info.Name}': {error}");
		}

		public override object Create(ActorInitializer init) { return new ReplaceOnPrerequisite(init, this); }
	}

	public class ReplaceOnPrerequisite : INotifyCreated, INotifyAddedToWorld, INotifyEnteredCargo
	{
		readonly ReplaceOnPrerequisiteInfo info;

		// Kept only while a replacement is pending, so the replacement can receive the creation-time inits.
		ActorInitializer creationInit;

		bool pending;
		bool queued;
		int createdTick;

		public ReplaceOnPrerequisite(ActorInitializer init, ReplaceOnPrerequisiteInfo info)
		{
			this.info = info;
			creationInit = init;
		}

		void INotifyCreated.Created(Actor self)
		{
			// The replacement carries a marker so that an IntoActor which also has this trait can never loop.
			var owner = self.Owner;
			pending = !creationInit.Contains<ReplacedOnPrerequisiteInit>(null) && self.Info.Name != info.IntoActor
				&& ReplaceDecision.ShouldReplace(owner.NonCombatant, !owner.NonCombatant && info.OwnerHasPrerequisite(owner));

			if (pending)
				createdTick = self.World.WorldTick;
			else
				creationInit = null;
		}

		bool InCreationWindow(Actor self)
		{
			return ReplaceDecision.InCreationWindow(createdTick, self.World.WorldTick);
		}

		void INotifyAddedToWorld.AddedToWorld(Actor self)
		{
			if (!pending || queued)
				return;

			if (!InCreationWindow(self))
			{
				pending = false;
				creationInit = null;
				return;
			}

			queued = true;

			// Actors spawned while the map loads are replaced once the map actors and starting units are all spawned,
			// before the first tick, so that first-tick orders (e.g. the AI deploying its MCV in place) reach the replacement.
			var atLoad = self.World.WorldActor.TraitOrDefault<ReplaceOnPrerequisiteAtLoad>();
			if (atLoad != null && atLoad.TryDefer(w => ReplacePendingInWorld(self, w)))
				return;

			// Otherwise defer until the creator has finished setting the actor up (e.g. crates set the sub-cell after creation).
			self.World.AddFrameEndTask(w => ReplacePendingInWorld(self, w));
		}

		void ReplacePendingInWorld(Actor self, World w)
		{
			queued = false;
			if (!pending || self.IsDead || self.WillDispose)
				return;

			// Picked up by a transport in the meantime: OnEnteredCargo will handle it.
			if (!self.IsInWorld)
				return;

			pending = false;
			ReplaceInWorld(self, w);
		}

		void INotifyEnteredCargo.OnEnteredCargo(Actor self, Actor cargo)
		{
			if (!pending || queued)
				return;

			if (!InCreationWindow(self))
			{
				pending = false;
				creationInit = null;
				return;
			}

			queued = true;
			self.World.AddFrameEndTask(w =>
			{
				queued = false;
				if (!pending || self.IsDead || self.WillDispose)
					return;

				// The transport is itself being replaced and hands its passengers to its replacement,
				// which notifies OnEnteredCargo again once it is initialised.
				if (cargo.IsDead || cargo.WillDispose || self.TraitOrDefault<Passenger>()?.Transport != cargo)
					return;

				pending = false;
				ReplaceInCargo(self, cargo, w);
			});
		}

		TypeDictionary BaseInit(Actor self)
		{
			var init = new TypeDictionary
			{
				new OwnerInit(self.Owner),
				new ReplacedOnPrerequisiteInit(),
			};

			// Creation-time flags and settings; positional and state inits (location, facing, health, stance, cargo, ...)
			// are rebuilt from the live actor instead.
			AddCreationInit<FactionInit>(init);
			AddCreationInit<SkipMakeAnimsInit>(init);
			AddCreationInit<SpawnedByMapInit>(init);
			AddCreationInit<HiddenUnderFogInit>(init);
			AddCreationInit<DeployStateInit>(init);
			AddCreationInit<RallyPointInit>(init);
			AddCreationInit<CreationActivityDelayInit>(init);

			var health = self.TraitOrDefault<IHealth>();
			if (health != null)
			{
				// Cast to long to avoid overflow when multiplying by the health
				init.Add(new HealthInit((int)(health.HP * 100L / health.MaxHP)));
			}

			var autoTarget = self.TraitOrDefault<AutoTarget>();
			if (autoTarget != null)
				init.Add(new StanceInit(autoTarget.Info, autoTarget.Stance));

			foreach (var modifier in self.TraitsImplementing<ITransformActorInitModifier>())
				modifier.ModifyTransformActorInit(self, init);

			// A FreeActor on this actor has already queued its free unit when it was created (and that unit is replaced
			// in turn if needed): the replacement must not spawn a second one.
			if (self.Info.HasTraitInfo<FreeActorInfo>())
				foreach (var freeActor in self.World.Map.Rules.Actors[info.IntoActor].TraitInfos<FreeActorInfo>())
					init.Add(new FreeActorInit(freeActor, false));

			return init;
		}

		void AddCreationInit<T>(TypeDictionary init) where T : ActorInit
		{
			var value = creationInit.GetOrDefault<T>(null);
			if (value != null)
				init.Add(value);
		}

		void ReplaceInWorld(Actor self, World w)
		{
			var init = BaseInit(self);
			init.Add(new LocationInit(self.Location));

			var facing = self.TraitOrDefault<IFacing>();
			if (facing != null)
				init.Add(new FacingInit(facing.Facing));

			foreach (var turret in self.TraitsImplementing<Turreted>())
				turret.ModifyDeathActorInit(self, init);

			var mobile = self.TraitOrDefault<Mobile>();
			if (mobile != null)
			{
				init.Add(new SubCellInit(mobile.ToSubCell));

				// Paradropped or otherwise airborne units continue falling from where they are.
				if (self.World.Map.DistanceAboveTerrain(self.CenterPosition) > WDist.Zero)
					init.Add(new CenterPositionInit(self.CenterPosition));
			}
			else if (self.TraitOrDefault<Aircraft>() != null)
				init.Add(new CenterPositionInit(self.CenterPosition));

			var orders = OrdersForQueuedTargets(self);
			var selected = w.Selection.Contains(self);
			var controlGroup = w.ControlGroups.GetControlGroupForActor(self);

			ReleasePassengers(self);

			// Leave the world now, so that the replacement does not share cells (building influence) with this actor.
			w.Remove(self);
			self.Dispose();

			var replacement = w.CreateActor(info.IntoActor, init);
			self.ReplacedByActor = replacement;

			foreach (var (orderString, target) in orders)
			{
				var order = new Order(orderString, replacement, target, true);
				foreach (var t in replacement.TraitsImplementing<IResolveOrder>())
					t.ResolveOrder(replacement, order);
			}

			if (selected)
				w.Selection.Add(replacement);

			if (controlGroup.HasValue)
				w.ControlGroups.AddToControlGroup(replacement, controlGroup.Value);

			UpdateNamedMapActor(self, replacement, w);
		}

		void ReplaceInCargo(Actor self, Actor transport, World w)
		{
			var cargo = transport.TraitsImplementing<Cargo>().FirstOrDefault(c => c.Passengers.Contains(self));
			if (cargo == null || !w.Map.Rules.Actors[info.IntoActor].HasTraitInfo<PassengerInfo>())
				return;

			var init = BaseInit(self);
			ReleasePassengers(self);

			cargo.Unload(transport, self);
			self.Dispose();

			var replacement = w.CreateActor(false, info.IntoActor, init);
			self.ReplacedByActor = replacement;
			cargo.Load(transport, replacement);
		}

		/// <summary>
		/// Passengers are handed over to the replacement through RuntimeCargoInit (Cargo's ITransformActorInitModifier):
		/// take them out so that disposing this actor does not dispose them as well.
		/// </summary>
		static void ReleasePassengers(Actor self)
		{
			foreach (var cargo in self.TraitsImplementing<Cargo>())
				while (!cargo.IsEmpty())
					cargo.Unload(self);
		}

		/// <summary>
		/// Re-issue the destinations of the activities queued by the creator (e.g. scripted reinforcements),
		/// queued behind the replacement's own creation activity. Creation activities themselves are rebuilt
		/// by the replacement from the carried inits.
		/// </summary>
		static List<(string Order, Target Target)> OrdersForQueuedTargets(Actor self)
		{
			var orders = new List<(string, Target)>();
			for (var activity = self.CurrentActivity; activity != null; activity = activity.NextActivity)
			{
				if (activity.IsCanceling)
					continue;

				var target = activity.GetTargets(self).LastOrDefault();
				if (target.Type != TargetType.Terrain)
					continue;

				orders.Add((activity is AttackMoveActivity ? "AttackMove" : "Move", target));
			}

			return orders;
		}

		static void UpdateNamedMapActor(Actor self, Actor replacement, World w)
		{
			// Keep map-named actors (used by map scripts and SpawnMapActors.Actors lookups) pointing at the live actor.
			var mapActors = w.WorldActor.TraitOrDefault<SpawnMapActors>();
			if (mapActors == null || self.ActorID > mapActors.LastMapActorID)
				return;

			var name = mapActors.Actors.FirstOrDefault(kv => kv.Value == self).Key;
			if (name != null)
				mapActors.Actors[name] = replacement;
		}
	}

	public class ReplacedOnPrerequisiteInit : RuntimeFlagInit, ISingleInstanceInit { }
}
