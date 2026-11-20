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

using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Traits.Render;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.RA2.Traits.Render
{
	[Desc("Creates the illusion of another actor. Hidden from enemies with a DisguiseDetector in range.")]
	sealed class WithMirageSpriteBodyInfo : WithSpriteBodyInfo, Requires<MirageInfo>
	{
		public override object Create(ActorInitializer init) { return new WithMirageSpriteBody(init, this); }
	}

	// Not a WithSpriteBody subclass: that registers its animation with a visibility check this trait cannot extend.
	sealed class WithMirageSpriteBody : PausableConditionalTrait<WithMirageSpriteBodyInfo>, ITick, INotifyDamageStateChanged, IAutoMouseBounds
	{
		readonly Mirage mirage;
		readonly RenderSprites renderSprites;
		readonly Animation defaultAnimation;
		readonly Animation boundsAnimation;
		ActorInfo disguiseActor;
		Player disguisePlayer;
		string disguiseImage;

		public WithMirageSpriteBody(ActorInitializer init, WithMirageSpriteBodyInfo info)
			: base(info)
		{
			var self = init.Self;
			renderSprites = self.Trait<RenderSprites>();
			mirage = self.Trait<Mirage>();

			bool Paused() => IsTraitPaused &&
				defaultAnimation.CurrentSequence.Name == NormalizeSequence(self, Info.Sequence);

			defaultAnimation = new Animation(init.World, renderSprites.GetImage(self), Paused);
			renderSprites.Add(new AnimationWithOffset(defaultAnimation, null,
				() => IsTraitDisabled || DisguiseDetection.IsRevealed(self)), info.Palette, info.IsPlayerPalette);

			// Cache the bounds from the default sequence to avoid flickering when the animation changes
			boundsAnimation = new Animation(init.World, renderSprites.GetImage(self), Paused);
			boundsAnimation.PlayRepeating(info.Sequence);
		}

		string NormalizeSequence(Actor self, string sequence)
		{
			return RenderSprites.NormalizeSequence(defaultAnimation, self.GetDamageState(), sequence);
		}

		protected override void TraitEnabled(Actor self)
		{
			defaultAnimation.PlayRepeating(NormalizeSequence(self, Info.Sequence));
		}

		void ITick.Tick(Actor self)
		{
			if (IsTraitDisabled || IsTraitPaused || mirage.ActorType == null)
				return;

			if (mirage.ActorType != disguiseActor || mirage.Owner != disguisePlayer)
			{
				disguiseActor = mirage.ActorType;
				disguisePlayer = mirage.Owner;
				disguiseImage = null;

				if (disguisePlayer != null)
				{
					var renderSprites = disguiseActor.TraitInfoOrDefault<RenderSpritesInfo>();
					if (renderSprites != null)
						disguiseImage = renderSprites.GetImage(disguiseActor, disguisePlayer.Faction.InternalName);
				}

				var withSpriteBody = disguiseActor.TraitInfoOrDefault<WithSpriteBodyInfo>();
				if (withSpriteBody != null && disguiseImage != null)
				{
					defaultAnimation.PlayRepeating(NormalizeSequence(self, withSpriteBody.Sequence));
					defaultAnimation.ChangeImage(disguiseImage, withSpriteBody.Sequence);
				}

				renderSprites.UpdatePalette();
			}
		}

		void INotifyDamageStateChanged.DamageStateChanged(Actor self, AttackInfo e)
		{
			if (defaultAnimation.CurrentSequence != null)
				defaultAnimation.ReplaceAnim(NormalizeSequence(self, defaultAnimation.CurrentSequence.Name));
		}

		Rectangle IAutoMouseBounds.AutoMouseoverBounds(Actor self, WorldRenderer wr)
		{
			return boundsAnimation.ScreenBounds(wr, self.CenterPosition, WVec.Zero);
		}
	}
}
