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
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.RA2.Traits.SupportPowers;
using OpenRA.Scripting;
using OpenRA.Traits;

namespace OpenRA.Mods.RA2.Scripting
{
	[ScriptPropertyGroup("Support Powers")]
	public class WeatherControlProperties : ScriptActorProperties, Requires<WeatherControlSupportPowerInfo>
	{
		readonly WeatherControlSupportPower power;

		public WeatherControlProperties(ScriptContext context, Actor self)
			: base(context, self)
		{
			power = self.TraitsImplementing<WeatherControlSupportPower>().First();
		}

		[Desc("Start the actor's weather storm on the target cell, whether or not the power is charged.")]
		public void ActivateWeatherControl(CPos target)
		{
			var order = new Order(power.Info.OrderName, Self, Target.FromCell(Self.World, target), false);
			power.Activate(Self, order, Self.Owner.PlayerActor.Trait<SupportPowerManager>());

			foreach (var notify in Self.TraitsImplementing<INotifySupportPower>())
				notify.Activated(Self, power.Info.OrderName);
		}
	}
}
