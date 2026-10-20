using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Illusion.ShaderTools.Syntax.Common;

namespace Illusion.ShaderTools.Syntax.HLSL
{
    using HLSLToken = Token<TokenKind>;

    #region Common types
    public enum TokenKind
    {
        InvalidToken,

        AppendStructuredBufferKeyword,
        BlendStateKeyword,
        BoolKeyword,
        Bool1Keyword,
        Bool2Keyword,
        Bool3Keyword,
        Bool4Keyword,
        Bool1x1Keyword,
        Bool1x2Keyword,
        Bool1x3Keyword,
        Bool1x4Keyword,
        Bool2x1Keyword,
        Bool2x2Keyword,
        Bool2x3Keyword,
        Bool2x4Keyword,
        Bool3x1Keyword,
        Bool3x2Keyword,
        Bool3x3Keyword,
        Bool3x4Keyword,
        Bool4x1Keyword,
        Bool4x2Keyword,
        Bool4x3Keyword,
        Bool4x4Keyword,
        BufferKeyword,
        ByteAddressBufferKeyword,
        BreakKeyword,
        CaseKeyword,
        CBufferKeyword,
        CentroidKeyword,
        ClassKeyword,
        ColumnMajorKeyword,
        CompileKeyword,
        ConstKeyword,
        ConsumeStructuredBufferKeyword,
        ContinueKeyword,
        DefaultKeyword,
        DefKeyword,
        DepthStencilStateKeyword,
        DiscardKeyword,
        DoKeyword,
        DoubleKeyword,
        Double1Keyword,
        Double2Keyword,
        Double3Keyword,
        Double4Keyword,
        Double1x1Keyword,
        Double1x2Keyword,
        Double1x3Keyword,
        Double1x4Keyword,
        Double2x1Keyword,
        Double2x2Keyword,
        Double2x3Keyword,
        Double2x4Keyword,
        Double3x1Keyword,
        Double3x2Keyword,
        Double3x3Keyword,
        Double3x4Keyword,
        Double4x1Keyword,
        Double4x2Keyword,
        Double4x3Keyword,
        Double4x4Keyword,
        ElseKeyword,
        ErrorKeyword,
        ExportKeyword,
        ExternKeyword,
        FloatKeyword,
        Float1Keyword,
        Float2Keyword,
        Float3Keyword,
        Float4Keyword,
        Float1x1Keyword,
        Float1x2Keyword,
        Float1x3Keyword,
        Float1x4Keyword,
        Float2x1Keyword,
        Float2x2Keyword,
        Float2x3Keyword,
        Float2x4Keyword,
        Float3x1Keyword,
        Float3x2Keyword,
        Float3x3Keyword,
        Float3x4Keyword,
        Float4x1Keyword,
        Float4x2Keyword,
        Float4x3Keyword,
        Float4x4Keyword,
        ForKeyword,
        GloballycoherentKeyword,
        GroupsharedKeyword,
        HalfKeyword,
        Half1Keyword,
        Half2Keyword,
        Half3Keyword,
        Half4Keyword,
        Half1x1Keyword,
        Half1x2Keyword,
        Half1x3Keyword,
        Half1x4Keyword,
        Half2x1Keyword,
        Half2x2Keyword,
        Half2x3Keyword,
        Half2x4Keyword,
        Half3x1Keyword,
        Half3x2Keyword,
        Half3x3Keyword,
        Half3x4Keyword,
        Half4x1Keyword,
        Half4x2Keyword,
        Half4x3Keyword,
        Half4x4Keyword,
        IfKeyword,
        IndicesKeyword,
        InKeyword,
        InlineKeyword,
        InoutKeyword,
        InputPatchKeyword,
        IntKeyword,
        Int1Keyword,
        Int2Keyword,
        Int3Keyword,
        Int4Keyword,
        Int1x1Keyword,
        Int1x2Keyword,
        Int1x3Keyword,
        Int1x4Keyword,
        Int2x1Keyword,
        Int2x2Keyword,
        Int2x3Keyword,
        Int2x4Keyword,
        Int3x1Keyword,
        Int3x2Keyword,
        Int3x3Keyword,
        Int3x4Keyword,
        Int4x1Keyword,
        Int4x2Keyword,
        Int4x3Keyword,
        Int4x4Keyword,
        InterfaceKeyword,
        LineKeyword,
        LineAdjKeyword,
        LinearKeyword,
        LineStreamKeyword,
        MatrixKeyword,
        MessageKeyword,
        Min10FloatKeyword,
        Min10Float1Keyword,
        Min10Float2Keyword,
        Min10Float3Keyword,
        Min10Float4Keyword,
        Min10Float1x1Keyword,
        Min10Float1x2Keyword,
        Min10Float1x3Keyword,
        Min10Float1x4Keyword,
        Min10Float2x1Keyword,
        Min10Float2x2Keyword,
        Min10Float2x3Keyword,
        Min10Float2x4Keyword,
        Min10Float3x1Keyword,
        Min10Float3x2Keyword,
        Min10Float3x3Keyword,
        Min10Float3x4Keyword,
        Min10Float4x1Keyword,
        Min10Float4x2Keyword,
        Min10Float4x3Keyword,
        Min10Float4x4Keyword,
        Min12IntKeyword,
        Min12Int1Keyword,
        Min12Int2Keyword,
        Min12Int3Keyword,
        Min12Int4Keyword,
        Min12Int1x1Keyword,
        Min12Int1x2Keyword,
        Min12Int1x3Keyword,
        Min12Int1x4Keyword,
        Min12Int2x1Keyword,
        Min12Int2x2Keyword,
        Min12Int2x3Keyword,
        Min12Int2x4Keyword,
        Min12Int3x1Keyword,
        Min12Int3x2Keyword,
        Min12Int3x3Keyword,
        Min12Int3x4Keyword,
        Min12Int4x1Keyword,
        Min12Int4x2Keyword,
        Min12Int4x3Keyword,
        Min12Int4x4Keyword,
        Min12UintKeyword,
        Min12Uint1Keyword,
        Min12Uint2Keyword,
        Min12Uint3Keyword,
        Min12Uint4Keyword,
        Min12Uint1x1Keyword,
        Min12Uint1x2Keyword,
        Min12Uint1x3Keyword,
        Min12Uint1x4Keyword,
        Min12Uint2x1Keyword,
        Min12Uint2x2Keyword,
        Min12Uint2x3Keyword,
        Min12Uint2x4Keyword,
        Min12Uint3x1Keyword,
        Min12Uint3x2Keyword,
        Min12Uint3x3Keyword,
        Min12Uint3x4Keyword,
        Min12Uint4x1Keyword,
        Min12Uint4x2Keyword,
        Min12Uint4x3Keyword,
        Min12Uint4x4Keyword,
        Min16FloatKeyword,
        Min16Float1Keyword,
        Min16Float2Keyword,
        Min16Float3Keyword,
        Min16Float4Keyword,
        Min16Float1x1Keyword,
        Min16Float1x2Keyword,
        Min16Float1x3Keyword,
        Min16Float1x4Keyword,
        Min16Float2x1Keyword,
        Min16Float2x2Keyword,
        Min16Float2x3Keyword,
        Min16Float2x4Keyword,
        Min16Float3x1Keyword,
        Min16Float3x2Keyword,
        Min16Float3x3Keyword,
        Min16Float3x4Keyword,
        Min16Float4x1Keyword,
        Min16Float4x2Keyword,
        Min16Float4x3Keyword,
        Min16Float4x4Keyword,
        Min16IntKeyword,
        Min16Int1Keyword,
        Min16Int2Keyword,
        Min16Int3Keyword,
        Min16Int4Keyword,
        Min16Int1x1Keyword,
        Min16Int1x2Keyword,
        Min16Int1x3Keyword,
        Min16Int1x4Keyword,
        Min16Int2x1Keyword,
        Min16Int2x2Keyword,
        Min16Int2x3Keyword,
        Min16Int2x4Keyword,
        Min16Int3x1Keyword,
        Min16Int3x2Keyword,
        Min16Int3x3Keyword,
        Min16Int3x4Keyword,
        Min16Int4x1Keyword,
        Min16Int4x2Keyword,
        Min16Int4x3Keyword,
        Min16Int4x4Keyword,
        Min16UintKeyword,
        Min16Uint1Keyword,
        Min16Uint2Keyword,
        Min16Uint3Keyword,
        Min16Uint4Keyword,
        Min16Uint1x1Keyword,
        Min16Uint1x2Keyword,
        Min16Uint1x3Keyword,
        Min16Uint1x4Keyword,
        Min16Uint2x1Keyword,
        Min16Uint2x2Keyword,
        Min16Uint2x3Keyword,
        Min16Uint2x4Keyword,
        Min16Uint3x1Keyword,
        Min16Uint3x2Keyword,
        Min16Uint3x3Keyword,
        Min16Uint3x4Keyword,
        Min16Uint4x1Keyword,
        Min16Uint4x2Keyword,
        Min16Uint4x3Keyword,
        Min16Uint4x4Keyword,
        NamespaceKeyword,
        NointerpolationKeyword,
        NoperspectiveKeyword,
        NullKeyword,
        OutKeyword,
        OutputPatchKeyword,
        PackMatrixKeyword,
        PackoffsetKeyword,
        PassKeyword,
        PayloadKeyword,
        PointKeyword,
        PointStreamKeyword,
        PragmaKeyword,
        PreciseKeyword,
        PrimitivesKeyword,
        RasterizerOrderedBufferKeyword,
        RasterizerOrderedByteAddressBufferKeyword,
        RasterizerOrderedStructuredBufferKeyword,
        RasterizerOrderedTexture1DKeyword,
        RasterizerOrderedTexture1DArrayKeyword,
        RasterizerOrderedTexture2DKeyword,
        RasterizerOrderedTexture2DArrayKeyword,
        RasterizerOrderedTexture3DKeyword,
        RasterizerStateKeyword,
        RegisterKeyword,
        ReturnKeyword,
        RowMajorKeyword,
        RWBufferKeyword,
        RWByteAddressBufferKeyword,
        RWStructuredBufferKeyword,
        RWTexture1DKeyword,
        RWTexture1DArrayKeyword,
        RWTexture2DKeyword,
        RWTexture2DArrayKeyword,
        RWTexture3DKeyword,
        SamplerKeyword,
        Sampler1DKeyword,
        Sampler2DKeyword,
        Sampler3DKeyword,
        SamplerCubeKeyword,
        SamplerComparisonStateKeyword,
        SamplerStateKeyword,
        SamplerStateLegacyKeyword,
        SharedKeyword,
        SNormKeyword,
        StaticKeyword,
        StringKeyword,
        CharKeyword,
        StructKeyword,
        StructuredBufferKeyword,
        SwitchKeyword,
        TBufferKeyword,
        TechniqueKeyword,
        Technique10Keyword,
        Technique11Keyword,
        TextureKeyword,
        Texture2DLegacyKeyword,
        TextureCubeLegacyKeyword,
        Texture1DKeyword,
        Texture1DArrayKeyword,
        Texture2DKeyword,
        Texture2DArrayKeyword,
        Texture2DMSKeyword,
        Texture2DMSArrayKeyword,
        Texture3DKeyword,
        TextureCubeKeyword,
        TextureCubeArrayKeyword,
        TriangleKeyword,
        TriangleAdjKeyword,
        TriangleStreamKeyword,
        TypedefKeyword,
        UniformKeyword,
        UNormKeyword,
        UintKeyword,
        Uint1Keyword,
        Uint2Keyword,
        Uint3Keyword,
        Uint4Keyword,
        Uint1x1Keyword,
        Uint1x2Keyword,
        Uint1x3Keyword,
        Uint1x4Keyword,
        Uint2x1Keyword,
        Uint2x2Keyword,
        Uint2x3Keyword,
        Uint2x4Keyword,
        Uint3x1Keyword,
        Uint3x2Keyword,
        Uint3x3Keyword,
        Uint3x4Keyword,
        Uint4x1Keyword,
        Uint4x2Keyword,
        Uint4x3Keyword,
        Uint4x4Keyword,
        VectorKeyword,
        VerticesKeyword,
        VolatileKeyword,
        VoidKeyword,
        WarningKeyword,
        WhileKeyword,
        TrueKeyword,
        FalseKeyword,
        UnsignedKeyword,
        DwordKeyword,
        CompileFragmentKeyword,
        DepthStencilViewKeyword,
        PixelfragmentKeyword,
        RenderTargetViewKeyword,
        StateblockStateKeyword,
        StateblockKeyword,

        OpenParenToken,
        CloseParenToken,
        OpenBracketToken,
        CloseBracketToken,
        OpenBraceToken,
        CloseBraceToken,
        SemiToken,
        CommaToken,
        LessThanToken,
        LessThanEqualsToken,
        GreaterThanToken,
        GreaterThanEqualsToken,
        LessThanLessThanToken,
        GreaterThanGreaterThanToken,
        PlusToken,
        PlusPlusToken,
        MinusToken,
        MinusMinusToken,
        AsteriskToken,
        SlashToken,
        PercentToken,
        AmpersandToken,
        BarToken,
        AmpersandAmpersandToken,
        BarBarToken,
        CaretToken,
        NotToken,
        TildeToken,
        QuestionToken,
        ColonToken,
        ColonColonToken,
        EqualsToken,
        AsteriskEqualsToken,
        SlashEqualsToken,
        PercentEqualsToken,
        PlusEqualsToken,
        MinusEqualsToken,
        LessThanLessThanEqualsToken,
        GreaterThanGreaterThanEqualsToken,
        AmpersandEqualsToken,
        CaretEqualsToken,
        BarEqualsToken,
        EqualsEqualsToken,
        ExclamationEqualsToken,
        DotToken,
        HashToken,
        HashHashToken,

        IdentifierToken,
        IntegerLiteralToken,
        FloatLiteralToken,
        CharacterLiteralToken,
        StringLiteralToken,

        DefineDirectiveKeyword,
        IncludeDirectiveKeyword,
        LineDirectiveKeyword,
        UndefDirectiveKeyword,
        ErrorDirectiveKeyword,
        PragmaDirectiveKeyword,
        IfDirectiveKeyword,
        IfdefDirectiveKeyword,
        IfndefDirectiveKeyword,
        ElifDirectiveKeyword,
        ElseDirectiveKeyword,
        EndifDirectiveKeyword,
        SystemIncludeLiteralToken,
        EndDirectiveToken,
        OpenFunctionLikeMacroParenToken,

        EndOfFileToken,
    }

    [PrettyEnum(PrettyEnumStyle.AllLowerCase)]
    public enum ScalarType
    {
        Void,
        Bool,
        Int,
        Uint,
        Half,
        Float,
        Double,
        Min16Float,
        Min10Float,
        Min16Int,
        Min12Int,
        Min16Uint,
        Min12Uint,
        String,
        Char,
        [PrettyName("unorm float")] UNormFloat,
        [PrettyName("snorm float")] SNormFloat,
    }

    [PrettyEnum(PrettyEnumStyle.AllLowerCase)]
    public enum LiteralKind
    {
        String,
        Float,
        [PrettyName("int")] Integer,
        [PrettyName("char")] Character,
        [PrettyName("bool")] Boolean,
        [PrettyName("NULL")] Null,
    }

    [PrettyEnum(PrettyEnumStyle.AllLowerCase)]
    public enum RegisterKind
    {
        [PrettyName("t")] Texture,
        [PrettyName("s")] Sampler,
        [PrettyName("u")] UAV,
        [PrettyName("b")] Buffer,
    }

    [PrettyEnum(PrettyEnumStyle.PascalCase)]
    public enum PredefinedObjectType
    {
        [PrettyName("texture")] Texture,
        Texture1D,
        Texture1DArray,
        Texture2D,
        Texture2DArray,
        Texture3D,
        TextureCube,
        TextureCubeArray,
        Texture2DMS,
        Texture2DMSArray,
        RWTexture1D,
        RWTexture1DArray,
        RWTexture2D,
        RWTexture2DArray,
        RWTexture3D,
        AppendStructuredBuffer,
        Buffer,
        ByteAddressBuffer,
        ConsumeStructuredBuffer,
        StructuredBuffer,
        ConstantBuffer,
        RasterizerOrderedBuffer,
        RasterizerOrderedByteAddressBuffer,
        RasterizerOrderedStructuredBuffer,
        RasterizerOrderedTexture1D,
        RasterizerOrderedTexture1DArray,
        RasterizerOrderedTexture2D,
        RasterizerOrderedTexture2DArray,
        RasterizerOrderedTexture3D,
        RWBuffer,
        RWByteAddressBuffer,
        RWStructuredBuffer,
        InputPatch,
        OutputPatch,
        PointStream,
        LineStream,
        TriangleStream,
        BlendState,
        DepthStencilState,
        RasterizerState,
        [PrettyName("sampler")] Sampler,
        [PrettyName("sampler1D")] Sampler1D,
        [PrettyName("sampler2D")] Sampler2D,
        [PrettyName("sampler3D")] Sampler3D,
        [PrettyName("samplerCUBE")] SamplerCube,
        SamplerState,
        SamplerComparisonState,
        BuiltInTriangleIntersectionAttributes,
        RayDesc,
        RaytracingAccelerationStructure
    }

    [PrettyEnum(PrettyEnumStyle.AllLowerCase)]
    public enum BindingModifier
    {
        Const,
        [PrettyName("row_major")] RowMajor,
        [PrettyName("column_major")] ColumnMajor,
        Export,
        Extern,
        Inline,
        Precise,
        Shared,
        Globallycoherent,
        Groupshared,
        Static,
        Uniform,
        Volatile,
        SNorm,
        UNorm,
        Linear,
        Centroid,
        Nointerpolation,
        Noperspective,
        Sample,

        In,
        Out,
        Inout,
        Point,
        Triangle,
        TriangleAdj,
        Line,
        LineAdj,
    }

    [PrettyEnum(PrettyEnumStyle.PascalCase)]
    public enum StateKind
    {
        SamplerState,
        SamplerComparisonState,
        BlendState,
    }

    [PrettyEnum(PrettyEnumStyle.AllLowerCase)]
    public enum OperatorKind
    {
        [PrettyName("=")] Assignment,
        [PrettyName("+=")] PlusAssignment,
        [PrettyName("-=")] MinusAssignment,
        [PrettyName("*=")] MulAssignment,
        [PrettyName("/=")] DivAssignment,
        [PrettyName("%=")] ModAssignment,
        [PrettyName("<<=")] ShiftLeftAssignment,
        [PrettyName(">>=")] ShiftRightAssignment,
        [PrettyName("&=")] BitwiseAndAssignment,
        [PrettyName("^=")] BitwiseXorAssignment,
        [PrettyName("|=")] BitwiseOrAssignment,

        [PrettyName("||")] LogicalOr,
        [PrettyName("&&")] LogicalAnd,
        [PrettyName("|")] BitwiseOr,
        [PrettyName("&")] BitwiseAnd,
        [PrettyName("^")] BitwiseXor,

        [PrettyName(",")] Compound,
        [PrettyName("?")] Ternary,

        [PrettyName("==")] Equals,
        [PrettyName("!=")] NotEquals,
        [PrettyName("<")] LessThan,
        [PrettyName("<=")] LessThanOrEquals,
        [PrettyName(">")] GreaterThan,
        [PrettyName(">=")] GreaterThanOrEquals,

        [PrettyName("<<")] ShiftLeft,
        [PrettyName(">>")] ShiftRight,

        [PrettyName("+")] Plus,
        [PrettyName("-")] Minus,
        [PrettyName("*")] Mul,
        [PrettyName("/")] Div,
        [PrettyName("%")] Mod,

        [PrettyName("++")] Increment,
        [PrettyName("--")] Decrement,

        [PrettyName("!")] Not,
        [PrettyName("~")] BitFlip,
    }

    public enum OperatorFixity
    {
        Prefix,
        Postfix,
        Infix,
    }

    public enum OperatorPrecedence
    {                 // Associativity:
        Compound,     // left
        Assignment,   // right
        Ternary,      // right
        LogicalOr,    // left
        LogicalAnd,   // left
        BitwiseOr,    // left
        BitwiseXor,   // left
        BitwiseAnd,   // left
        Equality,     // left
        Comparison,   // left
        BitShift,     // left
        AddSub,       // left
        MulDivMod,    // left
        PrefixUnary,  // right
        PostFixUnary, // left
    }
    #endregion

    #region Syntax tree
    public abstract class HLSLSyntaxNode : SyntaxNode<HLSLSyntaxNode>
    {
        public abstract void Accept(HLSLSyntaxVisitor visitor);
        public abstract T Accept<T>(HLSLSyntaxVisitor<T> visitor);

        public override SourceSpan Span => span;

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private SourceSpan span;

        public override SourceSpan OriginalSpan => originalSpan;

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private SourceSpan originalSpan;

        public IList<HLSLToken> Tokens => tokens;

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private IList<HLSLToken> tokens;

        public string GetCodeInSourceText(string sourceText) => Span.GetCodeInSourceText(sourceText);
        public string GetPrettyPrintedCode()
        {
            HLSLPrinter printer = new HLSLPrinter();
            printer.Visit(this);
            return printer.Text;
        }

        public HLSLSyntaxNode(IList<HLSLToken> tokens)
        {
            if (tokens?.Count > 0)
            {
                var first = tokens[0];
                var last = tokens[tokens.Count - 1];
                this.span = SourceSpan.Between(first.Span, last.Span);
                this.originalSpan = SourceSpan.Between(first.OriginalSpan, last.OriginalSpan);
            }
            this.tokens = tokens;
        }
    }

    public class IdentifierNode : HLSLSyntaxNode
    {
        public string Identifier { get; set; }

        public static implicit operator string(IdentifierNode node) => node.Identifier;
        public override string ToString() => Identifier;

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            Enumerable.Empty<HLSLSyntaxNode>();

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitIdentifierNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitIdentifierNode(this);

        public IdentifierNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public abstract class FunctionNode : HLSLSyntaxNode
    {
        public List<AttributeNode> Attributes { get; set; }
        public List<BindingModifier> Modifiers { get; set; }
        public TypeNode ReturnType { get; set; }
        public UserDefinedNamedTypeNode Name { get; set; }
        public List<FormalParameterNode> Parameters { get; set; }
        public SemanticNode Semantic { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            MergeChildren(Attributes, Child(ReturnType), Child(Name), Parameters, OptionalChild(Semantic));

        public FunctionNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class FormalParameterNode : HLSLSyntaxNode
    {
        public List<AttributeNode> Attributes { get; set; }
        public List<BindingModifier> Modifiers { get; set; }
        public TypeNode ParamType { get; set; }
        public VariableDeclaratorNode Declarator { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            MergeChildren(Attributes, Child(ParamType), Child(Declarator));

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitFormalParameterNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitFormalParameterNode(this);

        public FormalParameterNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class VariableDeclaratorNode : HLSLSyntaxNode
    {
        public IdentifierNode Name { get; set; }
        public List<ArrayRankNode> ArrayRanks { get; set; }
        public List<VariableDeclaratorQualifierNode> Qualifiers { get; set; }
        public List<VariableDeclarationStatementNode> Annotations { get; set; }
        public InitializerNode Initializer { get; set; } // Optional

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            MergeChildren(Child(Name), ArrayRanks, Qualifiers, Annotations, OptionalChild(Initializer));

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitVariableDeclaratorNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitVariableDeclaratorNode(this);

        public VariableDeclaratorNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class ArrayRankNode : HLSLSyntaxNode
    {
        public ExpressionNode Dimension { get; set; } // Optional

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            OptionalChild(Dimension);

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitArrayRankNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitArrayRankNode(this);

        public ArrayRankNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public abstract class InitializerNode : HLSLSyntaxNode
    {
        public InitializerNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class ValueInitializerNode : InitializerNode
    {
        public ExpressionNode Expression { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            Child(Expression);

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitValueInitializerNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitValueInitializerNode(this);

        public ValueInitializerNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    // BlendState, SamplerState, etc.
    public class StateInitializerNode : InitializerNode
    {
        public List<StatePropertyNode> States { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            States;

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitStateInitializerNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitStateInitializerNode(this);

        public StateInitializerNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class StateArrayInitializerNode : InitializerNode
    {
        public List<StateInitializerNode> Initializers { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            Initializers;

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitStateArrayInitializerNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitStateArrayInitializerNode(this);

        public StateArrayInitializerNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class FunctionDeclarationNode : FunctionNode
    {
        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitFunctionDeclarationNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitFunctionDeclarationNode(this);

        public FunctionDeclarationNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class FunctionDefinitionNode : FunctionNode
    {
        public BlockNode Body { get; set; }

        public bool BodyIsSingleStatement => !(Body is BlockNode);

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            MergeChildren(base.GetChildren, Child(Body));

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitFunctionDefinitionNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitFunctionDefinitionNode(this);

        public FunctionDefinitionNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class StructDefinitionNode : StatementNode
    {
        public StructTypeNode StructType { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            MergeChildren(base.GetChildren, Child(StructType));

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitStructDefinitionNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitStructDefinitionNode(this);

        public StructDefinitionNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class InterfaceDefinitionNode : StatementNode
    {
        public UserDefinedNamedTypeNode Name { get; set; }
        public List<FunctionDeclarationNode> Functions { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            MergeChildren(base.GetChildren, Child(Name), Functions);

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitInterfaceDefinitionNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitInterfaceDefinitionNode(this);

        public InterfaceDefinitionNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class ConstantBufferNode : HLSLSyntaxNode
    {
        public UserDefinedNamedTypeNode Name { get; set; }
        public RegisterLocationNode RegisterLocation { get; set; } // Optional
        public List<VariableDeclarationStatementNode> Declarations { get; set; }
        public bool IsTextureBuffer { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            MergeChildren(Child(Name), OptionalChild(RegisterLocation), Declarations);

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitConstantBufferNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitConstantBufferNode(this);

        public ConstantBufferNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class NamespaceNode : HLSLSyntaxNode
    {
        public UserDefinedNamedTypeNode Name { get; set; }
        public List<HLSLSyntaxNode> Declarations { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            MergeChildren(Child(Name), Declarations);

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitNamespaceNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitNamespaceNode(this);

        public NamespaceNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class TypedefNode : StatementNode
    {
        public TypeNode FromType { get; set; }
        public List<UserDefinedNamedTypeNode> ToNames { get; set; }
        public bool IsConst { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            MergeChildren(base.GetChildren, Child(FromType), ToNames);

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitTypedefNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitTypedefNode(this);

        public TypedefNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public abstract class VariableDeclaratorQualifierNode : HLSLSyntaxNode
    {
        public VariableDeclaratorQualifierNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class SemanticNode : VariableDeclaratorQualifierNode
    {
        public IdentifierNode Name { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            Child(Name);

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitSemanticNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitSemanticNode(this);

        public SemanticNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class RegisterLocationNode : VariableDeclaratorQualifierNode
    {
        public RegisterKind Kind { get; set; }
        public int Location { get; set; }
        public int? Space { get; set; } // Optional

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            Enumerable.Empty<HLSLSyntaxNode>();

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitRegisterLocationNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitRegisterLocationNode(this);

        public RegisterLocationNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class PackoffsetNode : VariableDeclaratorQualifierNode
    {
        public int Location { get; set; }
        public string Swizzle { get; set; } // Optional

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            Enumerable.Empty<HLSLSyntaxNode>();

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitPackoffsetNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitPackoffsetNode(this);

        public PackoffsetNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public abstract class StatementNode : HLSLSyntaxNode
    {
        public List<AttributeNode> Attributes { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            Attributes;

        public StatementNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class BlockNode : StatementNode
    {
        public List<StatementNode> Statements { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            MergeChildren(base.GetChildren, Statements);

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitBlockNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitBlockNode(this);

        public BlockNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class VariableDeclarationStatementNode : StatementNode
    {
        public List<BindingModifier> Modifiers { get; set; }
        public TypeNode Kind { get; set; }
        public List<VariableDeclaratorNode> Declarators { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            MergeChildren(base.GetChildren, Child(Kind), Declarators);

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitVariableDeclarationStatementNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitVariableDeclarationStatementNode(this);

        public VariableDeclarationStatementNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class ReturnStatementNode : StatementNode
    {
        public ExpressionNode Expression { get; set; } // Optional

        protected override IEnumerable<HLSLSyntaxNode> GetChildren => 
            MergeChildren(base.GetChildren, OptionalChild(Expression));

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitReturnStatementNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitReturnStatementNode(this);

        public ReturnStatementNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class BreakStatementNode : StatementNode
    {
        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitBreakStatementNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitBreakStatementNode(this);

        public BreakStatementNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class ContinueStatementNode : StatementNode
    {
        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitContinueStatementNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitContinueStatementNode(this);

        public ContinueStatementNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class DiscardStatementNode : StatementNode
    {
        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitDiscardStatementNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitDiscardStatementNode(this);

        public DiscardStatementNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class EmptyStatementNode : StatementNode
    {
        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitEmptyStatementNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitEmptyStatementNode(this);

        public EmptyStatementNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class ForStatementNode : StatementNode
    {
        public VariableDeclarationStatementNode Declaration { get; set; } // This is mutually exclusive with Initializer
        public ExpressionNode Initializer { get; set; }

        public ExpressionNode Condition { get; set; } // Optional
        public ExpressionNode Increment { get; set; } // Optional
        public StatementNode Body { get; set; }

        public bool FirstIsDeclaration => Declaration != null;
        public bool FirstIsExpression => Initializer != null;
        public bool BodyIsSingleStatement => !(Body is BlockNode);

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            MergeChildren(base.GetChildren, OptionalChild(Declaration), OptionalChild(Initializer), OptionalChild(Condition), OptionalChild(Increment), Child(Body));

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitForStatementNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitForStatementNode(this);

        public ForStatementNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class WhileStatementNode : StatementNode
    {
        public ExpressionNode Condition { get; set; }
        public StatementNode Body { get; set; }

        public bool BodyIsSingleStatement => !(Body is BlockNode);

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            MergeChildren(base.GetChildren, Child(Condition), Child(Body));

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitWhileStatementNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitWhileStatementNode(this);

        public WhileStatementNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class DoWhileStatementNode : StatementNode
    {
        public StatementNode Body { get; set; }
        public ExpressionNode Condition { get; set; }

        public bool BodyIsSingleStatement => !(Body is BlockNode);

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            MergeChildren(base.GetChildren, Child(Body), Child(Condition));

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitDoWhileStatementNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitDoWhileStatementNode(this);

        public DoWhileStatementNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class IfStatementNode : StatementNode
    {
        public ExpressionNode Condition { get; set; }
        public StatementNode Body { get; set; }
        public StatementNode ElseClause { get; set; } // Optional

        public bool BodyIsSingleStatement => !(Body is BlockNode);
        public bool BodyIsElseIfClause => Parent is IfStatementNode;
        public bool ElseClauseIsSingleStatement => !(ElseClause is BlockNode);
        public bool ElseClauseIsElseIfClause => ElseClause is IfStatementNode;

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            MergeChildren(base.GetChildren, Child(Condition), Child(Body), OptionalChild(ElseClause));

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitIfStatementNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitIfStatementNode(this);

        public IfStatementNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class SwitchStatementNode : StatementNode
    {
        public ExpressionNode Expression { get; set; }
        public List<SwitchClauseNode> Clauses { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            MergeChildren(base.GetChildren, Child(Expression), Clauses);

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitSwitchStatementNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitSwitchStatementNode(this);

        public SwitchStatementNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class SwitchClauseNode : HLSLSyntaxNode
    {
        public List<SwitchLabelNode> Labels { get; set; }
        public List<StatementNode> Statements { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            MergeChildren(Labels, Statements);

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitSwitchClauseNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitSwitchClauseNode(this);

        public SwitchClauseNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public abstract class SwitchLabelNode : HLSLSyntaxNode
    {
        public SwitchLabelNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class SwitchCaseLabelNode : SwitchLabelNode
    {
        public ExpressionNode Value { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            Child(Value);

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitSwitchCaseLabelNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitSwitchCaseLabelNode(this);

        public SwitchCaseLabelNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class SwitchDefaultLabelNode : SwitchLabelNode
    {
        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            Enumerable.Empty<HLSLSyntaxNode>();

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitSwitchDefaultLabelNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitSwitchDefaultLabelNode(this);

        public SwitchDefaultLabelNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class ExpressionStatementNode : StatementNode
    {
        public ExpressionNode Expression { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            MergeChildren(base.GetChildren, Child(Expression));

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitExpressionStatementNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitExpressionStatementNode(this);

        public ExpressionStatementNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class AttributeNode : HLSLSyntaxNode
    {
        public IdentifierNode Name { get; set; }
        public List<ExpressionNode> Arguments { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            MergeChildren(Child(Name), Arguments);

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitAttributeNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitAttributeNode(this);

        public AttributeNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public abstract class ExpressionNode : HLSLSyntaxNode
    {
        public ExpressionNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public abstract class NamedExpressionNode : ExpressionNode
    {
        public abstract string GetName();
        public abstract string GetUnqualifiedName();

        public NamedExpressionNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class QualifiedIdentifierExpressionNode : NamedExpressionNode
    {
        public IdentifierExpressionNode Left { get; set; }
        public NamedExpressionNode Right { get; set; }

        public override string GetName() => $"{Left.GetName()}::{Right.GetName()}";
        public override string GetUnqualifiedName() => Right.GetUnqualifiedName();

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            MergeChildren(Child(Left), Child(Right));

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitQualifiedIdentifierExpressionNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitQualifiedIdentifierExpressionNode(this);

        public QualifiedIdentifierExpressionNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class IdentifierExpressionNode : NamedExpressionNode
    {
        public IdentifierNode Name { get; set; }

        public override string GetName() => Name.Identifier;
        public override string GetUnqualifiedName() => Name.Identifier;

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            Child(Name);

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitIdentifierExpressionNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitIdentifierExpressionNode(this);

        public IdentifierExpressionNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class LiteralExpressionNode : ExpressionNode
    {
        public string Lexeme { get; set; }
        public LiteralKind Kind { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            Enumerable.Empty<HLSLSyntaxNode>();

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitLiteralExpressionNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitLiteralExpressionNode(this);

        public LiteralExpressionNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class AssignmentExpressionNode : ExpressionNode
    {
        public ExpressionNode Left { get; set; }
        public OperatorKind Operator { get; set; }
        public ExpressionNode Right { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            MergeChildren(Child(Left), Child(Right));

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitAssignmentExpressionNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitAssignmentExpressionNode(this);

        public AssignmentExpressionNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class BinaryExpressionNode : ExpressionNode
    {
        public ExpressionNode Left { get; set; }
        public OperatorKind Operator { get; set; }
        public ExpressionNode Right { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            MergeChildren(Child(Left), Child(Right));

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitBinaryExpressionNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitBinaryExpressionNode(this);

        public BinaryExpressionNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class CompoundExpressionNode : ExpressionNode
    {
        public ExpressionNode Left { get; set; }
        public ExpressionNode Right { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            MergeChildren(Child(Left), Child(Right));

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitCompoundExpressionNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitCompoundExpressionNode(this);

        public CompoundExpressionNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class PrefixUnaryExpressionNode : ExpressionNode
    {
        public OperatorKind Operator { get; set; }
        public ExpressionNode Expression { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            Child(Expression);

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitPrefixUnaryExpressionNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitPrefixUnaryExpressionNode(this);

        public PrefixUnaryExpressionNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class PostfixUnaryExpressionNode : ExpressionNode
    {
        public ExpressionNode Expression { get; set; }
        public OperatorKind Operator { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            Child(Expression);

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitPostfixUnaryExpressionNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitPostfixUnaryExpressionNode(this);

        public PostfixUnaryExpressionNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class FieldAccessExpressionNode : ExpressionNode
    {
        public ExpressionNode Target { get; set; }
        public IdentifierNode Name { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            new HLSLSyntaxNode[] { Target, Name };

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitFieldAccessExpressionNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitFieldAccessExpressionNode(this);

        public FieldAccessExpressionNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class MethodCallExpressionNode : ExpressionNode
    {
        public ExpressionNode Target { get; set; }
        public IdentifierNode Name { get; set; }
        public List<ExpressionNode> Arguments { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            MergeChildren(Child(Name), Child(Target), Arguments);

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitMethodCallExpressionNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitMethodCallExpressionNode(this);

        public MethodCallExpressionNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class FunctionCallExpressionNode : ExpressionNode
    {
        public NamedExpressionNode Name { get; set; }
        public List<ExpressionNode> Arguments { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            MergeChildren(Child(Name), Arguments);

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitFunctionCallExpressionNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitFunctionCallExpressionNode(this);

        public FunctionCallExpressionNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class NumericConstructorCallExpressionNode : ExpressionNode
    {
        public NumericTypeNode Kind { get; set; }
        public List<ExpressionNode> Arguments { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            MergeChildren(Child(Kind), Arguments);

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitNumericConstructorCallExpressionNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitNumericConstructorCallExpressionNode(this);

        public NumericConstructorCallExpressionNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class ElementAccessExpressionNode : ExpressionNode
    {
        public ExpressionNode Target { get; set; }
        public ExpressionNode Index { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            MergeChildren(Child(Target), Child(Index));

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitElementAccessExpressionNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitElementAccessExpressionNode(this);

        public ElementAccessExpressionNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class CastExpressionNode : ExpressionNode
    {
        public TypeNode Kind { get; set; }
        public ExpressionNode Expression { get; set; }
        public List<ArrayRankNode> ArrayRanks { get; set; }
        public bool IsFunctionLike { get; set; }
        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            MergeChildren(Child(Kind), Child(Expression), ArrayRanks);

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitCastExpressionNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitCastExpressionNode(this);

        public CastExpressionNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class ArrayInitializerExpressionNode : ExpressionNode
    {
        public List<ExpressionNode> Elements { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            Elements;

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitArrayInitializerExpressionNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitArrayInitializerExpressionNode(this);

        public ArrayInitializerExpressionNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class TernaryExpressionNode : ExpressionNode
    {
        public ExpressionNode Condition { get; set; }
        public ExpressionNode TrueCase { get; set; }
        public ExpressionNode FalseCase { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            MergeChildren(Child(Condition), Child(TrueCase), Child(FalseCase));

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitTernaryExpressionNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitTernaryExpressionNode(this);

        public TernaryExpressionNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    // Part of legacy sampler syntax (d3d9)
    public class SamplerStateLiteralExpressionNode : ExpressionNode
    {
        public List<StatePropertyNode> States { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            States;

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitSamplerStateLiteralExpressionNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitSamplerStateLiteralExpressionNode(this);

        public SamplerStateLiteralExpressionNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    // From FX framework
    public class CompileExpressionNode : ExpressionNode
    {
        public IdentifierNode Target { get; set; }
        public FunctionCallExpressionNode Invocation { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            new HLSLSyntaxNode[] { Target, Invocation };

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitCompileExpressionNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitCompileExpressionNode(this);

        public CompileExpressionNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public abstract class TypeNode : HLSLSyntaxNode
    {
        public TypeNode(IList<HLSLToken> tokens) : base(tokens) { }
    }
    public abstract class UserDefinedTypeNode : TypeNode
    {
        public UserDefinedTypeNode(IList<HLSLToken> tokens) : base(tokens) { }
    }
    public abstract class UserDefinedNamedTypeNode : UserDefinedTypeNode
    {
        public abstract string GetName();
        public abstract string GetUnqualifiedName();

        public UserDefinedNamedTypeNode(IList<HLSLToken> tokens) : base(tokens) { }
    }
    public abstract class PredefinedTypeNode : TypeNode
    {
        public PredefinedTypeNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class QualifiedNamedTypeNode : UserDefinedNamedTypeNode
    {
        public NamedTypeNode Left { get; set; }
        public UserDefinedNamedTypeNode Right { get; set; }

        public override string GetName() => $"{Left.GetName()}::{Right.GetName()}";
        public override string GetUnqualifiedName() => Right.GetUnqualifiedName();

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            MergeChildren(Child(Left), Child(Right));

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitQualifiedNamedTypeNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitQualifiedNamedTypeNode(this);

        public QualifiedNamedTypeNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class NamedTypeNode : UserDefinedNamedTypeNode
    {
        public IdentifierNode Name { get; set; }

        public override string GetName() => Name.Identifier;
        public override string GetUnqualifiedName() => Name.Identifier;

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            Child(Name);

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitNamedTypeNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitNamedTypeNode(this);

        public NamedTypeNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class PredefinedObjectTypeNode : PredefinedTypeNode
    {
        public PredefinedObjectType Kind { get; set; }
        public List<TypeNode> TemplateArguments { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            TemplateArguments;

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitPredefinedObjectTypeNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitPredefinedObjectTypeNode(this);

        public PredefinedObjectTypeNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class StructTypeNode : UserDefinedTypeNode
    {
        public UserDefinedNamedTypeNode Name { get; set; }
        public List<UserDefinedNamedTypeNode> Inherits { get; set; }
        public List<VariableDeclarationStatementNode> Fields { get; set; }
        public List<FunctionNode> Methods { get; set; }
        public List<StructDefinitionNode> NestedTypes { get; set; } = new List<StructDefinitionNode>();
        public List<EnumDefinitionNode> Enums { get; set; } = new List<EnumDefinitionNode>();
        public bool IsClass { get; set; }
        public bool IsForwardDeclaration { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            MergeChildren(OptionalChild(Name), Inherits, Fields, Methods, Enums, NestedTypes);

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitStructTypeNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitStructTypeNode(this);

        public StructTypeNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public abstract class NumericTypeNode : PredefinedTypeNode
    {
        public ScalarType Kind { get; set; }

        public NumericTypeNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class ScalarTypeNode : NumericTypeNode
    {
        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            Enumerable.Empty<HLSLSyntaxNode>();

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitScalarTypeNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitScalarTypeNode(this);

        public ScalarTypeNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public abstract class BaseMatrixTypeNode : NumericTypeNode
    {
        public BaseMatrixTypeNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public abstract class BaseVectorTypeNode : NumericTypeNode
    {
        public BaseVectorTypeNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class MatrixTypeNode : BaseMatrixTypeNode
    {
        public int FirstDimension { get; set; }
        public int SecondDimension { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            Enumerable.Empty<HLSLSyntaxNode>();

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitMatrixTypeNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitMatrixTypeNode(this);

        public MatrixTypeNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class GenericMatrixTypeNode : BaseMatrixTypeNode
    {
        public ExpressionNode FirstDimension { get; set; }
        public ExpressionNode SecondDimension { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            MergeChildren(Child(FirstDimension), Child(SecondDimension));

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitGenericMatrixTypeNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitGenericMatrixTypeNode(this);

        public GenericMatrixTypeNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class VectorTypeNode : BaseVectorTypeNode
    {
        public int Dimension { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            Enumerable.Empty<HLSLSyntaxNode>();

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitVectorTypeNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitVectorTypeNode(this);

        public VectorTypeNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class GenericVectorTypeNode : BaseVectorTypeNode
    {
        public ExpressionNode Dimension { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            Child(Dimension);

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitGenericVectorTypeNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitGenericVectorTypeNode(this);

        public GenericVectorTypeNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    // This type mostly exists such that template can receive literal arguments.
    // It's basically constexpr.
    public class LiteralTemplateArgumentType : TypeNode
    {
        public LiteralExpressionNode Literal { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            Child(Literal);

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitLiteralTemplateArgumentType(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitLiteralTemplateArgumentType(this);

        public LiteralTemplateArgumentType(IList<HLSLToken> tokens) : base(tokens) { }
    }

    // Part of an object literal (SamplerState, BlendState, etc)
    public class StatePropertyNode : StatementNode
    {
        public UserDefinedNamedTypeNode Name { get; set; }
        public ArrayRankNode ArrayRank { get; set; } // Optional
        public ExpressionNode Value { get; set; }
        public bool IsReference { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            MergeChildren(OptionalChild(ArrayRank), Child(Value));

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitStatePropertyNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitStatePropertyNode(this);

        public StatePropertyNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    // Old FX pipeline syntax
    public class TechniqueNode : HLSLSyntaxNode
    {
        public int Version { get; set; }
        public UserDefinedNamedTypeNode Name { get; set; } // Optional
        public List<VariableDeclarationStatementNode> Annotations { get; set; }
        public List<PassNode> Passes { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            MergeChildren(OptionalChild(Name), Annotations, Passes);

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitTechniqueNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitTechniqueNode(this);

        public TechniqueNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    // Old FX pipeline syntax
    public class PassNode : HLSLSyntaxNode
    {
        public UserDefinedNamedTypeNode Name { get; set; } // Optional
        public List<VariableDeclarationStatementNode> Annotations { get; set; }
        public List<StatementNode> Statements { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            MergeChildren(OptionalChild(Name), Annotations, Statements);

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitPassNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitPassNode(this);

        public PassNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public abstract class PreProcessorDirectiveNode : StatementNode
    {
        protected PreProcessorDirectiveNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class ObjectLikeMacroNode : PreProcessorDirectiveNode
    {
        public string Name { get; set; }
        public List<HLSLToken> Value { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            Enumerable.Empty<HLSLSyntaxNode>();

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitObjectLikeMacroNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitObjectLikeMacroNode(this);

        public ObjectLikeMacroNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class FunctionLikeMacroNode : PreProcessorDirectiveNode
    {
        public string Name { get; set; }
        public List<string> Arguments { get; set; }
        public List<HLSLToken> Value { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            Enumerable.Empty<HLSLSyntaxNode>();

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitFunctionLikeMacroNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitFunctionLikeMacroNode(this);

        public FunctionLikeMacroNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class IncludeDirectiveNode : PreProcessorDirectiveNode
    {
        public string Path { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            Enumerable.Empty<HLSLSyntaxNode>();

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitIncludeDirectiveNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitIncludeDirectiveNode(this);

        public IncludeDirectiveNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class LineDirectiveNode : PreProcessorDirectiveNode
    {
        public int Line { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            Enumerable.Empty<HLSLSyntaxNode>();

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitLineDirectiveNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitLineDirectiveNode(this);

        public LineDirectiveNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class UndefDirectiveNode : PreProcessorDirectiveNode
    {
        public string Name { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            Enumerable.Empty<HLSLSyntaxNode>();

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitUndefDirectiveNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitUndefDirectiveNode(this);

        public UndefDirectiveNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class ErrorDirectiveNode : PreProcessorDirectiveNode
    {
        public List<HLSLToken> Value { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            Enumerable.Empty<HLSLSyntaxNode>();

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitErrorDirectiveNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitErrorDirectiveNode(this);

        public ErrorDirectiveNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class PragmaDirectiveNode : PreProcessorDirectiveNode
    {
        public List<HLSLToken> Value { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            Enumerable.Empty<HLSLSyntaxNode>();

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitPragmaDirectiveNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitPragmaDirectiveNode(this);

        public PragmaDirectiveNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class IfDefDirectiveNode : PreProcessorDirectiveNode
    {
        public string Condition { get; set; }
        public List<HLSLSyntaxNode> Body { get; set; }
        public PreProcessorDirectiveNode ElseClause { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            MergeChildren(Body, OptionalChild(ElseClause));

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitIfDefDirectiveNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitIfDefDirectiveNode(this);

        public IfDefDirectiveNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class IfNotDefDirectiveNode : PreProcessorDirectiveNode
    {
        public string Condition { get; set; }
        public List<HLSLSyntaxNode> Body { get; set; }
        public PreProcessorDirectiveNode ElseClause { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            MergeChildren(Body, OptionalChild(ElseClause));

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitIfNotDefDirectiveNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitIfNotDefDirectiveNode(this);

        public IfNotDefDirectiveNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    public class IfDirectiveNode : PreProcessorDirectiveNode
    {
        public ExpressionNode Condition { get; set; }
        public List<HLSLSyntaxNode> Body { get; set; }
        public PreProcessorDirectiveNode ElseClause { get; set; }

        public bool IsElif
        {
            get
            {
                switch (Parent)
                {
                    case IfDefDirectiveNode p: return p.ElseClause == this;
                    case IfNotDefDirectiveNode p: return p.ElseClause == this;
                    case IfDirectiveNode p: return p.ElseClause == this;
                    default: return false;
                }
            }
        }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            MergeChildren(Child(Condition), Body, OptionalChild(ElseClause));

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitIfDirectiveNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitIfDirectiveNode(this);

        public IfDirectiveNode(IList<HLSLToken> tokens) : base(tokens) { }
    }

    // The remainder of an if-directive
    public class ElseDirectiveNode : PreProcessorDirectiveNode
    {
        public List<HLSLSyntaxNode> Body { get; set; }

        protected override IEnumerable<HLSLSyntaxNode> GetChildren =>
            Body;

        [DebuggerStepThrough]
        public override void Accept(HLSLSyntaxVisitor visitor) => visitor.VisitElseDirectiveNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(HLSLSyntaxVisitor<T> visitor) => visitor.VisitElseDirectiveNode(this);

        public ElseDirectiveNode(IList<HLSLToken> tokens) : base(tokens) { }
    }
    #endregion
}
