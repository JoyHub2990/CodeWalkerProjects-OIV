using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using TC = System.ComponentModel.TypeConverterAttribute;
using EXP = System.ComponentModel.ExpandableObjectConverter;

// AWC Shader Library (SGD2 / Shader Group Data v2) reader & writer.
// Used by GTA V Enhanced (Gen9) compiled-shader containers. Distinct from the
// audio Audio Wave Container (AwcFile.cs / magic ADAT) which shares the .awc
// extension.
//
// Semantic model is informed by Neodymium's FxdbFile.cs (SGD1 / RDR3 sibling
// format). Many struct definitions are direct ports of the SGD1 layouts —
// FxdbRasterizerStateDesc, FxdbDepthStencilStateDesc, etc. — renamed to the
// AwcEffect* convention used here.
//
// CAVEAT: the byte-layout of the per-effect data region after the 6 shader
// stage arrays does NOT directly match SGD1's FxdbEffect.Read() — see the
// extended comments on AwcEffectPreProplstRegion for the SGD2-specific
// chunking we use. The struct definitions below are still valuable as a
// type system / vocabulary, and individual records would decode with the
// SGD1 readers once the SGD2 envelope is fully reverse-engineered.

namespace CodeWalker.GameFiles
{
    public enum AwcShaderValueType : ushort
    {
        Bool     = 0,
        Uint     = 1,
        Uint2    = 2,
        Uint3    = 3,
        Uint4    = 4,
        Int      = 5,
        Int2     = 6,
        Int3     = 7,
        Int4     = 8,
        Float    = 9,
        Float2   = 10,
        Float3   = 11,
        Float4   = 12,
        Float4x3 = 13,
        Float4x4 = 14,
    }

    public enum AwcShaderResourceType : ushort
    {
        Texture2D                = 0x0102,
        Texture2DArray           = 0x0142,
        TextureCube              = 0x0202,
        Texture3D                = 0x0302,
        Buffer                   = 0x0401,
        StructuredBuffer         = 0x0405,
        ByteAddressBuffer        = 0x0407,
        RWTexture2D              = 0x011C,
        RWTexture2DArray         = 0x015C,
        RWStructuredBufferAppend = 0x040E,
        RWStructuredBuffer       = 0x0414,
        RWStructuredBufferConsume= 0x0416,
        RWByteAddressBuffer      = 0x0418,
        SamplerState             = 0x0423,
        ConstantBuffer           = 0x0430,
    }

    public enum AwcShaderStage
    {
        Vertex,
        Pixel,
        Geometry,
        Domain,
        Hull,
        Compute,
    }

    // ---------- Effect-state structure types (ported from FxdbFile.cs / SGD1) ----------
    // These define the *vocabulary* of state-block records used by the SGD1
    // sibling format. They are well-typed value structs (StructLayout-fixed
    // size) that match the on-disk layout of the equivalent SGD1 records.
    // SGD2's per-effect envelope around them differs (see AwcEffectPreProplstRegion);
    // round-tripping currently goes via the raw-bytes fast path, but these
    // types are available for callers who want to interpret the data.

    public enum AwcEffectFillMode : byte { Wireframe = 2, Solid = 3 }
    public enum AwcEffectCullMode : byte { None = 1, Front = 2, Back = 3 }

    public enum AwcEffectComparisonFunc : byte
    {
        Never = 1, Less = 2, Equal = 3, LessEqual = 4,
        Greater = 5, NotEqual = 6, GreaterEqual = 7, Always = 8,
    }

    public enum AwcEffectStencilOp : byte
    {
        Keep = 1, Zero = 2, Replace = 3,
        IncrementAndClamp = 4, DecrementAndClamp = 5,
        Invert = 6, IncrementAndWrap = 7, DecrementAndWrap = 8,
    }

    public enum AwcEffectBlendOp : byte
    {
        Add = 1, Subtract = 2, ReverseSubtract = 3, Min = 4, Max = 5,
    }

    public enum AwcEffectBlendFactor : byte
    {
        Zero = 1, One = 2, SrcColor = 3, InvSrcColor = 4,
        SrcAlpha = 5, InvSrcAlpha = 6, DestAlpha = 7, InvDestAlpha = 8,
        DestColor = 9, InvDestColor = 10, SrcAlphaSat = 11,
        BlendFactor = 14, InvBlendFactor = 15,
        Src1Color = 16, InvSrc1Color = 17,
        Src1Alpha = 18, InvSrc1Alpha = 19,
        AlphaFactor = 20, InvAlphaFactor = 21,
    }

    [Flags]
    public enum AwcEffectColorWriteEnable : byte
    {
        Red = 1, Green = 2, Blue = 4, Alpha = 8,
    }

    public enum AwcEffectTextureAddressMode : byte
    {
        Wrap = 1, Mirror = 2, Clamp = 3, Border = 4, MirrorOnce = 5,
    }

    [Flags]
    public enum AwcEffectRenderShaderSetFlags : byte
    {
        HasRasterizerState = 1 << 0,
        HasDepthStencilState = 1 << 1,
        HasBlendState = 1 << 2,
        HasUnknown09 = 1 << 3,
        Unk4 = 1 << 4,
        DepthStencilStateIndexExtraBit = 1 << 5,
        BlendStateIndexExtraBit = 1 << 6,
    }

    [TC(typeof(EXP)), StructLayout(LayoutKind.Sequential, Size = SizeOf)]
    public struct AwcEffectStencilOpDesc
    {
        public const int SizeOf = 4;
        public AwcEffectStencilOp StencilFailOp;
        public AwcEffectStencilOp StencilDepthFailOp;
        public AwcEffectStencilOp StencilPassOp;
        public AwcEffectComparisonFunc StencilFunc;
        public override string ToString() => StencilFailOp + "/" + StencilDepthFailOp + "/" + StencilPassOp + " " + StencilFunc;
    }

    [TC(typeof(EXP)), StructLayout(LayoutKind.Sequential, Size = SizeOf)]
    public struct AwcEffectRasterizerStateDesc
    {
        // SGD1 FxdbRasterizerStateDesc: similar to D3D12_RASTERIZER_DESC.
        public const int SizeOf = 16;
        public AwcEffectFillMode FillMode;
        public AwcEffectCullMode CullMode;
        [MarshalAs(UnmanagedType.I1)] public bool FrontCounterClockwise;
        public sbyte DepthBias;
        public float DepthBiasClamp;
        public float SlopeScaledDepthBias;
        [MarshalAs(UnmanagedType.I1)] public bool DepthClipEnable;
        public byte Unknown0D;
        [MarshalAs(UnmanagedType.I1)] public bool MultisampleEnable;
        [MarshalAs(UnmanagedType.I1)] public bool AntialiasedLineEnable;
        public override string ToString() => "Fill=" + FillMode + " Cull=" + CullMode;
    }

    [TC(typeof(EXP)), StructLayout(LayoutKind.Sequential, Size = SizeOf)]
    public struct AwcEffectDepthStencilStateDesc
    {
        // SGD1 FxdbDepthStencilStateDesc: similar to D3D12_DEPTH_STENCIL_DESC1.
        public const int SizeOf = 24;
        [MarshalAs(UnmanagedType.I1)] public bool DepthEnable;
        [MarshalAs(UnmanagedType.I1)] public bool DepthWriteEnable;
        public AwcEffectComparisonFunc DepthFunc;
        [MarshalAs(UnmanagedType.I1)] public bool StencilEnable;
        public byte StencilReadMask;
        public byte StencilWriteMask;
        public AwcEffectStencilOpDesc FrontFace;
        public AwcEffectStencilOpDesc BackFace;
        [MarshalAs(UnmanagedType.I1)] public bool HasDifferentBackFace;
        [MarshalAs(UnmanagedType.I1)] public bool DepthBoundsTestEnable;
        public byte Unknown10;
        public byte Unknown11;
        public byte BackFaceStencilReadMask;
        public byte BackFaceStencilWriteMask;
        public int Unknown14;
        public override string ToString() => "Depth=" + DepthEnable + " Stencil=" + StencilEnable;
    }

    [TC(typeof(EXP)), StructLayout(LayoutKind.Sequential, Size = SizeOf)]
    public struct AwcEffectBlendRenderTargetDesc
    {
        public const int SizeOf = 8;
        [MarshalAs(UnmanagedType.I1)] public bool BlendEnable;
        public AwcEffectBlendFactor SrcBlend;
        public AwcEffectBlendFactor DestBlend;
        public AwcEffectBlendOp BlendOp;
        public AwcEffectBlendFactor SrcBlendAlpha;
        public AwcEffectBlendFactor DestBlendAlpha;
        public AwcEffectBlendOp BlendOpAlpha;
        public AwcEffectColorWriteEnable RenderTargetWriteMask;
        public override string ToString() => BlendEnable ? (SrcBlend + " " + BlendOp + " " + DestBlend) : "disabled";
    }

    [TC(typeof(EXP)), StructLayout(LayoutKind.Sequential, Size = SizeOf)]
    public struct AwcEffectBlendStateDesc
    {
        public const int SizeOf = 68;
        [MarshalAs(UnmanagedType.I1)] public bool AlphaToCoverageEnable;
        [MarshalAs(UnmanagedType.I1)] public bool IndependentBlendEnable;
        public byte Unknown02;
        public byte Unknown03;
        public AwcEffectBlendRenderTargetDesc RenderTarget0;
        public AwcEffectBlendRenderTargetDesc RenderTarget1;
        public AwcEffectBlendRenderTargetDesc RenderTarget2;
        public AwcEffectBlendRenderTargetDesc RenderTarget3;
        public AwcEffectBlendRenderTargetDesc RenderTarget4;
        public AwcEffectBlendRenderTargetDesc RenderTarget5;
        public AwcEffectBlendRenderTargetDesc RenderTarget6;
        public AwcEffectBlendRenderTargetDesc RenderTarget7;
        public override string ToString() => "A2C=" + AlphaToCoverageEnable + " IndepBlend=" + IndependentBlendEnable;
    }

    [TC(typeof(EXP)), StructLayout(LayoutKind.Sequential, Size = SizeOf)]
    public struct AwcEffectSamplerStateDesc
    {
        public const int SizeOf = 36;
        public byte Filter;
        public AwcEffectTextureAddressMode AddressU;
        public AwcEffectTextureAddressMode AddressV;
        public AwcEffectTextureAddressMode AddressW;
        public float MipLodBias;
        public byte MaxAnisotropy;
        public AwcEffectComparisonFunc ComparisonFunc;
        public byte Unknown0A;
        public byte Unknown0B;
        public float BorderColorR;
        public float BorderColorG;
        public float BorderColorB;
        public float BorderColorA;
        public float MinLod;
        public float MaxLod;
        public override string ToString() => "Filter=0x" + Filter.ToString("X2") + " " + AddressU + "/" + AddressV + "/" + AddressW;
    }

    [TC(typeof(EXP)), StructLayout(LayoutKind.Sequential, Size = SizeOf)]
    public struct AwcEffectTechniqueDesc
    {
        // 32-bit bitfield: NameOffset[15] | RenderShaderSetStartIndex[9] | RenderShaderSetCount[8]
        public const int SizeOf = 4;
        public uint Data;
        public uint NameOffset => Data & 0x7FFF;
        public uint RenderShaderSetStartIndex => (Data >> 15) & 0x1FF;
        public uint RenderShaderSetCount => Data >> 24;
        public override string ToString() => "name@" + NameOffset + " sets[" + RenderShaderSetStartIndex + "+" + RenderShaderSetCount + "]";
    }

    [TC(typeof(EXP)), StructLayout(LayoutKind.Sequential, Size = SizeOf)]
    public struct AwcEffectRenderShaderSetDesc
    {
        // SGD1 FxdbRenderShaderSetDesc: a single pass-binding mapping a
        // tuple of shader-stage indices + state-block indices.
        public const int SizeOf = 20;
        public byte VertexShaderIndex;
        public byte PixelShaderIndex;
        public byte GeometryShaderIndex;
        public byte DomainShaderIndex;
        public byte HullShaderIndex;
        public byte ComputeShaderIndex;
        public byte RasterizerStateIndex;
        public byte DepthStencilStateIndex;
        public byte BlendStateIndex;
        public byte Unknown09;
        public AwcEffectRenderShaderSetFlags Flags;
        public byte SamplerStateCount;
        public ushort SamplerStateStartIndex;
        public ushort Unknown0E;
        public byte Unknown10;
        public byte Unknown11;
        public byte Unknown12_0;
        public byte Unknown12_1;
        public override string ToString() => "VS=" + VertexShaderIndex + " PS=" + PixelShaderIndex;
    }

    [TC(typeof(EXP)), StructLayout(LayoutKind.Sequential, Size = SizeOf)]
    public struct AwcEffectSamplerStateRef
    {
        public const int SizeOf = 2;
        public byte NameOffset;
        public byte SamplerStateIndex;
        public override string ToString() => "name@" + NameOffset + " -> #" + SamplerStateIndex;
    }

    // SGD1 FxdbShaderResourceData / FxdbShaderElementData / FxdbShaderAnnotation
    // record types — kept as plain structs with the on-disk layout. These
    // describe individual records inside a FxdbShaderData blob (resource
    // descriptions + their elements + annotations + name heap).
    [TC(typeof(EXP)), StructLayout(LayoutKind.Sequential, Size = SizeOf)]
    public struct AwcEffectShaderResourceData
    {
        public const int SizeOf = 24;
        public byte ResourceTypeAndResourceClassAndUnkFlag;
        public byte ImageDimension;
        public byte BindPoint;
        public byte AnnotationCount;
        public byte ElementCount;
        public byte UnkAndSrtIndex;
        public ushort ElementsOffset;
        public ushort NameOffset;
        public ushort SemanticNameOffset;
        public ushort ByteSize;
        public ushort AnnotationsOffset;
        public uint NameHash;
        public uint SemanticNameHash;
        public override string ToString() => "res name@" + NameOffset;
    }

    [TC(typeof(EXP)), StructLayout(LayoutKind.Sequential, Size = SizeOf)]
    public struct AwcEffectShaderElementData
    {
        public const int SizeOf = 24;
        public byte Type;
        public byte AnnotationCount;
        public ushort ArraySize;
        public ushort StartOffset;
        public ushort NameOffset;
        public ushort SemanticNameOffset;
        public ushort DefaultValueOffset;
        public ushort AnnotationsOffset;
        public ushort Padding0E;
        public uint NameHash;
        public uint SemanticNameHash;
        public override string ToString() => "elem name@" + NameOffset;
    }

    [TC(typeof(EXP)), StructLayout(LayoutKind.Sequential, Size = SizeOf)]
    public struct AwcEffectShaderAnnotation
    {
        public const int SizeOf = 8;
        public uint TypeAndNameHash;
        public uint Value;
        public byte AnnotationType => (byte)(TypeAndNameHash & 3);
        public uint NameHash => TypeAndNameHash >> 2;
        public override string ToString() => "ann type=" + AnnotationType + " hash=0x" + NameHash.ToString("X8");
    }


    [TC(typeof(EXP))]
    public class AwcShaderCBufferData
    {
        public AwcShaderValueType Type { get; set; }
        public ushort ArraySize { get; set; }
        public ushort PackOffset { get; set; }
        public uint NameOffset { get; set; }
        public string Name { get; set; }
        [Browsable(false)] public byte[] NameHashData { get; set; } = new byte[14];

        public string TypeName => Type.ToString();

        public void WriteXml(StringBuilder sb, int indent)
        {
            MetaXmlBase.StringTag(sb, indent, "Name", MetaXmlBase.XmlEscape(Name ?? string.Empty));
            MetaXmlBase.StringTag(sb, indent, "Type", Type.ToString());
            MetaXmlBase.ValueTag(sb, indent, "ArraySize", ArraySize.ToString());
            MetaXmlBase.ValueTag(sb, indent, "PackOffset", PackOffset.ToString());
            MetaXmlBase.ValueTag(sb, indent, "NameOffset", NameOffset.ToString());
            MetaXmlBase.StringTag(sb, indent, "NameHashData", Convert.ToBase64String(NameHashData ?? new byte[14]));
        }

        public void ReadXml(XmlNode node)
        {
            Name = Xml.GetChildInnerText(node, "Name");
            Type = Xml.GetChildEnumInnerText<AwcShaderValueType>(node, "Type");
            ArraySize = (ushort)Xml.GetChildUIntAttribute(node, "ArraySize");
            PackOffset = (ushort)Xml.GetChildUIntAttribute(node, "PackOffset");
            NameOffset = Xml.GetChildUIntAttribute(node, "NameOffset");
            var b64 = Xml.GetChildInnerText(node, "NameHashData");
            NameHashData = string.IsNullOrEmpty(b64) ? new byte[14] : Convert.FromBase64String(b64);
            if (NameHashData.Length != 14)
            {
                var fix = new byte[14];
                Array.Copy(NameHashData, fix, Math.Min(14, NameHashData.Length));
                NameHashData = fix;
            }
        }

        public override string ToString() => Name + " : " + Type + (ArraySize > 1 ? "[" + ArraySize + "]" : string.Empty);
    }

    [TC(typeof(EXP))]
    public class AwcShaderRegister
    {
        public AwcShaderResourceType ResourceType { get; set; }
        public ushort RegisterSlot { get; set; }
        public byte CBufferCount { get; set; }
        public byte NumDescriptors { get; set; }
        public byte RegisterSpace { get; set; }
        public byte Reserved { get; set; }
        public ushort CBufferDataOffset { get; set; }
        public ushort RegStringOffset { get; set; }
        public string Name { get; set; }
        [Browsable(false)] public byte[] ExtraData { get; set; } = new byte[16];
        public AwcShaderCBufferData[] CBuffers { get; set; }

        public string RegisterPrefix
        {
            get
            {
                switch (ResourceType)
                {
                    case AwcShaderResourceType.ConstantBuffer: return "b";
                    case AwcShaderResourceType.SamplerState:   return "s";
                    case AwcShaderResourceType.Texture2D:
                    case AwcShaderResourceType.Texture2DArray:
                    case AwcShaderResourceType.TextureCube:
                    case AwcShaderResourceType.Texture3D:
                    case AwcShaderResourceType.Buffer:
                    case AwcShaderResourceType.StructuredBuffer:
                    case AwcShaderResourceType.ByteAddressBuffer:
                        return "t";
                    default: return "u";
                }
            }
        }

        public string Slot => RegisterPrefix + RegisterSlot + (RegisterSpace != 0 ? ",space" + RegisterSpace : string.Empty);

        public void WriteXml(StringBuilder sb, int indent)
        {
            MetaXmlBase.StringTag(sb, indent, "Name", MetaXmlBase.XmlEscape(Name ?? string.Empty));
            MetaXmlBase.StringTag(sb, indent, "ResourceType", ResourceType.ToString());
            MetaXmlBase.ValueTag(sb, indent, "RegisterSlot", RegisterSlot.ToString());
            MetaXmlBase.ValueTag(sb, indent, "CBufferCount", CBufferCount.ToString());
            MetaXmlBase.ValueTag(sb, indent, "NumDescriptors", NumDescriptors.ToString());
            MetaXmlBase.ValueTag(sb, indent, "RegisterSpace", RegisterSpace.ToString());
            MetaXmlBase.ValueTag(sb, indent, "Reserved", Reserved.ToString());
            MetaXmlBase.ValueTag(sb, indent, "CBufferDataOffset", CBufferDataOffset.ToString());
            MetaXmlBase.ValueTag(sb, indent, "RegStringOffset", RegStringOffset.ToString());
            MetaXmlBase.StringTag(sb, indent, "ExtraData", Convert.ToBase64String(ExtraData ?? new byte[16]));
            if (CBuffers != null && CBuffers.Length > 0)
            {
                MetaXmlBase.OpenTag(sb, indent, "CBuffers");
                var ci = indent + 1;
                var cci = ci + 1;
                for (int i = 0; i < CBuffers.Length; i++)
                {
                    if (CBuffers[i] != null)
                    {
                        MetaXmlBase.OpenTag(sb, ci, "Item");
                        CBuffers[i].WriteXml(sb, cci);
                        MetaXmlBase.CloseTag(sb, ci, "Item");
                    }
                    else
                    {
                        MetaXmlBase.SelfClosingTag(sb, ci, "Item");
                    }
                }
                MetaXmlBase.CloseTag(sb, indent, "CBuffers");
            }
            else
            {
                MetaXmlBase.SelfClosingTag(sb, indent, "CBuffers");
            }
        }

        public void ReadXml(XmlNode node)
        {
            Name = Xml.GetChildInnerText(node, "Name");
            ResourceType = Xml.GetChildEnumInnerText<AwcShaderResourceType>(node, "ResourceType");
            RegisterSlot = (ushort)Xml.GetChildUIntAttribute(node, "RegisterSlot");
            CBufferCount = (byte)Xml.GetChildUIntAttribute(node, "CBufferCount");
            NumDescriptors = (byte)Xml.GetChildUIntAttribute(node, "NumDescriptors");
            RegisterSpace = (byte)Xml.GetChildUIntAttribute(node, "RegisterSpace");
            Reserved = (byte)Xml.GetChildUIntAttribute(node, "Reserved");
            CBufferDataOffset = (ushort)Xml.GetChildUIntAttribute(node, "CBufferDataOffset");
            RegStringOffset = (ushort)Xml.GetChildUIntAttribute(node, "RegStringOffset");
            var b64 = Xml.GetChildInnerText(node, "ExtraData");
            ExtraData = string.IsNullOrEmpty(b64) ? new byte[16] : Convert.FromBase64String(b64);
            if (ExtraData.Length != 16)
            {
                var fix = new byte[16];
                Array.Copy(ExtraData, fix, Math.Min(16, ExtraData.Length));
                ExtraData = fix;
            }
            var cbsNode = node.SelectSingleNode("CBuffers");
            var list = new List<AwcShaderCBufferData>();
            if (cbsNode != null)
            {
                var inodes = cbsNode.SelectNodes("Item");
                if (inodes != null)
                {
                    foreach (XmlNode inode in inodes)
                    {
                        var cb = new AwcShaderCBufferData();
                        cb.ReadXml(inode);
                        list.Add(cb);
                    }
                }
            }
            CBuffers = list.ToArray();
        }

        public override string ToString() => Slot + " " + Name + " (" + ResourceType + ")";
    }

    [TC(typeof(EXP))]
    public class AwcShader
    {
        public string Name { get; set; }
        public byte WaveSize { get; set; }
        public uint Size { get; set; }
        [Browsable(false)] public byte[] Binary { get; set; }
        public ulong Hash { get; set; }
        [Browsable(false)] public byte[] RootSigData { get; set; }
        public uint BlockSize { get; set; }
        public ushort RegCount { get; set; }
        public ushort CBufferCount { get; set; }
        public ushort TexCount { get; set; }
        public ushort BlockSizeCopy { get; set; }
        public AwcShaderRegister[] Registers { get; set; }
        public AwcShaderStage Stage { get; set; }

        // Original-on-disk fragments preserved so unchanged shaders round-trip
        // byte-for-byte. Stale once the user mutates Binary / Size / Registers.
        [Browsable(false)] public byte NameLengthByte { get; set; }
        [Browsable(false)] public byte[] NameBytes { get; set; }
        [Browsable(false)] public byte[] OriginalBlockData { get; set; }
        [Browsable(false)] public bool BinaryDirty { get; set; }
        [Browsable(false)] public bool MetadataDirty { get; set; }

        public string StageName
        {
            get
            {
                switch (Stage)
                {
                    case AwcShaderStage.Vertex:   return "VS";
                    case AwcShaderStage.Pixel:    return "PS";
                    case AwcShaderStage.Geometry: return "GS";
                    case AwcShaderStage.Domain:   return "DS";
                    case AwcShaderStage.Hull:     return "HS";
                    case AwcShaderStage.Compute:  return "CS";
                    default: return "?";
                }
            }
        }

        public string HashHex => "0x" + Hash.ToString("X16");

        public void WriteXml(StringBuilder sb, int indent, string csoFolder, string subFolder = null)
        {
            var rawName = Name ?? "NameError";
            var safeName = rawName.Replace("/", "_").Replace("\\", "_").Replace(":", "_");
            // Group .cso side-files by owning effect (passed in via subFolder),
            // with stage prefixed on the filename so shaders stay distinguishable
            // within a single effect folder.
            var folder = string.IsNullOrEmpty(subFolder) ? StageName : subFolder;
            var fname = folder + "/" + StageName + "_" + safeName + ".cso";

            MetaXmlBase.StringTag(sb, indent, "Name", MetaXmlBase.XmlEscape(rawName));
            MetaXmlBase.StringTag(sb, indent, "Stage", Stage.ToString());
            MetaXmlBase.StringTag(sb, indent, "File", MetaXmlBase.XmlEscape(fname));
            MetaXmlBase.ValueTag(sb, indent, "WaveSize", WaveSize.ToString());
            MetaXmlBase.StringTag(sb, indent, "Hash", "0x" + Hash.ToString("X16"));
            MetaXmlBase.ValueTag(sb, indent, "Size", Size.ToString());
            MetaXmlBase.ValueTag(sb, indent, "BlockSize", BlockSize.ToString());
            MetaXmlBase.ValueTag(sb, indent, "RegCount", RegCount.ToString());
            MetaXmlBase.ValueTag(sb, indent, "CBufferCount", CBufferCount.ToString());
            MetaXmlBase.ValueTag(sb, indent, "TexCount", TexCount.ToString());
            MetaXmlBase.ValueTag(sb, indent, "BlockSizeCopy", BlockSizeCopy.ToString());
            MetaXmlBase.ValueTag(sb, indent, "NameLengthByte", NameLengthByte.ToString());
            MetaXmlBase.StringTag(sb, indent, "NameBytes", Convert.ToBase64String(NameBytes ?? Array.Empty<byte>()));
            MetaXmlBase.StringTag(sb, indent, "RootSigData", Convert.ToBase64String(RootSigData ?? new byte[144]));
            MetaXmlBase.StringTag(sb, indent, "OriginalBlockData", Convert.ToBase64String(OriginalBlockData ?? Array.Empty<byte>()));

            if (Registers != null && Registers.Length > 0)
            {
                MetaXmlBase.OpenTag(sb, indent, "Registers");
                var ci = indent + 1;
                var cci = ci + 1;
                for (int i = 0; i < Registers.Length; i++)
                {
                    if (Registers[i] != null)
                    {
                        MetaXmlBase.OpenTag(sb, ci, "Item");
                        Registers[i].WriteXml(sb, cci);
                        MetaXmlBase.CloseTag(sb, ci, "Item");
                    }
                    else
                    {
                        MetaXmlBase.SelfClosingTag(sb, ci, "Item");
                    }
                }
                MetaXmlBase.CloseTag(sb, indent, "Registers");
            }
            else
            {
                MetaXmlBase.SelfClosingTag(sb, indent, "Registers");
            }

            // Write the compiled bytecode out as a side-file referenced from XML.
            if (Binary != null && !string.IsNullOrEmpty(csoFolder))
            {
                try
                {
                    var outPath = Path.Combine(csoFolder, fname.Replace('/', Path.DirectorySeparatorChar));
                    var outDir = Path.GetDirectoryName(outPath);
                    if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir)) Directory.CreateDirectory(outDir);
                    File.WriteAllBytes(outPath, Binary);
                }
                catch { }
            }
        }

        public void ReadXml(XmlNode node, string csoFolder)
        {
            // Stage should be set by the caller (from the containing group); fall
            // back to parsing the Stage tag if present so single-shader callers work.
            Name = Xml.GetChildInnerText(node, "Name");
            var stageStr = Xml.GetChildInnerText(node, "Stage");
            if (!string.IsNullOrEmpty(stageStr) && Enum.TryParse<AwcShaderStage>(stageStr, out var st)) Stage = st;
            WaveSize = (byte)Xml.GetChildUIntAttribute(node, "WaveSize");
            var hashStr = Xml.GetChildInnerText(node, "Hash");
            if (!string.IsNullOrEmpty(hashStr))
            {
                if (hashStr.StartsWith("0x") || hashStr.StartsWith("0X"))
                    Hash = Convert.ToUInt64(hashStr.Substring(2), 16);
                else
                    Hash = ulong.Parse(hashStr);
            }
            Size = Xml.GetChildUIntAttribute(node, "Size");
            BlockSize = Xml.GetChildUIntAttribute(node, "BlockSize");
            RegCount = (ushort)Xml.GetChildUIntAttribute(node, "RegCount");
            CBufferCount = (ushort)Xml.GetChildUIntAttribute(node, "CBufferCount");
            TexCount = (ushort)Xml.GetChildUIntAttribute(node, "TexCount");
            BlockSizeCopy = (ushort)Xml.GetChildUIntAttribute(node, "BlockSizeCopy");
            NameLengthByte = (byte)Xml.GetChildUIntAttribute(node, "NameLengthByte");
            var nb = Xml.GetChildInnerText(node, "NameBytes");
            NameBytes = string.IsNullOrEmpty(nb) ? null : Convert.FromBase64String(nb);
            var rs = Xml.GetChildInnerText(node, "RootSigData");
            RootSigData = string.IsNullOrEmpty(rs) ? new byte[144] : Convert.FromBase64String(rs);
            if (RootSigData.Length != 144)
            {
                var fix = new byte[144];
                Array.Copy(RootSigData, fix, Math.Min(144, RootSigData.Length));
                RootSigData = fix;
            }
            var ob = Xml.GetChildInnerText(node, "OriginalBlockData");
            OriginalBlockData = string.IsNullOrEmpty(ob) ? null : Convert.FromBase64String(ob);

            // Load the .cso side-file if present. The exporter writes
            // "<Stage>/<Name>.cso" so the relative path may contain subfolders.
            // Older exports used a flat "<Stage>_<Name>.cso" — fall back to that
            // if the subfolder form isn't on disk.
            var fname = Xml.GetChildInnerText(node, "File");
            if (!string.IsNullOrEmpty(fname) && !string.IsNullOrEmpty(csoFolder))
            {
                var rel = fname.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                var fp = Path.Combine(csoFolder, rel);
                if (!File.Exists(fp))
                {
                    var legacy = fname.Replace('/', '_').Replace('\\', '_');
                    fp = Path.Combine(csoFolder, legacy);
                }
                if (File.Exists(fp)) Binary = File.ReadAllBytes(fp);
            }
            // If we have no Binary but Size was set, leave Binary null — caller
            // can fall back to Size/Hash for diagnostics.
            if (Binary != null) Size = (uint)Binary.Length;

            var rnode = node.SelectSingleNode("Registers");
            var list = new List<AwcShaderRegister>();
            if (rnode != null)
            {
                var inodes = rnode.SelectNodes("Item");
                if (inodes != null)
                {
                    foreach (XmlNode inode in inodes)
                    {
                        var r = new AwcShaderRegister();
                        r.ReadXml(inode);
                        list.Add(r);
                    }
                }
            }
            Registers = list.ToArray();

            // Round-trip mode: keep using OriginalBlockData when present, otherwise
            // mark MetadataDirty so it gets rebuilt from the Registers we just loaded.
            MetadataDirty = OriginalBlockData == null;
            BinaryDirty = false;
        }

        public override string ToString() => StageName + " " + Name;
    }

    // ---------- Trailer (effects DB) data model ----------
    // The AWC trailer is a per-effect database appended after the 6 shader-stage
    // arrays. Decoded faithfully from the Python reference parser; round-trips
    // byte-identical via the preserved OriginalRawBytes fast path when unchanged.

    [TC(typeof(EXP))]
    public class AwcEffectPropBinding
    {
        public uint NameHash { get; set; }
        public uint Tag { get; set; }
        public string Name { get; set; } // resolved best-effort from the string pool

        public void WriteXml(StringBuilder sb, int indent)
        {
            MetaXmlBase.StringTag(sb, indent, "Name", MetaXmlBase.XmlEscape(Name ?? string.Empty));
            MetaXmlBase.StringTag(sb, indent, "NameHash", "0x" + NameHash.ToString("X8"));
            MetaXmlBase.StringTag(sb, indent, "Tag", "0x" + Tag.ToString("X8"));
        }

        public void ReadXml(XmlNode node)
        {
            Name = Xml.GetChildInnerText(node, "Name");
            NameHash = ParseHex32(Xml.GetChildInnerText(node, "NameHash"));
            Tag = ParseHex32(Xml.GetChildInnerText(node, "Tag"));
        }

        internal static uint ParseHex32(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            s = s.Trim();
            if (s.StartsWith("0x") || s.StartsWith("0X")) return Convert.ToUInt32(s.Substring(2), 16);
            return uint.Parse(s);
        }

        internal static ulong ParseHex64(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            s = s.Trim();
            if (s.StartsWith("0x") || s.StartsWith("0X")) return Convert.ToUInt64(s.Substring(2), 16);
            return ulong.Parse(s);
        }

        public override string ToString() => (string.IsNullOrEmpty(Name) ? "0x" + NameHash.ToString("X8") : Name) + " : 0x" + Tag.ToString("X8");
    }

    [TC(typeof(EXP))]
    public class AwcEffectPropEntry
    {
        public ulong EntryHash { get; set; }
        public uint Flags1 { get; set; } // high 24 bits of count word
        public uint Flags2 { get; set; }
        public AwcEffectPropBinding[] Bindings { get; set; }

        public string EntryHashHex => "0x" + EntryHash.ToString("X16");

        public void WriteXml(StringBuilder sb, int indent)
        {
            MetaXmlBase.StringTag(sb, indent, "EntryHash", "0x" + EntryHash.ToString("X16"));
            MetaXmlBase.StringTag(sb, indent, "Flags1", "0x" + Flags1.ToString("X8"));
            MetaXmlBase.StringTag(sb, indent, "Flags2", "0x" + Flags2.ToString("X8"));
            if (Bindings != null && Bindings.Length > 0)
            {
                MetaXmlBase.OpenTag(sb, indent, "Bindings");
                var ci = indent + 1;
                var cci = ci + 1;
                for (int i = 0; i < Bindings.Length; i++)
                {
                    if (Bindings[i] != null)
                    {
                        MetaXmlBase.OpenTag(sb, ci, "Item");
                        Bindings[i].WriteXml(sb, cci);
                        MetaXmlBase.CloseTag(sb, ci, "Item");
                    }
                    else
                    {
                        MetaXmlBase.SelfClosingTag(sb, ci, "Item");
                    }
                }
                MetaXmlBase.CloseTag(sb, indent, "Bindings");
            }
            else
            {
                MetaXmlBase.SelfClosingTag(sb, indent, "Bindings");
            }
        }

        public void ReadXml(XmlNode node)
        {
            EntryHash = AwcEffectPropBinding.ParseHex64(Xml.GetChildInnerText(node, "EntryHash"));
            Flags1 = AwcEffectPropBinding.ParseHex32(Xml.GetChildInnerText(node, "Flags1"));
            Flags2 = AwcEffectPropBinding.ParseHex32(Xml.GetChildInnerText(node, "Flags2"));
            var bnode = node.SelectSingleNode("Bindings");
            var list = new List<AwcEffectPropBinding>();
            if (bnode != null)
            {
                var inodes = bnode.SelectNodes("Item");
                if (inodes != null)
                {
                    foreach (XmlNode inode in inodes)
                    {
                        var b = new AwcEffectPropBinding();
                        b.ReadXml(inode);
                        list.Add(b);
                    }
                }
            }
            Bindings = list.ToArray();
        }

        public override string ToString() => EntryHashHex + " (" + (Bindings?.Length ?? 0) + " bindings)";
    }

    [TC(typeof(EXP))]
    public class AwcEffectPass
    {
        public string Name { get; set; }

        public void WriteXml(StringBuilder sb, int indent)
        {
            MetaXmlBase.StringTag(sb, indent, "Name", MetaXmlBase.XmlEscape(Name ?? string.Empty));
        }

        public void ReadXml(XmlNode node)
        {
            Name = Xml.GetChildInnerText(node, "Name");
        }

        public override string ToString() => Name ?? string.Empty;
    }

    [TC(typeof(EXP))]
    public class AwcEffectTechnique
    {
        public string Name { get; set; }
        public AwcEffectPass[] Passes { get; set; }

        public void WriteXml(StringBuilder sb, int indent)
        {
            MetaXmlBase.StringTag(sb, indent, "Name", MetaXmlBase.XmlEscape(Name ?? string.Empty));
            if (Passes != null && Passes.Length > 0)
            {
                MetaXmlBase.OpenTag(sb, indent, "Passes");
                var ci = indent + 1;
                var cci = ci + 1;
                for (int i = 0; i < Passes.Length; i++)
                {
                    if (Passes[i] != null)
                    {
                        MetaXmlBase.OpenTag(sb, ci, "Item");
                        Passes[i].WriteXml(sb, cci);
                        MetaXmlBase.CloseTag(sb, ci, "Item");
                    }
                    else
                    {
                        MetaXmlBase.SelfClosingTag(sb, ci, "Item");
                    }
                }
                MetaXmlBase.CloseTag(sb, indent, "Passes");
            }
            else
            {
                MetaXmlBase.SelfClosingTag(sb, indent, "Passes");
            }
        }

        public void ReadXml(XmlNode node)
        {
            Name = Xml.GetChildInnerText(node, "Name");
            var pnode = node.SelectSingleNode("Passes");
            var list = new List<AwcEffectPass>();
            if (pnode != null)
            {
                var inodes = pnode.SelectNodes("Item");
                if (inodes != null)
                {
                    foreach (XmlNode inode in inodes)
                    {
                        var p = new AwcEffectPass();
                        p.ReadXml(inode);
                        list.Add(p);
                    }
                }
            }
            Passes = list.ToArray();
        }

        public override string ToString() => (Name ?? string.Empty) + " (" + (Passes?.Length ?? 0) + " passes)";
    }

    [TC(typeof(EXP))]
    public class AwcEffectPreProplstRegion
    {
        // Three observed invariant-tail variants (40 B each). Index matches the
        // proplst record's register-slot field (0, 0x100, 0x200). Verbatim
        // copies from the Python parser.
        public static readonly byte[][] InvariantVariants = new byte[][]
        {
            new byte[]
            {
                0x2d,0x00,0x00,0x00, 0x01,0x00,0x00,0x00, 0x00,0x00,0x2d,0x00,
                0x30,0x04,0x00,0x00, 0x00,0x00,0x01,0x00, 0x00,0x00,0x1d,0x00,
                0x1d,0x00,0x00,0x00, 0x00,0x00,0x00,0x00, 0x2b,0x59,0x35,0xe4,
                0x2b,0x59,0x35,0xe4,
            },
            new byte[]
            {
                0x35,0x00,0x00,0x00, 0x01,0x00,0x00,0x00, 0x01,0x00,0x35,0x00,
                0x30,0x04,0x00,0x01, 0x00,0x00,0x01,0x00, 0x00,0x00,0x25,0x00,
                0x25,0x00,0x00,0x00, 0x1c,0x00,0x00,0x00, 0x2b,0x59,0x35,0xe4,
                0x2b,0x59,0x35,0xe4,
            },
            new byte[]
            {
                0x3d,0x00,0x00,0x00, 0x01,0x00,0x00,0x00, 0x02,0x00,0x3d,0x00,
                0x30,0x04,0x00,0x02, 0x00,0x00,0x01,0x00, 0x00,0x00,0x2d,0x00,
                0x2d,0x00,0x00,0x00, 0x1c,0x00,0x00,0x00, 0x2b,0x59,0x35,0xe4,
                0x2b,0x59,0x35,0xe4,
            },
        };

        // 8-byte "proplst" hash dup anchor: little-endian repr of 0xe435592b,
        // twice. Marks the end of the param/defaults area and start of the
        // 40-byte invariant tail (positioned 32 bytes before the dup).
        public static readonly byte[] PropLstHashDup = new byte[]
            { 0x2b,0x59,0x35,0xe4, 0x2b,0x59,0x35,0xe4 };

        // Sub-region split. Mapping to SGD1 (FxdbEffect.Read() in FxdbFile.cs):
        //   ZeroPrefix     -> first 8 bytes of UnkStruct (72-byte block at
        //                     the effect start; the remaining 64 bytes are
        //                     prefixed onto HeaderBlock in SGD2).
        //   HeaderBlock    -> rest of UnkStruct + Unk1 (u64) + the four
        //                     state-block arrays (RasterizerStates,
        //                     DepthStencilStates, BlendStates, SamplerStates).
        //                     SGD2's exact encoding of these arrays is not
        //                     yet decoded (it does not match SGD1's u32
        //                     count + count*SizeOf layout directly).
        //   ParamSection   -> Techniques[] + RenderShaderSets[] + Unks[][]
        //                     + SamplerStateRefs[] + ShaderData (one
        //                     ShaderData per effect: resources, elements,
        //                     annotations, name heap, default values).
        //   DefaultsAndStrings -> PropertiesShaderData (a second ShaderData
        //                     whose single resource is named 'proplst' and
        //                     stores __rage_* annotations).
        //   InvariantTail  -> first 40 bytes of PropertiesShaderData (its
        //                     resource record + bits of header counts).
        //   Trailer        -> unkData_count (u32) + 0/N unkData blocks +
        //                     StringTable (u32 length + bytes).
        //
        // The split is structurally recovered by scanning for the proplst
        // hash-dup anchor (0xe435592b twice) near the end and walking back.
        // This is robust against the unknown SGD2-vs-SGD1 envelope deltas
        // and is what guarantees byte-identical round-trip.

        [Browsable(false)] public byte[] ZeroPrefix { get; set; }          // 8 B all-zero (start of UnkStruct)
        [Browsable(false)] public byte[] HeaderBlock { get; set; }         // UnkStruct rest + Unk1 + state arrays
        [Browsable(false)] public byte[] ParamSection { get; set; }        // Techniques + RenderShaderSets + Unks + SamplerStateRefs + ShaderData
        [Browsable(false)] public byte[] DefaultsAndStrings { get; set; }  // PropertiesShaderData body
        [Browsable(false)] public byte[] InvariantTail { get; set; }       // first 40 B of PropertiesShaderData
        [Browsable(false)] public byte[] Trailer { get; set; }             // unkData + StringTable (1/9/17 B observed)
        public byte InvariantVariant { get; set; }                         // 0/1/2

        public int HeaderBlockSize => HeaderBlock?.Length ?? 0;
        public int ParamSectionSize => ParamSection?.Length ?? 0;
        public int DefaultsAndStringsSize => DefaultsAndStrings?.Length ?? 0;
        public int TrailerSize => Trailer?.Length ?? 0;

        public byte[] Encode()
        {
            using (var ms = new MemoryStream())
            {
                ms.Write(ZeroPrefix ?? new byte[8], 0, 8);
                if (HeaderBlock != null && HeaderBlock.Length > 0) ms.Write(HeaderBlock, 0, HeaderBlock.Length);
                if (ParamSection != null && ParamSection.Length > 0) ms.Write(ParamSection, 0, ParamSection.Length);
                if (DefaultsAndStrings != null && DefaultsAndStrings.Length > 0) ms.Write(DefaultsAndStrings, 0, DefaultsAndStrings.Length);
                var inv = InvariantTail ?? InvariantVariants[InvariantVariant];
                ms.Write(inv, 0, inv.Length);
                if (Trailer != null && Trailer.Length > 0) ms.Write(Trailer, 0, Trailer.Length);
                return ms.ToArray();
            }
        }

        public void WriteXml(StringBuilder sb, int indent)
        {
            // Opaque byte blobs — base64 is the safest way to keep round-trip
            // fidelity given the SGD2 envelope inside these chunks is not yet
            // fully decoded (see class docs).
            MetaXmlBase.ValueTag(sb, indent, "InvariantVariant", InvariantVariant.ToString());
            MetaXmlBase.StringTag(sb, indent, "ZeroPrefix", Convert.ToBase64String(ZeroPrefix ?? new byte[8]));
            MetaXmlBase.StringTag(sb, indent, "HeaderBlock", Convert.ToBase64String(HeaderBlock ?? Array.Empty<byte>()));
            MetaXmlBase.StringTag(sb, indent, "ParamSection", Convert.ToBase64String(ParamSection ?? Array.Empty<byte>()));
            MetaXmlBase.StringTag(sb, indent, "DefaultsAndStrings", Convert.ToBase64String(DefaultsAndStrings ?? Array.Empty<byte>()));
            MetaXmlBase.StringTag(sb, indent, "InvariantTail", Convert.ToBase64String(InvariantTail ?? Array.Empty<byte>()));
            MetaXmlBase.StringTag(sb, indent, "Trailer", Convert.ToBase64String(Trailer ?? Array.Empty<byte>()));
        }

        public void ReadXml(XmlNode node)
        {
            InvariantVariant = (byte)Xml.GetChildUIntAttribute(node, "InvariantVariant");
            ZeroPrefix = DecodeB64(Xml.GetChildInnerText(node, "ZeroPrefix"));
            if (ZeroPrefix == null || ZeroPrefix.Length != 8)
            {
                var z = new byte[8];
                if (ZeroPrefix != null) Array.Copy(ZeroPrefix, z, Math.Min(8, ZeroPrefix.Length));
                ZeroPrefix = z;
            }
            HeaderBlock = DecodeB64(Xml.GetChildInnerText(node, "HeaderBlock")) ?? Array.Empty<byte>();
            ParamSection = DecodeB64(Xml.GetChildInnerText(node, "ParamSection")) ?? Array.Empty<byte>();
            DefaultsAndStrings = DecodeB64(Xml.GetChildInnerText(node, "DefaultsAndStrings")) ?? Array.Empty<byte>();
            InvariantTail = DecodeB64(Xml.GetChildInnerText(node, "InvariantTail"));
            Trailer = DecodeB64(Xml.GetChildInnerText(node, "Trailer")) ?? Array.Empty<byte>();
        }

        internal static byte[] DecodeB64(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            return Convert.FromBase64String(s.Trim());
        }
    }

    [TC(typeof(EXP))]
    public class AwcEffect
    {
        public string Name { get; set; }

        // Per FxdbEffect.DataBufferSize (SGD1 sibling). Bounds the per-effect
        // data region; correlates loosely with the total size of the records
        // packed into PreProplstRegion + PropEntries + strings.
        public uint DataBufferSize { get; set; }

        // Deprecated alias — old code referenced this as "UnknownU32" before
        // we identified it as DataBufferSize via the SGD1 layout. Kept for
        // backward compat; setting one updates the other.
        [Browsable(false)]
        public uint UnknownU32
        {
            get => DataBufferSize;
            set => DataBufferSize = value;
        }

        public uint[] VsIndices { get; set; }
        public uint[] PsIndices { get; set; }
        public uint[] GsIndices { get; set; }
        public uint[] DsIndices { get; set; }
        public uint[] HsIndices { get; set; }
        public uint[] CsIndices { get; set; }

        // The per-effect data region between the 6 stage-index arrays and the
        // proplst marker. Conceptually corresponds to SGD1's FxdbEffect
        // body: UnkStruct[72] + Unk1 (u64) + state-block arrays
        // (RasterizerStates, DepthStencilStates, BlendStates, SamplerStates)
        // + Techniques[] + RenderShaderSets[] + Unks[][] + SamplerStateRefs[]
        // + ShaderData + PropertiesShaderData(header). The SGD2 envelope
        // differs from SGD1 in non-trivial ways (see AwcEffectPreProplstRegion
        // doc) so the region is held as a structurally-split raw-byte split
        // until the SGD2-specific decoder is finished. Round-trips
        // byte-identical via the preserved OriginalRawBytes fast path.
        public AwcEffectPreProplstRegion PreProplstRegion { get; set; }
        public AwcEffectPropEntry[] PropEntries { get; set; }
        public string[] Strings { get; set; }
        public string[] SamplerNames { get; set; }
        public AwcEffectTechnique[] Techniques { get; set; }

        // 40-byte literal "proplst\0" + count is rebuilt; the preamble bytes
        // here cover any per-effect-specific bytes we want to preserve.
        // Currently unused (the proplst marker is constant) but reserved.
        [Browsable(false)] public byte[] InvariantPreamble { get; set; }

        // Original-on-disk record bytes (from the start of the length-prefixed
        // name through the strings_data block). Preserved so unchanged effects
        // round-trip byte-identical via the fast path in Save().
        [Browsable(false)] public byte[] OriginalRawBytes { get; set; }
        [Browsable(false)] public bool Dirty { get; set; }

        // Decoded state-block arrays — empty/null when the SGD2 layout could
        // not be decoded for this effect (the common case at present; see
        // AwcEffectPreProplstRegion documentation).
        public AwcEffectRasterizerStateDesc[] RasterizerStates { get; set; }
        public AwcEffectDepthStencilStateDesc[] DepthStencilStates { get; set; }
        public AwcEffectBlendStateDesc[] BlendStates { get; set; }
        public AwcEffectSamplerStateDesc[] SamplerStates { get; set; }
        public AwcEffectRenderShaderSetDesc[] RenderShaderSets { get; set; }
        public AwcEffectSamplerStateRef[] SamplerStateRefs { get; set; }
        // FxdbEffect.Unk1 — opaque 64-bit value sitting between UnkStruct and
        // the state-block arrays in SGD1. Present here as a typed placeholder
        // even though we don't currently extract it from the SGD2 envelope.
        public ulong Unk1 { get; set; }

        public int VsCount => VsIndices?.Length ?? 0;
        public int PsCount => PsIndices?.Length ?? 0;
        public int GsCount => GsIndices?.Length ?? 0;
        public int DsCount => DsIndices?.Length ?? 0;
        public int HsCount => HsIndices?.Length ?? 0;
        public int CsCount => CsIndices?.Length ?? 0;
        public int TotalShaderCount => VsCount + PsCount + GsCount + DsCount + HsCount + CsCount;
        public int TechniqueCount => Techniques?.Length ?? 0;
        public int SamplerCount => SamplerNames?.Length ?? 0;
        public int RasterizerStateCount => RasterizerStates?.Length ?? 0;
        public int DepthStencilStateCount => DepthStencilStates?.Length ?? 0;
        public int BlendStateCount => BlendStates?.Length ?? 0;
        public int SamplerStateCount => SamplerStates?.Length ?? 0;
        public int RenderShaderSetCount => RenderShaderSets?.Length ?? 0;

        public void WriteXml(StringBuilder sb, int indent)
        {
            MetaXmlBase.StringTag(sb, indent, "Name", MetaXmlBase.XmlEscape(Name ?? string.Empty));
            MetaXmlBase.ValueTag(sb, indent, "DataBufferSize", DataBufferSize.ToString());
            MetaXmlBase.StringTag(sb, indent, "Unk1", "0x" + Unk1.ToString("X16"));
            WriteUintArray(sb, indent, "VsIndices", VsIndices);
            WriteUintArray(sb, indent, "PsIndices", PsIndices);
            WriteUintArray(sb, indent, "GsIndices", GsIndices);
            WriteUintArray(sb, indent, "DsIndices", DsIndices);
            WriteUintArray(sb, indent, "HsIndices", HsIndices);
            WriteUintArray(sb, indent, "CsIndices", CsIndices);
            WriteStringArray(sb, indent, "SamplerNames", SamplerNames);
            WriteStringArray(sb, indent, "Strings", Strings);

            if (PreProplstRegion != null)
            {
                MetaXmlBase.OpenTag(sb, indent, "PreProplstRegion");
                PreProplstRegion.WriteXml(sb, indent + 1);
                MetaXmlBase.CloseTag(sb, indent, "PreProplstRegion");
            }
            else
            {
                MetaXmlBase.SelfClosingTag(sb, indent, "PreProplstRegion");
            }

            WriteSubArray(sb, indent, "PropEntries", PropEntries);
            WriteSubArray(sb, indent, "Techniques", Techniques);

            // Preserve the original raw record for byte-identical round-trip when
            // the user hasn't mutated anything.
            MetaXmlBase.StringTag(sb, indent, "OriginalRawBytes",
                OriginalRawBytes != null ? Convert.ToBase64String(OriginalRawBytes) : string.Empty);
            MetaXmlBase.StringTag(sb, indent, "InvariantPreamble",
                InvariantPreamble != null ? Convert.ToBase64String(InvariantPreamble) : string.Empty);
        }

        public void ReadXml(XmlNode node)
        {
            Name = Xml.GetChildInnerText(node, "Name");
            DataBufferSize = Xml.GetChildUIntAttribute(node, "DataBufferSize");
            Unk1 = AwcEffectPropBinding.ParseHex64(Xml.GetChildInnerText(node, "Unk1"));

            VsIndices = ReadUintArray(node, "VsIndices");
            PsIndices = ReadUintArray(node, "PsIndices");
            GsIndices = ReadUintArray(node, "GsIndices");
            DsIndices = ReadUintArray(node, "DsIndices");
            HsIndices = ReadUintArray(node, "HsIndices");
            CsIndices = ReadUintArray(node, "CsIndices");

            SamplerNames = ReadStringArray(node, "SamplerNames");
            Strings = ReadStringArray(node, "Strings");

            var preNode = node.SelectSingleNode("PreProplstRegion");
            if (preNode != null && preNode.HasChildNodes)
            {
                PreProplstRegion = new AwcEffectPreProplstRegion();
                PreProplstRegion.ReadXml(preNode);
            }
            else
            {
                PreProplstRegion = null;
            }

            PropEntries = ReadSubArray<AwcEffectPropEntry>(node, "PropEntries");
            Techniques = ReadSubArray<AwcEffectTechnique>(node, "Techniques");

            // Resolve binding names from the strings pool if not present.
            if (PropEntries != null && Strings != null)
            {
                var pool = new Dictionary<uint, string>();
                foreach (var s in Strings)
                {
                    if (string.IsNullOrEmpty(s)) continue;
                    uint h = AwcShaderFile.JoaatLower(s);
                    if (!pool.ContainsKey(h)) pool[h] = s;
                }
                foreach (var pe in PropEntries)
                {
                    if (pe?.Bindings == null) continue;
                    foreach (var b in pe.Bindings)
                    {
                        if (string.IsNullOrEmpty(b.Name) && pool.TryGetValue(b.NameHash, out var nm))
                            b.Name = nm;
                    }
                }
            }

            var orb = Xml.GetChildInnerText(node, "OriginalRawBytes");
            OriginalRawBytes = string.IsNullOrEmpty(orb) ? null : Convert.FromBase64String(orb);
            var ipb = Xml.GetChildInnerText(node, "InvariantPreamble");
            InvariantPreamble = string.IsNullOrEmpty(ipb) ? null : Convert.FromBase64String(ipb);

            // Round-tripped effects re-encode via EncodeEffect by default so any
            // edits in the XML take effect; the OriginalRawBytes path is only
            // preserved for callers that want full fidelity for unedited effects.
            Dirty = OriginalRawBytes == null;

            // Ensure the typed state-block arrays are non-null (we don't yet
            // populate these from XML — the SGD2 decode is incomplete).
            if (RasterizerStates == null) RasterizerStates = Array.Empty<AwcEffectRasterizerStateDesc>();
            if (DepthStencilStates == null) DepthStencilStates = Array.Empty<AwcEffectDepthStencilStateDesc>();
            if (BlendStates == null) BlendStates = Array.Empty<AwcEffectBlendStateDesc>();
            if (SamplerStates == null) SamplerStates = Array.Empty<AwcEffectSamplerStateDesc>();
            if (RenderShaderSets == null) RenderShaderSets = Array.Empty<AwcEffectRenderShaderSetDesc>();
            if (SamplerStateRefs == null) SamplerStateRefs = Array.Empty<AwcEffectSamplerStateRef>();
        }

        private static void WriteUintArray(StringBuilder sb, int indent, string name, uint[] arr)
        {
            if (arr == null || arr.Length == 0)
            {
                MetaXmlBase.SelfClosingTag(sb, indent, name);
                return;
            }
            MetaXmlBase.Indent(sb, indent);
            sb.Append("<").Append(name).Append(">");
            for (int i = 0; i < arr.Length; i++)
            {
                if (i > 0) sb.Append(" ");
                sb.Append(arr[i].ToString());
            }
            sb.Append("</").Append(name).Append(">");
            sb.AppendLine();
        }

        private static uint[] ReadUintArray(XmlNode parent, string name)
        {
            var n = parent.SelectSingleNode(name);
            if (n == null) return Array.Empty<uint>();
            var txt = n.InnerText;
            if (string.IsNullOrWhiteSpace(txt)) return Array.Empty<uint>();
            var parts = txt.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var arr = new uint[parts.Length];
            for (int i = 0; i < parts.Length; i++) arr[i] = uint.Parse(parts[i]);
            return arr;
        }

        private static void WriteStringArray(StringBuilder sb, int indent, string name, string[] arr)
        {
            if (arr == null || arr.Length == 0)
            {
                MetaXmlBase.SelfClosingTag(sb, indent, name);
                return;
            }
            MetaXmlBase.OpenTag(sb, indent, name);
            var ci = indent + 1;
            for (int i = 0; i < arr.Length; i++)
            {
                MetaXmlBase.StringTag(sb, ci, "Item", MetaXmlBase.XmlEscape(arr[i] ?? string.Empty));
            }
            MetaXmlBase.CloseTag(sb, indent, name);
        }

        private static string[] ReadStringArray(XmlNode parent, string name)
        {
            var n = parent.SelectSingleNode(name);
            if (n == null) return Array.Empty<string>();
            var items = n.SelectNodes("Item");
            if (items == null) return Array.Empty<string>();
            var list = new List<string>(items.Count);
            foreach (XmlNode it in items) list.Add(it.InnerText ?? string.Empty);
            return list.ToArray();
        }

        private static void WriteSubArray<T>(StringBuilder sb, int indent, string name, T[] arr) where T : class
        {
            if (arr == null || arr.Length == 0)
            {
                MetaXmlBase.SelfClosingTag(sb, indent, name);
                return;
            }
            MetaXmlBase.OpenTag(sb, indent, name);
            var ci = indent + 1;
            var cci = ci + 1;
            for (int i = 0; i < arr.Length; i++)
            {
                if (arr[i] == null) { MetaXmlBase.SelfClosingTag(sb, ci, "Item"); continue; }
                MetaXmlBase.OpenTag(sb, ci, "Item");
                if (arr[i] is AwcEffectPropEntry pe) pe.WriteXml(sb, cci);
                else if (arr[i] is AwcEffectTechnique tq) tq.WriteXml(sb, cci);
                MetaXmlBase.CloseTag(sb, ci, "Item");
            }
            MetaXmlBase.CloseTag(sb, indent, name);
        }

        private static T[] ReadSubArray<T>(XmlNode parent, string name) where T : class, new()
        {
            var n = parent.SelectSingleNode(name);
            if (n == null) return Array.Empty<T>();
            var items = n.SelectNodes("Item");
            if (items == null) return Array.Empty<T>();
            var list = new List<T>(items.Count);
            foreach (XmlNode it in items)
            {
                var obj = new T();
                if (obj is AwcEffectPropEntry pe) pe.ReadXml(it);
                else if (obj is AwcEffectTechnique tq) tq.ReadXml(it);
                list.Add(obj);
            }
            return list.ToArray();
        }

        public override string ToString() => Name ?? string.Empty;
    }

    [TC(typeof(EXP))]
    public class AwcShaderFile : PackedFile
    {
        public const uint MagicSGD2 = 0x32444753; // "SGD2"

        public string Name { get; set; }
        public RpfFileEntry FileEntry { get; set; }
        public string Magic { get; set; }
        public AwcShader[] VertexShaders { get; set; }
        public AwcShader[] PixelShaders { get; set; }
        public AwcShader[] GeometryShaders { get; set; }
        public AwcShader[] DomainShaders { get; set; }
        public AwcShader[] HullShaders { get; set; }
        public AwcShader[] ComputeShaders { get; set; }

        // Decoded trailer. If parsing fails, Effects is left null and FooterData
        // holds the raw trailer bytes for fallback round-trip.
        public AwcEffect[] Effects { get; set; }
        [Browsable(false)] public byte[] TrailerHeader { get; set; } // 8 zero bytes + u32 effect_count
        public int EffectCount => Effects?.Length ?? 0;

        [Browsable(false)] public byte[] FooterData { get; set; }

        public int VertexCount   => VertexShaders?.Length ?? 0;
        public int PixelCount    => PixelShaders?.Length ?? 0;
        public int GeometryCount => GeometryShaders?.Length ?? 0;
        public int DomainCount   => DomainShaders?.Length ?? 0;
        public int HullCount     => HullShaders?.Length ?? 0;
        public int ComputeCount  => ComputeShaders?.Length ?? 0;
        public int TotalShaderCount => VertexCount + PixelCount + GeometryCount + DomainCount + HullCount + ComputeCount;

        public IEnumerable<AwcShader> AllShaders()
        {
            if (VertexShaders   != null) foreach (var s in VertexShaders)   yield return s;
            if (PixelShaders    != null) foreach (var s in PixelShaders)    yield return s;
            if (GeometryShaders != null) foreach (var s in GeometryShaders) yield return s;
            if (DomainShaders   != null) foreach (var s in DomainShaders)   yield return s;
            if (HullShaders     != null) foreach (var s in HullShaders)     yield return s;
            if (ComputeShaders  != null) foreach (var s in ComputeShaders)  yield return s;
        }

        public void Load(byte[] data, RpfFileEntry entry)
        {
            FileEntry = entry;
            Name = entry?.Name;

            if (data == null || data.Length < 4)
                throw new InvalidDataException("AWC Shader Library: empty data.");

            if (BitConverter.ToUInt32(data, 0) != MagicSGD2)
                throw new InvalidDataException("AWC Shader Library: not an SGD2 file.");

            using (var ms = new MemoryStream(data))
            using (var br = new BinaryReader(ms, Encoding.GetEncoding("ISO-8859-1")))
            {
                Magic = Encoding.ASCII.GetString(br.ReadBytes(4));

                VertexShaders   = ReadShaderArray(br, AwcShaderStage.Vertex);
                PixelShaders    = ReadShaderArray(br, AwcShaderStage.Pixel);
                GeometryShaders = ReadShaderArray(br, AwcShaderStage.Geometry);
                DomainShaders   = ReadShaderArray(br, AwcShaderStage.Domain);
                HullShaders     = ReadShaderArray(br, AwcShaderStage.Hull);
                ComputeShaders  = ReadShaderArray(br, AwcShaderStage.Compute);

                long remaining = ms.Length - ms.Position;
                byte[] trailerBytes = remaining > 0 ? br.ReadBytes((int)remaining) : Array.Empty<byte>();
                TryParseTrailer(trailerBytes);
            }
        }

        private void TryParseTrailer(byte[] trailerBytes)
        {
            FooterData = trailerBytes;
            Effects = null;
            TrailerHeader = null;

            if (trailerBytes == null || trailerBytes.Length < 12) return;
            try
            {
                // 8 reserved zero bytes + u32 effect_count.
                uint count = BitConverter.ToUInt32(trailerBytes, 8);
                if (count == 0 || count > 1000000) return;

                var header = new byte[12];
                Array.Copy(trailerBytes, 0, header, 0, 12);

                var effects = new List<AwcEffect>((int)count);
                int pos = 12;
                for (uint i = 0; i < count; i++)
                {
                    if (pos >= trailerBytes.Length) return; // premature EOF
                    int startPos = pos;
                    AwcEffect eff = ParseEffect(trailerBytes, ref pos);
                    if (eff == null) return;
                    int len = pos - startPos;
                    eff.OriginalRawBytes = new byte[len];
                    Array.Copy(trailerBytes, startPos, eff.OriginalRawBytes, 0, len);
                    effects.Add(eff);
                }

                if (pos != trailerBytes.Length) return; // didn't consume cleanly — fall back

                TrailerHeader = header;
                Effects = effects.ToArray();
                FooterData = null; // structured path now owns the trailer
            }
            catch
            {
                // Defensive: keep FooterData so the file still round-trips via the
                // opaque-bytes fast path. Effects stays null and the UI falls
                // back to the flat-list view.
                Effects = null;
                TrailerHeader = null;
                FooterData = trailerBytes;
            }
        }

        private static AwcEffect ParseEffect(byte[] trailer, ref int pos)
        {
            int n = trailer.Length;
            if (pos >= n) throw new InvalidDataException("effect: past end");

            byte nameLenPlusOne = trailer[pos];
            if (nameLenPlusOne < 2) throw new InvalidDataException("effect: name_len_plus_one < 2");
            int nameStart = pos + 1;
            int nulPos = pos + nameLenPlusOne;
            if (nulPos >= n) throw new InvalidDataException("effect: name past end");
            if (trailer[nulPos] != 0) throw new InvalidDataException("effect: missing NUL after name");
            string name = Encoding.GetEncoding("ISO-8859-1").GetString(trailer, nameStart, nameLenPlusOne - 1);
            int p = nulPos + 1;

            if (p + 4 > n) throw new InvalidDataException("effect: truncated data_buffer_size");
            uint dataBufferSize = BitConverter.ToUInt32(trailer, p);
            p += 4;

            uint[] vs = ReadStage(trailer, ref p, n);
            uint[] ps = ReadStage(trailer, ref p, n);
            uint[] gs = ReadStage(trailer, ref p, n);
            uint[] ds = ReadStage(trailer, ref p, n);
            uint[] hs = ReadStage(trailer, ref p, n);
            uint[] cs = ReadStage(trailer, ref p, n);

            // Find the 'proplst\0' marker (structurally unique in the effect blob).
            int blobStart = p;
            int markerIdx = IndexOf(trailer, ProplstMarker, blobStart);
            if (markerIdx < 0) throw new InvalidDataException("effect '" + name + "': proplst marker not found");

            byte[] preProplstBytes = new byte[markerIdx - blobStart];
            Array.Copy(trailer, blobStart, preProplstBytes, 0, preProplstBytes.Length);
            var preRegion = ParsePreProplst(preProplstBytes);

            p = markerIdx + ProplstMarker.Length;

            if (p + 4 > n) throw new InvalidDataException("effect '" + name + "': truncated prop_entry_count");
            uint propCount = BitConverter.ToUInt32(trailer, p);
            p += 4;
            if (propCount > 1000000) throw new InvalidDataException("effect '" + name + "': prop_count out of range");

            var propEntries = new AwcEffectPropEntry[propCount];
            for (uint i = 0; i < propCount; i++)
            {
                if (p + 4 > n) throw new InvalidDataException("effect '" + name + "': truncated entry size");
                uint entrySize = BitConverter.ToUInt32(trailer, p);
                p += 4;
                if (entrySize < 24 || entrySize > 1000000 || p + entrySize > n)
                    throw new InvalidDataException("effect '" + name + "': prop entry size out of range");
                propEntries[i] = ParsePropEntry(trailer, p, (int)entrySize);
                p += (int)entrySize;
            }

            if (p + 4 > n) throw new InvalidDataException("effect '" + name + "': truncated strings_size");
            uint stringsSize = BitConverter.ToUInt32(trailer, p);
            p += 4;
            if (stringsSize > 10000000 || p + stringsSize > n)
                throw new InvalidDataException("effect '" + name + "': strings_size out of range");
            byte[] stringsData = new byte[stringsSize];
            Array.Copy(trailer, p, stringsData, 0, (int)stringsSize);
            p += (int)stringsSize;

            // Parse null-terminated tokens, drop empty (leading/trailing NULs).
            var strings = new List<string>();
            int sp = 0;
            var enc = Encoding.GetEncoding("ISO-8859-1");
            while (sp < stringsData.Length)
            {
                int sStart = sp;
                while (sp < stringsData.Length && stringsData[sp] != 0) sp++;
                int sLen = sp - sStart;
                if (sLen > 0) strings.Add(enc.GetString(stringsData, sStart, sLen));
                if (sp < stringsData.Length) sp++; // skip the NUL
            }

            // Resolve binding names from hashes; collect referenced hashes for split.
            var pool = new Dictionary<uint, string>();
            foreach (var s in strings)
            {
                uint h = JoaatLower(s);
                if (!pool.ContainsKey(h)) pool[h] = s;
            }
            var referenced = new HashSet<uint>();
            foreach (var entry in propEntries)
            {
                if (entry.Bindings == null) continue;
                foreach (var b in entry.Bindings)
                {
                    string nm;
                    b.Name = pool.TryGetValue(b.NameHash, out nm) ? nm : null;
                    referenced.Add(b.NameHash);
                }
            }

            string[] samplerNames;
            AwcEffectTechnique[] techniques;
            SplitTechniques(strings, referenced, out samplerNames, out techniques);

            pos = p;
            return new AwcEffect
            {
                Name = name,
                DataBufferSize = dataBufferSize,
                VsIndices = vs, PsIndices = ps, GsIndices = gs,
                DsIndices = ds, HsIndices = hs, CsIndices = cs,
                PreProplstRegion = preRegion,
                PropEntries = propEntries,
                Strings = strings.ToArray(),
                SamplerNames = samplerNames,
                Techniques = techniques,
                RasterizerStates = Array.Empty<AwcEffectRasterizerStateDesc>(),
                DepthStencilStates = Array.Empty<AwcEffectDepthStencilStateDesc>(),
                BlendStates = Array.Empty<AwcEffectBlendStateDesc>(),
                SamplerStates = Array.Empty<AwcEffectSamplerStateDesc>(),
                RenderShaderSets = Array.Empty<AwcEffectRenderShaderSetDesc>(),
                SamplerStateRefs = Array.Empty<AwcEffectSamplerStateRef>(),
            };
        }

        private static readonly byte[] ProplstMarker = new byte[] { (byte)'p', (byte)'r', (byte)'o', (byte)'p', (byte)'l', (byte)'s', (byte)'t', 0 };

        private static uint[] ReadStage(byte[] trailer, ref int p, int n)
        {
            if (p + 4 > n) throw new InvalidDataException("stage: truncated count");
            uint cnt = BitConverter.ToUInt32(trailer, p);
            p += 4;
            if (cnt > 100000) throw new InvalidDataException("stage count out of range");
            var arr = new uint[cnt];
            if (cnt == 0) return arr;
            if (p + 4 * cnt > n) throw new InvalidDataException("stage: truncated indices");
            for (uint i = 0; i < cnt; i++)
            {
                arr[i] = BitConverter.ToUInt32(trailer, p);
                p += 4;
            }
            return arr;
        }

        private static int IndexOf(byte[] haystack, byte[] needle, int start)
        {
            int end = haystack.Length - needle.Length;
            for (int i = start; i <= end; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j]) { match = false; break; }
                }
                if (match) return i;
            }
            return -1;
        }

        private static int LastIndexOf(byte[] haystack, byte[] needle)
        {
            for (int i = haystack.Length - needle.Length; i >= 0; i--)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j]) { match = false; break; }
                }
                if (match) return i;
            }
            return -1;
        }

        private static AwcEffectPropEntry ParsePropEntry(byte[] data, int offset, int size)
        {
            ulong entryHash = BitConverter.ToUInt64(data, offset);
            uint countWord = BitConverter.ToUInt32(data, offset + 8);
            uint flags2 = BitConverter.ToUInt32(data, offset + 12);
            int bindingCount = (int)(countWord & 0xFF);
            uint flags1 = countWord >> 8;
            // bytes [16:24] reserved zero.
            int expected = 24 + bindingCount * 8;
            if (expected > size) throw new InvalidDataException("PropEntry size mismatch");
            var bindings = new AwcEffectPropBinding[bindingCount];
            for (int i = 0; i < bindingCount; i++)
            {
                int boff = offset + 24 + i * 8;
                bindings[i] = new AwcEffectPropBinding
                {
                    NameHash = BitConverter.ToUInt32(data, boff),
                    Tag = BitConverter.ToUInt32(data, boff + 4),
                };
            }
            return new AwcEffectPropEntry
            {
                EntryHash = entryHash,
                Flags1 = flags1,
                Flags2 = flags2,
                Bindings = bindings,
            };
        }

        private static AwcEffectPreProplstRegion ParsePreProplst(byte[] data)
        {
            int n = data.Length;
            if (n < 8 + 40 + 1) throw new InvalidDataException("pre_proplst too short");
            for (int i = 0; i < 8; i++)
                if (data[i] != 0) throw new InvalidDataException("pre_proplst: missing 8B zero prefix");

            int idx = LastIndexOf(data, AwcEffectPreProplstRegion.PropLstHashDup);
            if (idx < 0) throw new InvalidDataException("pre_proplst: hash-dup anchor not found");
            int invStart = idx - 32;
            if (invStart < 8) throw new InvalidDataException("pre_proplst: invariant tail too close to start");
            byte[] inv40 = new byte[40];
            Array.Copy(data, invStart, inv40, 0, 40);

            byte variant = 255;
            for (byte v = 0; v < AwcEffectPreProplstRegion.InvariantVariants.Length; v++)
            {
                var cand = AwcEffectPreProplstRegion.InvariantVariants[v];
                bool eq = true;
                for (int i = 0; i < 40; i++)
                {
                    if (cand[i] != inv40[i]) { eq = false; break; }
                }
                if (eq) { variant = v; break; }
            }
            if (variant == 255) throw new InvalidDataException("pre_proplst: unrecognized invariant variant");

            // Trailer: 0..2 (u32 hash, u32 count) pairs followed by a 0x00 byte.
            int trailerStart = idx + 8;
            int trailerLen = n - trailerStart;
            if (trailerLen < 1 || data[n - 1] != 0)
                throw new InvalidDataException("pre_proplst: trailer not NUL-terminated");
            int extraLen = trailerLen - 1;
            if ((extraLen & 7) != 0) throw new InvalidDataException("pre_proplst: trailer extra not multiple of 8");

            byte[] trailer = new byte[trailerLen];
            Array.Copy(data, trailerStart, trailer, 0, trailerLen);

            // Sub-divide middle (data[8..invStart]) into header_block, param_section,
            // defaults_and_strings via the longest valid chain of param/cbuf records.
            var pool = CollectPreProplstNamePool(data);
            int bestStart = -1, bestEnd = -1, bestLen = 0;
            for (int start = 8; start < invStart; start++)
            {
                if (!IsPreProplstParamRecord(data, start, pool)) continue;
                int p = start;
                int chainLen = 0;
                while (p < invStart)
                {
                    if (IsPreProplstParamRecord(data, p, pool)) { p += 28; chainLen++; }
                    else if (IsPreProplstCbufRecord(data, p, pool)) { p += 24; chainLen++; }
                    else break;
                }
                if (chainLen > bestLen) { bestStart = start; bestEnd = p; bestLen = chainLen; }
            }

            byte[] headerBlock, paramSection, defaultsAndStrings;
            if (bestStart < 0)
            {
                headerBlock = new byte[invStart - 8];
                Array.Copy(data, 8, headerBlock, 0, headerBlock.Length);
                paramSection = Array.Empty<byte>();
                defaultsAndStrings = Array.Empty<byte>();
            }
            else
            {
                headerBlock = new byte[bestStart - 8];
                Array.Copy(data, 8, headerBlock, 0, headerBlock.Length);
                paramSection = new byte[bestEnd - bestStart];
                Array.Copy(data, bestStart, paramSection, 0, paramSection.Length);
                defaultsAndStrings = new byte[invStart - bestEnd];
                Array.Copy(data, bestEnd, defaultsAndStrings, 0, defaultsAndStrings.Length);
            }

            return new AwcEffectPreProplstRegion
            {
                ZeroPrefix = new byte[8],
                HeaderBlock = headerBlock,
                ParamSection = paramSection,
                DefaultsAndStrings = defaultsAndStrings,
                InvariantTail = inv40,
                Trailer = trailer,
                InvariantVariant = variant,
            };
        }

        private static Dictionary<uint, string> CollectPreProplstNamePool(byte[] data)
        {
            var pool = new Dictionary<uint, string>();
            int n = data.Length;
            int p = 0;
            while (p < n)
            {
                if (data[p] >= 32 && data[p] < 127)
                {
                    int start = p;
                    while (p < n && data[p] >= 32 && data[p] < 127) p++;
                    if (p < n && data[p] == 0 && (p - start) >= 2)
                    {
                        string tok = Encoding.GetEncoding("ISO-8859-1").GetString(data, start, p - start);
                        uint h = JoaatLower(tok);
                        if (!pool.ContainsKey(h)) pool[h] = tok;
                        if (!tok.StartsWith("_"))
                        {
                            string und = "_" + tok;
                            uint hu = JoaatLower(und);
                            if (!pool.ContainsKey(hu)) pool[hu] = und;
                        }
                    }
                }
                else
                {
                    p++;
                }
            }
            uint hp = JoaatLower("proplst");
            if (!pool.ContainsKey(hp)) pool[hp] = "proplst";
            return pool;
        }

        private static bool IsPreProplstParamRecord(byte[] data, int p, Dictionary<uint, string> pool)
        {
            if (p + 28 > data.Length) return false;
            ushort rtype = BitConverter.ToUInt16(data, p);
            byte high = (byte)((rtype >> 8) & 0xFF);
            if (high != 0x01 && high != 0x02 && high != 0x03 && high != 0x04) return false;
            if (BitConverter.ToUInt32(data, p + 16) != 0) return false;
            uint h1 = BitConverter.ToUInt32(data, p + 20);
            uint h2 = BitConverter.ToUInt32(data, p + 24);
            if (h1 == 0 || h2 == 0) return false;
            string n1, n2;
            if (!pool.TryGetValue(h1, out n1)) return false;
            if (!pool.TryGetValue(h2, out n2)) return false;
            if (h1 != h2)
            {
                if (n1 != "_" + n2) return false;
            }
            return true;
        }

        private static bool IsPreProplstCbufRecord(byte[] data, int p, Dictionary<uint, string> pool)
        {
            if (p + 24 > data.Length) return false;
            ushort t = BitConverter.ToUInt16(data, p);
            if (t > 14) return false;
            uint h1 = BitConverter.ToUInt32(data, p + 16);
            uint h2 = BitConverter.ToUInt32(data, p + 20);
            if (h1 == 0 || h1 != h2) return false;
            if (!pool.ContainsKey(h1)) return false;
            return true;
        }

        private static readonly System.Text.RegularExpressions.Regex PassNameRe =
            new System.Text.RegularExpressions.Regex(@"^[pP]\d+(?:_\w+)?$", System.Text.RegularExpressions.RegexOptions.Compiled);

        private static void SplitTechniques(
            List<string> strings, HashSet<uint> referenced,
            out string[] samplerNames, out AwcEffectTechnique[] techniques)
        {
            if (strings == null || strings.Count == 0)
            {
                samplerNames = Array.Empty<string>();
                techniques = Array.Empty<AwcEffectTechnique>();
                return;
            }

            // Trailing run of pN pass names.
            int passStart = strings.Count;
            while (passStart > 0 && PassNameRe.IsMatch(strings[passStart - 1])) passStart--;
            var passTokens = strings.GetRange(passStart, strings.Count - passStart);

            // Leading run of names referenced as bindings.
            int samplerEnd = 0;
            while (samplerEnd < passStart && referenced.Contains(JoaatLower(strings[samplerEnd]))) samplerEnd++;

            samplerNames = strings.GetRange(0, samplerEnd).ToArray();
            var techNames = strings.GetRange(samplerEnd, passStart - samplerEnd);

            var techsList = new List<AwcEffectTechnique>();
            if (techNames.Count == 0)
            {
                techniques = Array.Empty<AwcEffectTechnique>();
                return;
            }

            if (passTokens.Count > 0)
            {
                foreach (var tn in techNames)
                {
                    var passes = new AwcEffectPass[passTokens.Count];
                    for (int i = 0; i < passTokens.Count; i++) passes[i] = new AwcEffectPass { Name = passTokens[i] };
                    techsList.Add(new AwcEffectTechnique { Name = tn, Passes = passes });
                }
            }
            else if (techNames.Count >= 2)
            {
                var passes = new AwcEffectPass[techNames.Count - 1];
                for (int i = 1; i < techNames.Count; i++) passes[i - 1] = new AwcEffectPass { Name = techNames[i] };
                techsList.Add(new AwcEffectTechnique { Name = techNames[0], Passes = passes });
            }
            else
            {
                techsList.Add(new AwcEffectTechnique { Name = techNames[0], Passes = new[] { new AwcEffectPass { Name = "p0" } } });
            }

            techniques = techsList.ToArray();
        }

        // Rage Jenkins-one-at-a-time hash, lowercased input. Equivalent to
        // JenkHash.GenHash(text.ToLowerInvariant()) but doesn't allocate when
        // the string is already lowercase.
        public static uint JoaatLower(string s)
        {
            if (s == null) return 0;
            uint h = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c >= 'A' && c <= 'Z') c = (char)(c + 32);
                h += (byte)c;
                h += (h << 10);
                h ^= (h >> 6);
            }
            h += (h << 3);
            h ^= (h >> 11);
            h += (h << 15);
            return h;
        }

        private static AwcShader[] ReadShaderArray(BinaryReader br, AwcShaderStage stage)
        {
            uint count = br.ReadUInt32();
            var arr = new AwcShader[count];
            for (uint i = 0; i < count; i++)
                arr[i] = ReadShader(br, stage);
            return arr;
        }

        private static AwcShader ReadShader(BinaryReader br, AwcShaderStage stage)
        {
            byte slen = br.ReadByte();
            byte[] nameBytes = br.ReadBytes(slen);
            string name = Encoding.GetEncoding("ISO-8859-1").GetString(nameBytes).TrimEnd('\0');

            byte wave = br.ReadByte();
            uint size = br.ReadUInt32();
            byte[] binary = br.ReadBytes((int)size);
            ulong hash = br.ReadUInt64();
            byte[] rootSig = br.ReadBytes(144);
            uint blockSize = br.ReadUInt32();

            long blockStart = br.BaseStream.Position;
            byte[] blockData = br.ReadBytes((int)blockSize);

            // Re-parse the metadata block from blockData so the whole shader
            // stays self-contained (no further seeks into the outer stream).
            var (regCount, cbCount, texCount, blockSizeCopy, registers) = ParseBlock(blockData);

            return new AwcShader
            {
                Name = name,
                NameLengthByte = slen,
                NameBytes = nameBytes,
                WaveSize = wave,
                Size = size,
                Binary = binary,
                Hash = hash,
                RootSigData = rootSig,
                BlockSize = blockSize,
                BlockSizeCopy = blockSizeCopy,
                RegCount = regCount,
                CBufferCount = cbCount,
                TexCount = texCount,
                Registers = registers,
                OriginalBlockData = blockData,
                Stage = stage,
            };
        }

        private static (ushort reg, ushort cb, ushort tex, ushort blkSizeCopy, AwcShaderRegister[] regs) ParseBlock(byte[] block)
        {
            using (var ms = new MemoryStream(block))
            using (var br = new BinaryReader(ms))
            {
                ushort regCount = br.ReadUInt16();
                ushort cbCount  = br.ReadUInt16();
                ushort texCount = br.ReadUInt16();
                ushort blkCopy  = br.ReadUInt16();

                var regs = new AwcShaderRegister[regCount];
                for (int i = 0; i < regCount; i++)
                    regs[i] = ParseRegister(ms, br);

                return (regCount, cbCount, texCount, blkCopy, regs);
            }
        }

        private static AwcShaderRegister ParseRegister(MemoryStream ms, BinaryReader br)
        {
            long headerStart = ms.Position;

            ushort resType        = br.ReadUInt16();
            ushort regSlot        = br.ReadUInt16();
            byte   cbCount        = br.ReadByte();
            byte   numDesc        = br.ReadByte();
            byte   regSpace       = br.ReadByte();
            byte   reserved       = br.ReadByte();
            ushort cbDataOffset   = br.ReadUInt16();
            ushort regStringOff   = br.ReadUInt16();

            long afterHeader = ms.Position; // headerStart + 12
            byte[] extra = br.ReadBytes(16);

            // Offsets in the binary are relative to headerStart.
            string regName;
            long savedPos = ms.Position;
            ms.Position = headerStart + regStringOff;
            regName = ReadCString(br);
            ms.Position = savedPos;

            int validCb = cbDataOffset != 0 ? cbCount : 0;
            AwcShaderCBufferData[] cbs;
            if (validCb > 0)
            {
                cbs = new AwcShaderCBufferData[validCb];
                ms.Position = headerStart + cbDataOffset;
                for (int i = 0; i < validCb; i++)
                    cbs[i] = ParseCBufferData(ms, br);
            }
            else
            {
                cbs = Array.Empty<AwcShaderCBufferData>();
            }

            // Move past the 16-byte extra data area, ready for the next register.
            ms.Position = afterHeader + 16;

            return new AwcShaderRegister
            {
                ResourceType = (AwcShaderResourceType)resType,
                RegisterSlot = regSlot,
                CBufferCount = cbCount,
                NumDescriptors = numDesc,
                RegisterSpace = regSpace,
                Reserved = reserved,
                CBufferDataOffset = cbDataOffset,
                RegStringOffset = regStringOff,
                ExtraData = extra,
                Name = regName,
                CBuffers = cbs,
            };
        }

        private static AwcShaderCBufferData ParseCBufferData(MemoryStream ms, BinaryReader br)
        {
            long start = ms.Position;
            ushort type        = br.ReadUInt16();
            ushort arraySize   = br.ReadUInt16();
            ushort packOffset  = br.ReadUInt16();
            uint   nameOffset  = br.ReadUInt32();

            string cbName;
            long savedPos = ms.Position;
            ms.Position = start + nameOffset;
            cbName = ReadCString(br);
            ms.Position = savedPos;

            byte[] nameHashData = br.ReadBytes(14);

            return new AwcShaderCBufferData
            {
                Type = (AwcShaderValueType)type,
                ArraySize = arraySize,
                PackOffset = packOffset,
                NameOffset = nameOffset,
                Name = cbName,
                NameHashData = nameHashData,
            };
        }

        private static string ReadCString(BinaryReader br)
        {
            var sb = new StringBuilder();
            while (br.BaseStream.Position < br.BaseStream.Length)
            {
                byte b = br.ReadByte();
                if (b == 0) break;
                sb.Append((char)b);
            }
            return sb.ToString();
        }

        // ---------- Save ----------

        public byte[] Save()
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(MagicSGD2);

                WriteShaderArray(bw, VertexShaders);
                WriteShaderArray(bw, PixelShaders);
                WriteShaderArray(bw, GeometryShaders);
                WriteShaderArray(bw, DomainShaders);
                WriteShaderArray(bw, HullShaders);
                WriteShaderArray(bw, ComputeShaders);

                if (Effects != null)
                {
                    WriteTrailer(bw);
                }
                else if (FooterData != null && FooterData.Length > 0)
                {
                    bw.Write(FooterData);
                }

                return ms.ToArray();
            }
        }

        private void WriteTrailer(BinaryWriter bw)
        {
            // 8 zero bytes + u32 effect_count. Reuse TrailerHeader verbatim if
            // present (preserves any non-zero bits we didn't decode).
            if (TrailerHeader != null && TrailerHeader.Length == 12)
            {
                bw.Write(TrailerHeader);
            }
            else
            {
                for (int i = 0; i < 8; i++) bw.Write((byte)0);
                bw.Write((uint)Effects.Length);
            }
            foreach (var eff in Effects)
            {
                if (!eff.Dirty && eff.OriginalRawBytes != null)
                {
                    bw.Write(eff.OriginalRawBytes);
                }
                else
                {
                    bw.Write(EncodeEffect(eff));
                }
            }
        }

        private static byte[] EncodeEffect(AwcEffect eff)
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                var enc = Encoding.GetEncoding("ISO-8859-1");
                byte[] nameBytes = enc.GetBytes(eff.Name ?? string.Empty);
                bw.Write((byte)(nameBytes.Length + 1));
                bw.Write(nameBytes);
                bw.Write((byte)0);
                bw.Write(eff.DataBufferSize);

                WriteIdxArray(bw, eff.VsIndices);
                WriteIdxArray(bw, eff.PsIndices);
                WriteIdxArray(bw, eff.GsIndices);
                WriteIdxArray(bw, eff.DsIndices);
                WriteIdxArray(bw, eff.HsIndices);
                WriteIdxArray(bw, eff.CsIndices);

                byte[] preBytes = eff.PreProplstRegion != null
                    ? eff.PreProplstRegion.Encode()
                    : Array.Empty<byte>();
                bw.Write(preBytes);

                bw.Write(ProplstMarker);

                var entries = eff.PropEntries ?? Array.Empty<AwcEffectPropEntry>();
                bw.Write((uint)entries.Length);
                foreach (var entry in entries)
                {
                    byte[] body = EncodePropEntry(entry);
                    bw.Write((uint)body.Length);
                    bw.Write(body);
                }

                // strings: leading NUL + each token + trailing NUL per token.
                using (var sms = new MemoryStream())
                {
                    sms.WriteByte(0);
                    if (eff.Strings != null)
                    {
                        foreach (var s in eff.Strings)
                        {
                            byte[] sb = enc.GetBytes(s ?? string.Empty);
                            sms.Write(sb, 0, sb.Length);
                            sms.WriteByte(0);
                        }
                    }
                    byte[] strData = sms.ToArray();
                    bw.Write((uint)strData.Length);
                    bw.Write(strData);
                }

                return ms.ToArray();
            }
        }

        private static void WriteIdxArray(BinaryWriter bw, uint[] arr)
        {
            uint cnt = (uint)(arr?.Length ?? 0);
            bw.Write(cnt);
            if (arr == null) return;
            for (int i = 0; i < arr.Length; i++) bw.Write(arr[i]);
        }

        private static byte[] EncodePropEntry(AwcEffectPropEntry pe)
        {
            int bindingCount = pe.Bindings?.Length ?? 0;
            byte[] buf = new byte[24 + bindingCount * 8];
            BitConverter.GetBytes(pe.EntryHash).CopyTo(buf, 0);
            uint countWord = ((uint)bindingCount & 0xFF) | ((pe.Flags1 & 0xFFFFFF) << 8);
            BitConverter.GetBytes(countWord).CopyTo(buf, 8);
            BitConverter.GetBytes(pe.Flags2).CopyTo(buf, 12);
            // bytes [16:24] reserved zero (already zero).
            if (pe.Bindings != null)
            {
                for (int i = 0; i < pe.Bindings.Length; i++)
                {
                    BitConverter.GetBytes(pe.Bindings[i].NameHash).CopyTo(buf, 24 + i * 8);
                    BitConverter.GetBytes(pe.Bindings[i].Tag).CopyTo(buf, 28 + i * 8);
                }
            }
            return buf;
        }

        private static void WriteShaderArray(BinaryWriter bw, AwcShader[] arr)
        {
            uint count = (uint)(arr?.Length ?? 0);
            bw.Write(count);
            if (arr == null) return;
            for (int i = 0; i < arr.Length; i++)
                WriteShader(bw, arr[i]);
        }

        private static void WriteShader(BinaryWriter bw, AwcShader s)
        {
            // Preserve original name bytes/length (avoids Latin-1 round-trip risk).
            if (s.NameBytes != null)
            {
                bw.Write(s.NameLengthByte);
                bw.Write(s.NameBytes);
            }
            else
            {
                byte[] nb = Encoding.GetEncoding("ISO-8859-1").GetBytes(s.Name ?? string.Empty);
                byte[] nbz = new byte[nb.Length + 1];
                Array.Copy(nb, nbz, nb.Length);
                bw.Write((byte)nbz.Length);
                bw.Write(nbz);
            }

            bw.Write(s.WaveSize);
            uint binSize = (uint)(s.Binary?.Length ?? 0);
            bw.Write(binSize);
            if (binSize > 0) bw.Write(s.Binary);

            bw.Write(s.Hash);
            bw.Write(s.RootSigData ?? new byte[144]);

            byte[] block;
            if (s.MetadataDirty)
            {
                block = BuildMetadataBlock(s);
            }
            else if (s.OriginalBlockData != null)
            {
                block = s.OriginalBlockData;
            }
            else
            {
                block = BuildMetadataBlock(s);
            }

            bw.Write((uint)block.Length);
            bw.Write(block);
        }

        // Unused while MetadataDirty stays false (binary-only imports), but kept
        // so the data model is round-trippable end-to-end.
        private static byte[] BuildMetadataBlock(AwcShader s)
        {
            var regs = s.Registers ?? Array.Empty<AwcShaderRegister>();
            ushort regCount = (ushort)regs.Length;
            ushort cbCountTotal = 0;
            for (int i = 0; i < regs.Length; i++)
                cbCountTotal += (ushort)(regs[i].CBuffers?.Length ?? 0);

            var buf = new List<byte>(256);

            void WriteU16(ushort v) { buf.Add((byte)(v & 0xFF)); buf.Add((byte)(v >> 8)); }
            void WriteU32At(int pos, uint v) { buf[pos] = (byte)(v & 0xFF); buf[pos + 1] = (byte)((v >> 8) & 0xFF); buf[pos + 2] = (byte)((v >> 16) & 0xFF); buf[pos + 3] = (byte)((v >> 24) & 0xFF); }
            void WriteU16At(int pos, ushort v) { buf[pos] = (byte)(v & 0xFF); buf[pos + 1] = (byte)(v >> 8); }

            WriteU16(regCount);
            WriteU16(cbCountTotal);
            WriteU16(s.TexCount);
            WriteU16(0); // block_size_copy placeholder

            const int headerSize = 8;
            int regHeadersStart = headerSize;
            int regHeadersSize = regCount * 28;
            for (int i = 0; i < regHeadersSize; i++) buf.Add(0);

            int[] cbStructPos = new int[regCount];
            for (int i = 0; i < regs.Length; i++)
            {
                var cbs = regs[i].CBuffers;
                if (cbs != null && cbs.Length > 0)
                {
                    cbStructPos[i] = buf.Count;
                    for (int k = 0; k < cbs.Length * 24; k++) buf.Add(0);
                }
                else cbStructPos[i] = 0;
            }

            int[] regStringPos = new int[regCount];
            for (int i = 0; i < regs.Length; i++)
            {
                regStringPos[i] = buf.Count;
                byte[] nb = Encoding.GetEncoding("ISO-8859-1").GetBytes(regs[i].Name ?? string.Empty);
                buf.AddRange(nb);
                buf.Add(0);
            }

            for (int i = 0; i < regs.Length; i++)
            {
                var cbs = regs[i].CBuffers;
                if (cbs == null || cbs.Length == 0) continue;
                int cbBase = cbStructPos[i];
                for (int j = 0; j < cbs.Length; j++)
                {
                    var cb = cbs[j];
                    int pCb = cbBase + j * 24;

                    int cbStrPos = buf.Count;
                    byte[] nb = Encoding.GetEncoding("ISO-8859-1").GetBytes(cb.Name ?? string.Empty);
                    buf.AddRange(nb);
                    buf.Add(0);

                    uint cbNameOffset = (uint)(cbStrPos - pCb);
                    WriteU16At(pCb + 0, (ushort)cb.Type);
                    WriteU16At(pCb + 2, cb.ArraySize);
                    WriteU16At(pCb + 4, cb.PackOffset);
                    WriteU32At(pCb + 6, cbNameOffset);

                    byte[] hashBytes = cb.NameHashData;
                    if (hashBytes == null || hashBytes.Length != 14) hashBytes = new byte[14];
                    for (int k = 0; k < 14; k++) buf[pCb + 10 + k] = hashBytes[k];
                }
            }

            for (int i = 0; i < regs.Length; i++)
            {
                int pReg = regHeadersStart + i * 28;
                var r = regs[i];
                int cbCount = r.CBuffers?.Length ?? 0;
                ushort cbDataOffset = (ushort)(cbCount > 0 ? (cbStructPos[i] - pReg) : 0);
                ushort regStrOffset = (ushort)(regStringPos[i] - pReg);

                WriteU16At(pReg + 0, (ushort)r.ResourceType);
                WriteU16At(pReg + 2, r.RegisterSlot);
                buf[pReg + 4] = (byte)cbCount;
                buf[pReg + 5] = r.NumDescriptors;
                buf[pReg + 6] = r.RegisterSpace;
                buf[pReg + 7] = r.Reserved;
                WriteU16At(pReg + 8, cbDataOffset);
                WriteU16At(pReg + 10, regStrOffset);

                byte[] extra = r.ExtraData;
                if (extra == null || extra.Length != 16) extra = new byte[16];
                for (int k = 0; k < 16; k++) buf[pReg + 12 + k] = extra[k];
            }

            if ((buf.Count & 1) != 0) buf.Add(0);

            ushort total = (ushort)buf.Count;
            WriteU16At(6, total);

            return buf.ToArray();
        }

        // ---------- XML ----------

        public void WriteXml(StringBuilder sb, int indent, string csoFolder)
        {
            MetaXmlBase.StringTag(sb, indent, "Magic", Magic ?? "SGD2");

            // Map each shader to the first effect that references it, so .cso
            // side-files land in per-effect subfolders matching the UI groups.
            // Shaders not referenced by any effect go to "_unassigned".
            var owners = BuildShaderOwnerLookup();

            WriteShaderGroup(sb, indent, "VertexShaders", VertexShaders, csoFolder, owners);
            WriteShaderGroup(sb, indent, "PixelShaders", PixelShaders, csoFolder, owners);
            WriteShaderGroup(sb, indent, "GeometryShaders", GeometryShaders, csoFolder, owners);
            WriteShaderGroup(sb, indent, "DomainShaders", DomainShaders, csoFolder, owners);
            WriteShaderGroup(sb, indent, "HullShaders", HullShaders, csoFolder, owners);
            WriteShaderGroup(sb, indent, "ComputeShaders", ComputeShaders, csoFolder, owners);

            if (Effects != null && Effects.Length > 0)
            {
                MetaXmlBase.OpenTag(sb, indent, "Effects");
                var ci = indent + 1;
                var cci = ci + 1;
                for (int i = 0; i < Effects.Length; i++)
                {
                    if (Effects[i] == null) { MetaXmlBase.SelfClosingTag(sb, ci, "Item"); continue; }
                    MetaXmlBase.OpenTag(sb, ci, "Item");
                    Effects[i].WriteXml(sb, cci);
                    MetaXmlBase.CloseTag(sb, ci, "Item");
                }
                MetaXmlBase.CloseTag(sb, indent, "Effects");
            }
            else
            {
                MetaXmlBase.SelfClosingTag(sb, indent, "Effects");
            }

            // Round-trip the trailer header (12 bytes: 8 reserved zero + u32 effect_count)
            // verbatim — preserves any non-zero bits we didn't decode.
            MetaXmlBase.StringTag(sb, indent, "TrailerHeader",
                TrailerHeader != null ? Convert.ToBase64String(TrailerHeader) : string.Empty);
            // Opaque fallback when Effects couldn't be decoded.
            MetaXmlBase.StringTag(sb, indent, "FooterData",
                FooterData != null ? Convert.ToBase64String(FooterData) : string.Empty);
        }

        public void ReadXml(XmlElement node, string csoFolder)
        {
            Magic = Xml.GetChildInnerText(node, "Magic");
            if (string.IsNullOrEmpty(Magic)) Magic = "SGD2";

            VertexShaders = ReadShaderGroup(node, "VertexShaders", AwcShaderStage.Vertex, csoFolder);
            PixelShaders = ReadShaderGroup(node, "PixelShaders", AwcShaderStage.Pixel, csoFolder);
            GeometryShaders = ReadShaderGroup(node, "GeometryShaders", AwcShaderStage.Geometry, csoFolder);
            DomainShaders = ReadShaderGroup(node, "DomainShaders", AwcShaderStage.Domain, csoFolder);
            HullShaders = ReadShaderGroup(node, "HullShaders", AwcShaderStage.Hull, csoFolder);
            ComputeShaders = ReadShaderGroup(node, "ComputeShaders", AwcShaderStage.Compute, csoFolder);

            var enode = node.SelectSingleNode("Effects");
            if (enode != null)
            {
                var items = enode.SelectNodes("Item");
                if (items != null && items.Count > 0)
                {
                    var list = new List<AwcEffect>(items.Count);
                    foreach (XmlNode it in items)
                    {
                        var eff = new AwcEffect();
                        eff.ReadXml(it);
                        list.Add(eff);
                    }
                    Effects = list.ToArray();
                }
                else
                {
                    Effects = Array.Empty<AwcEffect>();
                }
            }

            var thb = Xml.GetChildInnerText(node, "TrailerHeader");
            TrailerHeader = string.IsNullOrEmpty(thb) ? null : Convert.FromBase64String(thb);
            var fdb = Xml.GetChildInnerText(node, "FooterData");
            FooterData = string.IsNullOrEmpty(fdb) ? null : Convert.FromBase64String(fdb);

            // Save() picks Effects over FooterData; if we have decoded effects,
            // drop the opaque fallback so re-import edits actually take effect.
            if (Effects != null && Effects.Length > 0) FooterData = null;
        }

        private static void WriteShaderGroup(StringBuilder sb, int indent, string name, AwcShader[] arr, string csoFolder, Dictionary<AwcShader, string> owners)
        {
            if (arr == null || arr.Length == 0)
            {
                MetaXmlBase.SelfClosingTag(sb, indent, name);
                return;
            }
            MetaXmlBase.OpenTag(sb, indent, name);
            var ci = indent + 1;
            var cci = ci + 1;
            for (int i = 0; i < arr.Length; i++)
            {
                if (arr[i] == null) { MetaXmlBase.SelfClosingTag(sb, ci, "Item"); continue; }
                MetaXmlBase.OpenTag(sb, ci, "Item");
                string subFolder = null;
                if (owners != null && owners.TryGetValue(arr[i], out var owner)) subFolder = owner;
                arr[i].WriteXml(sb, cci, csoFolder, subFolder);
                MetaXmlBase.CloseTag(sb, ci, "Item");
            }
            MetaXmlBase.CloseTag(sb, indent, name);
        }

        private Dictionary<AwcShader, string> BuildShaderOwnerLookup()
        {
            var map = new Dictionary<AwcShader, string>();
            if (Effects == null || Effects.Length == 0) return map;

            void Claim(AwcShader[] stageArr, uint[] indices, string effectName)
            {
                if (stageArr == null || indices == null) return;
                for (int k = 0; k < indices.Length; k++)
                {
                    var gi = indices[k];
                    if (gi >= stageArr.Length) continue;
                    var s = stageArr[gi];
                    if (s == null) continue;
                    if (!map.ContainsKey(s)) map[s] = SafeFolderName(effectName);
                }
            }

            foreach (var e in Effects)
            {
                if (e == null) continue;
                var n = e.Name ?? "unnamed";
                Claim(VertexShaders,   e.VsIndices, n);
                Claim(PixelShaders,    e.PsIndices, n);
                Claim(GeometryShaders, e.GsIndices, n);
                Claim(DomainShaders,   e.DsIndices, n);
                Claim(HullShaders,     e.HsIndices, n);
                Claim(ComputeShaders,  e.CsIndices, n);
            }
            return map;
        }

        private static string SafeFolderName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "_unassigned";
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(s.Length);
            foreach (var c in s) sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return sb.ToString();
        }

        private static AwcShader[] ReadShaderGroup(XmlNode parent, string name, AwcShaderStage stage, string csoFolder)
        {
            var n = parent.SelectSingleNode(name);
            if (n == null) return Array.Empty<AwcShader>();
            var items = n.SelectNodes("Item");
            if (items == null || items.Count == 0) return Array.Empty<AwcShader>();
            var list = new List<AwcShader>(items.Count);
            foreach (XmlNode it in items)
            {
                var s = new AwcShader();
                s.Stage = stage;
                s.ReadXml(it, csoFolder);
                if (s.Stage != stage) s.Stage = stage; // group wins
                list.Add(s);
            }
            return list.ToArray();
        }
    }

    public class AwcShaderXml : MetaXmlBase
    {
        public static string GetXml(AwcShaderFile awc, string outputFolder = "")
        {
            var sb = new StringBuilder();
            sb.AppendLine(XmlHeader);
            if (awc != null)
            {
                var name = "AwcShaderLibrary";
                OpenTag(sb, 0, name);
                awc.WriteXml(sb, 1, outputFolder);
                CloseTag(sb, 0, name);
            }
            return sb.ToString();
        }
    }

    public class XmlAwcShader
    {
        public static AwcShaderFile GetAwcShader(string xml, string inputFolder = "")
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            return GetAwcShader(doc, inputFolder);
        }

        public static AwcShaderFile GetAwcShader(XmlDocument doc, string inputFolder = "")
        {
            var awc = new AwcShaderFile();
            awc.ReadXml(doc.DocumentElement, inputFolder);
            return awc;
        }
    }
}
