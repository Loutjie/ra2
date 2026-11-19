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
using NUnit.Framework;
using OpenRA.Mods.RA2.Traits;

namespace OpenRA.Mods.RA2.Test
{
	// SPIKE: evidence that the flight model is deterministic and reaches its target.
	[TestFixture]
	public class MissileFlightTests
	{
		// RA2 V3RocketPauseFrames=0, TiltFrames=60, PitchInitial=.21, PitchFinal=.5, TurnRate=.05, Altitude=768 leptons.
		static MissileFlightParameters V3() => new()
		{
			PauseTicks = 0,
			TiltTicks = 60,
			PitchInitial = new WAngle(54),
			PitchFinal = new WAngle(128),
			PitchTurnRate = new WAngle(13),
			YawTurnRate = new WAngle(16),
			Acceleration = 8,
			MaxSpeed = 200,
			CruiseAltitude = new WDist(3072),
			Profile = MissileFlightProfile.LazyCurve,
		};

		// RA2 DMislPauseFrames=20, TiltFrames=60, PitchInitial=0, PitchFinal=.5, TurnRate=.08.
		static MissileFlightParameters Dmisl() => new()
		{
			PauseTicks = 20,
			TiltTicks = 60,
			PitchInitial = WAngle.Zero,
			PitchFinal = new WAngle(128),
			PitchTurnRate = new WAngle(20),
			YawTurnRate = new WAngle(24),
			Acceleration = 16,
			MaxSpeed = 250,
			CruiseAltitude = new WDist(3072),
			Profile = MissileFlightProfile.Cruise,
		};

		static (MissileFlight Flight, int MaxAltitude) Fly(MissileFlightParameters p, WPos target)
		{
			var flight = new MissileFlight(p, WPos.Zero, (target - WPos.Zero).Yaw);
			var maxAltitude = 0;
			while (!flight.Tick(target, WDist.Zero))
				maxAltitude = Math.Max(maxAltitude, flight.Position.Z);

			return (flight, maxAltitude);
		}

		[TestCase(5)]
		[TestCase(12)]
		[TestCase(18)]
		public void Ballistic_ReachesTarget(int cells)
		{
			var target = new WPos(cells * 1024, -cells * 512, 0);
			var (flight, maxAltitude) = Fly(V3(), target);

			Assert.That((flight.Position - target).Length, Is.LessThan(256), $"landed at {flight.Position}, {flight.Ticks} ticks");
			Assert.That(flight.Ticks, Is.LessThan(V3().MaxTicks));
			Assert.That(maxAltitude, Is.GreaterThan(1024));
		}

		[TestCase(8)]
		[TestCase(25)]
		public void Cruise_ReachesTarget(int cells)
		{
			var target = new WPos(-cells * 1024, 0, 0);
			var (flight, maxAltitude) = Fly(Dmisl(), target);

			Assert.That((flight.Position - target).Length, Is.LessThan(256), $"landed at {flight.Position}, {flight.Ticks} ticks");
			Assert.That(maxAltitude, Is.LessThan(3072 + 1024), "cruise missile levels off near cruise altitude");
		}

		[Test]
		public void Ballistic_TiltsForTiltFrames()
		{
			var flight = new MissileFlight(V3(), WPos.Zero, WAngle.Zero);
			var target = new WPos(10240, 0, 0);
			for (var i = 0; i < 30; i++)
				flight.Tick(target, WDist.Zero);

			Assert.That(flight.Position, Is.EqualTo(WPos.Zero), "does not move while tilting");
			Assert.That(flight.Pitch.Angle, Is.InRange(54, 128));

			for (var i = 0; i < 31; i++)
				flight.Tick(target, WDist.Zero);

			Assert.That(flight.Phase, Is.EqualTo(MissileFlightPhase.Fly));
		}

		// Play-test 2026-11-05: the V3 rocket wobbled up and down on the horizontal leg because
		// dipping below cruise altitude re-entered the climb. Once it starts descending it must not climb again.
		[TestCase(12)]
		[TestCase(18)]
		public void Ballistic_DoesNotClimbAgainAfterDescending(int cells)
		{
			var target = new WPos(cells * 1024, 0, 0);
			var flight = new MissileFlight(V3(), WPos.Zero, (target - WPos.Zero).Yaw);
			var lastZ = 0;
			var descending = false;
			var reclimbs = 0;
			while (!flight.Tick(target, WDist.Zero))
			{
				var z = flight.Position.Z;
				if (z < lastZ)
					descending = true;
				else if (descending && z > lastZ)
				{
					reclimbs++;
					descending = false;
				}

				lastZ = z;
			}

			Assert.That(reclimbs, Is.Zero);
		}

		// Play-test 2026-11-08: the V3 should climb, fly level, and only come down near the target.
		[TestCase(5, 0)]
		[TestCase(12, 3)]
		[TestCase(18, 9)]
		public void V3Cruise_FliesLevelThenDivesNearTarget(int cells, int minimumLevelCells)
		{
			var p = V3();
			p.Profile = MissileFlightProfile.Cruise;
			var target = new WPos(cells * 1024, 0, 0);
			var flight = new MissileFlight(p, WPos.Zero, (target - WPos.Zero).Yaw);
			var levelDistance = 0;
			var last = WPos.Zero;
			var firstDescentX = -1;
			while (!flight.Tick(target, WDist.Zero))
			{
				var step = flight.Position - last;
				if (step.Z == 0 && step.HorizontalLength > 0)
					levelDistance += step.HorizontalLength;
				else if (step.Z < 0 && firstDescentX < 0)
					firstDescentX = flight.Position.X;

				last = flight.Position;
			}

			Assert.That((flight.Position - target).Length, Is.LessThan(256), $"landed at {flight.Position}");
			Assert.That(levelDistance, Is.GreaterThanOrEqualTo(minimumLevelCells * 1024));
			Assert.That(target.X - firstDescentX, Is.LessThanOrEqualTo(5 * 1024), "starts descending only close to the target");
		}

		// Play-test 2026-11-08: the V3 path should look like an arc: one smooth rise and fall, peaking mid-route.
		[TestCase(5)]
		[TestCase(12)]
		[TestCase(18)]
		public void V3Arc_RisesAndFallsSmoothly(int cells)
		{
			var p = V3();
			p.Profile = MissileFlightProfile.Arc;
			var target = new WPos(cells * 1024, 0, 0);
			var flight = new MissileFlight(p, WPos.Zero, (target - WPos.Zero).Yaw);
			var lastSigned = int.MaxValue;
			var peakZ = 0;
			var peakX = 0;
			var pitchRises = 0;
			var maxPitchStep = 0;
			while (!flight.Tick(target, WDist.Zero))
			{
				if (flight.Phase != MissileFlightPhase.Fly || flight.Speed == 0)
					continue;

				var signed = flight.Pitch.Angle > 512 ? flight.Pitch.Angle - 1024 : flight.Pitch.Angle;
				if (signed > lastSigned && lastSigned != int.MaxValue)
					pitchRises++;

				if (lastSigned != int.MaxValue)
					maxPitchStep = Math.Max(maxPitchStep, Math.Abs(signed - lastSigned));

				lastSigned = signed;
				if (flight.Position.Z > peakZ)
				{
					peakZ = flight.Position.Z;
					peakX = flight.Position.X;
				}
			}

			Assert.That((flight.Position - target).Length, Is.LessThan(256), $"landed at {flight.Position}");
			Assert.That(pitchRises, Is.Zero, "nose only ever moves downwards");
			Assert.That(peakX, Is.InRange(target.X * 35 / 100, target.X * 65 / 100), "peaks near the middle");
			Assert.That(peakZ, Is.InRange(target.X * 15 / 100, target.X * 30 / 100), "peak height scales with range");
			Assert.That(maxPitchStep, Is.LessThanOrEqualTo(4 + 2048 / cells / 40), "no sudden nose movement, including just before impact");
		}

		[Test]
		public void FollowLauncher_MovesOnlyBeforeIgnition()
		{
			var flight = new MissileFlight(Dmisl(), WPos.Zero, WAngle.Zero);
			var target = new WPos(0, -20480, 0);
			var moved = new WPos(100, 0, 0);
			flight.FollowLauncher(moved, WAngle.Zero);
			Assert.That(flight.Position, Is.EqualTo(moved));

			while (flight.Phase != MissileFlightPhase.Fly)
				flight.Tick(target, WDist.Zero);

			flight.Tick(target, WDist.Zero);
			var flying = flight.Position;
			flight.FollowLauncher(WPos.Zero, new WAngle(256));
			Assert.That(flight.Position, Is.EqualTo(flying));
			Assert.That(flight.Yaw, Is.Not.EqualTo(new WAngle(256)));
		}

		// Play-test 2026-11-18: the Dreadnought missile tilted around its middle, dipping its tail into the deck.
		// It should tilt around its tail, which stays where it was on the deck.
		[Test]
		public void Tilt_PivotsAroundTheTail()
		{
			var p = Dmisl();
			p.TiltPivot = new WDist(600);
			var yaw = new WAngle(100);
			var flight = new MissileFlight(p, WPos.Zero, yaw);
			var target = new WPos(0, -20480, 0);

			WPos Tail() => flight.Position - new WVec(0, -600 * flight.Pitch.Cos() / 1024, 0).Rotate(WRot.FromYaw(flight.Yaw))
				- new WVec(0, 0, 600 * flight.Pitch.Sin() / 1024);

			var tail = Tail();
			var rose = false;
			while (flight.Phase != MissileFlightPhase.Fly)
			{
				flight.Tick(target, WDist.Zero);
				Assert.That((Tail() - tail).Length, Is.LessThanOrEqualTo(2), $"tail moved at pitch {flight.Pitch.Angle}");
				rose |= flight.Position.Z > 0;
			}

			Assert.That(rose, Is.True, "the centre rises as the nose lifts");
		}

		// Play-test 2026-11-18: the Dreadnought missile turned to face its target while still on the deck.
		// It should lift in line with the ship and turn only once it flies.
		[Test]
		public void Lift_KeepsLauncherHeadingThenTurnsGradually()
		{
			var p = Dmisl();
			var shipYaw = WAngle.Zero;
			var flight = new MissileFlight(p, WPos.Zero, shipYaw);

			// Target off to the side of the ship.
			var target = new WPos(20480, 0, 0);
			while (flight.Phase != MissileFlightPhase.Fly)
			{
				flight.FollowLauncher(WPos.Zero, shipYaw);
				flight.Tick(target, WDist.Zero);
				Assert.That(flight.Yaw, Is.EqualTo(shipYaw), "keeps the ship's heading while lifting");
			}

			var before = flight.Yaw;
			flight.Tick(target, WDist.Zero);
			var turned = Math.Abs((flight.Yaw - before).Angle > 512 ? (flight.Yaw - before).Angle - 1024 : (flight.Yaw - before).Angle);
			Assert.That(turned, Is.InRange(1, p.YawTurnRate.Angle), "turns towards the target at the yaw turn rate");
		}

		[Test]
		public void Flight_IsDeterministic()
		{
			var target = new WPos(15000, 4000, 0);
			var a = Fly(V3(), target).Flight;
			var b = Fly(V3(), target).Flight;
			Assert.That(a.Position, Is.EqualTo(b.Position));
			Assert.That(a.Ticks, Is.EqualTo(b.Ticks));
		}
	}
}
