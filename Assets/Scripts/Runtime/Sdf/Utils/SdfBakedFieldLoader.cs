using System;
using Scripts.Runtime.Sdf.Data;
using Scripts.Runtime.Sdf.ScriptableObjects;
using Unity.Collections;
using UnityEngine;

namespace Scripts.Runtime.Sdf.Utils
{
    /// <summary>
    /// Copies the asset's samples into a Native Array. The caller owns the returned
    /// <see cref="SdfBakedFieldData.Distances"/> and must dispose it.
    /// </summary>
    public static class SdfBakedFieldLoader
    {
        public static SdfBakedFieldData Load(SdfGridTextureSO fieldAsset, Allocator allocator)
        {
            if (fieldAsset == null)
            {
                throw new ArgumentNullException(nameof(fieldAsset), "No SDF asset assigned.");
            }

            if (!fieldAsset.IsBaked)
            {
                throw new InvalidOperationException(
                    $"{fieldAsset.name} has no baked texture. Select the SDF bake area and press Bake.");
            }

            var texture = fieldAsset.Texture;
            var grid = fieldAsset.GridData;

            if (texture.format != TextureFormat.RFloat)
            {
                throw new InvalidOperationException(
                    $"{fieldAsset.name}: expected an RFloat texture, found {texture.format}. Re-bake.");
            }

            if (!texture.isReadable)
            {
                throw new InvalidOperationException(
                    $"{fieldAsset.name}: the texture is not CPU-readable, so its samples cannot reach a job. Re-bake.");
            }

            var view = texture.GetPixelData<float>(mipLevel: 0);
            if (view.Length != grid.SampleCount)
            {
                throw new InvalidOperationException(
                    $"{fieldAsset.name}: texture holds {view.Length} samples but the grid describes {grid.SampleCount}. Re-bake.");
            }

            var distances = new NativeArray<float>(view, allocator);

            return new SdfBakedFieldData
            {
                Grid = grid,
                Distances = distances,
            };
        }
    }
}
