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
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.RA2.Traits
{
	public static class ChronoErasure
	{
		/// <summary>Warp points that erase an actor: RA2 sets a temporal target's warp to Strength x 10.</summary>
		public static int Total(int maxHP, int warpPerHP) => maxHP * warpPerHP;

		/// <summary>Warp after one more tick: every linked beam adds its rate; no beams resets it (RA2 releases the target).</summary>
		public static int Step(int warp, IEnumerable<int> rates)
		{
			var sum = rates.Sum();
			return sum == 0 ? 0 : warp + sum;
		}
	}

	[Desc("Can be erased by temporal beams (ChronoErase warhead, RA2 Temporal=yes). While a beam holds it the actor is",
		"frozen; once enough warp builds up it vanishes, without death effects. It is released, and the warp lost,",
		"when every beam stops: the attacker dies, leaves range or stops hitting it (ChronoErase Hold).")]
	public class ChronoErasableInfo : TraitInfo, Requires<IHealthInfo>
	{
		[GrantedConditionReference]
		[Desc("Condition granted while a beam holds the actor. Leave empty for none.")]
		public readonly string Condition = "chronodisable";

		[Desc("Warp points per HP of the actor's maximum health (RA2: 10 per point of Strength).")]
		public readonly int WarpPerHP = 10;

		[Desc("Colour of the erasure bar shown under the selection box.")]
		public readonly Color BarColor = Color.FromArgb(128, 200, 255);

		[Desc("Damage type reported to kill statistics and experience when the actor is erased.")]
		public readonly BitSet<DamageType> DamageTypes = new("ChronoErase");

		public override object Create(ActorInitializer init) { return new ChronoErasable(init.Self, this); }
	}

	public class ChronoErasable : ITick, ISelectionBar, ISync
	{
		sealed class BeamLink
		{
			public Actor Attacker;
			public int Rate;
			public WDist Range;
			public int Until;
		}

		readonly ChronoErasableInfo info;
		readonly IHealth health;
		readonly List<BeamLink> links = [];
		int token = Actor.InvalidConditionToken;
		bool erased;

		[VerifySync]
		int warp;

		public ChronoErasable(Actor self, ChronoErasableInfo info)
		{
			this.info = info;
			health = self.Trait<IHealth>();
		}

		int Total => ChronoErasure.Total(health.MaxHP, info.WarpPerHP);

		/// <summary>
		/// A temporal beam from attacker hit the actor: hold it for the next hold ticks, adding rate warp per tick.
		/// Returns true when this starts a new hold rather than renewing one.
		/// </summary>
		public bool Link(Actor self, Actor attacker, int rate, WDist range, int hold)
		{
			if (erased || attacker == null)
				return false;

			var until = self.World.WorldTick + hold;
			var link = links.FirstOrDefault(l => l.Attacker == attacker);
			if (link != null)
			{
				link.Rate = rate;
				link.Range = range;
				link.Until = until;
				return false;
			}

			links.Add(new BeamLink { Attacker = attacker, Rate = rate, Range = range, Until = until });
			return true;
		}

		void ITick.Tick(Actor self)
		{
			if (erased)
				return;

			links.RemoveAll(l => !Holds(self, l));
			warp = ChronoErasure.Step(warp, links.Select(l => l.Rate));

			if (links.Count == 0)
			{
				if (token != Actor.InvalidConditionToken)
					token = self.RevokeCondition(token);

				return;
			}

			if (token == Actor.InvalidConditionToken && !string.IsNullOrEmpty(info.Condition))
				token = self.GrantCondition(info.Condition);

			if (warp >= Total)
				Erase(self, links[0].Attacker);
		}

		// The beam holds while its attacker lives, stays in range and keeps hitting this actor.
		static bool Holds(Actor self, BeamLink link)
		{
			var a = link.Attacker;
			if (a.IsDead || !a.IsInWorld || !self.IsInWorld || self.World.WorldTick > link.Until)
				return false;

			return Target.FromActor(self).IsInRange(a.CenterPosition, link.Range + new WDist(512));
		}

		void Erase(Actor self, Actor attacker)
		{
			erased = true;
			links.Clear();

			// Credit the kill (experience and statistics) without the death effects: an erased unit leaves no
			// wreck, explosion, survivors or death animation. GivesExperience is internal to the engine.
			var attack = new AttackInfo
			{
				Attacker = attacker,
				Damage = new Damage(health.HP, info.DamageTypes),
				DamageState = DamageState.Dead,
				PreviousDamageState = health.DamageState,
			};

			foreach (var n in self.TraitsImplementing<INotifyKilled>())
				if (n is UpdatesPlayerStatistics || n.GetType().Name == "GivesExperience")
					n.Killed(self, attack);

			self.World.AddFrameEndTask(_ =>
			{
				if (!self.Disposed)
					self.Dispose();
			});
		}

		float ISelectionBar.GetValue() => links.Count == 0 ? 0 : (float)warp / Total;

		Color ISelectionBar.GetColor() => info.BarColor;

		bool ISelectionBar.DisplayWhenEmpty => false;
	}
}
