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
