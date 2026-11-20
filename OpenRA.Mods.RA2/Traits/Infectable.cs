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
using System.Collections.Generic;
using OpenRA.Mods.Common;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Support;
using OpenRA.Traits;

namespace OpenRA.Mods.RA2.Traits
{
	[Desc("Handle infection by infector units.")]
	public class InfectableInfo : ConditionalTraitInfo, Requires<HealthInfo>
	{
		[Desc("Damage types that removes the infector.")]
		public readonly BitSet<DamageType> RemoveInfectorDamageTypes = default;

		[Desc("Damage types that kills the infector.")]
		public readonly BitSet<DamageType> KillInfectorDamageTypes = default;

		[GrantedConditionReference]
		[Desc("The condition to grant to self while infected by any actor.")]
		public readonly string InfectedCondition = null;

		[GrantedConditionReference]
		[Desc("Condition granted when being infected by another actor.")]
		public readonly string BeingInfectedCondition = null;

		[Desc("Conditions to grant when infected by specified actors.",
			"A dictionary of [actor id]: [condition].")]
		public readonly Dictionary<string, string> InfectedByConditions = [];

		[ConsumedConditionReference]
		[Desc("Boolean expression that kills the infector while it is true (e.g. the Iron Curtain's invulnerability condition).")]
		public readonly BooleanExpression KillInfectorCondition = null;

		[Desc("Leave the infector behind at the origin when the host is teleported (e.g. by the Chronosphere).")]
		public readonly bool EjectInfectorOnTeleport = true;

		[GrantedConditionReference]
		public IEnumerable<string> LinterConditions { get { return InfectedByConditions.Values; } }

		public override object Create(ActorInitializer init) { return new Infectable(init.Self, this); }
	}

	public class Infectable : ConditionalTrait<InfectableInfo>, ISync, ITick, INotifyDamage, INotifyKilled, INotifyOwnerChanged, INotifyActorDisposing, IRemoveInfector
	{
		readonly Health health;

		public Actor Infector;
		public AttackInfect AttackInfect;

		public int[] FirepowerMultipliers = Array.Empty<int>();

		[VerifySync]
		public int Ticks;

		int beingInfectedToken = Actor.InvalidConditionToken;
		Actor enteringInfector;
		int infectedToken = Actor.InvalidConditionToken;
		int infectedByToken = Actor.InvalidConditionToken;

		// The infector is returned to the world in a frame end task; ignore further removals until then.
		bool removalPending;

		bool killInfectorConditionActive;

		// Teleport detection: the host's generation and position as of the previous tick.
		// Relies on every teleport bumping Actor.Generation, which the three current teleport
		// paths do: Cnc Teleport (Chronosphere and its return), Common SimpleTeleport and
		// RA2 ChronoResourceTeleport (chrono miner). Actor.ChangeOwnerSync bumps it as well.
		int observedGeneration;
		int ownerChangesSinceTick;
		WPos lastPosition;

		public Infectable(Actor self, InfectableInfo info)
			: base(info)
		{
			health = self.Trait<Health>();
		}

		protected override void Created(Actor self)
		{
			base.Created(self);
			observedGeneration = self.Generation;
			lastPosition = self.CenterPosition;
		}

		public override IEnumerable<VariableObserver> GetVariableObservers()
		{
			foreach (var observer in base.GetVariableObservers())
				yield return observer;

			if (Info.KillInfectorCondition != null)
				yield return new VariableObserver(KillInfectorConditionChanged, Info.KillInfectorCondition.Variables);
		}

		void KillInfectorConditionChanged(Actor self, IReadOnlyDictionary<string, int> conditions)
		{
			killInfectorConditionActive = Info.KillInfectorCondition.Evaluate(conditions);
		}

		public bool TryStartInfecting(Actor self, Actor infector)
		{
			if (infector != null)
			{
				if (enteringInfector == null)
				{
					enteringInfector = infector;

					if (beingInfectedToken == Actor.InvalidConditionToken && !string.IsNullOrEmpty(Info.BeingInfectedCondition))
						beingInfectedToken = self.GrantCondition(Info.BeingInfectedCondition);

					return true;
				}
			}

			return false;
		}

		public void GrantCondition(Actor self)
		{
			if (infectedToken == Actor.InvalidConditionToken && !string.IsNullOrEmpty(Info.InfectedCondition))
				infectedToken = self.GrantCondition(Info.InfectedCondition);

			if (Info.InfectedByConditions.TryGetValue(Infector.Info.Name, out var infectedByCondition))
				infectedByToken = self.GrantCondition(infectedByCondition);
		}

		public void RevokeCondition(Actor self, Actor infector = null)
		{
			if (infector != null)
			{
				if (enteringInfector == infector)
				{
					enteringInfector = null;

					if (beingInfectedToken != Actor.InvalidConditionToken)
						beingInfectedToken = self.RevokeCondition(beingInfectedToken);
				}
			}
			else
			{
				if (infectedToken != Actor.InvalidConditionToken)
					infectedToken = self.RevokeCondition(infectedToken);

				if (infectedByToken != Actor.InvalidConditionToken)
					infectedByToken = self.RevokeCondition(infectedByToken);
			}
		}

		void RemoveInfector(Actor self, InfectorOutcome outcome, AttackInfo info, WPos position)
		{
			if (outcome == InfectorOutcome.None || removalPending || Infector == null || Infector.IsDead)
				return;

			removalPending = true;
			Infector.TraitOrDefault<IPositionable>()?.SetPosition(Infector, position);

			self.World.AddFrameEndTask(w =>
			{
				removalPending = false;
				if (Infector == null)
					return;

				if (Infector.IsDead)
				{
					ClearInfector(self);
					return;
				}

				w.Add(Infector);

				if (outcome == InfectorOutcome.InfectorKilled)
				{
					// Without an AttackInfo (Iron Curtain) the host is credited with the kill, deliberately:
					// the drone died to the host's protection, not to a third party.
					if (info != null)
						Infector.Kill(info.Attacker, info.Damage.DamageTypes);
					else
						Infector.Kill(self);
				}
				else
				{
					Infector.QueueActivity(false, new Nudge(Infector));
				}

				ClearInfector(self);
			});
		}

		void ClearInfector(Actor self)
		{
			if (!self.Disposed)
				RevokeCondition(self);

			Infector = null;
			FirepowerMultipliers = Array.Empty<int>();
		}

		void INotifyDamage.Damaged(Actor self, AttackInfo e)
		{
			var outcome = InfectionTransitions.OnDamaged(Infector != null,
				e.Damage.DamageTypes.Overlaps(Info.KillInfectorDamageTypes),
				e.Damage.DamageTypes.Overlaps(Info.RemoveInfectorDamageTypes));

			RemoveInfector(self, outcome, e, self.CenterPosition);
		}

		void INotifyKilled.Killed(Actor self, AttackInfo e)
		{
			if (Infector == null)
				return;

			var infectInfo = AttackInfect.InfectInfo;
			var survives = InfectionTransitions.InfectorSurvivesHostDeath(infectInfo.AlwaysSurviveHostDeath,
				infectInfo.SurviveHostDamageTypes.Overlaps(e.Damage.DamageTypes));
			RemoveInfector(self, InfectionTransitions.OnHostKilled(true, survives), e, self.CenterPosition);
		}

		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner)
		{
			// Owner changes also bump Actor.Generation; they must not count as a teleport.
			ownerChangesSinceTick++;
		}

		void INotifyActorDisposing.Disposing(Actor self)
		{
			// The host left the game without dying (e.g. an infected MCV deploying via Transforms):
			// put the drone back next to it rather than losing it inside a disposed actor.
			if (self.World.Disposing)
				return;

			RemoveInfector(self, InfectionTransitions.OnHostDisposed(Infector != null), null, self.CenterPosition);
		}

		void ITick.Tick(Actor self)
		{
			// The drone can die while out of the world (e.g. OwnerLostAction when its owner is defeated).
			if (InfectionTransitions.InfectorLost(Infector != null, Infector != null && Infector.IsDead) && !removalPending)
				ClearInfector(self);

			// Activities tick before traits, so a teleport this tick has already moved the host:
			// lastPosition still holds the origin, where the infector is left behind.
			var teleported = InfectionTransitions.HostTeleported(observedGeneration, self.Generation, ownerChangesSinceTick);
			var origin = lastPosition;
			observedGeneration = self.Generation;
			ownerChangesSinceTick = 0;
			lastPosition = self.CenterPosition;

			if (teleported && Info.EjectInfectorOnTeleport)
				RemoveInfector(self, InfectionTransitions.OnChronoshift(Infector != null), null, origin);

			if (killInfectorConditionActive)
				RemoveInfector(self, InfectionTransitions.OnIronCurtain(Infector != null), null, self.CenterPosition);

			if (!IsTraitDisabled && Infector != null && !removalPending)
			{
				if (--Ticks < 0)
				{
					var info = AttackInfect.InfectInfo;
					if (InfectionTransitions.ShouldCull(health.HP, health.MaxHP, info.CullHealthPercent))
						self.Kill(Infector, info.DamageTypes);
					else
					{
						var damage = Util.ApplyPercentageModifiers(info.Damage, FirepowerMultipliers);
						health.InflictDamage(self, Infector, new Damage(damage, info.DamageTypes), false);
					}

					Ticks = info.DamageInterval;
				}
			}
		}

		void IRemoveInfector.RemoveInfector(Actor self, bool kill, AttackInfo e)
		{
			RemoveInfector(self, kill ? InfectorOutcome.InfectorKilled : InfectorOutcome.Ejected, e, self.CenterPosition);
		}
	}
}
