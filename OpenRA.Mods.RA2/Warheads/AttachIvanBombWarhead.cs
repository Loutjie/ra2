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
	[Desc("Sticks a timed bomb on the actor the weapon was fired at (RA2 IvanBomb). The actor carries it",
		"until Timer runs out, then Weapon explodes where the actor is. Needs IvanBombCarrier on the victim.")]
	public class AttachIvanBombWarhead : Warhead, IRulesetLoaded<WeaponInfo>
	{
		[WeaponReference]
		[FieldLoader.Require]
		[Desc("Weapon that explodes when the timer runs out or the carrier dies.")]
		public readonly string Weapon = null;

		[Desc("Ticks from attaching to the explosion (RA2 IvanTimedDelay).")]
		public readonly int Timer = 100;

		WeaponInfo weapon;

		public void RulesetLoaded(Ruleset rules, WeaponInfo info)
		{
			if (!rules.Weapons.TryGetValue(Weapon.ToLowerInvariant(), out weapon))
				throw new YamlException($"Weapons Ruleset does not contain an entry '{Weapon.ToLowerInvariant()}'");
		}

		public override void DoImpact(in Target target, WarheadArgs args)
		{
			var victim = args.WeaponTarget.Type == TargetType.Actor ? args.WeaponTarget.Actor : null;
			if (victim == null || victim.IsDead || !victim.IsInWorld || !IsValidAgainst(victim, args.SourceActor))
				return;

			victim.TraitOrDefault<IvanBombCarrier>()?.Attach(victim, weapon, Timer, args.SourceActor);
		}
	}
}
