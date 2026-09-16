using System;
using Scripts.Runtime.Sdf.Data;
using Scripts.Runtime.Sdf.ScriptableObjects;
using Unity.Collections;
using UnityEngine;

namespace Scripts.Runtime.Sdf.Utils
{
    /// <summary>
    /// The one place where the baked Texture3D turns into something a job can read.
    ///
    /// A Burst job cannot touch a UnityEngine.Texture3D. What it can touch is a NativeArray, and a
    /// readable texture exposes its CPU-side pixels as one through GetPixelData. That array is a
    /// view whose lifetime is the texture's, so it is copied once into an array the caller owns:
    /// explicit lifetime, no dependency on the asset staying loaded and untouched, and a chance to
    /// validate the data before a job ever indexes into it. One memcpy of a few megabytes at
    /// startup, never on the hot path.
    /// </summary>
    public static class SdfBakedFieldLoader
    {
        /// <summary>
        /// Copies the asset's samples into a new array. The caller owns the returned
        /// <see cref="SdfBakedFieldData.Distances"/> and must dispose it.
        /// </summary>
        public static SdfBakedFieldData Load(SdfGridTextureSO asset, Allocator allocator)
        {
            if (asset == null)
            {
                throw new ArgumentNullException(nameof(asset), "No SDF asset assigned.");
            }

            if (!asset.IsBaked)
            {
                throw new InvalidOperationException(
                    $"{asset.name} has no baked texture. Select the SDF bake area and press Bake.");
            }

            Texture3D texture = asset.Texture;
            SdfGridData grid = asset.GridData;

            // Each check turns a corrupt or hand-edited asset into a sentence instead of a
            // reinterpretation of the wrong bytes or an out-of-range read inside a job.
            if (texture.format != TextureFormat.RFloat)
            {
                throw new InvalidOperationException(
                    $"{asset.name}: expected an RFloat texture, found {texture.format}. Re-bake.");
            }

            if (!texture.isReadable)
            {
                throw new InvalidOperationException(
                    $"{asset.name}: the texture is not CPU-readable, so its samples cannot reach a job. Re-bake.");
            }

            NativeArray<float> view = texture.GetPixelData<float>(mipLevel: 0);
            if (view.Length != grid.SampleCount)
            {
                throw new InvalidOperationException(
                    $"{asset.name}: texture holds {view.Length} samples but the grid describes {grid.SampleCount}. Re-bake.");
            }

            // This constructor copies. Same memory order on both sides (x fastest, then y, then z),
            // so no reshuffling: the texture's bytes are the array's bytes.
            var distances = new NativeArray<float>(view, allocator);

            return new SdfBakedFieldData
            {
                Grid = grid,
                Distances = distances,
            };
        }
    }
}
