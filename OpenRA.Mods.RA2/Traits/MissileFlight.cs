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

		// A parabola from launcher to target, leaving at PitchFinal.
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

		// The tilt turns the missile around a point this far behind its centre (its tail), which stays put.
		public WDist TiltPivot;

		// Pitch to climb at after ignition, until cruise altitude. Defaults to PitchFinal, the end of the lift.
		public WAngle? ClimbPitch;

		// Fly straight for this far (horizontally) after ignition before turning towards the target, to clear the launcher.
		public WDist StraightAfterIgnition;
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

		// The launcher slot the missile sits in until it flies; the tilt moves it relative to this.
		WPos anchor;

		// Where the missile ignited, for the straight run before it turns.
		WPos ignition;

		// Set once cruise altitude is first reached, so dipping below it later does not restart the climb.
		bool climbed;

		// Arc profile: the launch point, horizontal length of the route and distance flown along it.
		WPos arcStart;
		int arcDistance = -1;
		int arcTravelled;

		public MissileFlight(MissileFlightParameters parameters, WPos launch, WAngle yaw)
		{
			p = parameters;
			anchor = launch;
			Position = launch;
			Yaw = yaw;
			Pitch = p.PitchInitial;
			Phase = p.PauseTicks > 0 ? MissileFlightPhase.Pause : MissileFlightPhase.Tilt;
		}

		public WRot Orientation => new(WAngle.Zero, Pitch, Yaw);

		/// <summary>True once the missile has flown its straight run after ignition and is clear of its launcher.</summary>
		public bool ClearOfLauncher => Phase == MissileFlightPhase.Arrived
			|| (Phase == MissileFlightPhase.Fly && (Position - ignition).HorizontalLength >= p.StraightAfterIgnition.Length);

		/// <summary>
		/// Keep a missile that has not ignited yet attached to its moving launcher: at the launcher's slot,
		/// pointing the way the launcher points. It turns towards its target only once it flies.
		/// </summary>
		public void FollowLauncher(WPos position, WAngle yaw)
		{
			if (Phase != MissileFlightPhase.Pause && Phase != MissileFlightPhase.Tilt)
				return;

			anchor = position;
			Yaw = yaw;
			Position = anchor + TiltOffset();
		}

		/// <summary>How far the centre has moved from the slot because the missile tilted around its tail.</summary>
		WVec TiltOffset()
		{
			var length = p.TiltPivot.Length;
			if (length == 0)
				return WVec.Zero;

			var forward = length * (Pitch.Cos() - p.PitchInitial.Cos()) / 1024;
			var up = length * (Pitch.Sin() - p.PitchInitial.Sin()) / 1024;
			return new WVec(0, -forward, 0).Rotate(WRot.FromYaw(Yaw)) + new WVec(0, 0, up);
		}

		/// <summary>A turn rate scaled by speed: the full rate at top speed, at least one step while moving.</summary>
		WAngle SpeedScaled(WAngle rate)
		{
			return p.MaxSpeed <= 0 ? rate : new WAngle(Math.Max(1, rate.Angle * Speed / p.MaxSpeed));
		}

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
						Position = anchor + TiltOffset();
						ignition = Position;
						Phase = MissileFlightPhase.Fly;
						return false;
					}

					Pitch = new WAngle(p.PitchInitial.Angle + (p.PitchFinal.Angle - p.PitchInitial.Angle) * tiltTick / p.TiltTicks);
					Position = anchor + TiltOffset();
					return false;
			}

			// Fly
			Speed = Math.Min(Speed + p.Acceleration, p.MaxSpeed);

			if (p.Profile == MissileFlightProfile.Arc)
				return TickArc(target, terrainHeightAtPosition);

			var delta = target - Position;
			var horizontal = delta.HorizontalLength;

			// Close enough to hit this tick.
			if (delta.Length <= Math.Max(Speed, 1))
			{
				Position = target;
				return Arrive();
			}

			// Leave the launcher in a straight line, then turn no faster than the missile moves, so it turns in the air.
			if (ClearOfLauncher)
				Yaw = Util.TickFacing(Yaw, delta.Yaw, SpeedScaled(p.YawTurnRate));

			var toTarget = WAngle.ArcTan(delta.Z, horizontal);
			var altitude = Position.Z - terrainHeightAtPosition.Length;
			if (altitude >= p.CruiseAltitude.Length)
				climbed = true;

			// Terminal guidance: in the last few ticks aim straight at the target with a tighter turn,
			// otherwise a lazy pitch rate overshoots short shots and undershoots long ones.
			var terminal = delta.Length <= Speed * p.TerminalTicks;
			WAngle desiredPitch;
			if (terminal)
				desiredPitch = toTarget;
			else if (!climbed && !Diving(toTarget))
				desiredPitch = p.ClimbPitch ?? p.PitchFinal;
			else if (p.Profile == MissileFlightProfile.LazyCurve || Diving(toTarget))
				desiredPitch = toTarget;
			else
				desiredPitch = WAngle.Zero;

			Pitch = Util.TickFacing(Pitch, desiredPitch, terminal ? new WAngle(p.PitchTurnRate.Angle * 4) : SpeedScaled(p.PitchTurnRate));
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

		// A parabola from the launch point to the target, so it needs no steering correction at the end.
		// Height above the launch-to-target line is tan(PitchFinal) * t * (d - t) / d, and the nose follows its slope.
		bool TickArc(WPos target, WDist terrainHeightAtPosition)
		{
			if (arcDistance < 0)
			{
				arcStart = Position;
				arcDistance = (target - Position).HorizontalLength;
			}

			var route = target - arcStart;
			arcTravelled += Math.Max(Speed * Pitch.Cos() / 1024, 1);
			if (arcTravelled >= arcDistance)
			{
				Position = target;
				return Arrive();
			}

			long t = arcTravelled;
			long d = arcDistance;
			long tan = p.PitchFinal.Tan();
			var rise = tan * t * (d - t) / d / 1024;

			Position = new WPos(
				arcStart.X + (int)(route.X * t / d),
				arcStart.Y + (int)(route.Y * t / d),
				arcStart.Z + (int)(route.Z * t / d + rise));

			Yaw = route.Yaw;
			Pitch = WAngle.ArcTan((int)((route.Z * 1024 + tan * (d - 2 * t)) / 1024), arcDistance);

			return Position.Z <= terrainHeightAtPosition.Length && Arrive();
		}

		bool Arrive()
		{
			Phase = MissileFlightPhase.Arrived;
			return true;
		}
	}
}
