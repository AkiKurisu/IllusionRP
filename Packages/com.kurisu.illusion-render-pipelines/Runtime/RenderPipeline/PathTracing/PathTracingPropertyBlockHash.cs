using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Illusion.Rendering.PathTracing
{
    internal sealed class PathTracingPropertyBlockHash
    {
        private readonly MaterialPropertyBlock _block = new();

        private readonly List<Material> _materials = new();

        public int Compute(IReadOnlyList<Renderer> renderers)
        {
            int hash = 0;
            foreach (var renderer in renderers)
            {
                if (!renderer.HasPropertyBlock())
                    continue;

                renderer.GetSharedMaterials(_materials);
                renderer.GetPropertyBlock(_block);
                foreach (var material in _materials)
                    hash = HashCode.Combine(hash, renderer.GetInstanceID(), HashBlock(material));
                for (int i = 0; i < _materials.Count; i++)
                {
                    renderer.GetPropertyBlock(_block, i);
                    hash = HashCode.Combine(hash, i, HashBlock(_materials[i]));
                }
            }
            return hash;
        }

        private int HashBlock(Material material)
        {
            if (!material || _block.isEmpty)
                return 0;

            var shader = material.shader;
            int hash = 0;
            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                int id = shader.GetPropertyNameId(i);
                switch (shader.GetPropertyType(i))
                {
                    case ShaderPropertyType.Color:
                    case ShaderPropertyType.Vector:
                        hash = HashCode.Combine(hash, _block.GetVector(id));
                        break;
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range:
                        hash = HashCode.Combine(hash, _block.GetFloat(id));
                        break;
                    case ShaderPropertyType.Int:
                        hash = HashCode.Combine(hash, _block.GetInteger(id));
                        break;
                    case ShaderPropertyType.Texture:
                        var texture = _block.GetTexture(id);
                        hash = HashCode.Combine(hash, texture ? texture.GetInstanceID() : 0);
                        break;
                }
            }
            return hash;
        }
    }
}
