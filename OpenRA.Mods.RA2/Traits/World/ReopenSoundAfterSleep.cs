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
using System.Runtime.InteropServices;
using OpenRA.Graphics;
using OpenRA.Traits;

namespace OpenRA.Mods.RA2.Traits
{
	[TraitLocation(SystemActors.World | SystemActors.EditorWorld)]
	[Desc("Restores audio after the computer sleeps, by reopening the OpenAL device in place. Unsynced.")]
	public class ReopenSoundAfterSleepInfo : TraitInfo<ReopenSoundAfterSleep> { }

	public class ReopenSoundAfterSleep : ITickRender
	{
		const int AlcConnected = 0x313;
		const int AlcDeviceClock = 0x1600;
		const string Library = "soft_oal";

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		delegate byte ReopenDevice(IntPtr device, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, IntPtr attributes);

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		delegate void GetInteger64(IntPtr device, int parameter, int size, out long value);

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
		static extern IntPtr alcGetCurrentContext();

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
		static extern IntPtr alcGetContextsDevice(IntPtr context);

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
		static extern void alcGetIntegerv(IntPtr device, int parameter, int size, out int value);

		[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
		static extern IntPtr alcGetProcAddress(IntPtr device, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

		readonly SoundDeviceWatchdog watchdog = new();
		GetInteger64 getInteger64;
		bool unavailable;

		void ITickRender.TickRender(WorldRenderer wr, Actor self)
		{
			if (unavailable)
				return;

			try
			{
				var context = alcGetCurrentContext();
				var device = context == IntPtr.Zero ? IntPtr.Zero : alcGetContextsDevice(context);
				if (device == IntPtr.Zero)
					return;

				alcGetIntegerv(device, AlcConnected, 1, out var connected);

				// Wall clock on purpose: it keeps counting while the machine is asleep.
				var now = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
				var address = getInteger64 == null ? alcGetProcAddress(device, "alcGetInteger64vSOFT") : IntPtr.Zero;
				if (address != IntPtr.Zero)
					getInteger64 = Marshal.GetDelegateForFunctionPointer<GetInteger64>(address);

				// Without the clock extension report a constant, and rely on the other signals only.
				var clock = 0L;
				getInteger64?.Invoke(device, AlcDeviceClock, 1, out clock);

				if (watchdog.ShouldReopen(now, connected != 0, getInteger64 == null ? now : clock))
				{
					Log.Write("sound", $"Reopening the audio device: {watchdog.LastReason}.");
					if (Reopen(device))
						watchdog.ReportReopened();
				}
			}
			catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
			{
				// Another sound backend or an old library: never let this take the game down.
				Log.Write("sound", $"Audio recovery after sleep is unavailable: {e.Message}");
				unavailable = true;
			}
		}

		bool Reopen(IntPtr device)
		{
			var address = alcGetProcAddress(device, "alcReopenDeviceSOFT");
			if (address == IntPtr.Zero)
			{
				Log.Write("sound", "alcReopenDeviceSOFT is not available.");
				unavailable = true;
				return false;
			}

			var reopen = Marshal.GetDelegateForFunctionPointer<ReopenDevice>(address);
			var success = reopen(device, Game.Settings.Sound.Device, IntPtr.Zero) != 0
				|| reopen(device, null, IntPtr.Zero) != 0;

			Log.Write("sound", success ? "Reopened the audio device." : "Failed to reopen the audio device.");
			return success;
		}
	}
}
