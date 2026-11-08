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

using NUnit.Framework;
using OpenRA.Mods.RA2.Traits;

namespace OpenRA.Mods.RA2.Test
{
	[TestFixture]
	public class WithLauncherMissileTests
	{
		[Test]
		public void LauncherMissile_HiddenWhileSlotReloading_ShownWhenLoaded()
		{
			// V3: one slot, Burst 1. Armament.IsReloading turns true in the same tick the shot fires.
			var slot = new LauncherSlot();
			Assert.That(slot.IsLoaded, Is.True);

			slot.Fired();
			Assert.That(slot.IsLoaded, Is.False, "the loaded missile leaves its slot when it launches");

			for (var i = 0; i < 150; i++)
			{
				slot.Tick(armamentReloading: true, burstRemaining: 1, weaponBurst: 1);
				Assert.That(slot.IsLoaded, Is.False, $"still reloading, tick {i}");
			}

			slot.Tick(armamentReloading: false, burstRemaining: 1, weaponBurst: 1);
			Assert.That(slot.IsLoaded, Is.True, "the missile reappears once the reload finishes");
		}

		[Test]
		public void LauncherMissile_StaysEmptyBetweenTheShotsOfABurst()
		{
			// Dreadnought: Burst 2. IsReloading is false for a moment before the second shot.
			var first = new LauncherSlot();
			first.Fired();
			first.Tick(armamentReloading: false, burstRemaining: 1, weaponBurst: 2);
			Assert.That(first.IsLoaded, Is.False);

			first.Tick(armamentReloading: false, burstRemaining: 2, weaponBurst: 2);
			Assert.That(first.IsLoaded, Is.True);
		}

		[Test]
		public void LauncherMissile_NotReloading_StaysLoaded()
		{
			var slot = new LauncherSlot();
			slot.Tick(armamentReloading: false, burstRemaining: 1, weaponBurst: 1);
			slot.Tick(armamentReloading: true, burstRemaining: 1, weaponBurst: 1);
			Assert.That(slot.IsLoaded, Is.True, "reloading alone does not empty a slot that has not fired");
		}

		[Test]
		public void LauncherMissile_AlternatesSlots()
		{
			// Dreadnought: two slots, Burst 2. The armament fires barrel 0, waits the burst delay (reloading),
			// fires barrel 1, then reloads both.
			var first = new LauncherSlot();
			var second = new LauncherSlot();

			void Tick(bool reloading)
			{
				first.Tick(reloading);
				second.Tick(reloading);
			}

			first.Fired();
			Assert.That(first.IsLoaded, Is.False);
			Assert.That(second.IsLoaded, Is.True, "the other slot keeps its missile");

			for (var i = 0; i < 5; i++)
				Tick(true);

			Assert.That(first.IsLoaded, Is.False, "an empty slot stays empty through the burst delay");
			Assert.That(second.IsLoaded, Is.True);

			second.Fired();
			Assert.That(first.IsLoaded, Is.False);
			Assert.That(second.IsLoaded, Is.False);

			for (var i = 0; i < 50; i++)
				Tick(true);

			Assert.That(first.IsLoaded || second.IsLoaded, Is.False);

			Tick(false);
			Assert.That(first.IsLoaded, Is.True);
			Assert.That(second.IsLoaded, Is.True);
		}
	}
}
