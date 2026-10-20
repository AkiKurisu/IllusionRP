using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Illusion.ShaderTools.Syntax.Common;

namespace Illusion.ShaderTools.Syntax.ShaderLab
{
    public class ShaderLabPrinter : ShaderLabSyntaxVisitor
    {
        // Settings
        // Pretty-print embedded HLSL from the AST, or just print the original source?
        public bool PrettyPrintEmbeddedHLSL { get; set; } = false;

        // State and helpers
        protected StringBuilder sb = new StringBuilder();
        public string Text => sb.ToString();

        protected int indentLevel = 0;
        protected void PushIndent() => indentLevel++;
        protected void PopIndent() => indentLevel--;
        protected string Indent() => new string(' ', indentLevel * 4);

        protected void Emit(string text) => sb.Append(text);
        protected void EmitLine(string text = "") => sb.AppendLine(text);
        protected void EmitIndented(string text = "")
        {
            sb.Append(Indent());
            sb.Append(text);
        }
        protected void EmitIndentedLine(string text)
        {
            sb.Append(Indent());
            sb.AppendLine(text);
        }

        protected void VisitManySeparated<T>(IList<T> nodes, string separator, bool trailing = false, bool leading = false)
            where T : ShaderLabSyntaxNode
        {
            if (leading && nodes.Count > 0)
            {
                Emit(separator);
            }
            VisitMany(nodes, () => Emit(separator));
            if (trailing && nodes.Count > 0)
            {
                Emit(separator);
            }
        }

        protected void EmitPropertyReferenceOr<T>(PropertyReferenceOr<T> prop)
        {
            if (prop.IsPropertyReference)
            {
                Emit($"[{prop.Property}]");
            }
            else
            {
                if (prop.Value is bool b)
                    Emit(b ? "On" : "Off");
                else if (prop.Value is Enum)
                    Emit(PrintingUtil.GetEnumNameTypeErased(prop.Value));
                else
                    Emit(string.Format(CultureInfo.InvariantCulture, "{0}", prop.Value));
            }
        }

        protected void HandleIncludeBlocks(IEnumerable<HLSLIncludeBlock> blocks)
        {
            // We only print include blocks if we aren't pretty printing HLSL
            // (i.e. the code isn't merged into the program blocks)
            if (!PrettyPrintEmbeddedHLSL)
            {
                foreach (var block in blocks)
                {
                    switch (block.Kind)
                    {
                        case ProgramKind.Cg: EmitIndented("CGINCLUDE"); break;
                        case ProgramKind.Hlsl: EmitIndented("HLSLINCLUDE"); break;
                        case ProgramKind.Glsl: EmitIndented("GLSLINCLUDE"); break;
                    }
                    Emit(block.Code);
                    switch (block.Kind)
                    {
                        case ProgramKind.Cg: EmitLine("ENDCG"); break;
                        case ProgramKind.Hlsl: EmitLine("ENDHLSL"); break;
                        case ProgramKind.Glsl: EmitLine("ENDGLSL"); break;
                    }
                }
            }
        }

        protected void HandleProgramBlocks(IEnumerable<HLSLProgramBlock> blocks)
        {
            foreach (var block in blocks)
            {
                if (PrettyPrintEmbeddedHLSL)
                {
                    EmitIndentedLine("HLSLPROGRAM");
                    HLSL.HLSLPrinter printer = new HLSL.HLSLPrinter();
                    printer.VisitMany(block.TopLevelDeclarations);
                    Emit(printer.Text);
                    EmitLine("ENDHLSL");
                }
                else
                {
                    switch (block.Kind)
                    {
                        case ProgramKind.Cg: EmitIndented("CGPROGRAM"); break;
                        case ProgramKind.Hlsl: EmitIndented("HLSLPROGRAM"); break;
                        case ProgramKind.Glsl: EmitIndented("GLSLPROGRAM"); break;
                    }
                    Emit(block.CodeWithoutIncludes);
                    switch (block.Kind)
                    {
                        case ProgramKind.Cg: EmitLine("ENDCG"); break;
                        case ProgramKind.Hlsl: EmitLine("ENDHLSL"); break;
                        case ProgramKind.Glsl: EmitLine("ENDGLSL"); break;
                    }
                }
            }
        }

        public override void VisitShaderNode(ShaderNode node)
        {
            EmitLine($"Shader \"{node.Name}\"");
            EmitLine("{");
            PushIndent();

            EmitIndentedLine("Properties");
            EmitIndentedLine("{");
            PushIndent();
            VisitMany(node.Properties);
            PopIndent();
            EmitIndentedLine("}");

            HandleIncludeBlocks(node.IncludeBlocks);

            VisitMany(node.SubShaders);

            if (node.FallbackDisabledExplicitly)
                EmitIndentedLine("Fallback Off");
            else if (node.Fallback != null)
                EmitIndentedLine($"Fallback \"{node.Fallback}\"");

            if (node.CustomEditor != null)
                EmitIndentedLine($"CustomEditor \"{node.CustomEditor}\"");

            foreach (var kvp in node.Dependencies)
            {
                EmitIndentedLine($"Dependency \"{kvp.Key}\" = \"{kvp.Value}\"");
            }

            PopIndent();

            EmitLine("}");
        }

        public override void VisitShaderPropertyNode(ShaderPropertyNode node)
        {
            EmitIndented();

            foreach (string attribute in node.Attributes)
            {
                Emit($"[{attribute}] ");
            }

            Emit($"{node.Uniform}(\"{node.Name}\", ");

            switch (node.Kind)
            {
                case ShaderPropertyKind.Texture2D: Emit("2D"); break;
                case ShaderPropertyKind.Texture3D: Emit("3D"); break;
                case ShaderPropertyKind.TextureCube: Emit("Cube"); break;
                case ShaderPropertyKind.TextureAny: Emit("Any"); break;
                case ShaderPropertyKind.Texture2DArray: Emit("2DArray"); break;
                case ShaderPropertyKind.Texture3DArray: Emit("3DArray"); break;
                case ShaderPropertyKind.TextureCubeArray: Emit("CubeArray"); break;
                case ShaderPropertyKind.Float: Emit("Float"); break;
                case ShaderPropertyKind.Int: Emit("Int"); break;
                case ShaderPropertyKind.Integer: Emit("Integer"); break;
                case ShaderPropertyKind.Color: Emit("Color"); break;
                case ShaderPropertyKind.Vector: Emit("Vector"); break;
                case ShaderPropertyKind.Range:
                    if (node.RangeMinMax != null)
                    {
                        Emit($"Range({string.Format(CultureInfo.InvariantCulture, "{0}", node.RangeMinMax.Value.Min)}, {string.Format(CultureInfo.InvariantCulture, "{0}", node.RangeMinMax.Value.Max)})");
                    }
                    else
                    {
                        Emit("Float");
                    }
                    break;
                default: Emit("Any"); break;
            }

            Emit(") = ");
            Visit(node.Value);
            EmitLine();
        }

        public override void VisitShaderPropertyValueFloatNode(ShaderPropertyValueFloatNode node)
        {
            Emit(string.Format(CultureInfo.InvariantCulture, "{0}", node.Number));
        }

        public override void VisitShaderPropertyValueIntegerNode(ShaderPropertyValueIntegerNode node)
        {
            Emit(string.Format(CultureInfo.InvariantCulture, "{0}", node.Number));
        }

        public override void VisitShaderPropertyValueVectorNode(ShaderPropertyValueVectorNode node)
        {
            Emit($"({string.Format(CultureInfo.InvariantCulture, "{0}", node.Vector.x)}, {string.Format(CultureInfo.InvariantCulture, "{0}", node.Vector.y)}, {string.Format(CultureInfo.InvariantCulture, "{0}", node.Vector.z)}");
            if (node.HasWChannel)
                Emit($", {string.Format(CultureInfo.InvariantCulture, "{0}", node.Vector.w)}");
            Emit(")");
        }

        public override void VisitShaderPropertyValueColorNode(ShaderPropertyValueColorNode node)
        {
            Emit($"({string.Format(CultureInfo.InvariantCulture, "{0}", node.Color.r)}, {string.Format(CultureInfo.InvariantCulture, "{0}", node.Color.g)}, {string.Format(CultureInfo.InvariantCulture, "{0}", node.Color.b)}");
            if (node.HasAlphaChannel)
                Emit($", {string.Format(CultureInfo.InvariantCulture, "{0}", node.Color.a)}");
            Emit(")");
        }

        public override void VisitShaderPropertyValueTextureNode(ShaderPropertyValueTextureNode node)
        {
            Emit($"\"{node.TextureName}\" {{}}");
        }

        public override void VisitSubShaderNode(SubShaderNode node)
        {
            EmitIndentedLine("SubShader");
            EmitIndentedLine("{");
            PushIndent();
            VisitMany(node.Commands);
            VisitMany(node.Passes);
            HandleIncludeBlocks(node.IncludeBlocks);
            HandleProgramBlocks(node.ProgramBlocks);
            PopIndent();
            EmitIndentedLine("}");
        }

        public override void VisitShaderCodePassNode(ShaderCodePassNode node)
        {
            EmitIndentedLine("Pass");
            EmitIndentedLine("{");
            PushIndent();

            VisitMany(node.Commands);

            HandleIncludeBlocks(node.IncludeBlocks);
            HandleProgramBlocks(node.ProgramBlocks);

            PopIndent();
            EmitIndentedLine("}");
        }

        public override void VisitShaderGrabPassNode(ShaderGrabPassNode node)
        {
            EmitIndentedLine("GrabPass");
            EmitIndentedLine("{");
            PushIndent();

            VisitMany(node.Commands);
            HandleIncludeBlocks(node.IncludeBlocks);

            if (!node.IsUnnamed)
            {
                Emit($"\"{node.TextureName}\"");
            }

            PopIndent();
            EmitIndentedLine("}");
        }

        public override void VisitShaderUsePassNode(ShaderUsePassNode node)
        {
            EmitIndentedLine($"UsePass \"{node.PassName}\"");
        }

        public override void VisitShaderLabCommandTagsNode(ShaderLabCommandTagsNode node)
        {
            EmitIndented("Tags { ");
            foreach (var kvp in node.Tags)
            {
                Emit($"\"{kvp.Key}\" = \"{kvp.Value}\" ");
            }
            EmitLine("}");
        }

        public override void VisitShaderLabCommandLodNode(ShaderLabCommandLodNode node)
        {
            EmitIndentedLine($"LOD {node.LodLevel}");
        }

        public override void VisitShaderLabCommandLightingNode(ShaderLabCommandLightingNode node)
        {
            EmitIndented($"Lighting ");
            EmitPropertyReferenceOr(node.Enabled);
            EmitLine();
        }

        public override void VisitShaderLabCommandSeparateSpecularNode(ShaderLabCommandSeparateSpecularNode node)
        {
            EmitIndented($"SeparateSpecular ");
            EmitPropertyReferenceOr(node.Enabled);
            EmitLine();
        }

        public override void VisitShaderLabCommandZWriteNode(ShaderLabCommandZWriteNode node)
        {
            EmitIndented($"ZWrite ");
            EmitPropertyReferenceOr(node.Enabled);
            EmitLine();
        }

        public override void VisitShaderLabCommandAlphaToMaskNode(ShaderLabCommandAlphaToMaskNode node)
        {
            EmitIndented($"AlphaToMask ");
            EmitPropertyReferenceOr(node.Enabled);
            EmitLine();
        }

        public override void VisitShaderLabCommandZClipNode(ShaderLabCommandZClipNode node)
        {
            EmitIndented($"ZClip ");
            EmitPropertyReferenceOr(node.Enabled);
            EmitLine();
        }

        public override void VisitShaderLabCommandConservativeNode(ShaderLabCommandConservativeNode node)
        {
            EmitIndented($"Conservative ");
            EmitPropertyReferenceOr(node.Enabled);
            EmitLine();
        }

        public override void VisitShaderLabCommandCullNode(ShaderLabCommandCullNode node)
        {
            EmitIndented($"Cull ");
            EmitPropertyReferenceOr(node.Mode);
            EmitLine();
        }

        public override void VisitShaderLabCommandZTestNode(ShaderLabCommandZTestNode node)
        {
            EmitIndented($"ZTest ");
            EmitPropertyReferenceOr(node.Mode);
            EmitLine();
        }

        public override void VisitShaderLabCommandBlendNode(ShaderLabCommandBlendNode node)
        {
            EmitIndented("Blend ");
            Emit($"{node.RenderTarget} ");

            if (!node.Enabled)
            {
                Emit("Off");
            }
            else
            {
                if (node.SourceFactorRGB != null)
                {
                    EmitPropertyReferenceOr(node.SourceFactorRGB.Value);
                    Emit(" ");
                }
                if (node.DestinationFactorRGB != null)
                {
                    EmitPropertyReferenceOr(node.DestinationFactorRGB.Value);
                    Emit(" ");
                }

                if (node.SourceFactorAlpha != null)
                {
                    Emit(", ");
                    EmitPropertyReferenceOr(node.SourceFactorAlpha.Value);
                    Emit(" ");
                }
                if (node.DestinationFactorAlpha != null)
                {
                    EmitPropertyReferenceOr(node.DestinationFactorAlpha.Value);
                }
            }

            EmitLine();
        }

        public override void VisitShaderLabCommandOffsetNode(ShaderLabCommandOffsetNode node)
        {
            EmitIndented($"Offset ");
            EmitPropertyReferenceOr(node.Factor);
            Emit(", ");
            EmitPropertyReferenceOr(node.Units);
            EmitLine();
        }

        public override void VisitShaderLabCommandColorMaskNode(ShaderLabCommandColorMaskNode node)
        {
            EmitIndented($"ColorMask ");
            EmitPropertyReferenceOr(node.Mask);
            if (node.Mask.IsValue) // RenderTarget not allowed with property-based mask
                Emit($" {node.RenderTarget}");
            EmitLine();
        }

        public override void VisitShaderLabCommandAlphaTestNode(ShaderLabCommandAlphaTestNode node)
        {
            EmitIndented($"AlphaTest ");
            if (node.Mode.IsPropertyReference && node.Mode.Value == ComparisonMode.Off)
            {
                Emit("Off");
            }
            else
            {
                EmitPropertyReferenceOr(node.Mode);
                if (node.AlphaValue != null)
                {
                    EmitPropertyReferenceOr(node.AlphaValue.Value);
                }
            }
            EmitLine();
        }

        public override void VisitShaderLabCommandFogNode(ShaderLabCommandFogNode node)
        {
            EmitIndented("Fog ");
            if (!node.Enabled)
            {
                Emit("{ Mode Off }");
            }
            else
            {
                if (node.Color != null)
                {
                    Emit($"{{ Color ({string.Format(CultureInfo.InvariantCulture, "{0}", node.Color.Value.r)}, {string.Format(CultureInfo.InvariantCulture, "{0}", node.Color.Value.g)}, {string.Format(CultureInfo.InvariantCulture, "{0}", node.Color.Value.b)}, {string.Format(CultureInfo.InvariantCulture, "{0}", node.Color.Value.a)}) }}");
                }
                else
                {
                    Emit("{ Mode Global }");
                }
            }
            EmitLine();
        }

        public override void VisitShaderLabCommandNameNode(ShaderLabCommandNameNode node)
        {
            EmitIndentedLine($"Name \"{node.Name}\"");
        }

        public override void VisitShaderLabCommandBindChannelsNode(ShaderLabCommandBindChannelsNode node)
        {
            EmitIndentedLine("BindChannels");
            EmitIndentedLine("{");
            PushIndent();

            foreach (var binding in node.Bindings)
            {
                EmitIndented("Bind ");
                Emit($"\"{PrintingUtil.GetEnumName(binding.Key)}\", {PrintingUtil.GetEnumName(binding.Value).ToLower()}");
                EmitLine();
            }

            PopIndent();
            EmitIndentedLine("}");
        }

        public override void VisitShaderLabCommandColorNode(ShaderLabCommandColorNode node)
        {
            EmitIndented($"Color ");
            if (node.Color.IsPropertyReference)
            {
                EmitPropertyReferenceOr(node.Color);
            }
            else
            {
                Emit($"({string.Format(CultureInfo.InvariantCulture, "{0}", node.Color.Value.r)}, {string.Format(CultureInfo.InvariantCulture, "{0}", node.Color.Value.g)}, {string.Format(CultureInfo.InvariantCulture, "{0}", node.Color.Value.b)}");
                if (node.HasAlphaChannel)
                {
                    Emit($", {string.Format(CultureInfo.InvariantCulture, "{0}", node.Color.Value.a)}");
                }
                Emit(")");
            }
            EmitLine();
        }

        public override void VisitShaderLabCommandBlendOpNode(ShaderLabCommandBlendOpNode node)
        {
            EmitIndented($"BlendOp ");
            EmitPropertyReferenceOr(node.BlendOp);
            if (node.BlendOpAlpha != null)
            {
                Emit(", ");
                EmitPropertyReferenceOr(node.BlendOpAlpha.Value);
            }
            EmitLine();
        }

        public override void VisitShaderLabCommandMaterialNode(ShaderLabCommandMaterialNode node)
        {
            EmitIndentedLine("Material");
            EmitIndentedLine("{");
            PushIndent();

            foreach (var binding in node.Properties)
            {
                EmitIndented(PrintingUtil.GetEnumName(binding.Key));
                Emit(" ");
                EmitPropertyReferenceOr(binding.Value);
            }

            PopIndent();
            EmitIndentedLine("}");
        }

        public override void VisitShaderLabCommandSetTextureNode(ShaderLabCommandSetTextureNode node)
        {
            EmitIndentedLine($"SetTexture [{node.TextureName}]");
            EmitIndentedLine("{");

            PushIndent();
            EmitIndentedLine(ShaderLabSyntaxFacts.TokensToString(node.Body));
            PopIndent();

            EmitIndentedLine("}");
        }

        public override void VisitShaderLabCommandColorMaterialNode(ShaderLabCommandColorMaterialNode node)
        {
            EmitIndented($"ColorMaterial ");
            Emit(node.AmbientAndDiffuse ? "AmbientAndDiffuse" : "Emission");
            EmitLine();
        }

        public override void VisitShaderLabCommandStencilNode(ShaderLabCommandStencilNode node)
        {
            EmitIndentedLine("Stencil");
            EmitIndentedLine("{");
            PushIndent();

            EmitIndented("Ref ");
            EmitPropertyReferenceOr(node.Ref);
            EmitLine();

            EmitIndented("ReadMask ");
            EmitPropertyReferenceOr(node.ReadMask);
            EmitLine();

            EmitIndented("WriteMask ");
            EmitPropertyReferenceOr(node.WriteMask);
            EmitLine();

            EmitIndented("CompBack ");
            EmitPropertyReferenceOr(node.ComparisonOperationBack);
            EmitLine();
            EmitIndented("PassBack ");
            EmitPropertyReferenceOr(node.PassOperationBack);
            EmitLine();
            EmitIndented("FailBack ");
            EmitPropertyReferenceOr(node.FailOperationBack);
            EmitLine();
            EmitIndented("ZFailBack ");
            EmitPropertyReferenceOr(node.ZFailOperationBack);
            EmitLine();

            EmitIndented("CompFront ");
            EmitPropertyReferenceOr(node.ComparisonOperationFront);
            EmitLine();
            EmitIndented("PassFront ");
            EmitPropertyReferenceOr(node.PassOperationFront);
            EmitLine();
            EmitIndented("FailFront ");
            EmitPropertyReferenceOr(node.FailOperationFront);
            EmitLine();
            EmitIndented("ZFailFront ");
            EmitPropertyReferenceOr(node.ZFailOperationFront);
            EmitLine();

            PopIndent();
            EmitIndentedLine("}");
        }

        public override void VisitShaderLabCommandPackageRequirementsNode(ShaderLabCommandPackageRequirementsNode node)
        {
            EmitIndentedLine("PackageRequirements");
            EmitIndentedLine("{");
            PushIndent();

            foreach (var reference in node.References)
            {
                EmitIndented($"\"{reference.Key}\"");
                if (reference.Value != null)
                {
                    Emit($": \"{reference.Value}\"");
                }
                EmitLine();
            }

            PopIndent();
            EmitIndentedLine("}");
        }
    }
}
