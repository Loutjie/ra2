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
using OpenRA.GameRules;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.RA2.Traits
{
	[Desc("Can carry a Crazy Ivan bomb (AttachIvanBomb warhead, RA2 Bombable). The bomb travels with the actor",
		"and goes off where it is when the timer runs out, or at once if the actor dies first. One bomb at a time.")]
	public class IvanBombCarrierInfo : TraitInfo
	{
		[GrantedConditionReference]
		[Desc("Condition granted while a bomb is attached (e.g. to stop further bombs being aimed at it).")]
		public readonly string Condition = "ivan-bomb";

		[Desc("Image of the bomb icon shown on the carrier.")]
		public readonly string Image = "bombcurs";

		[SequenceReference(nameof(Image))]
		[Desc("Looping sequence of the bomb icon.")]
		public readonly string Sequence = "idle";

		[PaletteReference]
		public readonly string Palette = "mouse";

		[Desc("Position of the icon relative to the carrier's centre.")]
		public readonly WVec Offset = new(0, 0, 512);

		public override object Create(ActorInitializer init) { return new IvanBombCarrier(init.Self, this); }
	}

	public class IvanBombCarrier : ITick, INotifyKilled, IRender, ISync
	{
		readonly IvanBombCarrierInfo info;
		readonly Animation icon;
		WeaponInfo weapon;
		Actor attacker;
		int token = Actor.InvalidConditionToken;

		[VerifySync]
		int ticks;

		public IvanBombCarrier(Actor self, IvanBombCarrierInfo info)
		{
			this.info = info;
			icon = new Animation(self.World, info.Image);
		}

		public bool HasBomb => weapon != null;

		public void Attach(Actor self, WeaponInfo weapon, int delay, Actor attacker)
		{
			if (HasBomb)
				return;

			this.weapon = weapon;
			this.attacker = attacker;
			ticks = delay;
			token = self.GrantCondition(info.Condition);
			icon.PlayRepeating(info.Sequence);
		}

		void ITick.Tick(Actor self)
		{
			if (!HasBomb)
				return;

			icon.Tick();

			// A carrier inside a transport or garrison keeps its bomb until it comes out.
			if (self.IsInWorld && --ticks <= 0)
				Detonate(self);
		}

		void INotifyKilled.Killed(Actor self, AttackInfo e)
		{
			if (HasBomb)
				Detonate(self);
		}

		void Detonate(Actor self)
		{
			var w = weapon;
			var firedBy = attacker != null && !attacker.Disposed ? attacker : self;
			var pos = self.CenterPosition;
			weapon = null;
			attacker = null;
			if (token != Actor.InvalidConditionToken)
				token = self.RevokeCondition(token);

			self.World.AddFrameEndTask(_ => w.Impact(Target.FromPos(pos), firedBy));
		}

		IEnumerable<IRenderable> IRender.Render(Actor self, WorldRenderer wr)
		{
			if (!HasBomb || self.World.FogObscures(self))
				return [];

			return icon.Render(self.CenterPosition + info.Offset, WVec.Zero, 0, wr.Palette(info.Palette));
		}

		IEnumerable<Rectangle> IRender.ScreenBounds(Actor self, WorldRenderer wr)
		{
			if (!HasBomb)
				yield break;

			yield return icon.ScreenBounds(wr, self.CenterPosition + info.Offset, WVec.Zero);
		}
	}
}
