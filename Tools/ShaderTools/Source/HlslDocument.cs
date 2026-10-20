using System;
using System.Collections.Generic;
using System.Linq;
using Illusion.ShaderTools.Syntax.HLSL;

namespace Illusion.ShaderTools
{
    public sealed class HlslDeclaration
    {
        public HLSLSyntaxNode Node { get; internal set; }
        public string Scope { get; internal set; }
        public string[] Names { get; internal set; }
        public string Key { get; internal set; }
        internal HlslDocument Document;
        internal bool Analyzed;
        private readonly HashSet<HlslDeclaration> dependencies = new HashSet<HlslDeclaration>();
        private readonly HashSet<string> writes = new HashSet<string>();
        private readonly HashSet<string> externalReferences = new HashSet<string>();
        public HashSet<HlslDeclaration> Dependencies { get { Document.Analyze(this); return dependencies; } }
        public HashSet<string> Writes { get { Document.Analyze(this); return writes; } }
        public HashSet<string> ExternalReferences { get { Document.Analyze(this); return externalReferences; } }
    }

    public sealed class HlslDocument
    {
        public string Source { get; }
        public IReadOnlyList<HLSLSyntaxNode> Nodes { get; }
        public IReadOnlyList<HlslDeclaration> Declarations => declarations;
        private readonly List<HlslDeclaration> declarations = new List<HlslDeclaration>();
        private readonly Dictionary<string, List<HlslDeclaration>> symbols = new Dictionary<string, List<HlslDeclaration>>();

        internal HlslDocument(string source, List<HLSLSyntaxNode> nodes)
        {
            Source = source;
            Nodes = nodes;
            Index(nodes, "");
        }

        internal void Analyze(HlslDeclaration declaration)
        {
            if (declaration.Analyzed) return;
            declaration.Analyzed = true;
            Visit(declaration.Node, declaration, new Scope(null), true);
        }

        public IEnumerable<HlslDeclaration> Find(string name, string scope = "")
        {
            if (name.StartsWith("::", StringComparison.Ordinal)) { scope = ""; name = name.Substring(2); }
            while (true)
            {
                string key = scope.Length == 0 ? name : scope + "::" + name;
                if (symbols.TryGetValue(key, out var found)) return found;
                int member = name.LastIndexOf("::", StringComparison.Ordinal);
                while (member > 0)
                {
                    string container = name.Substring(0, member);
                    key = scope.Length == 0 ? container : scope + "::" + container;
                    if (symbols.TryGetValue(key, out found))
                    {
                        var types = found.Where(d => d.Node is StructDefinitionNode || d.Node is TypedefNode).ToArray();
                        if (types.Length > 0) return types;
                    }
                    member = name.LastIndexOf("::", member - 1, StringComparison.Ordinal);
                }
                if (scope.Length == 0) return Enumerable.Empty<HlslDeclaration>();
                int parent = scope.LastIndexOf("::", StringComparison.Ordinal);
                scope = parent < 0 ? "" : scope.Substring(0, parent);
            }
        }

        public HashSet<HlslDeclaration> Reachable(IEnumerable<HlslDeclaration> roots)
        {
            var result = new HashSet<HlslDeclaration>();
            var pending = new Stack<HlslDeclaration>(roots);
            while (pending.Count > 0)
            {
                var item = pending.Pop();
                if (!result.Add(item)) continue;
                foreach (var dependency in item.Dependencies) pending.Push(dependency);
            }
            return result;
        }

        private void Index(IEnumerable<HLSLSyntaxNode> nodes, string scope)
        {
            foreach (var node in nodes)
            {
                if (node is NamespaceNode ns)
                {
                    Index(ns.Declarations, scope.Length == 0 ? ns.Name.GetName() : scope + "::" + ns.Name.GetName());
                    continue;
                }
                var names = Names(node).ToArray();
                string key = scope + "|" + node.GetType().Name + "|" + string.Join(",", names);
                if (names.Length == 0) key += ShaderSource.Fingerprint(node);
                if (node is StructDefinitionNode structure && structure.StructType.IsForwardDeclaration) key += "|forward";
                if (node is FunctionNode function)
                    key += "(" + string.Join(",", function.Parameters.Select(p =>
                        ShaderSource.Fingerprint(p.ParamType) + string.Join("", p.Declarator.ArrayRanks.Select(ShaderSource.Fingerprint)))) + ")";
                var item = new HlslDeclaration { Document = this, Node = node, Scope = scope, Names = names, Key = key };
                declarations.Add(item);
                foreach (string name in names)
                {
                    string qualified = scope.Length == 0 ? name : scope + "::" + name;
                    if (!symbols.TryGetValue(qualified, out var list)) symbols.Add(qualified, list = new List<HlslDeclaration>());
                    list.Add(item);
                }
            }
        }

        private static IEnumerable<string> Names(HLSLSyntaxNode node)
        {
            switch (node)
            {
                case FunctionNode f: yield return f.Name.GetName(); break;
                case StructDefinitionNode s:
                    if (s.StructType.Name != null) yield return s.StructType.Name.GetName();
                    break;
                case VariableDeclarationStatementNode v:
                    if (v.Kind is StructTypeNode type && type.Name != null) yield return type.Name.GetName();
                    foreach (var d in v.Declarators) yield return d.Name.Identifier;
                    break;
                case ConstantBufferNode c:
                    yield return c.Name.GetName();
                    foreach (var v in c.Declarations)
                        foreach (var d in v.Declarators) yield return d.Name.Identifier;
                    break;
                case TypedefNode t:
                    foreach (var n in t.ToNames) yield return n.GetName();
                    break;
                case InterfaceDefinitionNode i: yield return i.Name.GetName(); break;
                case EnumDefinitionNode e:
                    yield return e.Name.Identifier;
                    foreach (var value in e.Values)
                    {
                        yield return e.Name.Identifier + "::" + value.Name.Identifier;
                        if (!e.IsScoped) yield return value.Name.Identifier;
                    }
                    break;
            }
        }

        private sealed class Scope
        {
            private readonly Scope parent;
            private readonly HashSet<string> names = new HashSet<string>();
            public Scope(Scope parent) { this.parent = parent; }
            public void Add(string name) => names.Add(name);
            public bool Contains(string name) => names.Contains(name) || parent != null && parent.Contains(name);
        }

        private void Reference(string name, HlslDeclaration owner, Scope locals)
        {
            if (locals.Contains(name)) return;
            var matches = Find(name, owner.Scope).ToArray();
            if (matches.Length == 0) owner.ExternalReferences.Add(name);
            foreach (var match in matches)
                if (match != owner) owner.Dependencies.Add(match);
        }

        private void Write(ExpressionNode expression, HlslDeclaration owner, Scope locals)
        {
            while (true)
            {
                if (expression is FieldAccessExpressionNode field) expression = field.Target;
                else if (expression is ElementAccessExpressionNode element) expression = element.Target;
                else break;
            }
            if (expression is NamedExpressionNode named && !locals.Contains(named.GetName()))
                owner.Writes.Add(named.GetName());
        }

        private void Visit(HLSLSyntaxNode node, HlslDeclaration owner, Scope locals, bool global = false)
        {
            if (node == null) return;
            if (node is StatementNode attributed && attributed.Attributes != null)
                foreach (var attribute in attributed.Attributes) Visit(attribute, owner, locals);
            switch (node)
            {
                case FunctionNode f:
                    foreach (var attribute in f.Attributes) Visit(attribute, owner, locals);
                    Visit(f.ReturnType, owner, locals);
                    var parameters = new Scope(locals);
                    foreach (var parameter in f.Parameters)
                    {
                        Visit(parameter.ParamType, owner, parameters);
                        Visit(parameter.Declarator.Initializer, owner, parameters);
                        foreach (var rank in parameter.Declarator.ArrayRanks) Visit(rank, owner, parameters);
                        parameters.Add(parameter.Declarator.Name.Identifier);
                    }
                    if (f is FunctionDefinitionNode definition) Visit(definition.Body, owner, parameters);
                    return;
                case BlockNode block:
                    var blockScope = new Scope(locals);
                    foreach (var statement in block.Statements) Visit(statement, owner, blockScope);
                    return;
                case ForStatementNode loop:
                    var loopScope = new Scope(locals);
                    Visit(loop.Declaration, owner, loopScope);
                    Visit(loop.Initializer, owner, loopScope);
                    Visit(loop.Condition, owner, loopScope);
                    Visit(loop.Increment, owner, loopScope);
                    Visit(loop.Body, owner, new Scope(loopScope));
                    return;
                case IfStatementNode branch:
                    Visit(branch.Condition, owner, locals);
                    Visit(branch.Body, owner, new Scope(locals));
                    Visit(branch.ElseClause, owner, new Scope(locals));
                    return;
                case WhileStatementNode loop:
                    Visit(loop.Condition, owner, locals);
                    Visit(loop.Body, owner, new Scope(locals));
                    return;
                case DoWhileStatementNode loop:
                    Visit(loop.Body, owner, new Scope(locals));
                    Visit(loop.Condition, owner, locals);
                    return;
                case SwitchStatementNode selection:
                    Visit(selection.Expression, owner, locals);
                    var switchScope = new Scope(locals);
                    foreach (var clause in selection.Clauses) Visit(clause, owner, switchScope);
                    return;
                case VariableDeclarationStatementNode variable:
                    Visit(variable.Kind, owner, locals);
                    foreach (var declarator in variable.Declarators)
                    {
                        foreach (var rank in declarator.ArrayRanks) Visit(rank, owner, locals);
                        if (!global) locals.Add(declarator.Name.Identifier);
                        Visit(declarator.Initializer, owner, locals);
                    }
                    return;
                case ConstantBufferNode buffer:
                    foreach (var variable in buffer.Declarations) Visit(variable, owner, locals, true);
                    return;
                case StructDefinitionNode structure:
                    Visit(structure.StructType, owner, locals);
                    return;
                case StructTypeNode structure:
                    var members = new Scope(locals);
                    foreach (var field in structure.Fields)
                        foreach (var declarator in field.Declarators) members.Add(declarator.Name.Identifier);
                    foreach (var method in structure.Methods) members.Add(method.Name.GetName());
                    foreach (var nested in structure.NestedTypes)
                        foreach (string name in Names(nested)) members.Add(name);
                    foreach (var nested in structure.NestedTypes) Visit(nested, owner, members);
                    foreach (var enumeration in structure.Enums)
                        foreach (string name in Names(enumeration)) members.Add(name);
                    foreach (var enumeration in structure.Enums) Visit(enumeration, owner, members);
                    foreach (var inherited in structure.Inherits) Visit(inherited, owner, locals);
                    foreach (var field in structure.Fields) Visit(field, owner, members);
                    foreach (var method in structure.Methods) Visit(method, owner, members);
                    return;
                case EnumDefinitionNode enumeration:
                    Visit(enumeration.UnderlyingType, owner, locals);
                    var enumScope = new Scope(locals);
                    foreach (var value in enumeration.Values)
                    {
                        Visit(value.Initializer, owner, enumScope);
                        enumScope.Add(value.Name.Identifier);
                    }
                    return;
                case TypedefNode alias:
                    Visit(alias.FromType, owner, locals);
                    return;
                case NamedExpressionNode name:
                    Reference(name.GetName(), owner, locals);
                    return;
                case UserDefinedNamedTypeNode name:
                    Reference(name.GetName(), owner, locals);
                    return;
                case AssignmentExpressionNode assignment:
                    Write(assignment.Left, owner, locals);
                    break;
                case PrefixUnaryExpressionNode prefix:
                    if (prefix.Operator == OperatorKind.Increment || prefix.Operator == OperatorKind.Decrement)
                        Write(prefix.Expression, owner, locals);
                    break;
                case PostfixUnaryExpressionNode postfix:
                    Write(postfix.Expression, owner, locals);
                    break;
                case FunctionCallExpressionNode call:
                    foreach (var candidate in Find(call.Name.GetName(), owner.Scope))
                        if (candidate.Node is FunctionNode function)
                            for (int i = 0; i < Math.Min(call.Arguments.Count, function.Parameters.Count); i++)
                                if (function.Parameters[i].Modifiers.Any(m => m == BindingModifier.Out || m == BindingModifier.Inout))
                                    Write(call.Arguments[i], owner, locals);
                    break;
            }
            foreach (var child in node.ChildNodes) Visit(child, owner, locals);
        }
    }
}
