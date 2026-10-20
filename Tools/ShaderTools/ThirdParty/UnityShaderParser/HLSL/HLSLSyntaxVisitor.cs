using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Illusion.ShaderTools.Syntax.HLSL
{
    public abstract class HLSLSyntaxVisitor
    {
        [DebuggerStepThrough]
        protected virtual void DefaultVisit(HLSLSyntaxNode node)
        {
            foreach (var child in node.Children)
            {
                child.Accept(this);
            }
        }

        public void VisitMany(IEnumerable<HLSLSyntaxNode> nodes)
        {
            foreach (HLSLSyntaxNode node in nodes)
            {
                Visit(node);
            }
        }

        public void VisitMany<T>(IList<T> nodes, Action runBetween)
            where T: HLSLSyntaxNode
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                Visit(nodes[i]);
                if (i < nodes.Count - 1)
                    runBetween();
            }
        }

        [DebuggerStepThrough]
        public virtual void Visit(HLSLSyntaxNode node) => node?.Accept(this);
        public virtual void VisitEnumDefinitionNode(EnumDefinitionNode node) => DefaultVisit(node);
        public virtual void VisitIdentifierNode(IdentifierNode node) => DefaultVisit(node);
        public virtual void VisitFormalParameterNode(FormalParameterNode node) => DefaultVisit(node);
        public virtual void VisitVariableDeclaratorNode(VariableDeclaratorNode node) => DefaultVisit(node);
        public virtual void VisitArrayRankNode(ArrayRankNode node) => DefaultVisit(node);
        public virtual void VisitValueInitializerNode(ValueInitializerNode node) => DefaultVisit(node);
        public virtual void VisitStateInitializerNode(StateInitializerNode node) => DefaultVisit(node);
        public virtual void VisitStateArrayInitializerNode(StateArrayInitializerNode node) => DefaultVisit(node);
        public virtual void VisitFunctionDeclarationNode(FunctionDeclarationNode node) => DefaultVisit(node);
        public virtual void VisitFunctionDefinitionNode(FunctionDefinitionNode node) => DefaultVisit(node);
        public virtual void VisitStructDefinitionNode(StructDefinitionNode node) => DefaultVisit(node);
        public virtual void VisitInterfaceDefinitionNode(InterfaceDefinitionNode node) => DefaultVisit(node);
        public virtual void VisitConstantBufferNode(ConstantBufferNode node) => DefaultVisit(node);
        public virtual void VisitNamespaceNode(NamespaceNode node) => DefaultVisit(node);
        public virtual void VisitTypedefNode(TypedefNode node) => DefaultVisit(node);
        public virtual void VisitSemanticNode(SemanticNode node) => DefaultVisit(node);
        public virtual void VisitRegisterLocationNode(RegisterLocationNode node) => DefaultVisit(node);
        public virtual void VisitPackoffsetNode(PackoffsetNode node) => DefaultVisit(node);
        public virtual void VisitBlockNode(BlockNode node) => DefaultVisit(node);
        public virtual void VisitVariableDeclarationStatementNode(VariableDeclarationStatementNode node) => DefaultVisit(node);
        public virtual void VisitReturnStatementNode(ReturnStatementNode node) => DefaultVisit(node);
        public virtual void VisitBreakStatementNode(BreakStatementNode node) => DefaultVisit(node);
        public virtual void VisitContinueStatementNode(ContinueStatementNode node) => DefaultVisit(node);
        public virtual void VisitDiscardStatementNode(DiscardStatementNode node) => DefaultVisit(node);
        public virtual void VisitEmptyStatementNode(EmptyStatementNode node) => DefaultVisit(node);
        public virtual void VisitForStatementNode(ForStatementNode node) => DefaultVisit(node);
        public virtual void VisitWhileStatementNode(WhileStatementNode node) => DefaultVisit(node);
        public virtual void VisitDoWhileStatementNode(DoWhileStatementNode node) => DefaultVisit(node);
        public virtual void VisitIfStatementNode(IfStatementNode node) => DefaultVisit(node);
        public virtual void VisitSwitchStatementNode(SwitchStatementNode node) => DefaultVisit(node);
        public virtual void VisitSwitchClauseNode(SwitchClauseNode node) => DefaultVisit(node);
        public virtual void VisitSwitchCaseLabelNode(SwitchCaseLabelNode node) => DefaultVisit(node);
        public virtual void VisitSwitchDefaultLabelNode(SwitchDefaultLabelNode node) => DefaultVisit(node);
        public virtual void VisitExpressionStatementNode(ExpressionStatementNode node) => DefaultVisit(node);
        public virtual void VisitAttributeNode(AttributeNode node) => DefaultVisit(node);
        public virtual void VisitQualifiedIdentifierExpressionNode(QualifiedIdentifierExpressionNode node) => DefaultVisit(node);
        public virtual void VisitIdentifierExpressionNode(IdentifierExpressionNode node) => DefaultVisit(node);
        public virtual void VisitLiteralExpressionNode(LiteralExpressionNode node) => DefaultVisit(node);
        public virtual void VisitAssignmentExpressionNode(AssignmentExpressionNode node) => DefaultVisit(node);
        public virtual void VisitBinaryExpressionNode(BinaryExpressionNode node) => DefaultVisit(node);
        public virtual void VisitCompoundExpressionNode(CompoundExpressionNode node) => DefaultVisit(node);
        public virtual void VisitPrefixUnaryExpressionNode(PrefixUnaryExpressionNode node) => DefaultVisit(node);
        public virtual void VisitPostfixUnaryExpressionNode(PostfixUnaryExpressionNode node) => DefaultVisit(node);
        public virtual void VisitFieldAccessExpressionNode(FieldAccessExpressionNode node) => DefaultVisit(node);
        public virtual void VisitMethodCallExpressionNode(MethodCallExpressionNode node) => DefaultVisit(node);
        public virtual void VisitFunctionCallExpressionNode(FunctionCallExpressionNode node) => DefaultVisit(node);
        public virtual void VisitNumericConstructorCallExpressionNode(NumericConstructorCallExpressionNode node) => DefaultVisit(node);
        public virtual void VisitElementAccessExpressionNode(ElementAccessExpressionNode node) => DefaultVisit(node);
        public virtual void VisitCastExpressionNode(CastExpressionNode node) => DefaultVisit(node);
        public virtual void VisitArrayInitializerExpressionNode(ArrayInitializerExpressionNode node) => DefaultVisit(node);
        public virtual void VisitTernaryExpressionNode(TernaryExpressionNode node) => DefaultVisit(node);
        public virtual void VisitSamplerStateLiteralExpressionNode(SamplerStateLiteralExpressionNode node) => DefaultVisit(node);
        public virtual void VisitCompileExpressionNode(CompileExpressionNode node) => DefaultVisit(node);
        public virtual void VisitQualifiedNamedTypeNode(QualifiedNamedTypeNode node) => DefaultVisit(node);
        public virtual void VisitNamedTypeNode(NamedTypeNode node) => DefaultVisit(node);
        public virtual void VisitPredefinedObjectTypeNode(PredefinedObjectTypeNode node) => DefaultVisit(node);
        public virtual void VisitStructTypeNode(StructTypeNode node) => DefaultVisit(node);
        public virtual void VisitScalarTypeNode(ScalarTypeNode node) => DefaultVisit(node);
        public virtual void VisitMatrixTypeNode(MatrixTypeNode node) => DefaultVisit(node);
        public virtual void VisitGenericMatrixTypeNode(GenericMatrixTypeNode node) => DefaultVisit(node);
        public virtual void VisitVectorTypeNode(VectorTypeNode node) => DefaultVisit(node);
        public virtual void VisitGenericVectorTypeNode(GenericVectorTypeNode node) => DefaultVisit(node);
        public virtual void VisitTechniqueNode(TechniqueNode node) => DefaultVisit(node);
        public virtual void VisitLiteralTemplateArgumentType(LiteralTemplateArgumentType node) => DefaultVisit(node);
        public virtual void VisitStatePropertyNode(StatePropertyNode node) => DefaultVisit(node);
        public virtual void VisitPassNode(PassNode node) => DefaultVisit(node);
        public virtual void VisitObjectLikeMacroNode(ObjectLikeMacroNode node) => DefaultVisit(node);
        public virtual void VisitFunctionLikeMacroNode(FunctionLikeMacroNode node) => DefaultVisit(node);
        public virtual void VisitIncludeDirectiveNode(IncludeDirectiveNode node) => DefaultVisit(node);
        public virtual void VisitLineDirectiveNode(LineDirectiveNode node) => DefaultVisit(node);
        public virtual void VisitUndefDirectiveNode(UndefDirectiveNode node) => DefaultVisit(node);
        public virtual void VisitErrorDirectiveNode(ErrorDirectiveNode node) => DefaultVisit(node);
        public virtual void VisitPragmaDirectiveNode(PragmaDirectiveNode node) => DefaultVisit(node);
        public virtual void VisitIfDefDirectiveNode(IfDefDirectiveNode node) => DefaultVisit(node);
        public virtual void VisitIfNotDefDirectiveNode(IfNotDefDirectiveNode node) => DefaultVisit(node);
        public virtual void VisitIfDirectiveNode(IfDirectiveNode node) => DefaultVisit(node);
        public virtual void VisitElseDirectiveNode(ElseDirectiveNode node) => DefaultVisit(node);
    }

    public abstract class HLSLSyntaxVisitor<TReturn>
    {
        [DebuggerStepThrough]
        protected virtual TReturn DefaultVisit(HLSLSyntaxNode node)
        {
            foreach (var child in node.Children)
            {
                child.Accept(this);
            }
            return default;
        }

        public List<TReturn> VisitMany(IEnumerable<HLSLSyntaxNode> nodes)
        {
            List<TReturn> result = new List<TReturn>();
            foreach (HLSLSyntaxNode node in nodes)
            {
                result.Add(Visit(node));
            }
            return result;
        }

        public List<TReturn> VisitMany<T>(IList<T> nodes, Action runBetween)
            where T : HLSLSyntaxNode
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
        public virtual TReturn Visit(HLSLSyntaxNode node) => node == null ? default : node.Accept(this);
        public virtual TReturn VisitEnumDefinitionNode(EnumDefinitionNode node) => DefaultVisit(node);
        public virtual TReturn VisitIdentifierNode(IdentifierNode node) => DefaultVisit(node);
        public virtual TReturn VisitFormalParameterNode(FormalParameterNode node) => DefaultVisit(node);
        public virtual TReturn VisitVariableDeclaratorNode(VariableDeclaratorNode node) => DefaultVisit(node);
        public virtual TReturn VisitArrayRankNode(ArrayRankNode node) => DefaultVisit(node);
        public virtual TReturn VisitValueInitializerNode(ValueInitializerNode node) => DefaultVisit(node);
        public virtual TReturn VisitStateInitializerNode(StateInitializerNode node) => DefaultVisit(node);
        public virtual TReturn VisitStateArrayInitializerNode(StateArrayInitializerNode node) => DefaultVisit(node);
        public virtual TReturn VisitFunctionDeclarationNode(FunctionDeclarationNode node) => DefaultVisit(node);
        public virtual TReturn VisitFunctionDefinitionNode(FunctionDefinitionNode node) => DefaultVisit(node);
        public virtual TReturn VisitStructDefinitionNode(StructDefinitionNode node) => DefaultVisit(node);
        public virtual TReturn VisitInterfaceDefinitionNode(InterfaceDefinitionNode node) => DefaultVisit(node);
        public virtual TReturn VisitConstantBufferNode(ConstantBufferNode node) => DefaultVisit(node);
        public virtual TReturn VisitNamespaceNode(NamespaceNode node) => DefaultVisit(node);
        public virtual TReturn VisitTypedefNode(TypedefNode node) => DefaultVisit(node);
        public virtual TReturn VisitSemanticNode(SemanticNode node) => DefaultVisit(node);
        public virtual TReturn VisitRegisterLocationNode(RegisterLocationNode node) => DefaultVisit(node);
        public virtual TReturn VisitPackoffsetNode(PackoffsetNode node) => DefaultVisit(node);
        public virtual TReturn VisitBlockNode(BlockNode node) => DefaultVisit(node);
        public virtual TReturn VisitVariableDeclarationStatementNode(VariableDeclarationStatementNode node) => DefaultVisit(node);
        public virtual TReturn VisitReturnStatementNode(ReturnStatementNode node) => DefaultVisit(node);
        public virtual TReturn VisitBreakStatementNode(BreakStatementNode node) => DefaultVisit(node);
        public virtual TReturn VisitContinueStatementNode(ContinueStatementNode node) => DefaultVisit(node);
        public virtual TReturn VisitDiscardStatementNode(DiscardStatementNode node) => DefaultVisit(node);
        public virtual TReturn VisitEmptyStatementNode(EmptyStatementNode node) => DefaultVisit(node);
        public virtual TReturn VisitForStatementNode(ForStatementNode node) => DefaultVisit(node);
        public virtual TReturn VisitWhileStatementNode(WhileStatementNode node) => DefaultVisit(node);
        public virtual TReturn VisitDoWhileStatementNode(DoWhileStatementNode node) => DefaultVisit(node);
        public virtual TReturn VisitIfStatementNode(IfStatementNode node) => DefaultVisit(node);
        public virtual TReturn VisitSwitchStatementNode(SwitchStatementNode node) => DefaultVisit(node);
        public virtual TReturn VisitSwitchClauseNode(SwitchClauseNode node) => DefaultVisit(node);
        public virtual TReturn VisitSwitchCaseLabelNode(SwitchCaseLabelNode node) => DefaultVisit(node);
        public virtual TReturn VisitSwitchDefaultLabelNode(SwitchDefaultLabelNode node) => DefaultVisit(node);
        public virtual TReturn VisitExpressionStatementNode(ExpressionStatementNode node) => DefaultVisit(node);
        public virtual TReturn VisitAttributeNode(AttributeNode node) => DefaultVisit(node);
        public virtual TReturn VisitQualifiedIdentifierExpressionNode(QualifiedIdentifierExpressionNode node) => DefaultVisit(node);
        public virtual TReturn VisitIdentifierExpressionNode(IdentifierExpressionNode node) => DefaultVisit(node);
        public virtual TReturn VisitLiteralExpressionNode(LiteralExpressionNode node) => DefaultVisit(node);
        public virtual TReturn VisitAssignmentExpressionNode(AssignmentExpressionNode node) => DefaultVisit(node);
        public virtual TReturn VisitBinaryExpressionNode(BinaryExpressionNode node) => DefaultVisit(node);
        public virtual TReturn VisitCompoundExpressionNode(CompoundExpressionNode node) => DefaultVisit(node);
        public virtual TReturn VisitPrefixUnaryExpressionNode(PrefixUnaryExpressionNode node) => DefaultVisit(node);
        public virtual TReturn VisitPostfixUnaryExpressionNode(PostfixUnaryExpressionNode node) => DefaultVisit(node);
        public virtual TReturn VisitFieldAccessExpressionNode(FieldAccessExpressionNode node) => DefaultVisit(node);
        public virtual TReturn VisitMethodCallExpressionNode(MethodCallExpressionNode node) => DefaultVisit(node);
        public virtual TReturn VisitFunctionCallExpressionNode(FunctionCallExpressionNode node) => DefaultVisit(node);
        public virtual TReturn VisitNumericConstructorCallExpressionNode(NumericConstructorCallExpressionNode node) => DefaultVisit(node);
        public virtual TReturn VisitElementAccessExpressionNode(ElementAccessExpressionNode node) => DefaultVisit(node);
        public virtual TReturn VisitCastExpressionNode(CastExpressionNode node) => DefaultVisit(node);
        public virtual TReturn VisitArrayInitializerExpressionNode(ArrayInitializerExpressionNode node) => DefaultVisit(node);
        public virtual TReturn VisitTernaryExpressionNode(TernaryExpressionNode node) => DefaultVisit(node);
        public virtual TReturn VisitSamplerStateLiteralExpressionNode(SamplerStateLiteralExpressionNode node) => DefaultVisit(node);
        public virtual TReturn VisitCompileExpressionNode(CompileExpressionNode node) => DefaultVisit(node);
        public virtual TReturn VisitQualifiedNamedTypeNode(QualifiedNamedTypeNode node) => DefaultVisit(node);
        public virtual TReturn VisitNamedTypeNode(NamedTypeNode node) => DefaultVisit(node);
        public virtual TReturn VisitPredefinedObjectTypeNode(PredefinedObjectTypeNode node) => DefaultVisit(node);
        public virtual TReturn VisitStructTypeNode(StructTypeNode node) => DefaultVisit(node);
        public virtual TReturn VisitScalarTypeNode(ScalarTypeNode node) => DefaultVisit(node);
        public virtual TReturn VisitMatrixTypeNode(MatrixTypeNode node) => DefaultVisit(node);
        public virtual TReturn VisitGenericMatrixTypeNode(GenericMatrixTypeNode node) => DefaultVisit(node);
        public virtual TReturn VisitVectorTypeNode(VectorTypeNode node) => DefaultVisit(node);
        public virtual TReturn VisitGenericVectorTypeNode(GenericVectorTypeNode node) => DefaultVisit(node);
        public virtual TReturn VisitTechniqueNode(TechniqueNode node) => DefaultVisit(node);
        public virtual TReturn VisitLiteralTemplateArgumentType(LiteralTemplateArgumentType node) => DefaultVisit(node);
        public virtual TReturn VisitStatePropertyNode(StatePropertyNode node) => DefaultVisit(node);
        public virtual TReturn VisitPassNode(PassNode node) => DefaultVisit(node);
        public virtual TReturn VisitObjectLikeMacroNode(ObjectLikeMacroNode node) => DefaultVisit(node);
        public virtual TReturn VisitFunctionLikeMacroNode(FunctionLikeMacroNode node) => DefaultVisit(node);
        public virtual TReturn VisitIncludeDirectiveNode(IncludeDirectiveNode node) => DefaultVisit(node);
        public virtual TReturn VisitLineDirectiveNode(LineDirectiveNode node) => DefaultVisit(node);
        public virtual TReturn VisitUndefDirectiveNode(UndefDirectiveNode node) => DefaultVisit(node);
        public virtual TReturn VisitErrorDirectiveNode(ErrorDirectiveNode node) => DefaultVisit(node);
        public virtual TReturn VisitPragmaDirectiveNode(PragmaDirectiveNode node) => DefaultVisit(node);
        public virtual TReturn VisitIfDefDirectiveNode(IfDefDirectiveNode node) => DefaultVisit(node);
        public virtual TReturn VisitIfNotDefDirectiveNode(IfNotDefDirectiveNode node) => DefaultVisit(node);
        public virtual TReturn VisitIfDirectiveNode(IfDirectiveNode node) => DefaultVisit(node);
        public virtual TReturn VisitElseDirectiveNode(ElseDirectiveNode node) => DefaultVisit(node);
    }
}
