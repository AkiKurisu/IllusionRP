using System;
using System.Collections.Generic;
using System.Diagnostics;
using Illusion.ShaderTools.Syntax.Common;

namespace Illusion.ShaderTools.Syntax.ShaderLab
{
    public abstract class ShaderLabSyntaxVisitor
    {
        [DebuggerStepThrough]
        protected virtual void DefaultVisit(ShaderLabSyntaxNode node)
        {
            foreach (var child in node.Children)
            {
                child.Accept(this);
            }
        }

        public void VisitMany(IEnumerable<ShaderLabSyntaxNode> nodes)
        {
            foreach (ShaderLabSyntaxNode node in nodes)
            {
                Visit(node);
            }
        }

        public void VisitMany<T>(IList<T> nodes, Action runBetween)
            where T : ShaderLabSyntaxNode
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                Visit(nodes[i]);
                if (i < nodes.Count - 1)
                    runBetween();
            }
        }

        [DebuggerStepThrough]
        public virtual void Visit(ShaderLabSyntaxNode node) => node?.Accept(this);
        public virtual void VisitShaderNode(ShaderNode node) => DefaultVisit(node);
        public virtual void VisitShaderPropertyNode(ShaderPropertyNode node) => DefaultVisit(node);
        public virtual void VisitShaderPropertyValueFloatNode(ShaderPropertyValueFloatNode node) => DefaultVisit(node);
        public virtual void VisitShaderPropertyValueIntegerNode(ShaderPropertyValueIntegerNode node) => DefaultVisit(node);
        public virtual void VisitShaderPropertyValueVectorNode(ShaderPropertyValueVectorNode node) => DefaultVisit(node);
        public virtual void VisitShaderPropertyValueColorNode(ShaderPropertyValueColorNode node) => DefaultVisit(node);
        public virtual void VisitShaderPropertyValueTextureNode(ShaderPropertyValueTextureNode node) => DefaultVisit(node);
        public virtual void VisitSubShaderNode(SubShaderNode node) => DefaultVisit(node);
        public virtual void VisitShaderCodePassNode(ShaderCodePassNode node) => DefaultVisit(node);
        public virtual void VisitShaderGrabPassNode(ShaderGrabPassNode node) => DefaultVisit(node);
        public virtual void VisitShaderUsePassNode(ShaderUsePassNode node) => DefaultVisit(node);
        public virtual void VisitShaderLabCommandTagsNode(ShaderLabCommandTagsNode node) => DefaultVisit(node);
        public virtual void VisitShaderLabCommandLodNode(ShaderLabCommandLodNode node) => DefaultVisit(node);
        public virtual void VisitShaderLabCommandLightingNode(ShaderLabCommandLightingNode node) => DefaultVisit(node);
        public virtual void VisitShaderLabCommandSeparateSpecularNode(ShaderLabCommandSeparateSpecularNode node) => DefaultVisit(node);
        public virtual void VisitShaderLabCommandZWriteNode(ShaderLabCommandZWriteNode node) => DefaultVisit(node);
        public virtual void VisitShaderLabCommandAlphaToMaskNode(ShaderLabCommandAlphaToMaskNode node) => DefaultVisit(node);
        public virtual void VisitShaderLabCommandZClipNode(ShaderLabCommandZClipNode node) => DefaultVisit(node);
        public virtual void VisitShaderLabCommandConservativeNode(ShaderLabCommandConservativeNode node) => DefaultVisit(node);
        public virtual void VisitShaderLabCommandCullNode(ShaderLabCommandCullNode node) => DefaultVisit(node);
        public virtual void VisitShaderLabCommandZTestNode(ShaderLabCommandZTestNode node) => DefaultVisit(node);
        public virtual void VisitShaderLabCommandBlendNode(ShaderLabCommandBlendNode node) => DefaultVisit(node);
        public virtual void VisitShaderLabCommandOffsetNode(ShaderLabCommandOffsetNode node) => DefaultVisit(node);
        public virtual void VisitShaderLabCommandColorMaskNode(ShaderLabCommandColorMaskNode node) => DefaultVisit(node);
        public virtual void VisitShaderLabCommandAlphaTestNode(ShaderLabCommandAlphaTestNode node) => DefaultVisit(node);
        public virtual void VisitShaderLabCommandFogNode(ShaderLabCommandFogNode node) => DefaultVisit(node);
        public virtual void VisitShaderLabCommandNameNode(ShaderLabCommandNameNode node) => DefaultVisit(node);
        public virtual void VisitShaderLabCommandBindChannelsNode(ShaderLabCommandBindChannelsNode node) => DefaultVisit(node);
        public virtual void VisitShaderLabCommandColorNode(ShaderLabCommandColorNode node) => DefaultVisit(node);
        public virtual void VisitShaderLabCommandBlendOpNode(ShaderLabCommandBlendOpNode node) => DefaultVisit(node);
        public virtual void VisitShaderLabCommandMaterialNode(ShaderLabCommandMaterialNode node) => DefaultVisit(node);
        public virtual void VisitShaderLabCommandSetTextureNode(ShaderLabCommandSetTextureNode node) => DefaultVisit(node);
        public virtual void VisitShaderLabCommandColorMaterialNode(ShaderLabCommandColorMaterialNode node) => DefaultVisit(node);
        public virtual void VisitShaderLabCommandStencilNode(ShaderLabCommandStencilNode node) => DefaultVisit(node);
        public virtual void VisitShaderLabCommandPackageRequirementsNode(ShaderLabCommandPackageRequirementsNode node) => DefaultVisit(node);
    }

    public abstract class ShaderLabSyntaxVisitor<TReturn>
    {
        [DebuggerStepThrough]
        protected virtual TReturn DefaultVisit(ShaderLabSyntaxNode node)
        {
            foreach (var child in node.Children)
            {
                child.Accept(this);
            }
            return default;
        }

        public List<TReturn> VisitMany(IEnumerable<ShaderLabSyntaxNode> nodes)
        {
            List<TReturn> result = new List<TReturn>();
            foreach (ShaderLabSyntaxNode node in nodes)
            {
                result.Add(Visit(node));
            }
            return result;
        }

        public List<TReturn> VisitMany<T>(IList<T> nodes, Action runBetween)
            where T : ShaderLabSyntaxNode
        {
            List<TReturn> result = new List<TReturn>();
            for (int i = 0; i < nodes.Count; i++)
            {
                result.Add(Visit(nodes[i]));
                if (i < nodes.Count - 1)
                    runBetween();
            }
            return result;
        }

        [DebuggerStepThrough]
        public virtual TReturn Visit(ShaderLabSyntaxNode node) => node == null ? default : node.Accept(this);
        public virtual TReturn VisitShaderNode(ShaderNode node) => DefaultVisit(node);
        public virtual TReturn VisitShaderPropertyNode(ShaderPropertyNode node) => DefaultVisit(node);
        public virtual TReturn VisitShaderPropertyValueFloatNode(ShaderPropertyValueFloatNode node) => DefaultVisit(node);
        public virtual TReturn VisitShaderPropertyValueIntegerNode(ShaderPropertyValueIntegerNode node) => DefaultVisit(node);
        public virtual TReturn VisitShaderPropertyValueVectorNode(ShaderPropertyValueVectorNode node) => DefaultVisit(node);
        public virtual TReturn VisitShaderPropertyValueColorNode(ShaderPropertyValueColorNode node) => DefaultVisit(node);
        public virtual TReturn VisitShaderPropertyValueTextureNode(ShaderPropertyValueTextureNode node) => DefaultVisit(node);
        public virtual TReturn VisitSubShaderNode(SubShaderNode node) => DefaultVisit(node);
        public virtual TReturn VisitShaderCodePassNode(ShaderCodePassNode node) => DefaultVisit(node);
        public virtual TReturn VisitShaderGrabPassNode(ShaderGrabPassNode node) => DefaultVisit(node);
        public virtual TReturn VisitShaderUsePassNode(ShaderUsePassNode node) => DefaultVisit(node);
        public virtual TReturn VisitShaderLabCommandTagsNode(ShaderLabCommandTagsNode node) => DefaultVisit(node);
        public virtual TReturn VisitShaderLabCommandLodNode(ShaderLabCommandLodNode node) => DefaultVisit(node);
        public virtual TReturn VisitShaderLabCommandLightingNode(ShaderLabCommandLightingNode node) => DefaultVisit(node);
        public virtual TReturn VisitShaderLabCommandSeparateSpecularNode(ShaderLabCommandSeparateSpecularNode node) => DefaultVisit(node);
        public virtual TReturn VisitShaderLabCommandZWriteNode(ShaderLabCommandZWriteNode node) => DefaultVisit(node);
        public virtual TReturn VisitShaderLabCommandAlphaToMaskNode(ShaderLabCommandAlphaToMaskNode node) => DefaultVisit(node);
        public virtual TReturn VisitShaderLabCommandZClipNode(ShaderLabCommandZClipNode node) => DefaultVisit(node);
        public virtual TReturn VisitShaderLabCommandConservativeNode(ShaderLabCommandConservativeNode node) => DefaultVisit(node);
        public virtual TReturn VisitShaderLabCommandCullNode(ShaderLabCommandCullNode node) => DefaultVisit(node);
        public virtual TReturn VisitShaderLabCommandZTestNode(ShaderLabCommandZTestNode node) => DefaultVisit(node);
        public virtual TReturn VisitShaderLabCommandBlendNode(ShaderLabCommandBlendNode node) => DefaultVisit(node);
        public virtual TReturn VisitShaderLabCommandOffsetNode(ShaderLabCommandOffsetNode node) => DefaultVisit(node);
        public virtual TReturn VisitShaderLabCommandColorMaskNode(ShaderLabCommandColorMaskNode node) => DefaultVisit(node);
        public virtual TReturn VisitShaderLabCommandAlphaTestNode(ShaderLabCommandAlphaTestNode node) => DefaultVisit(node);
        public virtual TReturn VisitShaderLabCommandFogNode(ShaderLabCommandFogNode node) => DefaultVisit(node);
        public virtual TReturn VisitShaderLabCommandNameNode(ShaderLabCommandNameNode node) => DefaultVisit(node);
        public virtual TReturn VisitShaderLabCommandBindChannelsNode(ShaderLabCommandBindChannelsNode node) => DefaultVisit(node);
        public virtual TReturn VisitShaderLabCommandColorNode(ShaderLabCommandColorNode node) => DefaultVisit(node);
        public virtual TReturn VisitShaderLabCommandBlendOpNode(ShaderLabCommandBlendOpNode node) => DefaultVisit(node);
        public virtual TReturn VisitShaderLabCommandMaterialNode(ShaderLabCommandMaterialNode node) => DefaultVisit(node);
        public virtual TReturn VisitShaderLabCommandSetTextureNode(ShaderLabCommandSetTextureNode node) => DefaultVisit(node);
        public virtual TReturn VisitShaderLabCommandColorMaterialNode(ShaderLabCommandColorMaterialNode node) => DefaultVisit(node);
        public virtual TReturn VisitShaderLabCommandStencilNode(ShaderLabCommandStencilNode node) => DefaultVisit(node);
        public virtual TReturn VisitShaderLabCommandPackageRequirementsNode(ShaderLabCommandPackageRequirementsNode node) => DefaultVisit(node);
    }
}
