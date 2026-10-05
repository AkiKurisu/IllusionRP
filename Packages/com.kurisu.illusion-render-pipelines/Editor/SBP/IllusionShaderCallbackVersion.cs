using System.Collections.Generic;
using UnityEditor.Build;
using UnityEditor.Build.Pipeline.Utilities;
using UnityEditor.Rendering;
using UnityEngine;

namespace Illusion.Rendering.Editor
{
    [VersionedCallback(IllusionShaderBuildScope.PolicyVersion)]
    internal sealed class IllusionShaderCallbackVersion : IPreprocessShaders, IPreprocessComputeShaders
    {
        public int callbackOrder => 100;

        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> variants)
        {
        }

        public void OnProcessComputeShader(ComputeShader shader, string kernelName, IList<ShaderCompilerData> variants)
        {
        }
    }
}
