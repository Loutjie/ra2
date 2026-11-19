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

using System.Collections.Generic;
using System.Linq;
using OpenRA.GameRules;
using OpenRA.Graphics;
using OpenRA.Mods.Common;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.RA2.Traits
{
	/// <summary>
	/// A launcher slot that can keep drawing a missile it launched until the missile flies, as part of the launcher's
	/// own model, so the missile is hidden behind the launcher's hull and superstructure correctly while it lifts.
	/// </summary>
	public interface ILauncherMissileSlot
	{
		/// <summary>Take over drawing the missile if it was launched from this slot.</summary>
		bool TryTake(Actor launcher, MissileActor missile);
	}

	[Desc("SPIKE: a flying, shootable missile actor (RA2 rocket locomotor).",
		"Spawned by the LaunchMissileActor projectile, which hands over the launching weapon's ProjectileArgs.",
		"On arrival the launching weapon's warheads are applied, attributed to the launcher.",
		"If the missile is killed in flight the weapon never fires, so the target takes no damage.",
		"Deliberately not IPositionable: the AI squad manager only recruits IPositionable actors.")]
	public class MissileActorInfo : TraitInfo, IOccupySpaceInfo, IFacingInfo
	{
		[Desc("Ticks the missile waits on the launcher before tilting (RA2 V3RocketPauseFrames / DMislPauseFrames).")]
		public readonly int PauseTicks = 0;

		[Desc("Ticks to tilt from PitchInitial to PitchFinal (RA2 V3RocketTiltFrames / DMislTiltFrames).")]
		public readonly int TiltTicks = 60;

		[Desc("Pitch on the launcher. RA2 fraction (0 = horizontal, 1 = vertical) * 256.")]
		public readonly WAngle PitchInitial = new(54);

		[Desc("Pitch at ignition. RA2 fraction * 256.")]
		public readonly WAngle PitchFinal = new(128);

		[Desc("Maximum pitch change per tick in flight (RA2 *TurnRate * 256).")]
		public readonly WAngle PitchTurnRate = new(13);

		[Desc("Maximum yaw change per tick in flight.")]
		public readonly WAngle YawTurnRate = new(16);

		[Desc("Speed added per tick after ignition.")]
		public readonly int Acceleration = 8;

		[Desc("Top speed.")]
		public readonly int MaxSpeed = 200;

		[Desc("Altitude at which the missile stops climbing (RA2 V3RocketAltitude / DMislAltitude, leptons * 4).")]
		public readonly WDist CruiseAltitude = new(3072);

		[Desc("LazyCurve, Cruise (Dreadnought) or Arc (V3).")]
		public readonly MissileFlightProfile Profile = MissileFlightProfile.LazyCurve;

		[Desc("Cruise profile: dive once the target is this far below the horizon.")]
		public readonly WAngle DiveAngle = new(128);

		[Desc("The tilt turns the missile around a point this far behind its centre (its tail), which stays in the launcher.",
			"0 tilts it around its centre.")]
		public readonly WDist TiltPivot = WDist.Zero;

		[Desc("Pitch to climb at after ignition, until cruise altitude. Defaults to PitchFinal, the end of the lift.")]
		public readonly WAngle? ClimbPitch = null;

		[Desc("Detonate after this many ticks even if the target was not reached.")]
		public readonly int MaxTicks = 1500;

		[Desc("Added to the draw order once the missile lifts off its launcher, so it is not drawn behind nearby buildings.")]
		public readonly int FlyingZOffset = 8192;

		[GrantedConditionReference]
		[Desc("Condition granted once the missile has left the launcher (after the pause and tilt).")]
		public readonly string FlyingCondition = null;

		public MissileFlightParameters FlightParameters => new()
		{
			PauseTicks = PauseTicks,
			TiltTicks = TiltTicks,
			PitchInitial = PitchInitial,
			PitchFinal = PitchFinal,
			PitchTurnRate = PitchTurnRate,
			YawTurnRate = YawTurnRate,
			Acceleration = Acceleration,
			MaxSpeed = MaxSpeed,
			CruiseAltitude = CruiseAltitude,
			Profile = Profile,
			DiveAngle = DiveAngle,
			MaxTicks = MaxTicks,
			TiltPivot = TiltPivot,
			ClimbPitch = ClimbPitch,
		};

		public override object Create(ActorInitializer init) { return new MissileActor(init, this); }

		static readonly Dictionary<CPos, SubCell> NoCells = [];
		IReadOnlyDictionary<CPos, SubCell> IOccupySpaceInfo.OccupiedCells(ActorInfo info, CPos location, SubCell subCell) { return NoCells; }
		bool IOccupySpaceInfo.SharesCell => false;
		WAngle IFacingInfo.GetInitialFacing() { return WAngle.Zero; }
	}

	public class MissileActor : IOccupySpace, IFacing, ITick, ISync, INotifyAddedToWorld, INotifyRemovedFromWorld, IRenderModifier
	{
		readonly MissileActorInfo info;
		ProjectileArgs launchArgs;
		MissileFlight flight;
		WPos target;
		int flyingToken = Actor.InvalidConditionToken;
		Actor launcher;
		IFacing launcherFacing;
		WAngle launchFacing;
		WVec launcherOffset;
		WAngle launchYaw;
		bool drawnByLauncher;

		[VerifySync]
		public WPos CenterPosition { get; private set; }

		public CPos TopLeft { get; private set; }

		public Actor Self { get; }

		public MissileActor(ActorInitializer init, MissileActorInfo info)
		{
			this.info = info;
			Self = init.Self;
			CenterPosition = init.GetValue<CenterPositionInit, WPos>(WPos.Zero);
			TopLeft = init.World.Map.CellContaining(CenterPosition);
			Facing = init.GetValue<FacingInit, WAngle>(WAngle.Zero);
		}

		public MissileFlightPhase Phase => flight?.Phase ?? MissileFlightPhase.Pause;

		/// <summary>Called by LaunchMissileActor before the actor is added to the world.</summary>
		public void Launch(ProjectileArgs args)
		{
			launchArgs = args;
			target = args.GuidedTarget.IsValidFor(args.SourceActor) ? args.GuidedTarget.CenterPosition : args.PassiveTarget;

			launcher = args.SourceActor;
			launcherFacing = launcher.TraitOrDefault<IFacing>();
			launchFacing = launcherFacing?.Facing ?? WAngle.Zero;
			launcherOffset = CenterPosition - launcher.CenterPosition;

			// Lift in line with the launcher, like the missile drawn in its slot; the flight turns to the target.
			launchYaw = launcherFacing?.Facing ?? (target - CenterPosition).Yaw;
			Facing = launchYaw;
			flight = new MissileFlight(info.FlightParameters, CenterPosition, Facing);

			foreach (var slot in launcher.TraitsImplementing<ILauncherMissileSlot>())
			{
				if (slot.TryTake(launcher, this))
				{
					drawnByLauncher = true;
					break;
				}
			}
		}

		void ITick.Tick(Actor self)
		{
			if (flight == null || self.IsDead || self.WillDispose)
				return;

			if (launcher is { IsInWorld: true, IsDead: false })
			{
				var turned = (launcherFacing?.Facing ?? WAngle.Zero) - launchFacing;
				flight.FollowLauncher(launcher.CenterPosition + launcherOffset.Rotate(WRot.FromYaw(turned)), launchYaw + turned);
			}

			var map = self.World.Map;
			var ground = new WDist(CenterPosition.Z - map.DistanceAboveTerrain(CenterPosition).Length);
			var arrived = flight.Tick(target, ground);

			SetCenterPosition(self, flight.Position);
			Facing = flight.Yaw;

			if (flight.Phase == MissileFlightPhase.Fly && flyingToken == Actor.InvalidConditionToken && !string.IsNullOrEmpty(info.FlyingCondition))
				flyingToken = self.GrantCondition(info.FlyingCondition);

			if (arrived)
				Detonate(self);
		}

		void Detonate(Actor self)
		{
			// The launching weapon's warheads, attributed to the launcher (kill credit, AI 'under attack' response).
			var args = new WarheadArgs(launchArgs)
			{
				ImpactPosition = CenterPosition,
				ImpactOrientation = flight.Orientation,
				WeaponTarget = Target.FromPos(CenterPosition),
			};

			launchArgs.Weapon.Impact(Target.FromPos(CenterPosition), args);

			// Dispose rather than Kill: the missile's own death effects (shot-down explosion) must not play on arrival.
			self.Dispose();
		}

		void SetCenterPosition(Actor self, WPos pos)
		{
			CenterPosition = pos;
			TopLeft = self.World.Map.CellContaining(pos);
			self.World.UpdateMaps(self, this);
		}

		/// <summary>True while the launcher draws this missile as part of its own model (before it flies).</summary>
		public bool IsDrawnByLauncher => drawnByLauncher && Phase != MissileFlightPhase.Fly && Phase != MissileFlightPhase.Arrived
			&& launcher is { IsInWorld: true, IsDead: false };

		IEnumerable<IRenderable> IRenderModifier.ModifyRender(Actor self, WorldRenderer wr, IEnumerable<IRenderable> r)
		{
			if (IsDrawnByLauncher)
				return [];

			return Phase != MissileFlightPhase.Pause ? r.Select(a => a.WithZOffset(a.ZOffset + info.FlyingZOffset)) : r;
		}

		IEnumerable<Rectangle> IRenderModifier.ModifyScreenBounds(Actor self, WorldRenderer wr, IEnumerable<Rectangle> bounds) { return bounds; }

		public (CPos Cell, SubCell SubCell)[] OccupiedCells() { return []; }

		public WAngle TurnSpeed => info.YawTurnRate;

		public WAngle Facing { get; set; }

		public WRot Orientation => flight?.Orientation ?? WRot.FromYaw(Facing);

		void INotifyAddedToWorld.AddedToWorld(Actor self) { self.World.AddToMaps(self, this); }
		void INotifyRemovedFromWorld.RemovedFromWorld(Actor self) { self.World.RemoveFromMaps(self, this); }
	}
}
