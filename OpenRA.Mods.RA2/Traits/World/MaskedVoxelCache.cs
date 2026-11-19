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
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.RA2.Graphics;
using OpenRA.Traits;

namespace OpenRA.Mods.RA2.Traits
{
	[TraitLocation(SystemActors.World | SystemActors.EditorWorld)]
	[Desc("Loads voxel models, like the engine's VoxelCache, and can clear regions of chosen models at load time.",
		"Used to take the loaded missiles out of launcher hull models, so a separately drawn missile can leave its slot.",
		"The masks are applied in memory to the player's own files; no modified art is written anywhere.",
		"Replaces VoxelCache on the world actor.")]
	public sealed class MaskedVoxelCacheInfo : TraitInfo, IModelCacheInfo, IRulesetLoaded
	{
		public readonly int SheetSize = 2048;

		[FieldLoader.LoadUsing(nameof(LoadMasks))]
		[Desc("Voxel file name (without .vxl) -> named mask shapes (VoxelMaskShape: Origin, Pitch, Start, End, Radius,",
			"MinSide, MaxSide, MinUp, MaxUp), in that file's voxel-grid coordinates.")]
		public readonly IReadOnlyDictionary<string, ImmutableArray<VoxelMaskShape>> Masks = null;

		static object LoadMasks(MiniYaml yaml)
		{
			var masks = new Dictionary<string, ImmutableArray<VoxelMaskShape>>();
			var node = yaml.NodeWithKeyOrDefault("Masks");
			if (node != null)
				foreach (var model in node.Value.Nodes)
					masks.Add(model.Key, model.Value.Nodes.Select(s => FieldLoader.Load<VoxelMaskShape>(s.Value)).ToImmutableArray());

			return masks;
		}

		void IRulesetLoaded<ActorInfo>.RulesetLoaded(Ruleset rules, ActorInfo info)
		{
			// Fail loudly when a mask names a model that no model sequence loads: it would silently do nothing.
			var used = rules.ModelSequences
				.SelectMany(kv => kv.Value.Value.Nodes.Select(s => MaskedVoxelCache.ModelFiles(kv.Key, s.Value).Vxl))
				.ToHashSet();

			foreach (var model in Masks.Keys)
				if (!used.Contains(model))
					throw new YamlException($"{nameof(MaskedVoxelCache)} has a mask for `{model}`, but no model sequence loads `{model}.vxl`.");
		}

		public override object Create(ActorInitializer init) { return new MaskedVoxelCache(this, init.Self); }
	}

	public sealed class MaskedVoxelCache : IModelCache, INotifyActorDisposing, IDisposable
	{
		readonly MaskedVoxelLoader loader;
		readonly Dictionary<string, Dictionary<string, IModel>> models = [];

		public MaskedVoxelCache(MaskedVoxelCacheInfo info, Actor self)
		{
			var map = self.World.Map;
			loader = new MaskedVoxelLoader(map, info.SheetSize, info.Masks);
			foreach (var kv in map.Rules.ModelSequences)
			{
				Game.ModData.LoadScreen.Display();
				try
				{
					models.Add(kv.Key, kv.Value.Value.ToDictionary(my => LoadVoxel(kv.Key, my)));
				}
				catch (FileNotFoundException ex) when (!IsMasked(kv.Key, kv.Value.Value, info))
				{
					// Like the engine's VoxelCache: eat missing files, except for masked models.
					Console.WriteLine(ex);
					Log.Write("debug", ex.Message);
				}
			}

			loader.RefreshBuffer();
			loader.Finish();
		}

		static bool IsMasked(string unit, MiniYaml sequences, MaskedVoxelCacheInfo info)
		{
			return sequences.Nodes.Any(s => info.Masks.ContainsKey(ModelFiles(unit, s.Value).Vxl));
		}

		/// <summary>The engine's VoxelCache convention: a sequence value of "vxl" or "vxl, hva", else the unit name.</summary>
		public static (string Vxl, string Hva) ModelFiles(string unit, MiniYaml info)
		{
			var vxl = unit;
			var hva = unit;
			if (info.Value != null)
			{
				var fields = info.Value.Split(',', StringSplitOptions.RemoveEmptyEntries);
				if (fields.Length >= 1)
					vxl = hva = fields[0].Trim();

				if (fields.Length >= 2)
					hva = fields[1].Trim();
			}

			return (vxl, hva);
		}

		IModel LoadVoxel(string unit, MiniYaml info)
		{
			var (vxl, hva) = ModelFiles(unit, info);
			return loader.Load(vxl, hva);
		}

		public IModel GetModel(string model)
		{
			return loader.Load(model, model);
		}

		public IModel GetModelSequence(string model, string sequence)
		{
			if (models.TryGetValue(model, out var sequences))
			{
				if (sequences.TryGetValue(sequence, out var m))
					return m;

				throw new InvalidOperationException($"Model `{model}` does not have a sequence `{sequence}`");
			}

			throw new InvalidOperationException($"Model `{model}` does not have any sequences defined.");
		}

		public bool HasModelSequence(string model, string sequence)
		{
			if (!models.TryGetValue(model, out var sequences))
				throw new InvalidOperationException($"Model `{model}` does not have any sequences defined.");

			return sequences.ContainsKey(sequence);
		}

		public IVertexBuffer<ModelVertex> VertexBuffer => loader.VertexBuffer;

		public void Dispose()
		{
			loader.Dispose();
		}

		void INotifyActorDisposing.Disposing(Actor a)
		{
			Dispose();
		}
	}
}
