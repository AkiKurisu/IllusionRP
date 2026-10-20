using System.Collections.Generic;
using System.Linq;

namespace Illusion.ShaderTools.Syntax.ShaderLab
{
    public static class ShaderLabSyntaxFacts
    {
        public static bool TryParseShaderLabKeyword(string keyword, out TokenKind token)
        {
            token = default;

            switch (keyword.ToLower())
            {
                case "shader": token = TokenKind.ShaderKeyword; return true;
                case "properties": token = TokenKind.PropertiesKeyword; return true;
                case "range": token = TokenKind.RangeKeyword; return true;
                case "float": token = TokenKind.FloatKeyword; return true;
                case "integer": token = TokenKind.IntegerKeyword; return true;
                case "int": token = TokenKind.IntKeyword; return true;
                case "color": token = TokenKind.ColorKeyword; return true;
                case "vector": token = TokenKind.VectorKeyword; return true;
                case "2d": token = TokenKind._2DKeyword; return true;
                case "3d": token = TokenKind._3DKeyword; return true;
                case "cube": token = TokenKind.CubeKeyword; return true;
                case "2darray": token = TokenKind._2DArrayKeyword; return true;
                case "3darray": token = TokenKind._3DArrayKeyword; return true;
                case "cubearray": token = TokenKind.CubeArrayKeyword; return true;
                case "any": token = TokenKind.AnyKeyword; return true;
                case "rect": token = TokenKind.RectKeyword; return true;
                case "category": token = TokenKind.CategoryKeyword; return true;
                case "subshader": token = TokenKind.SubShaderKeyword; return true;
                case "tags": token = TokenKind.TagsKeyword; return true;
                case "pass": token = TokenKind.PassKeyword; return true;
                case "cgprogram": token = TokenKind.CgProgramKeyword; return true;
                case "cginclude": token = TokenKind.CgIncludeKeyword; return true;
                case "endcg": token = TokenKind.EndCgKeyword; return true;
                case "hlslprogram": token = TokenKind.HlslProgramKeyword; return true;
                case "hlslinclude": token = TokenKind.HlslIncludeKeyword; return true;
                case "endhlsl": token = TokenKind.EndHlslKeyword; return true;
                case "glslprogram": token = TokenKind.GlslProgramKeyword; return true;
                case "glslinclude": token = TokenKind.GlslIncludeKeyword; return true;
                case "endglsl": token = TokenKind.EndGlslKeyword; return true;
                case "fallback": token = TokenKind.FallbackKeyword; return true;
                case "customeditor": token = TokenKind.CustomEditorKeyword; return true;
                case "cull": token = TokenKind.CullKeyword; return true;
                case "zwrite": token = TokenKind.ZWriteKeyword; return true;
                case "ztest": token = TokenKind.ZTestKeyword; return true;
                case "offset": token = TokenKind.OffsetKeyword; return true;
                case "blend": token = TokenKind.BlendKeyword; return true;
                case "blendop": token = TokenKind.BlendOpKeyword; return true;
                case "colormask": token = TokenKind.ColorMaskKeyword; return true;
                case "alphatomask": token = TokenKind.AlphaToMaskKeyword; return true;
                case "zclip": token = TokenKind.ZClipKeyword; return true;
                case "conservative": token = TokenKind.ConservativeKeyword; return true;
                case "lod": token = TokenKind.LodKeyword; return true;
                case "name": token = TokenKind.NameKeyword; return true;
                case "lighting": token = TokenKind.LightingKeyword; return true;
                case "stencil": token = TokenKind.StencilKeyword; return true;
                case "ref": token = TokenKind.RefKeyword; return true;
                case "readmask": token = TokenKind.ReadMaskKeyword; return true;
                case "writemask": token = TokenKind.WriteMaskKeyword; return true;
                case "comp": token = TokenKind.CompKeyword; return true;
                case "compback": token = TokenKind.CompBackKeyword; return true;
                case "compfront": token = TokenKind.CompFrontKeyword; return true;
                case "fail": token = TokenKind.FailKeyword; return true;
                case "zfail": token = TokenKind.ZFailKeyword; return true;
                case "failback": token = TokenKind.FailBackKeyword; return true;
                case "failfront": token = TokenKind.FailFrontKeyword; return true;
                case "zfailback": token = TokenKind.ZFailBackKeyword; return true;
                case "zfailfront": token = TokenKind.ZFailFrontKeyword; return true;
                case "passfront": token = TokenKind.PassFrontKeyword; return true;
                case "passback": token = TokenKind.PassBackKeyword; return true;
                case "usepass": token = TokenKind.UsePassKeyword; return true;
                case "grabpass": token = TokenKind.GrabPassKeyword; return true;
                case "dependency": token = TokenKind.DependencyKeyword; return true;
                case "material": token = TokenKind.MaterialKeyword; return true;
                case "diffuse": token = TokenKind.DiffuseKeyword; return true;
                case "ambient": token = TokenKind.AmbientKeyword; return true;
                case "shininess": token = TokenKind.ShininessKeyword; return true;
                case "specular": token = TokenKind.SpecularKeyword; return true;
                case "emission": token = TokenKind.EmissionKeyword; return true;
                case "ambientanddiffuse": token = TokenKind.AmbientAndDiffuseKeyword; return true;
                case "fog": token = TokenKind.FogKeyword; return true;
                case "mode": token = TokenKind.ModeKeyword; return true;
                case "density": token = TokenKind.DensityKeyword; return true;
                case "separatespecular": token = TokenKind.SeparateSpecularKeyword; return true;
                case "settexture": token = TokenKind.SetTextureKeyword; return true;
                case "combine": token = TokenKind.CombineKeyword; return true;
                case "alpha": token = TokenKind.AlphaKeyword; return true;
                case "lerp": token = TokenKind.LerpKeyword; return true;
                case "double": token = TokenKind.DoubleKeyword; return true;
                case "quad": token = TokenKind.QuadKeyword; return true;
                case "constantcolor": token = TokenKind.ConstantColorKeyword; return true;
                case "matrix": token = TokenKind.MatrixKeyword; return true;
                case "alphatest": token = TokenKind.AlphaTestKeyword; return true;
                case "colormaterial": token = TokenKind.ColorMaterialKeyword; return true;
                case "bindchannels": token = TokenKind.BindChannelsKeyword; return true;
                case "bind": token = TokenKind.BindKeyword; return true;
                case "packagerequirements": token = TokenKind.PackageRequirementsKeyword; return true;
                case "true": token = TokenKind.TrueKeyword; return true;
                case "false": token = TokenKind.FalseKeyword; return true;
                case "off": token = TokenKind.OffKeyword; return true;
                case "on": token = TokenKind.OnKeyword; return true;
                case "front": token = TokenKind.FrontKeyword; return true;
                case "back": token = TokenKind.BackKeyword; return true;
                case "one": token = TokenKind.OneKeyword; return true;
                case "zero": token = TokenKind.ZeroKeyword; return true;
                case "srccolor": token = TokenKind.SrcColorKeyword; return true;
                case "srcalpha": token = TokenKind.SrcAlphaKeyword; return true;
                case "srcalphasaturate": token = TokenKind.SrcAlphaSaturateKeyword; return true;
                case "dstcolor": token = TokenKind.DstColorKeyword; return true;
                case "dstalpha": token = TokenKind.DstAlphaKeyword; return true;
                case "oneminussrccolor": token = TokenKind.OneMinusSrcColorKeyword; return true;
                case "oneminussrcalpha": token = TokenKind.OneMinusSrcAlphaKeyword; return true;
                case "oneminusdstcolor": token = TokenKind.OneMinusDstColorKeyword; return true;
                case "oneminusdstalpha": token = TokenKind.OneMinusDstAlphaKeyword; return true;
                case "global": token = TokenKind.GlobalKeyword; return true;
                case "add": token = TokenKind.AddKeyword; return true;
                case "sub": token = TokenKind.SubKeyword; return true;
                case "revsub": token = TokenKind.RevSubKeyword; return true;
                case "min": token = TokenKind.MinKeyword; return true;
                case "max": token = TokenKind.MaxKeyword; return true;
                case "logicalclear": token = TokenKind.LogicalClearKeyword; return true;
                case "logicalset": token = TokenKind.LogicalSetKeyword; return true;
                case "logicalcopy": token = TokenKind.LogicalCopyKeyword; return true;
                case "logicalcopyinverted": token = TokenKind.LogicalCopyInvertedKeyword; return true;
                case "logicalnoop": token = TokenKind.LogicalNoopKeyword; return true;
                case "logicalinvert": token = TokenKind.LogicalInvertKeyword; return true;
                case "logicaland": token = TokenKind.LogicalAndKeyword; return true;
                case "logicalnand": token = TokenKind.LogicalNandKeyword; return true;
                case "logicalor": token = TokenKind.LogicalOrKeyword; return true;
                case "logicalnor": token = TokenKind.LogicalNorKeyword; return true;
                case "logicalxor": token = TokenKind.LogicalXorKeyword; return true;
                case "logicalequiv": token = TokenKind.LogicalEquivKeyword; return true;
                case "logicalandreverse": token = TokenKind.LogicalAndReverseKeyword; return true;
                case "logicalorreverse": token = TokenKind.LogicalOrReverseKeyword; return true;
                case "logicalorinverted": token = TokenKind.LogicalOrInvertedKeyword; return true;
                case "multiply": token = TokenKind.MultiplyKeyword; return true;
                case "screen": token = TokenKind.ScreenKeyword; return true;
                case "overlay": token = TokenKind.OverlayKeyword; return true;
                case "darken": token = TokenKind.DarkenKeyword; return true;
                case "lighten": token = TokenKind.LightenKeyword; return true;
                case "colordodge": token = TokenKind.ColorDodgeKeyword; return true;
                case "colorburn": token = TokenKind.ColorBurnKeyword; return true;
                case "hardlight": token = TokenKind.HardLightKeyword; return true;
                case "softlight": token = TokenKind.SoftLightKeyword; return true;
                case "difference": token = TokenKind.DifferenceKeyword; return true;
                case "exclusion": token = TokenKind.ExclusionKeyword; return true;
                case "hslhue": token = TokenKind.HSLHueKeyword; return true;
                case "hslsaturation": token = TokenKind.HSLSaturationKeyword; return true;
                case "hslcolor": token = TokenKind.HSLColorKeyword; return true;
                case "hslluminosity": token = TokenKind.HSLLuminosityKeyword; return true;
                default: return false;
            }
        }

        public static bool TryParseBindChannelName(string name, out BindChannel bindChannel)
        {
            bindChannel = default;

            switch (name.ToLower())
            {
                case "vertex": bindChannel = BindChannel.Vertex; return true;
                case "normal": bindChannel = BindChannel.Normal; return true;
                case "tangent": bindChannel = BindChannel.Tangent; return true;
                case "texcoord0": case "texcoord": bindChannel = BindChannel.TexCoord0; return true;
                case "texcoord1": bindChannel = BindChannel.TexCoord1; return true;
                case "texcoord2": bindChannel = BindChannel.TexCoord2; return true;
                case "texcoord3": bindChannel = BindChannel.TexCoord3; return true;
                case "texcoord4": bindChannel = BindChannel.TexCoord4; return true;
                case "texcoord5": bindChannel = BindChannel.TexCoord5; return true;
                case "texcoord6": bindChannel = BindChannel.TexCoord6; return true;
                case "texcoord7": bindChannel = BindChannel.TexCoord7; return true;
                case "color": bindChannel = BindChannel.Color; return true;
                default: return false;
            }
        }

        public static TextureType ShaderPropertyTypeToTextureType(ShaderPropertyKind kind)
        {
            switch (kind)
            {
                case ShaderPropertyKind.Texture2D: return TextureType.Texture2D; 
                case ShaderPropertyKind.Texture3D: return TextureType.Texture3D; 
                case ShaderPropertyKind.TextureCube: return TextureType.TextureCube; 
                case ShaderPropertyKind.TextureAny: return TextureType.TextureAny; 
                case ShaderPropertyKind.Texture2DArray: return TextureType.Texture2DArray; 
                case ShaderPropertyKind.Texture3DArray: return TextureType.Texture3DArray; 
                case ShaderPropertyKind.TextureCubeArray: return TextureType.TextureCubeArray;
                default: return default;
            }
        }

        public static bool TryConvertKeywordToString(TokenKind kind, out string result)
        {
            switch (kind)
            {
                case TokenKind.ShaderKeyword: result = "Shader"; return true;
                case TokenKind.PropertiesKeyword: result = "Properties"; return true;
                case TokenKind.RangeKeyword: result = "Range"; return true;
                case TokenKind.FloatKeyword: result = "Float"; return true;
                case TokenKind.IntegerKeyword: result = "Integer"; return true;
                case TokenKind.IntKeyword: result = "Int"; return true;
                case TokenKind.ColorKeyword: result = "Color"; return true;
                case TokenKind.VectorKeyword: result = "Vector"; return true;
                case TokenKind._2DKeyword: result = "2D"; return true;
                case TokenKind._3DKeyword: result = "3D"; return true;
                case TokenKind.CubeKeyword: result = "Cube"; return true;
                case TokenKind._2DArrayKeyword: result = "2DArray"; return true;
                case TokenKind._3DArrayKeyword: result = "3DArray"; return true;
                case TokenKind.CubeArrayKeyword: result = "CubeArray"; return true;
                case TokenKind.AnyKeyword: result = "Any"; return true;
                case TokenKind.RectKeyword: result = "Rect"; return true;
                case TokenKind.CategoryKeyword: result = "Category"; return true;
                case TokenKind.SubShaderKeyword: result = "SubShader"; return true;
                case TokenKind.TagsKeyword: result = "Tags"; return true;
                case TokenKind.PassKeyword: result = "Pass"; return true;
                case TokenKind.CgProgramKeyword: result = "CGPROGRAM"; return true;
                case TokenKind.CgIncludeKeyword: result = "CGINCLUDE"; return true;
                case TokenKind.EndCgKeyword: result = "ENDCG"; return true;
                case TokenKind.HlslProgramKeyword: result = "HLSLPROGRAM"; return true;
                case TokenKind.HlslIncludeKeyword: result = "HLSLINCLUDE"; return true;
                case TokenKind.EndHlslKeyword: result = "ENDHLSL"; return true;
                case TokenKind.GlslProgramKeyword: result = "GLSLPROGRAM"; return true;
                case TokenKind.GlslIncludeKeyword: result = "GLSLINCLUDE"; return true;
                case TokenKind.EndGlslKeyword: result = "ENDGLSL"; return true;
                case TokenKind.FallbackKeyword: result = "Fallback"; return true;
                case TokenKind.CustomEditorKeyword: result = "CustomEditor"; return true;
                case TokenKind.CullKeyword: result = "Cull"; return true;
                case TokenKind.ZWriteKeyword: result = "ZWrite"; return true;
                case TokenKind.ZTestKeyword: result = "ZTest"; return true;
                case TokenKind.OffsetKeyword: result = "Offset"; return true;
                case TokenKind.BlendKeyword: result = "Blend"; return true;
                case TokenKind.BlendOpKeyword: result = "BlendOp"; return true;
                case TokenKind.ColorMaskKeyword: result = "ColorMask"; return true;
                case TokenKind.AlphaToMaskKeyword: result = "AlphaToMask"; return true;
                case TokenKind.ZClipKeyword: result = "ZClip"; return true;
                case TokenKind.ConservativeKeyword: result = "Conservative"; return true;
                case TokenKind.LodKeyword: result = "LOD"; return true;
                case TokenKind.NameKeyword: result = "Name"; return true;
                case TokenKind.LightingKeyword: result = "Lighting"; return true;
                case TokenKind.StencilKeyword: result = "Stencil"; return true;
                case TokenKind.RefKeyword: result = "Ref"; return true;
                case TokenKind.ReadMaskKeyword: result = "ReadMask"; return true;
                case TokenKind.WriteMaskKeyword: result = "WriteMask"; return true;
                case TokenKind.CompKeyword: result = "Comp"; return true;
                case TokenKind.CompBackKeyword: result = "CompBack"; return true;
                case TokenKind.CompFrontKeyword: result = "CompFront"; return true;
                case TokenKind.FailKeyword: result = "Fail"; return true;
                case TokenKind.ZFailKeyword: result = "ZFail"; return true;
                case TokenKind.FailBackKeyword: result = "FailBack"; return true;
                case TokenKind.FailFrontKeyword: result = "failFront"; return true;
                case TokenKind.ZFailBackKeyword: result = "ZFailBack"; return true;
                case TokenKind.ZFailFrontKeyword: result = "ZFailFront"; return true;
                case TokenKind.PassFrontKeyword: result = "PassFront"; return true;
                case TokenKind.PassBackKeyword: result = "PassBack"; return true;
                case TokenKind.UsePassKeyword: result = "UsePass"; return true;
                case TokenKind.GrabPassKeyword: result = "GrabPass"; return true;
                case TokenKind.DependencyKeyword: result = "Dependency"; return true;
                case TokenKind.MaterialKeyword: result = "Material"; return true;
                case TokenKind.DiffuseKeyword: result = "Diffuse"; return true;
                case TokenKind.AmbientKeyword: result = "Ambient"; return true;
                case TokenKind.ShininessKeyword: result = "Shininess"; return true;
                case TokenKind.SpecularKeyword: result = "Specular"; return true;
                case TokenKind.EmissionKeyword: result = "Emission"; return true;
                case TokenKind.AmbientAndDiffuseKeyword: result = "AmbientAndDiffuse"; return true;
                case TokenKind.FogKeyword: result = "Fog"; return true;
                case TokenKind.ModeKeyword: result = "Mode"; return true;
                case TokenKind.DensityKeyword: result = "Density"; return true;
                case TokenKind.SeparateSpecularKeyword: result = "SeparateSpecular"; return true;
                case TokenKind.SetTextureKeyword: result = "SetTexture"; return true;
                case TokenKind.CombineKeyword: result = "Combine"; return true;
                case TokenKind.AlphaKeyword: result = "Alpha"; return true;
                case TokenKind.LerpKeyword: result = "Lerp"; return true;
                case TokenKind.DoubleKeyword: result = "DOUBLE"; return true;
                case TokenKind.QuadKeyword: result = "Quad"; return true;
                case TokenKind.ConstantColorKeyword: result = "constantColor"; return true;
                case TokenKind.MatrixKeyword: result = "Matrix"; return true;
                case TokenKind.AlphaTestKeyword: result = "AlphaTest"; return true;
                case TokenKind.ColorMaterialKeyword: result = "ColorMaterial"; return true;
                case TokenKind.BindChannelsKeyword: result = "BindChannels"; return true;
                case TokenKind.PackageRequirementsKeyword: result = "PackageRequirements"; return true;
                case TokenKind.BindKeyword: result = "Bind"; return true;
                case TokenKind.TrueKeyword: result = "True"; return true;
                case TokenKind.FalseKeyword: result = "False"; return true;
                case TokenKind.OffKeyword: result = "Off"; return true;
                case TokenKind.OnKeyword: result = "On"; return true;
                case TokenKind.FrontKeyword: result = "Front"; return true;
                case TokenKind.BackKeyword: result = "Back"; return true;
                case TokenKind.OneKeyword: result = "One"; return true;
                case TokenKind.ZeroKeyword: result = "Zero"; return true;
                case TokenKind.SrcColorKeyword: result = "SrcColor"; return true;
                case TokenKind.SrcAlphaKeyword: result = "SrcAlpha"; return true;
                case TokenKind.SrcAlphaSaturateKeyword: result = "SrcAlphaSaturate"; return true;
                case TokenKind.DstColorKeyword: result = "DstColor"; return true;
                case TokenKind.DstAlphaKeyword: result = "DstAlpha"; return true;
                case TokenKind.OneMinusSrcColorKeyword: result = "OneMinusSrcColor"; return true;
                case TokenKind.OneMinusSrcAlphaKeyword: result = "OneMinusSrcAlpha"; return true;
                case TokenKind.OneMinusDstColorKeyword: result = "OneMinusDstColor"; return true;
                case TokenKind.OneMinusDstAlphaKeyword: result = "OneMinusDstAlpha"; return true;
                case TokenKind.GlobalKeyword: result = "Global"; return true;
                case TokenKind.AddKeyword: result = "Add"; return true;
                case TokenKind.SubKeyword: result = "Sub"; return true;
                case TokenKind.RevSubKeyword: result = "RevSub"; return true;
                case TokenKind.MinKeyword: result = "Min"; return true;
                case TokenKind.MaxKeyword: result = "Max"; return true;
                case TokenKind.LogicalClearKeyword: result = "LogicalClear"; return true;
                case TokenKind.LogicalSetKeyword: result = "LogicalSet"; return true;
                case TokenKind.LogicalCopyKeyword: result = "LogicalCopy"; return true;
                case TokenKind.LogicalCopyInvertedKeyword: result = "LogicalCopyInverted"; return true;
                case TokenKind.LogicalNoopKeyword: result = "LogicalNoop"; return true;
                case TokenKind.LogicalInvertKeyword: result = "LogicalInvert"; return true;
                case TokenKind.LogicalAndKeyword: result = "LogicalAnd"; return true;
                case TokenKind.LogicalNandKeyword: result = "LogicalNand"; return true;
                case TokenKind.LogicalOrKeyword: result = "LogicalOr"; return true;
                case TokenKind.LogicalNorKeyword: result = "LogicalNor"; return true;
                case TokenKind.LogicalXorKeyword: result = "LogicalXor"; return true;
                case TokenKind.LogicalEquivKeyword: result = "LogicalEquiv"; return true;
                case TokenKind.LogicalAndReverseKeyword: result = "LogicalAndReverse"; return true;
                case TokenKind.LogicalOrReverseKeyword: result = "LogicalOrReverse"; return true;
                case TokenKind.LogicalOrInvertedKeyword: result = "LogicalOrInverted"; return true;
                case TokenKind.MultiplyKeyword: result = "Multiply"; return true;
                case TokenKind.ScreenKeyword: result = "Screen"; return true;
                case TokenKind.OverlayKeyword: result = "Overlay"; return true;
                case TokenKind.DarkenKeyword: result = "Darken"; return true;
                case TokenKind.LightenKeyword: result = "Lighten"; return true;
                case TokenKind.ColorDodgeKeyword: result = "ColorDodge"; return true;
                case TokenKind.ColorBurnKeyword: result = "ColorBurn"; return true;
                case TokenKind.HardLightKeyword: result = "HardLight"; return true;
                case TokenKind.SoftLightKeyword: result = "SoftLight"; return true;
                case TokenKind.DifferenceKeyword: result = "Difference"; return true;
                case TokenKind.ExclusionKeyword: result = "Exclusion"; return true;
                case TokenKind.HSLHueKeyword: result = "HSLHue"; return true;
                case TokenKind.HSLSaturationKeyword: result = "HSLSaturation"; return true;
                case TokenKind.HSLColorKeyword: result = "HSLColor"; return true;
                case TokenKind.HSLLuminosityKeyword: result = "HSLLuminosity"; return true;
                default: result = string.Empty; return false;
            }
        }

        public static string IdentifierOrKeywordToString(Common.Token<TokenKind> token)
        {
            if (token.Identifier != null)
                return token.Identifier;

            if (TryConvertKeywordToString(token.Kind, out string result))
                return result;

            return "__INVALID";
        }

        public static string TokenToString(Common.Token<TokenKind> token)
        {
            switch (token.Kind)
            {
                case TokenKind.InvalidToken: return "__INVALID";
                case TokenKind.OpenParenToken: return "(";
                case TokenKind.CloseParenToken: return ")";
                case TokenKind.OpenBracketToken: return "[";
                case TokenKind.CloseBracketToken: return "]";
                case TokenKind.OpenBraceToken: return "{";
                case TokenKind.CloseBraceToken: return "}";
                case TokenKind.SemiToken: return ";";
                case TokenKind.CommaToken: return ",";
                case TokenKind.LessThanToken: return "<";
                case TokenKind.LessThanEqualsToken: return "<=";
                case TokenKind.GreaterThanToken: return ">";
                case TokenKind.GreaterThanEqualsToken: return ">=";
                case TokenKind.LessThanLessThanToken: return "<<";
                case TokenKind.GreaterThanGreaterThanToken: return ">>";
                case TokenKind.PlusToken: return "+";
                case TokenKind.PlusPlusToken: return "++";
                case TokenKind.MinusToken: return "-";
                case TokenKind.MinusMinusToken: return "--";
                case TokenKind.AsteriskToken: return "*";
                case TokenKind.SlashToken: return "/";
                case TokenKind.PercentToken: return "%";
                case TokenKind.AmpersandToken: return "&";
                case TokenKind.BarToken: return "|";
                case TokenKind.AmpersandAmpersandToken: return "&&";
                case TokenKind.BarBarToken: return "||";
                case TokenKind.CaretToken: return "^";
                case TokenKind.NotToken: return "!";
                case TokenKind.TildeToken: return "~";
                case TokenKind.QuestionToken: return "?";
                case TokenKind.ColonToken: return ":";
                case TokenKind.ColonColonToken: return "::";
                case TokenKind.EqualsToken: return "=";
                case TokenKind.AsteriskEqualsToken: return "*=";
                case TokenKind.SlashEqualsToken: return "/=";
                case TokenKind.PercentEqualsToken: return "%=";
                case TokenKind.PlusEqualsToken: return "+=";
                case TokenKind.MinusEqualsToken: return "-=";
                case TokenKind.LessThanLessThanEqualsToken: return "<<=";
                case TokenKind.GreaterThanGreaterThanEqualsToken: return ">>=";
                case TokenKind.AmpersandEqualsToken: return "&=";
                case TokenKind.CaretEqualsToken: return "^=";
                case TokenKind.BarEqualsToken: return "|=";
                case TokenKind.EqualsEqualsToken: return "==";
                case TokenKind.ExclamationEqualsToken: return "!=";
                case TokenKind.DotToken: return ".";

                case TokenKind.BracketedStringLiteralToken: return $"[{token.Identifier}]";
                case TokenKind.StringLiteralToken: return $"\"{token.Identifier}\"";

                default: return IdentifierOrKeywordToString(token);
            }
        }

        public static string TokensToString(IEnumerable<Common.Token<TokenKind>> tokens)
        {
            return string.Join(" ", tokens.Select(x => TokenToString(x)));
        }
    }
}
