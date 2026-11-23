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

using System.Linq;
using OpenRA.GameRules;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.RA2.Traits.SupportPowers
{
	public class WeatherControlSupportPowerInfo : SupportPowerInfo, IRulesetLoaded
	{
		[WeaponReference]
		[FieldLoader.Require]
		[Desc("What weapon will attack the target in range?")]
		public readonly string Weapon = null;

		[WeaponReference]
		[Desc("Visual-only weapon fired around the target during the build-up (the gathering clouds).")]
		public readonly string BuildUpWeapon = null;

		[Desc("Ticks between launch and the first strike (RA2 [General] LightningDeferment).",
			"The sky darkens and clouds gather over this time.")]
		public readonly int BuildUpDelay = 0;

		[Desc("Ticks between gathering clouds at the start of the build-up. The interval shrinks to ScatterDelay by its end.")]
		public readonly int BuildUpCloudInterval = 20;

		[Desc("Corresponds to `Type` from `WeatherPaletteEffect` on the world actor.")]
		public readonly string PaletteEffectType = null;

		[Desc("Active duration during the time clouds can spawn.")]
		public readonly int Duration = 180;

		[Desc("Delay between direct hits.")]
		public readonly int HitDelay = 10;

		[Desc("Delay between random cloud spawning.")]
		public readonly int ScatterDelay = 5;

		[Desc("Amount of random clouds spawned.")]
		public readonly int ScatterCount = 1;

		[Desc("Spawn offset interval for the clouds relative to the target in X direction.")]
		public int2 OffsetsX = new(-5120, 5120);

		[Desc("Spawn offset interval for the clouds relative to the target in Y direction.")]
		public int2 OffsetsY = new(-5120, 5120);

		public override object Create(ActorInitializer init)
		{
			return new WeatherControlSupportPower(init.Self, this);
		}

		public WeaponInfo WeaponInfo { get; private set; }
		public WeaponInfo BuildUpWeaponInfo { get; private set; }

		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			var weaponToLower = (Weapon ?? string.Empty).ToLowerInvariant();
			if (!rules.Weapons.TryGetValue(weaponToLower, out var weapon))
				throw new YamlException($"Weapons Ruleset does not contain an entry '{weaponToLower}'");

			WeaponInfo = weapon;

			if (BuildUpWeapon != null)
			{
				var buildUpToLower = BuildUpWeapon.ToLowerInvariant();
				if (!rules.Weapons.TryGetValue(buildUpToLower, out var buildUpWeapon))
					throw new YamlException($"Weapons Ruleset does not contain an entry '{buildUpToLower}'");

				BuildUpWeaponInfo = buildUpWeapon;
			}

			if (BuildUpDelay < 0)
				throw new YamlException("WeatherControlSupportPower.BuildUpDelay must not be negative.");

			base.RulesetLoaded(rules, ai);
		}
	}

	public class WeatherControlSupportPower : SupportPower, ITick
	{
		readonly WeatherControlSupportPowerInfo info;

		int hitDelay;
		int scatterDelay;
		int duration;
		int buildUpElapsed;
		int cloudDelay;
		bool launched;
		WPos targetPos;

		public WeatherControlSupportPower(Actor self, WeatherControlSupportPowerInfo info)
			: base(self, info)
		{
			this.info = info;
			hitDelay = info.HitDelay;
			scatterDelay = info.ScatterDelay;
		}

		WVec RandomOffset(World world)
		{
			var x = world.SharedRandom.Next(info.OffsetsX.X, info.OffsetsX.Y);
			var y = world.SharedRandom.Next(info.OffsetsY.X, info.OffsetsY.Y);
			return new WVec(x, y, 0);
		}

		public override void Activate(Actor self, Order order, SupportPowerManager manager)
		{
			base.Activate(self, order, manager);

			targetPos = order.Target.CenterPosition;

			self.World.AddFrameEndTask(w =>
			{
				PlayLaunchSounds();

				launched = true;
				duration = info.Duration;
				hitDelay = info.HitDelay;
				scatterDelay = info.ScatterDelay;
				buildUpElapsed = 0;
				cloudDelay = 0;

				if (!string.IsNullOrEmpty(info.PaletteEffectType))
				{
					var paletteEffects = w.WorldActor.TraitsImplementing<WeatherPaletteEffect>().Where(p => p.Info.Type == info.PaletteEffectType);
					foreach (var paletteEffect in paletteEffects)
					{
						if (info.BuildUpDelay > 0)
							paletteEffect.BuildUp(info.BuildUpDelay);
						else
							paletteEffect.Enable(-1);
					}
				}
			});
		}

		void TickBuildUp(Actor self)
		{
			if (info.BuildUpWeaponInfo != null && --cloudDelay < 0)
			{
				cloudDelay = LightningStormBuildUp.CloudInterval(buildUpElapsed, info.BuildUpDelay, info.BuildUpCloudInterval, info.ScatterDelay);
				var warheadArgs = new WarheadArgs()
				{
					SourceActor = self
				};

				info.BuildUpWeaponInfo.Impact(Target.FromPos(targetPos + RandomOffset(self.World)), warheadArgs);
			}

			buildUpElapsed++;
		}

		void ITick.Tick(Actor self)
		{
			if (!launched)
				return;

			if (buildUpElapsed < info.BuildUpDelay)
			{
				TickBuildUp(self);
				return;
			}

			if (--duration < 0)
				launched = false;

			if (--hitDelay < 0)
			{
				hitDelay = info.HitDelay;

				var warheadArgs = new WarheadArgs()
				{
					SourceActor = self
				};

				info.WeaponInfo.Impact(Target.FromPos(targetPos), warheadArgs);
			}

			if (--scatterDelay < 0)
			{
				scatterDelay = info.ScatterDelay;

				for (var i = 0; i < info.ScatterCount; i++)
				{
					var offset = RandomOffset(self.World);
					var newPos = targetPos + offset;
					var scatterTarget = Target.FromPos(newPos);

					var warheadArgs = new WarheadArgs()
					{
						SourceActor = self
					};

					info.WeaponInfo.Impact(scatterTarget, warheadArgs);
				}
			}

			if (info.WeaponInfo.Report != null && info.WeaponInfo.Report.Length != 0)
				Game.Sound.Play(SoundType.World, info.WeaponInfo.Report.Random(self.World.LocalRandom), targetPos);
		}
	}
}
