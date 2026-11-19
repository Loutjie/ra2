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
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.RA2.Traits
{
	[Desc("Shows the owner and its allies the attack lines of enemy units near this actor (RA2 Psychic Sensor,",
		"PsychicDetectionRadius). Lines are drawn over the shroud and the fog.")]
	public class PsychicSensorInfo : ConditionalTraitInfo
	{
		[Desc("Enemy units within this distance of the actor have their attack lines shown.")]
		public readonly WDist Range = WDist.FromCells(15);

		[Desc("Width (in pixels) of the attack lines.")]
		public readonly int LineWidth = 1;

		[Desc("Width (in pixels) of the marker at each end of a line.")]
		public readonly int MarkerWidth = 2;

		public override object Create(ActorInitializer init) { return new PsychicSensor(this); }
	}

	public class PsychicSensor : ConditionalTrait<PsychicSensorInfo>, IRenderAnnotations
	{
		readonly List<IRenderable> lines = [];

		public PsychicSensor(PsychicSensorInfo info)
			: base(info) { }

		bool ShouldRender(Actor self)
		{
			if (IsTraitDisabled || !self.IsInWorld || !Ui.WidgetsVisible)
				return false;

			if (Game.Settings.Game.TargetLines == TargetLinesType.Disabled)
				return false;

			var viewer = self.World.RenderPlayer;
			return viewer != null && self.Owner.IsAlliedWith(viewer);
		}

		IEnumerable<IRenderable> IRenderAnnotations.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			if (!ShouldRender(self))
				return [];

			lines.Clear();
			foreach (var unit in self.World.FindActorsInCircle(self.CenterPosition, Info.Range))
			{
				if (unit.IsDead || !unit.IsInWorld || unit.CurrentActivity == null
						|| self.Owner.RelationshipWith(unit.Owner) != PlayerRelationship.Enemy)
					continue;

				for (var a = unit.CurrentActivity; a != null; a = a.NextActivity)
				{
					if (a.IsCanceling)
						continue;

					foreach (var n in a.TargetLineNodes(unit))
					{
						if (!IsAttack(unit, n.Target))
							continue;

						lines.Add(new TargetLineRenderable([unit.CenterPosition, n.Target.CenterPosition],
							n.Color, Info.LineWidth, Info.MarkerWidth));
						break;
					}
				}
			}

			return lines.Count == 0 ? [] : lines.ToArray();
		}

		// An order against an enemy actor: attacks, and also captures and infiltrations.
		// Moves, guards and attacks on the ground are left out.
		static bool IsAttack(Actor unit, in Target target)
		{
			var owner = target.Type switch
			{
				TargetType.Actor => target.Actor.Owner,
				TargetType.FrozenActor => target.FrozenActor.Owner,
				_ => null,
			};

			return owner != null && unit.Owner.RelationshipWith(owner) == PlayerRelationship.Enemy;
		}

		bool IRenderAnnotations.SpatiallyPartitionable => false;
	}
}
