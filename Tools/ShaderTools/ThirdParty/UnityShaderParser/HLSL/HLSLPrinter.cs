using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Illusion.ShaderTools.Syntax.Common;

namespace Illusion.ShaderTools.Syntax.HLSL
{
    public class HLSLPrinter : HLSLSyntaxVisitor
    {
        // Settings
        public int MaxParametersUntilLineBreak { get; set; } = 4;
        public bool IndentBlockLikeSwitchClauses { get; set; } = false;

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

        protected Stack<int> expressionPrecedences = new Stack<int>();

        protected void VisitManySeparated<T>(IList<T> nodes, string separator, bool trailing = false, bool leading = false)
            where T : HLSLSyntaxNode
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

        protected void EmitExpression(OperatorPrecedence prec, Action expressionEmitter)
        {
            int precedence = (int)prec;
            bool needsParen = false;
            if (expressionPrecedences.Count > 0 && expressionPrecedences.Peek() >= precedence)
            {
                needsParen = true;
            }
            if (prec == OperatorPrecedence.Compound)
            {
                needsParen = true;
            }

            expressionPrecedences.Push(precedence);
            if (needsParen) Emit("(");
            expressionEmitter();
            if (needsParen) Emit(")");
            expressionPrecedences.Pop();
        }

        // Visitor implementation
        public override void VisitIdentifierNode(IdentifierNode node)
        {
            Emit(node.Identifier);
        }
        public override void VisitFormalParameterNode(FormalParameterNode node)
        {
            VisitManySeparated(node.Attributes, " ", true);
            string modifiers = string.Join("", node.Modifiers.Select(PrintingUtil.GetEnumName).Select(x => x + " "));
            Emit(modifiers);
            Visit(node.ParamType);
            Emit(" ");
            Visit(node.Declarator);
        }
        public override void VisitVariableDeclaratorNode(VariableDeclaratorNode node)
        {
            Emit(node.Name.Identifier);
            VisitMany(node.ArrayRanks);
            VisitMany(node.Qualifiers);
            if (node.Annotations?.Count > 0)
            {
                EmitLine();
                EmitIndentedLine("<");
                PushIndent();
                VisitMany(node.Annotations);
                PopIndent();
                EmitIndented(">");
            }
            Visit(node.Initializer);
        }
        public override void VisitArrayRankNode(ArrayRankNode node)
        {
            Emit("[");
            Visit(node.Dimension);
            Emit("]");
        }    
        public override void VisitValueInitializerNode(ValueInitializerNode node)
        {
            Emit(" = ");
            Visit(node.Expression);
        }
        public override void VisitStateInitializerNode(StateInitializerNode node)
        {
            EmitLine();
            EmitIndentedLine("{");
            PushIndent();
            VisitMany(node.States);
            PopIndent();
            EmitIndented("}");
        }
        public override void VisitStateArrayInitializerNode(StateArrayInitializerNode node)
        {
            EmitLine();
            EmitIndentedLine("{");
            PushIndent();
            VisitManySeparated(node.Initializers, ",");
            PopIndent();
            EmitLine();
            EmitIndented("}");
        }
        private void VisitFunctionNode(FunctionNode node)
        {
            EmitIndented();
            string modifiers = string.Join("", node.Modifiers.Select(PrintingUtil.GetEnumName).Select(x => x + " "));
            Emit(modifiers);
            VisitManySeparated(node.Attributes, "\n", true);
            if (node.Attributes.Count > 0) EmitLine();
            Visit(node.ReturnType);
            Emit(" ");
            Visit(node.Name);
            Emit("(");
            if (node.Parameters?.Count > MaxParametersUntilLineBreak)
            {
                EmitLine();
                PushIndent();
                for (int i = 0; i < node.Parameters.Count; i++)
                {
                    EmitIndented();
                    Visit(node.Parameters[i]);
                    if (i < node.Parameters.Count - 1)
                        EmitLine(",");
                }
                PopIndent();
            }
            else
            {
                VisitManySeparated(node.Parameters, ", ");
            }
            Emit(")");
            Visit(node.Semantic);
        }
        public override void VisitFunctionDeclarationNode(FunctionDeclarationNode node)
        {
            VisitFunctionNode(node);
            EmitLine(";");
        }
        public override void VisitFunctionDefinitionNode(FunctionDefinitionNode node)
        {
            VisitFunctionNode(node);
            EmitLine();
            if (node.BodyIsSingleStatement)
            {
                EmitIndented();
            }
            Visit(node.Body);
        }
        public override void VisitStructDefinitionNode(StructDefinitionNode node)
        {
            EmitIndented();
            VisitManySeparated(node.Attributes, " ", true);
            Visit(node.StructType);
            EmitLine(";");
        }
        public override void VisitInterfaceDefinitionNode(InterfaceDefinitionNode node)
        {
            EmitIndented();
            VisitManySeparated(node.Attributes, " ", true);
            Emit("interface ");
            Visit(node.Name);
            EmitLine();
            EmitIndentedLine("{");
            PushIndent();
            VisitMany(node.Functions);
            PopIndent();
            EmitIndentedLine("};");
        }
        public override void VisitConstantBufferNode(ConstantBufferNode node)
        {
            if (node.IsTextureBuffer)
            {
                Emit("tbuffer ");
            }
            else
            {
                Emit("cbuffer ");
            }
            Visit(node.Name);
            Visit(node.RegisterLocation);
            EmitLine();
            EmitIndentedLine("{");
            PushIndent();
            VisitMany(node.Declarations);
            PopIndent();
            EmitIndentedLine("}");
        }
        public override void VisitNamespaceNode(NamespaceNode node)
        {
            EmitIndented("namespace ");
            Visit(node.Name);
            EmitLine();
            EmitIndentedLine("{");
            PushIndent();
            VisitMany(node.Declarations);
            PopIndent();
            EmitIndentedLine("}");
        }
        public override void VisitTypedefNode(TypedefNode node)
        {
            EmitIndented();
            VisitManySeparated(node.Attributes, " ", true);
            Emit("typedef ");
            if (node.IsConst)
            {
                Emit("const ");
            }
            Visit(node.FromType);
            Emit(" ");
            VisitManySeparated(node.ToNames, ", ");
            EmitLine(";");
        }
        public override void VisitSemanticNode(SemanticNode node)
        {
            Emit($" : {node.Name}");
        }
        public override void VisitRegisterLocationNode(RegisterLocationNode node)
        {
            Emit($" : register({PrintingUtil.GetEnumName(node.Kind)}{node.Location}");
            if (node.Space != null)
            {
                Emit($", space{node.Space})");
            }
            else
            {
                Emit(")");
            }
        }
        public override void VisitPackoffsetNode(PackoffsetNode node)
        {
            Emit($" : packoffset(c{node.Location}");
            if (string.IsNullOrEmpty(node.Swizzle))
            {
                Emit(")");
            }
            else
            {
                Emit($".{node.Swizzle})");
            }
        }
        public override void VisitBlockNode(BlockNode node)
        {
            EmitIndented();
            VisitManySeparated(node.Attributes, " ", true);
            EmitLine("{");
            PushIndent();
            VisitMany(node.Statements);
            PopIndent();
            EmitIndentedLine("}");
        }
        public override void VisitVariableDeclarationStatementNode(VariableDeclarationStatementNode node)
        {
            bool partOfFor = node.Parent is ForStatementNode forStatement && forStatement.Declaration == node;

            if (!partOfFor) EmitIndented();
            VisitManySeparated(node.Attributes, " ", true);
            string modifiers = string.Join("", node.Modifiers.Select(PrintingUtil.GetEnumName).Select(x => x + " "));
            Emit(modifiers);
            Visit(node.Kind);
            Emit(" ");
            VisitManySeparated(node.Declarators, ", ");
            Emit(";");
            if (!partOfFor) EmitLine();
        }
        public override void VisitReturnStatementNode(ReturnStatementNode node)
        {
            if (node.Expression != null)
            {
                EmitIndented("return ");
                Visit(node.Expression);
                EmitLine(";");
            }
            else
            {
                EmitIndentedLine("return;");
            }
        }

        public override void VisitBreakStatementNode(BreakStatementNode node)
        {
            EmitIndentedLine("break;");
        }
        public override void VisitContinueStatementNode(ContinueStatementNode node)
        {
            EmitIndentedLine("continue;");
        }
        public override void VisitDiscardStatementNode(DiscardStatementNode node)
        {
            EmitIndentedLine("discard;");
        }
        public override void VisitEmptyStatementNode(EmptyStatementNode node)
        {
            EmitIndentedLine(";");
        }
        public override void VisitForStatementNode(ForStatementNode node)
        {
            EmitIndented();
            VisitManySeparated(node.Attributes, " ", true);
            Emit("for (");
            if (node.FirstIsDeclaration)
            {
                Visit(node.Declaration);
                Emit(" ");
            }
            else
            {
                Visit(node.Initializer);
                Emit("; ");
            }

            Visit(node.Condition);
            Emit("; ");

            Visit(node.Increment);
            EmitLine(")");
            if (node.BodyIsSingleStatement)
            {
                EmitIndented();
            }
            Visit(node.Body);
        }
        public override void VisitWhileStatementNode(WhileStatementNode node)
        {
            EmitIndented();
            VisitManySeparated(node.Attributes, " ", true);
            Emit("while (");
            Visit(node.Condition);
            EmitLine(")");
            if (node.BodyIsSingleStatement)
            {
                EmitIndented();
            }
            Visit(node.Body);
        }
        public override void VisitDoWhileStatementNode(DoWhileStatementNode node)
        {
            EmitIndented();
            VisitManySeparated(node.Attributes, " ", true);
            EmitLine("do");
            if (node.BodyIsSingleStatement)
            {
                EmitIndented();
            }
            Visit(node.Body);
            EmitIndented("while (");
            Visit(node.Condition);
            EmitLine(");");
        }
        public override void VisitIfStatementNode(IfStatementNode node)
        {
            if (!node.BodyIsElseIfClause)
            {
                EmitIndented();
            }
            VisitManySeparated(node.Attributes, " ", true);
            Emit("if (");
            Visit(node.Condition);
            EmitLine(")");
            if (node.BodyIsSingleStatement)
            {
                EmitIndented();
            }
            Visit(node.Body);
            if (node.ElseClause != null)
            {
                EmitIndented("else ");
                if (node.ElseClauseIsSingleStatement && !node.ElseClauseIsElseIfClause)
                {
                    EmitLine();
                    EmitIndented();
                }
                else if (!node.ElseClauseIsElseIfClause)
                {
                    EmitLine();
                }

                Visit(node.ElseClause);
            }
        }
        public override void VisitSwitchStatementNode(SwitchStatementNode node)
        {
            EmitIndented();
            VisitManySeparated(node.Attributes, " ", true);
            Emit("switch (");
            Visit(node.Expression);
            EmitLine(")");
            EmitIndentedLine("{");
            PushIndent();
            VisitMany(node.Clauses);
            PopIndent();
            EmitIndentedLine("}");
        }
        public override void VisitSwitchClauseNode(SwitchClauseNode node)
        {
            VisitMany(node.Labels);
            bool isSingleBlock = node.Statements.Count == 1 && node.Statements[0] is BlockNode;
            if (!isSingleBlock && !IndentBlockLikeSwitchClauses) PushIndent();
            VisitMany(node.Statements);
            if (!isSingleBlock && !IndentBlockLikeSwitchClauses) PopIndent();
        }
        public override void VisitSwitchCaseLabelNode(SwitchCaseLabelNode node)
        {
            EmitIndented("case ");
            Visit(node.Value);
            EmitLine(":");
        }
        public override void VisitSwitchDefaultLabelNode(SwitchDefaultLabelNode node)
        {
            EmitIndentedLine("default:");
        }
        public override void VisitExpressionStatementNode(ExpressionStatementNode node)
        {
            EmitIndented();
            Visit(node.Expression);
            EmitLine(";");
        }
        public override void VisitAttributeNode(AttributeNode node)
        {
            Emit("[");
            Emit(node.Name.Identifier);
            if (node.Arguments?.Count > 0)
            {
                Emit("(");
                VisitManySeparated(node.Arguments, ", ");
                Emit(")");
            }
            Emit("]");
        }
        public override void VisitQualifiedIdentifierExpressionNode(QualifiedIdentifierExpressionNode node)
        {
            Emit(node.GetName());
        }
        public override void VisitIdentifierExpressionNode(IdentifierExpressionNode node)
        {
            Emit(node.GetName());
        }
        public override void VisitLiteralExpressionNode(LiteralExpressionNode node)
        {
            if (node.Kind == LiteralKind.String)
            {
                Emit($"\"{node.Lexeme}\"");
            }
            else if (node.Kind == LiteralKind.Character)
            {
                Emit($"'{node.Lexeme}'");
            }
            else
            {
                Emit(node.Lexeme);
            }
        }
        public override void VisitAssignmentExpressionNode(AssignmentExpressionNode node)
        {
            EmitExpression(OperatorPrecedence.Assignment, () =>
            {
                Visit(node.Left);
                Emit($" {PrintingUtil.GetEnumName(node.Operator)} ");
                Visit(node.Right);
            });
        }
        public override void VisitBinaryExpressionNode(BinaryExpressionNode node)
        {
            EmitExpression(HLSLSyntaxFacts.GetPrecedence(node.Operator, OperatorFixity.Infix), () =>
            {
                Visit(node.Left);
                Emit($" {PrintingUtil.GetEnumName(node.Operator)} ");
                Visit(node.Right);
            });
        }
        public override void VisitCompoundExpressionNode(CompoundExpressionNode node)
        {
            EmitExpression(OperatorPrecedence.Compound, () =>
            {
                Visit(node.Left);
                Emit(", ");
                Visit(node.Right);
            });
        }
        public override void VisitPrefixUnaryExpressionNode(PrefixUnaryExpressionNode node)
        {
            EmitExpression(OperatorPrecedence.PrefixUnary, () =>
            {
                Emit($"{PrintingUtil.GetEnumName(node.Operator)}");
                Visit(node.Expression);
            });
        }
        public override void VisitPostfixUnaryExpressionNode(PostfixUnaryExpressionNode node)
        {
            EmitExpression(OperatorPrecedence.PostFixUnary, () =>
            {
                Visit(node.Expression);
                Emit($"{PrintingUtil.GetEnumName(node.Operator)}");
            });
        }
        public override void VisitFieldAccessExpressionNode(FieldAccessExpressionNode node)
        {
            EmitExpression(OperatorPrecedence.PostFixUnary, () =>
            {
                bool needsExtraParen = node.Target is LiteralExpressionNode; // Can't directly swizzle a literal
                if (needsExtraParen) Emit("(");
                Visit(node.Target);
                if (needsExtraParen) Emit(")");
                Emit($".{node.Name}");
            });
        }
        public override void VisitMethodCallExpressionNode(MethodCallExpressionNode node)
        {
            EmitExpression(OperatorPrecedence.PostFixUnary, () =>
            {
                Visit(node.Target);
                Emit($".{node.Name}(");
                VisitManySeparated(node.Arguments, ", ");
                Emit(")");
            });
        }
        public override void VisitFunctionCallExpressionNode(FunctionCallExpressionNode node)
        {
            EmitExpression(OperatorPrecedence.PostFixUnary, () =>
            {
                Visit(node.Name);
                Emit("(");
                VisitManySeparated(node.Arguments, ", ");
                Emit(")");
            });
        }
        public override void VisitNumericConstructorCallExpressionNode(NumericConstructorCallExpressionNode node)
        {
            EmitExpression(OperatorPrecedence.PostFixUnary, () =>
            {
                Visit(node.Kind);
                Emit("(");
                VisitManySeparated(node.Arguments, ", ");
                Emit(")");
            });
        }
        public override void VisitElementAccessExpressionNode(ElementAccessExpressionNode node)
        {
            EmitExpression(OperatorPrecedence.PostFixUnary, () =>
            {
                Visit(node.Target);
                Emit("[");
                Visit(node.Index);
                Emit("]");
            });
        }
        public override void VisitCastExpressionNode(CastExpressionNode node)
        {
            if (node.IsFunctionLike)
            {
                EmitExpression(OperatorPrecedence.PostFixUnary, () =>
                {
                    Visit(node.Kind);
                    Emit("(");
                    Visit(node.Expression);
                    Emit(")");
                });
            }
            else
            {
                EmitExpression(OperatorPrecedence.PrefixUnary, () =>
                {
                    Emit("(");
                    Visit(node.Kind);
                    VisitMany(node.ArrayRanks);
                    Emit(")");
                    Visit(node.Expression);
                });
            }
        }
        public override void VisitArrayInitializerExpressionNode(ArrayInitializerExpressionNode node)
        {
            EmitLine();
            EmitIndentedLine("{");
            PushIndent();
            foreach (var element in node.Elements)
            {
                EmitIndented();
                Visit(element);
                EmitLine(",");
            }
            PopIndent();
            EmitIndented("}");
        }
        public override void VisitTernaryExpressionNode(TernaryExpressionNode node)
        {
            EmitExpression(OperatorPrecedence.Ternary, () =>
            {
                Visit(node.Condition);
                Emit(" ? ");
                Visit(node.TrueCase);
                Emit(" : ");
                Visit(node.FalseCase);
            });
        }
        public override void VisitSamplerStateLiteralExpressionNode(SamplerStateLiteralExpressionNode node)
        {
            EmitLine("sampler_state");
            EmitIndentedLine("{");
            PushIndent();
            VisitMany(node.States);
            PopIndent();
            EmitIndented("}");
        }
        public override void VisitCompileExpressionNode(CompileExpressionNode node)
        {
            Emit($"compile {node.Target} ");
            Visit(node.Invocation);
        }
        public override void VisitQualifiedNamedTypeNode(QualifiedNamedTypeNode node)
        {
            Emit(node.GetName());
        }
        public override void VisitNamedTypeNode(NamedTypeNode node)
        {
            Emit(node.GetName());
        }
        public override void VisitPredefinedObjectTypeNode(PredefinedObjectTypeNode node)
        {
            Emit(PrintingUtil.GetEnumName(node.Kind));
            if (node.TemplateArguments?.Count > 0)
            {
                Emit("<");
                VisitManySeparated(node.TemplateArguments, ", ");
                Emit(">");
            }
        }
        public override void VisitStructTypeNode(StructTypeNode node)
        {
            if (node.IsClass)
            {
                Emit("class ");
            }
            else
            {
                Emit("struct ");
            }

            Visit(node.Name);
            if (node.IsForwardDeclaration) return;
            if (node.Inherits.Count > 0)
            {
                Emit(" : ");
                VisitManySeparated(node.Inherits, ", ");
            }
            EmitLine();
            EmitIndentedLine("{");
            PushIndent();
            VisitMany(node.Enums);
            VisitMany(node.NestedTypes);
            VisitMany(node.Fields);
            VisitMany(node.Methods);
            PopIndent();
            EmitIndented("}");
        }
        public override void VisitEnumDefinitionNode(EnumDefinitionNode node)
        {
            Emit(node.IsScoped ? "enum class " : "enum ");
            Visit(node.Name);
            if (node.UnderlyingType != null)
            {
                Emit(" : ");
                Visit(node.UnderlyingType);
            }
            Emit(" { ");
            VisitManySeparated(node.Values, ", ");
            EmitLine(" };");
        }
        public override void VisitScalarTypeNode(ScalarTypeNode node)
        {
            Emit(PrintingUtil.GetEnumName(node.Kind));
        }
        public override void VisitMatrixTypeNode(MatrixTypeNode node)
        {
            Emit($"{PrintingUtil.GetEnumName(node.Kind)}{node.FirstDimension}x{node.SecondDimension}");
        }
        public override void VisitGenericMatrixTypeNode(GenericMatrixTypeNode node)
        {
            Emit($"matrix<{PrintingUtil.GetEnumName(node.Kind)}, ");
            Visit(node.FirstDimension);
            Emit(", ");
            Visit(node.SecondDimension);
            Emit(">");
        }
        public override void VisitVectorTypeNode(VectorTypeNode node)
        {
            Emit($"{PrintingUtil.GetEnumName(node.Kind)}{node.Dimension}");
        }
        public override void VisitGenericVectorTypeNode(GenericVectorTypeNode node)
        {
            Emit($"vector<{PrintingUtil.GetEnumName(node.Kind)}, ");
            Visit(node.Dimension);
            Emit(">");
        }
        public override void VisitTechniqueNode(TechniqueNode node)
        {
            Emit(node.Version == 11 ? "technique " : $"technique{node.Version} ");
            Visit(node.Name);
            EmitLine();
            if (node.Annotations?.Count > 0)
            {
                EmitIndentedLine("<");
                PushIndent();
                VisitMany(node.Annotations);
                PopIndent();
                EmitIndentedLine(">");
            }
            EmitIndentedLine("{");
            PushIndent();
            VisitMany(node.Passes);
            PopIndent();
            EmitIndentedLine("}");
        }
        public override void VisitStatePropertyNode(StatePropertyNode node)
        {
            EmitIndented();
            Visit(node.Name);
            Visit(node.ArrayRank);
            Emit(" = ");
            if (node.IsReference) Emit("<");
            Visit(node.Value);
            if (node.IsReference) Emit(">");
            EmitLine(";");
        }
        public override void VisitPassNode(PassNode node)
        {
            EmitIndented("pass ");
            Visit(node.Name);
            EmitLine();
            if (node.Annotations?.Count > 0)
            {
                EmitIndentedLine("<");
                PushIndent();
                VisitMany(node.Annotations);
                PopIndent();
                EmitIndentedLine(">");
            }
            EmitIndentedLine("{");
            PushIndent();
            VisitMany(node.Statements);
            PopIndent();
            EmitIndentedLine("}");
        }

        private string TokensToString(IEnumerable<Token<HLSL.TokenKind>> tokens)
        {
            return string.Join(" ", tokens.Select(x => HLSLSyntaxFacts.TokenToString(x)));
        }

        public override void VisitObjectLikeMacroNode(ObjectLikeMacroNode node)
        {
            EmitIndentedLine($"#define {node.Name} {TokensToString(node.Value)}");
        }

        public override void VisitFunctionLikeMacroNode(FunctionLikeMacroNode node)
        {
            EmitIndentedLine($"#define {node.Name}({string.Join(", ", node.Arguments)}) {TokensToString(node.Value)}");
        }

        public override void VisitErrorDirectiveNode(ErrorDirectiveNode node)
        {
            EmitIndentedLine($"#error {TokensToString(node.Value)}");
        }

        public override void VisitIncludeDirectiveNode(IncludeDirectiveNode node)
        {
            EmitIndentedLine($"#include \"{node.Path}\"");
        }

        public override void VisitLineDirectiveNode(LineDirectiveNode node)
        {
            EmitIndentedLine($"#line {node.Line}");
        }

        public override void VisitPragmaDirectiveNode(PragmaDirectiveNode node)
        {
            EmitIndentedLine($"#pragma {TokensToString(node.Value)}");
        }

        public override void VisitUndefDirectiveNode(UndefDirectiveNode node)
        {
            EmitIndentedLine($"#undef {node.Name}");
        }

        public override void VisitIfDirectiveNode(IfDirectiveNode node)
        {
            if (node.IsElif)
            {
                EmitIndented($"#elif ");
            }
            else
            {
                EmitIndented($"#if ");
            }
            Visit(node.Condition);
            EmitLine();
            VisitMany(node.Body);
            if (node.ElseClause == null)
            {
                EmitIndentedLine("#endif");
            }
            else
            {
                Visit(node.ElseClause);
            }
        }

        public override void VisitIfDefDirectiveNode(IfDefDirectiveNode node)
        {
            EmitIndentedLine($"#ifdef {node.Condition}");
            VisitMany(node.Body);
            if (node.ElseClause == null)
            {
                EmitIndentedLine("#endif");
            }
            else
            {
                Visit(node.ElseClause);
            }
        }

        public override void VisitIfNotDefDirectiveNode(IfNotDefDirectiveNode node)
        {
            EmitIndentedLine($"#ifndef {node.Condition}");
            VisitMany(node.Body);
            if (node.ElseClause == null)
            {
                EmitIndentedLine("#endif");
            }
            else
            {
                Visit(node.ElseClause);
            }
        }

        public override void VisitElseDirectiveNode(ElseDirectiveNode node)
        {
            EmitIndentedLine("#else");
            VisitMany(node.Body);
            EmitIndentedLine("#endif");
        }
    }
}
