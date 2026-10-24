using System;
using System.Collections.Generic;
using System.Linq;
using Illusion.ShaderTools;
using Illusion.ShaderTools.Syntax.HLSL;

namespace Illusion.Rendering.Editor
{
    internal static class PathTracingCoverageSlice
    {
        public static string Apply(string source)
        {
            var document = ShaderSource.ParseHlsl(source);
            var edits = new List<(int, int, string)>();
            foreach (var declaration in document.Declarations)
            {
                if (declaration.Node is not FunctionDefinitionNode function || function.Name.GetName() != "frag") continue;
                if (!document.Find("PathTracingEvaluateTemplate", declaration.Scope).Any()) continue;
                var returns = Walk(function.Body).OfType<ReturnStatementNode>().ToArray();
                if (returns.Length == 0 || returns.Any(r => r.Expression is not IdentifierExpressionNode)) continue;
                string output = ((IdentifierExpressionNode)returns[0].Expression).GetName();
                if (returns.Any(r => ((IdentifierExpressionNode)r.Expression).GetName() != output)) continue;
                var localDeclarations = Walk(function.Body).OfType<VariableDeclarationStatementNode>().ToArray();
                var locals = localDeclarations.SelectMany(v => v.Declarators).Select(v => v.Name.Identifier).ToHashSet();
                var shadowed = localDeclarations.SelectMany(v => v.Declarators).GroupBy(v => v.Name.Identifier).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
                var live = new HashSet<string>();
                var used = new HashSet<string> { output };
                foreach (string name in new[] { "PathTracingAnyHit", "PathTracingTemplateCoverage" })
                    foreach (var consumer in document.Find(name, declaration.Scope))
                        foreach (var field in Walk(consumer.Node).OfType<FieldAccessExpressionNode>())
                            live.Add(output + "." + field.Name.Identifier);
                if (live.Count == 0) continue;
                Slice(function.Body, live);

                bool Slice(StatementNode statement, HashSet<string> needed)
                {
                    if (statement is BlockNode block)
                    {
                        bool kept = false;
                        for (int i = block.Statements.Count - 1; i >= 0; i--)
                        {
                            var child = block.Statements[i];
                            int before = edits.Count;
                            if (Slice(child, needed)) kept = true;
                            else
                            {
                                edits.RemoveRange(before, edits.Count - before);
                                edits.Add((child.Span.StartIndex, child.Span.Length, ""));
                            }
                        }
                        return kept;
                    }
                    if (statement is ReturnStatementNode) return true;
                    if (statement is ForStatementNode or WhileStatementNode or DoWhileStatementNode or SwitchStatementNode)
                    {
                        Read(statement, needed);
                        return true;
                    }
                    if (statement is IfStatementNode conditional)
                    {
                        var left = new HashSet<string>(needed);
                        var right = new HashSet<string>(needed);
                        bool a = Branch(conditional.Body, left);
                        bool b = conditional.ElseClause != null && Branch(conditional.ElseClause, right);
                        if (!a && !b && !Effects(conditional.Condition)) return false;
                        needed.UnionWith(left);
                        needed.UnionWith(right);
                        Read(conditional.Condition, needed);
                        return true;

                        bool Branch(StatementNode branch, HashSet<string> state)
                        {
                            int before = edits.Count;
                            bool keep = Slice(branch, state);
                            if (!keep)
                            {
                                edits.RemoveRange(before, edits.Count - before);
                                edits.Add((branch.Span.StartIndex, branch.Span.Length, "{}"));
                            }
                            return keep;
                        }
                    }
                    var declarations = statement is VariableDeclarationStatementNode variables
                        ? variables.Declarators.Select(v => v.Name.Identifier).ToArray() : Array.Empty<string>();
                    var writes = Mutations(statement).Select(Key).Where(k => k != null).ToArray();
                    bool required = declarations.Any(v => needed.Contains(v) || used.Contains(v)) ||
                        writes.Any(w => needed.Contains(w) || w == output && needed.Any(n => n.StartsWith(output + ".", StringComparison.Ordinal))) || Effects(statement);
                    if (!required) return false;
                    foreach (string name in declarations) if (!shadowed.Contains(name)) needed.Remove(name);
                    if (statement is ExpressionStatementNode { Expression: AssignmentExpressionNode assignment } &&
                        assignment.Operator == OperatorKind.Assignment && assignment.Left is IdentifierExpressionNode identifier)
                        if (!shadowed.Contains(identifier.GetName())) needed.Remove(identifier.GetName());
                    Read(statement, needed);
                    return true;
                }

                string Key(ExpressionNode expression)
                {
                    if (expression is FieldAccessExpressionNode field && field.Target is IdentifierExpressionNode target && target.GetName() == output)
                        return output + "." + field.Name.Identifier;
                    while (expression is FieldAccessExpressionNode f) expression = f.Target;
                    while (expression is ElementAccessExpressionNode element) expression = element.Target;
                    return expression is IdentifierExpressionNode name ? name.GetName() : null;
                }

                void Read(HLSLSyntaxNode node, HashSet<string> needed)
                {
                    if (node is FieldAccessExpressionNode field && field.Target is IdentifierExpressionNode target && target.GetName() == output)
                    {
                        needed.Add(output + "." + field.Name.Identifier);
                        used.Add(output);
                        return;
                    }
                    if (node is AssignmentExpressionNode assignment)
                    {
                        Read(assignment.Right, needed);
                        foreach (var element in Walk(assignment.Left).OfType<ElementAccessExpressionNode>()) Read(element.Index, needed);
                        if (assignment.Operator != OperatorKind.Assignment) Read(assignment.Left, needed);
                        string key = Key(assignment.Left);
                        if (key != null) used.Add(key.Split('.')[0]);
                        return;
                    }
                    if (node is IdentifierExpressionNode name)
                    {
                        needed.Add(name.GetName());
                        used.Add(name.GetName());
                    }
                    foreach (var child in node.ChildNodes) Read(child, needed);
                }

                bool Effects(HLSLSyntaxNode node)
                {
                    if (node is ExpressionStatementNode { Expression: FunctionCallExpressionNode or MethodCallExpressionNode }) return true;
                    foreach (var mutation in Mutations(node))
                    {
                        string key = Key(mutation);
                        if (key == null || !locals.Contains(key.Split('.')[0])) return true;
                    }
                    foreach (var call in Walk(node).OfType<FunctionCallExpressionNode>())
                    {
                        var matches = document.Find(call.Name.GetName(), declaration.Scope).ToArray();
                        if (matches.Any(d => d.Node is FunctionNode f && f.Parameters.Any(p => p.Modifiers.Contains(BindingModifier.Out) || p.Modifiers.Contains(BindingModifier.Inout))) ||
                            matches.Length > 0 && document.Reachable(matches).Any(d => d.Writes.Count > 0)) return true;
                        if (matches.Length == 0)
                        {
                            string name = call.Name.GetName();
                            if (name is "clip" or "sincos" or "modf" or "frexp" || name.StartsWith("Interlocked", StringComparison.Ordinal)) return true;
                            if (!PureIntrinsics.Contains(name) && !name.StartsWith("PathTracingInlineSample", StringComparison.Ordinal)) return true;
                        }
                    }
                    foreach (var method in Walk(node).OfType<MethodCallExpressionNode>())
                        if (!method.Name.Identifier.StartsWith("Sample", StringComparison.Ordinal) && method.Name.Identifier != "Load") return true;
                    return Walk(node).Any(n => n is DiscardStatementNode);
                }
            }
            return ShaderSource.Edit(source, edits);
        }

        private static IEnumerable<ExpressionNode> Mutations(HLSLSyntaxNode node)
        {
            foreach (var item in Walk(node))
            {
                if (item is AssignmentExpressionNode assignment) yield return assignment.Left;
                else if (item is PostfixUnaryExpressionNode postfix) yield return postfix.Expression;
                else if (item is PrefixUnaryExpressionNode prefix && prefix.Operator is OperatorKind.Increment or OperatorKind.Decrement)
                    yield return prefix.Expression;
            }
        }

        private static readonly HashSet<string> PureIntrinsics = new HashSet<string>(
            ("abs acos all any asin atan atan2 ceil clamp cos cosh cross ddx ddy degrees determinant distance dot exp exp2 floor fmod frac frexp fwidth isfinite isinf isnan ldexp length lerp log log10 log2 max min modf mul normalize pow radians rcp reflect refract round rsqrt saturate sign sin sincos sinh smoothstep sqrt step tan tanh transpose trunc asfloat asint asuint f16tof32 f32tof16 countbits firstbithigh firstbitlow reversebits NonUniformResourceIndex").Split(' '));

        private static IEnumerable<HLSLSyntaxNode> Walk(HLSLSyntaxNode node)
        {
            yield return node;
            foreach (var child in node.ChildNodes)
                foreach (var descendant in Walk(child)) yield return descendant;
        }
    }
}
