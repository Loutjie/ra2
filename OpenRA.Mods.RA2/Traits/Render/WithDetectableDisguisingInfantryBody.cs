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
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.RA2.Traits.Render
{
	[Desc("Infantry body that shows the actor's disguise, like WithDisguisingInfantryBody. Enemies with a",
		"DisguiseDetector in range see the real actor in its real owner's colours instead.")]
	sealed class WithDetectableDisguisingInfantryBodyInfo : WithInfantryBodyInfo
	{
		public override object Create(ActorInitializer init) { return new WithDetectableDisguisingInfantryBody(init, this); }

		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			base.RulesetLoaded(rules, ai);
			EngineDisguise.RequireOn(ai, nameof(WithDetectableDisguisingInfantryBody));
		}
	}

	sealed class WithDetectableDisguisingInfantryBody : WithInfantryBody, IRenderModifier
	{
		readonly RenderSprites rs;
		IEffectiveOwner disguise;
		ActorInfo disguiseActor;
		Player disguisePlayer;
		bool revealed;
		WithInfantryBodyInfo disguiseInfantryBody;
		string disguiseImage;

		public WithDetectableDisguisingInfantryBody(ActorInitializer init, WithDetectableDisguisingInfantryBodyInfo info)
			: base(init, info)
		{
			rs = init.Self.Trait<RenderSprites>();
		}

		protected override void Created(Actor self)
		{
			disguise = EngineDisguise.Get(self);
			base.Created(self);
		}

		protected override WithInfantryBodyInfo GetDisplayInfo()
		{
			return disguiseInfantryBody ?? Info;
		}

		protected override void Tick(Actor self)
		{
			var asActor = EngineDisguise.AsActor(disguise);
			var asPlayer = disguise.Owner;
			var isRevealed = asPlayer != null && DisguiseDetection.IsRevealed(self);

			if (asActor != disguiseActor || asPlayer != disguisePlayer || isRevealed != revealed)
			{
				// Force actor back to the stand state to avoid mismatched sequences
				PlayStandAnimation(self);

				disguiseActor = asActor;
				disguisePlayer = asPlayer;
				revealed = isRevealed;
				disguiseImage = null;
				disguiseInfantryBody = null;

				if (disguisePlayer != null && !revealed)
				{
					var renderSprites = disguiseActor.TraitInfoOrDefault<RenderSpritesInfo>();
					var infantryBody = disguiseActor.TraitInfos<WithInfantryBodyInfo>()
						.FirstOrDefault(t => t.EnabledByDefault);
					if (renderSprites != null && infantryBody != null)
					{
						disguiseImage = renderSprites.GetImage(disguiseActor, disguisePlayer.Faction.InternalName);
						disguiseInfantryBody = infantryBody;
					}
				}

				var sequence = DefaultAnimation.GetRandomExistingSequence(GetDisplayInfo().StandSequences, Game.CosmeticRandom);
				if (sequence != null)
					DefaultAnimation.ChangeImage(disguiseImage ?? rs.GetImage(self), sequence);

				rs.UpdatePalette();
			}

			base.Tick(self);
		}

		IEnumerable<IRenderable> IRenderModifier.ModifyRender(Actor self, WorldRenderer wr, IEnumerable<IRenderable> r)
		{
			// RenderSprites colours a disguised actor with the disguise owner's player palette for every viewer.
			// Swap it for the real owner's palette when this viewer sees through the disguise.
			if (!revealed || disguisePlayer == null || Info.Palette != null)
				return r;

			var playerPalette = rs.Info.Palette == null ? rs.Info.PlayerPalette : null;
			if (playerPalette == null)
				return r;

			var disguisePalette = wr.Palette(playerPalette + disguisePlayer.InternalName);
			var ownerPalette = wr.Palette(playerPalette + self.Owner.InternalName);
			return r.Select(x => x is IPalettedRenderable p && p.Palette == disguisePalette ? p.WithPalette(ownerPalette) : x);
		}

		IEnumerable<Rectangle> IRenderModifier.ModifyScreenBounds(Actor self, WorldRenderer wr, IEnumerable<Rectangle> r)
		{
			return r;
		}
	}
}
