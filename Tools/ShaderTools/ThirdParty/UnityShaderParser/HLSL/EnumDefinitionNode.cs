using System.Collections.Generic;
using System.Linq;
using Illusion.ShaderTools.Syntax.Common;

namespace Illusion.ShaderTools.Syntax.HLSL
{
    public sealed class EnumDefinitionNode : HLSLSyntaxNode
    {
        public IdentifierNode Name { get; set; }
        public bool IsScoped { get; set; }
        public TypeNode UnderlyingType { get; set; }
        public List<VariableDeclaratorNode> Values { get; set; }
        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            OptionalChild(UnderlyingType).Concat(Values);
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitEnumDefinitionNode(this);
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitEnumDefinitionNode(this);
        public EnumDefinitionNode(IList<Token<TokenKind>> tokens) : base(tokens) { }
    }
}
