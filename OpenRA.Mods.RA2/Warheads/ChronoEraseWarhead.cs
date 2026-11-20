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

using OpenRA.GameRules;
using OpenRA.Mods.Common.Warheads;
using OpenRA.Mods.RA2.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.RA2.Warheads
{
	[Desc("Links a temporal beam to the actor fired at (RA2 Temporal=yes): while the attacker keeps hitting it in range,",
		"the actor is frozen and gathers Warp per tick until ChronoErasable erases it.")]
	public class ChronoEraseWarhead : Warhead
	{
		[Desc("Warp points added per tick while the beam holds (RA2: the weapon's Damage per frame).")]
		public readonly int Warp = 10;

		[Desc("Ticks one hit holds the target. Keep it above the weapon's ReloadDelay so the hold lasts between shots.")]
		public readonly int Hold = 25;

		[Desc("Sound played when a beam starts holding a target (not on every renewing shot).")]
		public readonly string LinkSound = null;

		public override void DoImpact(in Target target, WarheadArgs args)
		{
			var victim = args.WeaponTarget.Type == TargetType.Actor ? args.WeaponTarget.Actor : null;
			if (victim == null || victim.IsDead || !victim.IsInWorld || !IsValidAgainst(victim, args.SourceActor))
				return;

			var erasable = victim.TraitOrDefault<ChronoErasable>();
			if (erasable != null && erasable.Link(victim, args.SourceActor, Warp, args.Weapon.Range, Hold) && LinkSound != null)
				Game.Sound.Play(SoundType.World, LinkSound, victim.CenterPosition);
		}
	}
}
