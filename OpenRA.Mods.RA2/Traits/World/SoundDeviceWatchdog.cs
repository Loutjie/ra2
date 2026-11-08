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

namespace OpenRA.Mods.RA2.Traits
{
	/// <summary>
	/// Decides when the audio device should be reopened. Pure logic so it can be unit tested:
	/// a long gap between rendered frames means the machine slept, and macOS can leave the
	/// device silently dead without ever reporting it as disconnected.
	/// </summary>
	public sealed class SoundDeviceWatchdog
	{
		public const int SleepGapMs = 10000;
		public const int RetryIntervalMs = 1000;
		public const int MaxRetries = 10;

		long lastFrameMs = -1;
		long nextAttemptMs;
		int retriesLeft;

		public bool ShouldReopen(long nowMs, bool connected)
		{
			var slept = lastFrameMs >= 0 && nowMs - lastFrameMs >= SleepGapMs;
			lastFrameMs = nowMs;

			if (slept || (!connected && retriesLeft == 0 && nowMs >= nextAttemptMs))
			{
				retriesLeft = MaxRetries;
				nextAttemptMs = nowMs;
			}

			if (retriesLeft == 0 || nowMs < nextAttemptMs)
				return false;

			retriesLeft--;
			nextAttemptMs = nowMs + RetryIntervalMs;
			return true;
		}

		public void ReportReopened()
		{
			retriesLeft = 0;
		}
	}
}
