using System;
using System.Linq;
using Illusion.Rendering.PRTGI;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace Illusion.Rendering.Editor
{
    internal static class PRTCaptureMaterial
    {
        internal static Material Create(Material source, Renderer renderer, int submesh, uint key,
            out MaterialPropertyBlock properties, out int pass, out bool solid)
        {
            EnsureCompiled(source.shader);
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block, submesh);
            if (block.isEmpty) renderer.GetPropertyBlock(block);
            uint layers = renderer.renderingLayerMask;
            Vector4 metadata = new(layers & 0xFFFFu, layers >> 16, key, renderer.gameObject.layer);
            pass = source.FindPass("ForwardGBuffer");
            solid = IsSolid(source);
            if (pass < 0)
                throw new NotSupportedException($"PRT capture requires ForwardGBuffer for '{source.shader.name}' ({source.name}).");
            properties = block;
            properties.SetVector("_PRTMetadata", metadata);
            return new Material(source) { hideFlags = HideFlags.HideAndDontSave };
        }

        private static bool IsSolid(Material material)
        {
            if (material.doubleSidedGI) return false;
            if (material.IsKeywordEnabled("_ALPHATEST_ON") || material.GetTag("RenderType", false) == "Transparent" ||
                material.GetTag("RenderType", false) == "TransparentCutout") return false;
            if (material.HasProperty("_Surface") && material.GetFloat("_Surface") != 0) return false;
            if (material.HasProperty("_Cull") && material.GetInt("_Cull") != (int)CullMode.Back) return false;
            if (material.HasProperty("_CullMode") && material.GetInt("_CullMode") != (int)CullMode.Back) return false;
            return true;
        }
        private static void EnsureCompiled(Shader shader)
        {
            if (!shader) throw new InvalidOperationException("PRT capture shader is unavailable.");
            ShaderMessage[] errors = ShaderUtil.GetShaderMessages(shader)
                .Where(message => message.severity == ShaderCompilerMessageSeverity.Error).ToArray();
            if (errors.Length > 0) throw new InvalidOperationException($"PRT capture shader '{shader.name}' failed to compile: " +
                string.Join("; ", errors.Select(message => message.file + ":" + message.line + " " + message.message)));
        }
    }
}
