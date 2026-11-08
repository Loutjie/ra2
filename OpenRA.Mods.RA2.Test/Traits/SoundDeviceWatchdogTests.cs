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
	public class SoundDeviceWatchdogTests
	{
		[Test]
		public void NormalFrames_DoNotReopen()
		{
			var w = new SoundDeviceWatchdog();
			for (var t = 0; t < 60000; t += 16)
				Assert.That(w.ShouldReopen(t, true), Is.False);
		}

		[Test]
		public void LongGap_ReopensEvenWhenReportedConnected()
		{
			var w = new SoundDeviceWatchdog();
			w.ShouldReopen(1000, true);
			Assert.That(w.ShouldReopen(1000 + SoundDeviceWatchdog.SleepGapMs, true), Is.True);
		}

		[Test]
		public void Disconnected_Reopens()
		{
			var w = new SoundDeviceWatchdog();
			Assert.That(w.ShouldReopen(1000, false), Is.True);
		}

		[Test]
		public void Failure_RetriesAtIntervalThenStops()
		{
			var w = new SoundDeviceWatchdog();
			w.ShouldReopen(0, true);
			var t = (long)SoundDeviceWatchdog.SleepGapMs;
			Assert.That(w.ShouldReopen(t, true), Is.True);
			Assert.That(w.ShouldReopen(t + 16, true), Is.False, "throttled");

			var attempts = 1;
			for (var i = 1; i <= 20; i++)
				if (w.ShouldReopen(t + i * SoundDeviceWatchdog.RetryIntervalMs, true))
					attempts++;

			Assert.That(attempts, Is.EqualTo(SoundDeviceWatchdog.MaxRetries));
		}

		[Test]
		public void Success_StopsRetrying()
		{
			var w = new SoundDeviceWatchdog();
			w.ShouldReopen(0, true);
			Assert.That(w.ShouldReopen(SoundDeviceWatchdog.SleepGapMs, true), Is.True);
			w.ReportReopened();
			Assert.That(w.ShouldReopen(SoundDeviceWatchdog.SleepGapMs + SoundDeviceWatchdog.RetryIntervalMs, true), Is.False);
		}
	}
}
