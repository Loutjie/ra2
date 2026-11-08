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
using OpenRA.Mods.Common;

namespace OpenRA.Mods.RA2.Traits
{
	public enum MissileFlightPhase { Pause, Tilt, Fly, Arrived }

	public enum MissileFlightProfile
	{
		// Climb, then curve continuously towards the target (V3: V3RocketLazyCurve=yes).
		LazyCurve,

		// Climb to cruise altitude, fly level, then dive onto the target (Dreadnought: DMislLazyCurve=no).
		Cruise,

		// Smooth arc: pitch falls steadily from PitchFinal at launch to -PitchFinal over the target.
		Arc
	}

	/// <summary>Parameters of a RA2 'rocket locomotor' flight, in OpenRA units.</summary>
	public sealed class MissileFlightParameters
	{
		public int PauseTicks;
		public int TiltTicks;
		public WAngle PitchInitial;
		public WAngle PitchFinal;
		public WAngle PitchTurnRate;
		public WAngle YawTurnRate;
		public int Acceleration;
		public int MaxSpeed;
		public WDist CruiseAltitude;
		public MissileFlightProfile Profile;

		// Cruise profile: start the dive once the target is at least this far below the horizon.
		public WAngle DiveAngle = new(128);

		// Ticks before impact at which terminal guidance takes over.
		public int TerminalTicks = 12;

		// Safety net: arrive after this many ticks regardless of position.
		public int MaxTicks = 1500;
	}

	/// <summary>
	/// Deterministic (integer-only) flight path of a RA2-style rocket.
	/// Pure state machine: no world access, so it can be unit tested and is sync-safe.
	/// Pitch is positive when the nose points up.
	/// </summary>
	public sealed class MissileFlight
	{
		readonly MissileFlightParameters p;

		public WPos Position { get; private set; }
		public WAngle Yaw { get; private set; }
		public WAngle Pitch { get; private set; }
		public int Speed { get; private set; }
		public int Ticks { get; private set; }
		public MissileFlightPhase Phase { get; private set; }

		// Set once cruise altitude is first reached, so dipping below it later does not restart the climb.
		bool climbed;

		// Arc profile: horizontal launch-to-target distance, fixed on the first flying tick.
		int arcDistance;

		public MissileFlight(MissileFlightParameters parameters, WPos launch, WAngle yaw)
		{
			p = parameters;
			Position = launch;
			Yaw = yaw;
			Pitch = p.PitchInitial;
			Phase = p.PauseTicks > 0 ? MissileFlightPhase.Pause : MissileFlightPhase.Tilt;
		}

		public WRot Orientation => new(WAngle.Zero, Pitch, Yaw);

		/// <summary>Advance one tick. Returns true when the missile has arrived (or must detonate).</summary>
		public bool Tick(WPos target, WDist terrainHeightAtPosition)
		{
			if (Phase == MissileFlightPhase.Arrived)
				return true;

			Ticks++;
			if (Ticks >= p.MaxTicks)
				return Arrive();

			switch (Phase)
			{
				case MissileFlightPhase.Pause:
					if (Ticks >= p.PauseTicks)
						Phase = MissileFlightPhase.Tilt;
					return false;

				case MissileFlightPhase.Tilt:
					var tiltTick = Ticks - p.PauseTicks;
					if (p.TiltTicks <= 0 || tiltTick >= p.TiltTicks)
					{
						Pitch = p.PitchFinal;
						Phase = MissileFlightPhase.Fly;
						return false;
					}

					Pitch = new WAngle(p.PitchInitial.Angle + (p.PitchFinal.Angle - p.PitchInitial.Angle) * tiltTick / p.TiltTicks);
					return false;
			}

			// Fly
			Speed = Math.Min(Speed + p.Acceleration, p.MaxSpeed);

			var delta = target - Position;
			var horizontal = delta.HorizontalLength;

			// Close enough to hit this tick.
			if (delta.Length <= Math.Max(Speed, 1))
			{
				Position = target;
				return Arrive();
			}

			Yaw = Util.TickFacing(Yaw, delta.Yaw, p.YawTurnRate);

			var toTarget = WAngle.ArcTan(delta.Z, horizontal);
			var altitude = Position.Z - terrainHeightAtPosition.Length;
			if (altitude >= p.CruiseAltitude.Length)
				climbed = true;

			// Terminal guidance: in the last few ticks aim straight at the target with a tighter turn,
			// otherwise a lazy pitch rate overshoots short shots and undershoots long ones.
			var terminal = delta.Length <= Speed * p.TerminalTicks;
			WAngle desiredPitch;
			if (terminal)
				desiredPitch = p.Profile == MissileFlightProfile.Arc && Signed(toTarget) > Signed(Pitch) ? Pitch : toTarget;
			else if (p.Profile == MissileFlightProfile.Arc)
			{
				if (arcDistance == 0)
					arcDistance = Math.Max(horizontal, 1);

				// +PitchFinal at launch, zero halfway, -PitchFinal over the target.
				desiredPitch = new WAngle(p.PitchFinal.Angle * (2 * Math.Min(horizontal, arcDistance) - arcDistance) / arcDistance);
			}
			else if (!climbed && !Diving(toTarget))
				desiredPitch = p.PitchFinal;
			else if (p.Profile == MissileFlightProfile.LazyCurve || Diving(toTarget))
				desiredPitch = toTarget;
			else
				desiredPitch = WAngle.Zero;

			Pitch = Util.TickFacing(Pitch, desiredPitch, terminal ? new WAngle(p.PitchTurnRate.Angle * 4) : p.PitchTurnRate);
			if (terminal)
				Yaw = delta.Yaw;

			var h = Speed * Pitch.Cos() / 1024;
			var step = new WVec(0, -h, 0).Rotate(WRot.FromYaw(Yaw)) + new WVec(0, 0, Speed * Pitch.Sin() / 1024);
			Position += step;

			// Hit the ground before reaching the target.
			if (Position.Z <= terrainHeightAtPosition.Length && Phase == MissileFlightPhase.Fly && Ticks > p.PauseTicks + p.TiltTicks + 1)
				return Arrive();

			return false;
		}

		// The target is far enough below the horizon that a cruise missile should start diving.
		// Angles in (512, 1024) are below the horizon; 1024 - angle is the depression.
		bool Diving(WAngle toTarget)
		{
			return toTarget.Angle > 512 && 1024 - toTarget.Angle >= p.DiveAngle.Angle;
		}

		// Nose-up positive, nose-down negative. Lets an arc refuse to lift its nose near the target.
		static int Signed(WAngle angle)
		{
			return angle.Angle > 512 ? angle.Angle - 1024 : angle.Angle;
		}

		bool Arrive()
		{
			Phase = MissileFlightPhase.Arrived;
			return true;
		}
	}
}
