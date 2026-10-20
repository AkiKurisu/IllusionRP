using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Illusion.ShaderTools.Syntax.Common;
using Illusion.ShaderTools.Syntax.HLSL;

namespace Illusion.ShaderTools.Syntax.ShaderLab
{
    using SLToken = Token<TokenKind>;

    #region Tokens
    public enum TokenKind
    {
        InvalidToken,

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

        IdentifierToken,
        IntegerLiteralToken,
        FloatLiteralToken,
        StringLiteralToken,
        BracketedStringLiteralToken,

        ShaderKeyword,
        PropertiesKeyword,
        RangeKeyword,
        FloatKeyword,
        IntKeyword,
        IntegerKeyword,
        ColorKeyword,
        VectorKeyword,
        _2DKeyword,
        _3DKeyword,
        CubeKeyword,
        _2DArrayKeyword,
        _3DArrayKeyword,
        CubeArrayKeyword,
        AnyKeyword,
        RectKeyword,
        CategoryKeyword,
        SubShaderKeyword,
        TagsKeyword,
        PassKeyword,
        CgProgramKeyword,
        CgIncludeKeyword,
        EndCgKeyword,
        HlslProgramKeyword,
        HlslIncludeKeyword,
        EndHlslKeyword,
        GlslProgramKeyword,
        GlslIncludeKeyword,
        EndGlslKeyword,
        FallbackKeyword,
        CustomEditorKeyword,
        CullKeyword,
        ZWriteKeyword,
        ZTestKeyword,
        OffsetKeyword,
        BlendKeyword,
        BlendOpKeyword,
        ColorMaskKeyword,
        AlphaToMaskKeyword,
        ZClipKeyword,
        ConservativeKeyword,
        LodKeyword,
        NameKeyword,
        LightingKeyword,
        StencilKeyword,
        RefKeyword,
        ReadMaskKeyword,
        WriteMaskKeyword,
        CompKeyword,
        CompBackKeyword,
        CompFrontKeyword,
        FailKeyword,
        ZFailKeyword,
        FailBackKeyword,
        FailFrontKeyword,
        ZFailBackKeyword,
        ZFailFrontKeyword,
        PassFrontKeyword,
        PassBackKeyword,
        UsePassKeyword,
        GrabPassKeyword,
        DependencyKeyword,
        MaterialKeyword,
        DiffuseKeyword,
        AmbientKeyword,
        ShininessKeyword,
        SpecularKeyword,
        EmissionKeyword,
        AmbientAndDiffuseKeyword,
        FogKeyword,
        ModeKeyword,
        DensityKeyword,
        SeparateSpecularKeyword,
        SetTextureKeyword,
        CombineKeyword,
        AlphaKeyword,
        LerpKeyword,
        DoubleKeyword,
        QuadKeyword,
        ConstantColorKeyword,
        MatrixKeyword,
        AlphaTestKeyword,
        ColorMaterialKeyword,
        BindChannelsKeyword,
        BindKeyword,
        PackageRequirementsKeyword,

        TrueKeyword,
        FalseKeyword,
        OffKeyword,
        OnKeyword,
        FrontKeyword,
        BackKeyword,
        OneKeyword,
        ZeroKeyword,
        SrcColorKeyword,
        SrcAlphaKeyword,
        SrcAlphaSaturateKeyword,
        DstColorKeyword,
        DstAlphaKeyword,
        OneMinusSrcColorKeyword,
        OneMinusSrcAlphaKeyword,
        OneMinusDstColorKeyword,
        OneMinusDstAlphaKeyword,
        GlobalKeyword,
        AddKeyword,
        SubKeyword,
        RevSubKeyword,
        MinKeyword,
        MaxKeyword,
        LogicalClearKeyword,
        LogicalSetKeyword,
        LogicalCopyKeyword,
        LogicalCopyInvertedKeyword,
        LogicalNoopKeyword,
        LogicalInvertKeyword,
        LogicalAndKeyword,
        LogicalNandKeyword,
        LogicalOrKeyword,
        LogicalNorKeyword,
        LogicalXorKeyword,
        LogicalEquivKeyword,
        LogicalAndReverseKeyword,
        LogicalOrReverseKeyword,
        LogicalOrInvertedKeyword,
        MultiplyKeyword,
        ScreenKeyword,
        OverlayKeyword,
        DarkenKeyword,
        LightenKeyword,
        ColorDodgeKeyword,
        ColorBurnKeyword,
        HardLightKeyword,
        SoftLightKeyword,
        DifferenceKeyword,
        ExclusionKeyword,
        HSLHueKeyword,
        HSLSaturationKeyword,
        HSLColorKeyword,
        HSLLuminosityKeyword,

        HlslIncludeBlock,
        HlslProgramBlock,
        CgIncludeBlock,
        CgProgramBlock,
        GlslIncludeBlock,
        GlslProgramBlock,

        EndOfFileToken,
    }
    #endregion

    #region Common types
    // Either a reference to a property or some other type
    public struct PropertyReferenceOr<TOther>
    {
        public TOther Value;
        public string Property;

        public bool IsValue => Value != null;
        public bool IsPropertyReference => Property != null;
        public bool IsValid => IsValue || IsPropertyReference;

        public override string ToString()
        {
            if (Property != null) return Property;
            else if (Value != null) return Value.ToString();
            else return string.Empty;
        }
    }

    [PrettyEnum(PrettyEnumStyle.PascalCase)]
    public enum ShaderPropertyKind
    {
        [PrettyName("Any")] None,
        [PrettyName("2D")] Texture2D,
        [PrettyName("3D")] Texture3D,
        [PrettyName("Cube")] TextureCube,
        [PrettyName("Any")] TextureAny,
        [PrettyName("2DArray")] Texture2DArray,
        [PrettyName("3DArray")] Texture3DArray,
        [PrettyName("CubeArray")] TextureCubeArray,
        Float,
        Int,
        Integer,
        Color,
        Vector,
        Range,
    }

    [PrettyEnum(PrettyEnumStyle.PascalCase)]
    public enum TextureType
    {
        [PrettyName("2D")] Texture2D,
        [PrettyName("3D")] Texture3D,
        [PrettyName("Cube")] TextureCube,
        [PrettyName("Any")] TextureAny,
        [PrettyName("2DArray")] Texture2DArray,
        [PrettyName("3DArray")] Texture3DArray,
        [PrettyName("CubeArray")] TextureCubeArray,
    }

    [PrettyEnum(PrettyEnumStyle.PascalCase)]
    public enum CullMode
    {
        Off,
        Front,
        Back,
    }

    [PrettyEnum(PrettyEnumStyle.PascalCase)]
    public enum ComparisonMode
    {
        Off,
        Never,
        Less,
        Equal,
        LEqual,
        Greater,
        NotEqual,
        GEqual,
        Always,
    }

    [PrettyEnum(PrettyEnumStyle.PascalCase)]
    public enum BlendFactor
    {
        One,
        Zero,
        SrcColor,
        SrcAlpha,
        SrcAlphaSaturate,
        DstColor,
        DstAlpha,
        OneMinusSrcColor,
        OneMinusSrcAlpha,
        OneMinusDstColor,
        OneMinusDstAlpha,
    }

    [PrettyEnum(PrettyEnumStyle.PascalCase)]
    public enum BindChannel
    {
        Vertex,
        Normal,
        Tangent,
        TexCoord0,
        TexCoord1,
        TexCoord2,
        TexCoord3,
        TexCoord4,
        TexCoord5,
        TexCoord6,
        TexCoord7,
        Color,
    }

    [PrettyEnum(PrettyEnumStyle.PascalCase)]
    public enum BlendOp
    {
        Add,
        Sub,
        RevSub,
        Min,
        Max,
        LogicalClear,
        LogicalSet,
        LogicalCopy,
        LogicalCopyInverted,
        LogicalNoop,
        LogicalInvert,
        LogicalAnd,
        LogicalNand,
        LogicalOr,
        LogicalNor,
        LogicalXor,
        LogicalEquiv,
        LogicalAndReverse,
        LogicalOrReverse,
        LogicalOrInverted,
        Multiply,
        Screen,
        Overlay,
        Darken,
        Lighten,
        ColorDodge,
        ColorBurn,
        HardLight,
        SoftLight,
        Difference,
        Exclusion,
        HSLHue,
        HSLSaturation,
        HSLColor,
        HSLLuminosity,
    }

    [PrettyEnum(PrettyEnumStyle.PascalCase)]
    public enum FixedFunctionMaterialProperty
    {
        Diffuse,
        Ambient,
        Shininess,
        Specular,
        Emission,
    }

    [PrettyEnum(PrettyEnumStyle.PascalCase)]
    public enum StencilOp
    {
        Keep,
        Zero,
        Replace,
        IncrSat,
        DecrSat,
        Invert,
        IncrWrap,
        DecrWrap,
    }
    #endregion

    #region Syntax Tree
    // Embedded HLSL
    public enum ProgramKind
    {
        Cg,   // CGINCLUDE, CGPROGRAM
        Hlsl, // HLSLINCLUDE, HLSLPROGRAM
        Glsl, // GLSLINCLUDE, GLSLPROGRAM
    }

    public struct HLSLProgramBlock
    {
        public string FullCode;
        public string CodeWithoutIncludes;
        public SourceSpan Span;
        public List<string> Pragmas;
        public List<HLSLSyntaxNode> TopLevelDeclarations;
        public ProgramKind Kind;
    }

    public struct HLSLIncludeBlock
    {
        public string Code;
        public SourceSpan Span;
        public ProgramKind Kind;
    }

    public abstract class ShaderLabSyntaxNode : SyntaxNode<ShaderLabSyntaxNode>
    {
        public abstract void Accept(ShaderLabSyntaxVisitor visitor);
        public abstract T Accept<T>(ShaderLabSyntaxVisitor<T> visitor);

        public override SourceSpan Span => span;

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private SourceSpan span;

        public override SourceSpan OriginalSpan => originalSpan;

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private SourceSpan originalSpan;

        public List<SLToken> Tokens => tokens;

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private List<SLToken> tokens;

        public string GetCodeInSourceText(string sourceText) => Span.GetCodeInSourceText(sourceText);
        public string GetPrettyPrintedCode(bool prettyPrintEmbeddedHLSL = false)
        {
            ShaderLabPrinter printer = new ShaderLabPrinter();
            printer.PrettyPrintEmbeddedHLSL = prettyPrintEmbeddedHLSL;
            printer.Visit(this);
            return printer.Text;
        }

        public ShaderLabSyntaxNode(List<SLToken> tokens)
        {
            if (tokens.Count > 0)
            {
                this.span = SourceSpan.Between(tokens.First().Span, tokens.Last().Span);
                this.originalSpan = SourceSpan.Between(tokens.First().OriginalSpan, tokens.Last().OriginalSpan);
            }
            this.tokens = tokens;
        }
    }

    public class ShaderNode : ShaderLabSyntaxNode
    {
        public string Name { get; set; }
        public List<ShaderPropertyNode> Properties { get; set; }
        public List<SubShaderNode> SubShaders { get; set; }
        public string Fallback { get; set; } // Optional
        public bool FallbackDisabledExplicitly { get; set; }
        public string CustomEditor { get; set; } // Optional
        public Dictionary<string, string> Dependencies { get; set; }
        public List<HLSLIncludeBlock> IncludeBlocks { get; set; }

        protected override IEnumerable<ShaderLabSyntaxNode> GetChildren => MergeChildren(Properties, SubShaders);
        [DebuggerStepThrough]
        public override void Accept(ShaderLabSyntaxVisitor visitor) => visitor.VisitShaderNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(ShaderLabSyntaxVisitor<T> visitor) => visitor.VisitShaderNode(this);

        public ShaderNode(List<SLToken> tokens) : base(tokens) { }
    }

    public class ShaderPropertyNode : ShaderLabSyntaxNode
    {
        public List<string> Attributes { get; set; }
        public string Uniform { get; set; }
        public string Name { get; set; }
        public ShaderPropertyKind Kind = ShaderPropertyKind.None;
        public (float Min, float Max)? RangeMinMax { get; set; } // Optional
        public ShaderPropertyValueNode Value { get; set; }

        protected override IEnumerable<ShaderLabSyntaxNode> GetChildren => new[] { Value };
        [DebuggerStepThrough]
        public override void Accept(ShaderLabSyntaxVisitor visitor) => visitor.VisitShaderPropertyNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(ShaderLabSyntaxVisitor<T> visitor) => visitor.VisitShaderPropertyNode(this);

        public ShaderPropertyNode(List<SLToken> tokens) : base(tokens) { }
    }

    public abstract class ShaderPropertyValueNode : ShaderLabSyntaxNode
    {
        protected override IEnumerable<ShaderLabSyntaxNode> GetChildren => Enumerable.Empty<ShaderLabSyntaxNode>();
        public ShaderPropertyValueNode(List<SLToken> tokens) : base(tokens) { }
    }

    public class ShaderPropertyValueFloatNode : ShaderPropertyValueNode
    {
        public float Number { get; set; } = 0;

        [DebuggerStepThrough]
        public override void Accept(ShaderLabSyntaxVisitor visitor) => visitor.VisitShaderPropertyValueFloatNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(ShaderLabSyntaxVisitor<T> visitor) => visitor.VisitShaderPropertyValueFloatNode(this);

        public ShaderPropertyValueFloatNode(List<SLToken> tokens) : base(tokens) { }
    }

    public class ShaderPropertyValueIntegerNode : ShaderPropertyValueNode
    {
        public int Number { get; set; } = 0;

        [DebuggerStepThrough]
        public override void Accept(ShaderLabSyntaxVisitor visitor) => visitor.VisitShaderPropertyValueIntegerNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(ShaderLabSyntaxVisitor<T> visitor) => visitor.VisitShaderPropertyValueIntegerNode(this);

        public ShaderPropertyValueIntegerNode(List<SLToken> tokens) : base(tokens) { }
    }

    public class ShaderPropertyValueVectorNode : ShaderPropertyValueNode
    {
        public bool HasWChannel { get; set; }
        public (float x, float y, float z, float w) Vector { get; set; } = default;

        [DebuggerStepThrough]
        public override void Accept(ShaderLabSyntaxVisitor visitor) => visitor.VisitShaderPropertyValueVectorNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(ShaderLabSyntaxVisitor<T> visitor) => visitor.VisitShaderPropertyValueVectorNode(this);

        public ShaderPropertyValueVectorNode(List<SLToken> tokens) : base(tokens) { }
    }

    public class ShaderPropertyValueColorNode : ShaderPropertyValueNode
    {
        public bool HasAlphaChannel { get; set; }
        public (float r, float g, float b, float a) Color { get; set; } = default;

        [DebuggerStepThrough]
        public override void Accept(ShaderLabSyntaxVisitor visitor) => visitor.VisitShaderPropertyValueColorNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(ShaderLabSyntaxVisitor<T> visitor) => visitor.VisitShaderPropertyValueColorNode(this);

        public ShaderPropertyValueColorNode(List<SLToken> tokens) : base(tokens) { }
    }

    public class ShaderPropertyValueTextureNode : ShaderPropertyValueNode
    {
        public TextureType Kind { get; set; }
        public string TextureName { get; set; }

        [DebuggerStepThrough]
        public override void Accept(ShaderLabSyntaxVisitor visitor) => visitor.VisitShaderPropertyValueTextureNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(ShaderLabSyntaxVisitor<T> visitor) => visitor.VisitShaderPropertyValueTextureNode(this);

        public ShaderPropertyValueTextureNode(List<SLToken> tokens) : base(tokens) { }
    }

    public class SubShaderNode : ShaderLabSyntaxNode
    {
        public List<ShaderLabCommandNode> Commands { get; set; }
        public List<ShaderPassNode> Passes { get; set; }
        public List<HLSLProgramBlock> ProgramBlocks { get; set; }
        public List<HLSLIncludeBlock> IncludeBlocks { get; set; }
        public HLSLProgramBlock? ProgramBlock => ProgramBlocks.Count > 0 ? (HLSLProgramBlock?)ProgramBlocks[0] : null;

        protected override IEnumerable<ShaderLabSyntaxNode> GetChildren => MergeChildren(Passes, Commands);
        [DebuggerStepThrough]
        public override void Accept(ShaderLabSyntaxVisitor visitor) => visitor.VisitSubShaderNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(ShaderLabSyntaxVisitor<T> visitor) => visitor.VisitSubShaderNode(this);

        public SubShaderNode(List<SLToken> tokens) : base(tokens) { }
    }

    public abstract class ShaderPassNode : ShaderLabSyntaxNode
    {
        protected override IEnumerable<ShaderLabSyntaxNode> GetChildren => Enumerable.Empty<ShaderLabSyntaxNode>();
        public ShaderPassNode(List<SLToken> tokens) : base(tokens) { }
    }

    public class ShaderCodePassNode : ShaderPassNode
    {
        public List<ShaderLabCommandNode> Commands { get; set; }
        public List<HLSLProgramBlock> ProgramBlocks { get; set; }
        public List<HLSLIncludeBlock> IncludeBlocks { get; set; }
        public HLSLProgramBlock? ProgramBlock => ProgramBlocks.Count > 0 ? (HLSLProgramBlock?)ProgramBlocks[0] : null;

        protected override IEnumerable<ShaderLabSyntaxNode> GetChildren => Commands;
        [DebuggerStepThrough]
        public override void Accept(ShaderLabSyntaxVisitor visitor) => visitor.VisitShaderCodePassNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(ShaderLabSyntaxVisitor<T> visitor) => visitor.VisitShaderCodePassNode(this);

        public ShaderCodePassNode(List<SLToken> tokens) : base(tokens) { }
    }

    public class ShaderGrabPassNode : ShaderPassNode
    {
        public string TextureName { get; set; } // Optional
        public List<ShaderLabCommandNode> Commands { get; set; }
        public List<HLSLIncludeBlock> IncludeBlocks { get; set; }

        public bool IsUnnamed => string.IsNullOrEmpty(TextureName);
        protected override IEnumerable<ShaderLabSyntaxNode> GetChildren => Commands;
        [DebuggerStepThrough]
        public override void Accept(ShaderLabSyntaxVisitor visitor) => visitor.VisitShaderGrabPassNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(ShaderLabSyntaxVisitor<T> visitor) => visitor.VisitShaderGrabPassNode(this);

        public ShaderGrabPassNode(List<SLToken> tokens) : base(tokens) { }
    }

    public class ShaderUsePassNode : ShaderPassNode
    {
        public string PassName { get; set; }

        [DebuggerStepThrough]
        public override void Accept(ShaderLabSyntaxVisitor visitor) => visitor.VisitShaderUsePassNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(ShaderLabSyntaxVisitor<T> visitor) => visitor.VisitShaderUsePassNode(this);

        public ShaderUsePassNode(List<SLToken> tokens) : base(tokens) { }
    }

    public abstract class ShaderLabCommandNode : ShaderLabSyntaxNode
    {
        protected override IEnumerable<ShaderLabSyntaxNode> GetChildren => Enumerable.Empty<ShaderLabSyntaxNode>();
        public ShaderLabCommandNode(List<SLToken> tokens) : base(tokens) { }
    }

    public class ShaderLabCommandTagsNode : ShaderLabCommandNode
    {
        public Dictionary<string, string> Tags { get; set; }

        [DebuggerStepThrough]
        public override void Accept(ShaderLabSyntaxVisitor visitor) => visitor.VisitShaderLabCommandTagsNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(ShaderLabSyntaxVisitor<T> visitor) => visitor.VisitShaderLabCommandTagsNode(this);

        public ShaderLabCommandTagsNode(List<SLToken> tokens) : base(tokens) { }
    }

    public class ShaderLabCommandLodNode : ShaderLabCommandNode
    {
        public int LodLevel { get; set; } = 0;

        [DebuggerStepThrough]
        public override void Accept(ShaderLabSyntaxVisitor visitor) => visitor.VisitShaderLabCommandLodNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(ShaderLabSyntaxVisitor<T> visitor) => visitor.VisitShaderLabCommandLodNode(this);

        public ShaderLabCommandLodNode(List<SLToken> tokens) : base(tokens) { }
    }

    public abstract class ShaderLabBasicToggleCommandNode : ShaderLabCommandNode
    {
        public PropertyReferenceOr<bool> Enabled { get; set; }
        public ShaderLabBasicToggleCommandNode(List<SLToken> tokens) : base(tokens) { }
    }

    public class ShaderLabCommandLightingNode : ShaderLabBasicToggleCommandNode
    {
        [DebuggerStepThrough]
        public override void Accept(ShaderLabSyntaxVisitor visitor) => visitor.VisitShaderLabCommandLightingNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(ShaderLabSyntaxVisitor<T> visitor) => visitor.VisitShaderLabCommandLightingNode(this);

        public ShaderLabCommandLightingNode(List<SLToken> tokens) : base(tokens) { }
    }

    public class ShaderLabCommandSeparateSpecularNode : ShaderLabBasicToggleCommandNode
    {
        [DebuggerStepThrough]
        public override void Accept(ShaderLabSyntaxVisitor visitor) => visitor.VisitShaderLabCommandSeparateSpecularNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(ShaderLabSyntaxVisitor<T> visitor) => visitor.VisitShaderLabCommandSeparateSpecularNode(this);

        public ShaderLabCommandSeparateSpecularNode(List<SLToken> tokens) : base(tokens) { }
    }

    public class ShaderLabCommandZWriteNode : ShaderLabBasicToggleCommandNode
    {
        [DebuggerStepThrough]
        public override void Accept(ShaderLabSyntaxVisitor visitor) => visitor.VisitShaderLabCommandZWriteNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(ShaderLabSyntaxVisitor<T> visitor) => visitor.VisitShaderLabCommandZWriteNode(this);

        public ShaderLabCommandZWriteNode(List<SLToken> tokens) : base(tokens) { }
    }

    public class ShaderLabCommandAlphaToMaskNode : ShaderLabBasicToggleCommandNode
    {
        [DebuggerStepThrough]
        public override void Accept(ShaderLabSyntaxVisitor visitor) => visitor.VisitShaderLabCommandAlphaToMaskNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(ShaderLabSyntaxVisitor<T> visitor) => visitor.VisitShaderLabCommandAlphaToMaskNode(this);

        public ShaderLabCommandAlphaToMaskNode(List<SLToken> tokens) : base(tokens) { }
    }

    public class ShaderLabCommandZClipNode : ShaderLabBasicToggleCommandNode
    {
        [DebuggerStepThrough]
        public override void Accept(ShaderLabSyntaxVisitor visitor) => visitor.VisitShaderLabCommandZClipNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(ShaderLabSyntaxVisitor<T> visitor) => visitor.VisitShaderLabCommandZClipNode(this);

        public ShaderLabCommandZClipNode(List<SLToken> tokens) : base(tokens) { }
    }

    public class ShaderLabCommandConservativeNode : ShaderLabBasicToggleCommandNode
    {
        [DebuggerStepThrough]
        public override void Accept(ShaderLabSyntaxVisitor visitor) => visitor.VisitShaderLabCommandConservativeNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(ShaderLabSyntaxVisitor<T> visitor) => visitor.VisitShaderLabCommandConservativeNode(this);

        public ShaderLabCommandConservativeNode(List<SLToken> tokens) : base(tokens) { }
    }

    public class ShaderLabCommandCullNode : ShaderLabCommandNode
    {
        public PropertyReferenceOr<CullMode> Mode { get; set; }

        [DebuggerStepThrough]
        public override void Accept(ShaderLabSyntaxVisitor visitor) => visitor.VisitShaderLabCommandCullNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(ShaderLabSyntaxVisitor<T> visitor) => visitor.VisitShaderLabCommandCullNode(this);

        public ShaderLabCommandCullNode(List<SLToken> tokens) : base(tokens) { }
    }

    public class ShaderLabCommandZTestNode : ShaderLabCommandNode
    {
        public PropertyReferenceOr<ComparisonMode> Mode { get; set; }

        [DebuggerStepThrough]
        public override void Accept(ShaderLabSyntaxVisitor visitor) => visitor.VisitShaderLabCommandZTestNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(ShaderLabSyntaxVisitor<T> visitor) => visitor.VisitShaderLabCommandZTestNode(this);

        public ShaderLabCommandZTestNode(List<SLToken> tokens) : base(tokens) { }
    }

    public class ShaderLabCommandBlendNode : ShaderLabCommandNode
    {
        public int RenderTarget { get; set; } = 0;
        public bool Enabled { get; set; }
        public PropertyReferenceOr<BlendFactor>? SourceFactorRGB { get; set; }
        public PropertyReferenceOr<BlendFactor>? DestinationFactorRGB { get; set; }
        public PropertyReferenceOr<BlendFactor>? SourceFactorAlpha { get; set; }
        public PropertyReferenceOr<BlendFactor>? DestinationFactorAlpha { get; set; }

        [DebuggerStepThrough]
        public override void Accept(ShaderLabSyntaxVisitor visitor) => visitor.VisitShaderLabCommandBlendNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(ShaderLabSyntaxVisitor<T> visitor) => visitor.VisitShaderLabCommandBlendNode(this);

        public ShaderLabCommandBlendNode(List<SLToken> tokens) : base(tokens) { }
    }

    public class ShaderLabCommandOffsetNode : ShaderLabCommandNode
    {
        public PropertyReferenceOr<float> Factor { get; set; }
        public PropertyReferenceOr<float> Units { get; set; }

        [DebuggerStepThrough]
        public override void Accept(ShaderLabSyntaxVisitor visitor) => visitor.VisitShaderLabCommandOffsetNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(ShaderLabSyntaxVisitor<T> visitor) => visitor.VisitShaderLabCommandOffsetNode(this);

        public ShaderLabCommandOffsetNode(List<SLToken> tokens) : base(tokens) { }
    }

    public class ShaderLabCommandColorMaskNode : ShaderLabCommandNode
    {
        public PropertyReferenceOr<string> Mask { get; set; }
        public int RenderTarget { get; set; } = 0;

        public bool IsZeroMask => Mask.Value == "0";

        [DebuggerStepThrough]
        public override void Accept(ShaderLabSyntaxVisitor visitor) => visitor.VisitShaderLabCommandColorMaskNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(ShaderLabSyntaxVisitor<T> visitor) => visitor.VisitShaderLabCommandColorMaskNode(this);

        public ShaderLabCommandColorMaskNode(List<SLToken> tokens) : base(tokens) { }
    }

    public class ShaderLabCommandAlphaTestNode : ShaderLabCommandNode
    {
        public PropertyReferenceOr<ComparisonMode> Mode { get; set; }
        public PropertyReferenceOr<float>? AlphaValue { get; set; } // Optional

        [DebuggerStepThrough]
        public override void Accept(ShaderLabSyntaxVisitor visitor) => visitor.VisitShaderLabCommandAlphaTestNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(ShaderLabSyntaxVisitor<T> visitor) => visitor.VisitShaderLabCommandAlphaTestNode(this);

        public ShaderLabCommandAlphaTestNode(List<SLToken> tokens) : base(tokens) { }
    }

    public class ShaderLabCommandFogNode : ShaderLabCommandNode
    {
        public bool Enabled { get; set; }
        public (float r, float g, float b, float a)? Color { get; set; } // Optional

        [DebuggerStepThrough]
        public override void Accept(ShaderLabSyntaxVisitor visitor) => visitor.VisitShaderLabCommandFogNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(ShaderLabSyntaxVisitor<T> visitor) => visitor.VisitShaderLabCommandFogNode(this);

        public ShaderLabCommandFogNode(List<SLToken> tokens) : base(tokens) { }
    }

    public class ShaderLabCommandNameNode : ShaderLabCommandNode
    {
        public string Name { get; set; }

        [DebuggerStepThrough]
        public override void Accept(ShaderLabSyntaxVisitor visitor) => visitor.VisitShaderLabCommandNameNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(ShaderLabSyntaxVisitor<T> visitor) => visitor.VisitShaderLabCommandNameNode(this);

        public ShaderLabCommandNameNode(List<SLToken> tokens) : base(tokens) { }
    }

    public class ShaderLabCommandBindChannelsNode : ShaderLabCommandNode
    {
        public Dictionary<BindChannel, BindChannel> Bindings { get; set; }

        [DebuggerStepThrough]
        public override void Accept(ShaderLabSyntaxVisitor visitor) => visitor.VisitShaderLabCommandBindChannelsNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(ShaderLabSyntaxVisitor<T> visitor) => visitor.VisitShaderLabCommandBindChannelsNode(this);

        public ShaderLabCommandBindChannelsNode(List<SLToken> tokens) : base(tokens) { }
    }

    public class ShaderLabCommandColorNode : ShaderLabCommandNode
    {
        public bool HasAlphaChannel { get; set; }
        public PropertyReferenceOr<(float r, float g, float b, float a)> Color { get; set; }

        [DebuggerStepThrough]
        public override void Accept(ShaderLabSyntaxVisitor visitor) => visitor.VisitShaderLabCommandColorNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(ShaderLabSyntaxVisitor<T> visitor) => visitor.VisitShaderLabCommandColorNode(this);

        public ShaderLabCommandColorNode(List<SLToken> tokens) : base(tokens) { }
    }

    public class ShaderLabCommandBlendOpNode : ShaderLabCommandNode
    {
        public PropertyReferenceOr<BlendOp> BlendOp { get; set; }
        public PropertyReferenceOr<BlendOp>? BlendOpAlpha { get; set; }

        [DebuggerStepThrough]
        public override void Accept(ShaderLabSyntaxVisitor visitor) => visitor.VisitShaderLabCommandBlendOpNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(ShaderLabSyntaxVisitor<T> visitor) => visitor.VisitShaderLabCommandBlendOpNode(this);

        public ShaderLabCommandBlendOpNode(List<SLToken> tokens) : base(tokens) { }
    }

    public class ShaderLabCommandMaterialNode : ShaderLabCommandNode
    {
        public Dictionary<FixedFunctionMaterialProperty, PropertyReferenceOr<(float r, float g, float b, float a)>> Properties { get; set; }

        [DebuggerStepThrough]
        public override void Accept(ShaderLabSyntaxVisitor visitor) => visitor.VisitShaderLabCommandMaterialNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(ShaderLabSyntaxVisitor<T> visitor) => visitor.VisitShaderLabCommandMaterialNode(this);

        public ShaderLabCommandMaterialNode(List<SLToken> tokens) : base(tokens) { }
    }

    public class ShaderLabCommandSetTextureNode : ShaderLabCommandNode
    {
        // TODO: Not the lazy way
        public string TextureName { get; set; }
        public List<SLToken> Body { get; set; }

        [DebuggerStepThrough]
        public override void Accept(ShaderLabSyntaxVisitor visitor) => visitor.VisitShaderLabCommandSetTextureNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(ShaderLabSyntaxVisitor<T> visitor) => visitor.VisitShaderLabCommandSetTextureNode(this);

        public ShaderLabCommandSetTextureNode(List<SLToken> tokens) : base(tokens) { }
    }

    public class ShaderLabCommandColorMaterialNode : ShaderLabCommandNode
    {
        public bool AmbientAndDiffuse { get; set; }
        public bool Emission => !AmbientAndDiffuse;

        [DebuggerStepThrough]
        public override void Accept(ShaderLabSyntaxVisitor visitor) => visitor.VisitShaderLabCommandColorMaterialNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(ShaderLabSyntaxVisitor<T> visitor) => visitor.VisitShaderLabCommandColorMaterialNode(this);

        public ShaderLabCommandColorMaterialNode(List<SLToken> tokens) : base(tokens) { }
    }

    public class ShaderLabCommandStencilNode : ShaderLabCommandNode
    {
        public PropertyReferenceOr<byte> Ref { get; set; }
        public PropertyReferenceOr<byte> ReadMask { get; set; }
        public PropertyReferenceOr<byte> WriteMask { get; set; }
        public PropertyReferenceOr<ComparisonMode> ComparisonOperationBack { get; set; }
        public PropertyReferenceOr<StencilOp> PassOperationBack { get; set; }
        public PropertyReferenceOr<StencilOp> FailOperationBack { get; set; }
        public PropertyReferenceOr<StencilOp> ZFailOperationBack { get; set; }
        public PropertyReferenceOr<ComparisonMode> ComparisonOperationFront { get; set; }
        public PropertyReferenceOr<StencilOp> PassOperationFront { get; set; }
        public PropertyReferenceOr<StencilOp> FailOperationFront { get; set; }
        public PropertyReferenceOr<StencilOp> ZFailOperationFront { get; set; }
        public PropertyReferenceOr<ComparisonMode> ComparisonOperation => ComparisonOperationFront;
        public PropertyReferenceOr<StencilOp> PassOperation => PassOperationFront;
        public PropertyReferenceOr<StencilOp> FailOperation => FailOperationFront;
        public PropertyReferenceOr<StencilOp> ZFailOperation => ZFailOperationFront;

        [DebuggerStepThrough]
        public override void Accept(ShaderLabSyntaxVisitor visitor) => visitor.VisitShaderLabCommandStencilNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(ShaderLabSyntaxVisitor<T> visitor) => visitor.VisitShaderLabCommandStencilNode(this);

        public ShaderLabCommandStencilNode(List<SLToken> tokens) : base(tokens) { }
    }

    public class ShaderLabCommandPackageRequirementsNode : ShaderLabCommandNode
    {
        // Key: Package name, Value: Package version (optional)
        public Dictionary<string, string> References { get; set; }

        [DebuggerStepThrough]
        public override void Accept(ShaderLabSyntaxVisitor visitor) => visitor.VisitShaderLabCommandPackageRequirementsNode(this);
        [DebuggerStepThrough]
        public override T Accept<T>(ShaderLabSyntaxVisitor<T> visitor) => visitor.VisitShaderLabCommandPackageRequirementsNode(this);

        public ShaderLabCommandPackageRequirementsNode(List<SLToken> tokens) : base(tokens) { }
    }
    #endregion
}
