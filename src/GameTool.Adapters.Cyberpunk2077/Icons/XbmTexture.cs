using GameTool.Adapters.Cyberpunk2077.Resources;
using GameTool.Adapters.Cyberpunk2077.Textures;

namespace GameTool.Adapters.Cyberpunk2077.Icons;

/// <summary>
/// CBitmapTexture（.xbm）的 mip 0。导出 0 是 CBitmapTexture（width、height、setup.compression），
/// renderTextureResource.renderResourceBlobPC 指向 rendRenderTextureBlobPC：header 里有 mip 布局（rowPitch、placement.offset/size），
/// textureData 是延迟缓冲序号，像素数据在归档文件的同号段里。
/// </summary>
public sealed class XbmTexture
{
    private XbmTexture(int width, int height, TextureFormat format, int rowPitch, int mipOffset, int mipSize, int bufferIndex)
    {
        Width = width;
        Height = height;
        Format = format;
        RowPitch = rowPitch;
        MipOffset = mipOffset;
        MipSize = mipSize;
        BufferIndex = bufferIndex;
    }

    public int Width { get; }

    public int Height { get; }

    public TextureFormat Format { get; }

    public int RowPitch { get; }

    public int MipOffset { get; }

    public int MipSize { get; }

    /// <summary>像素数据所在的缓冲序号（1 起），也就是归档文件的段号。</summary>
    public int BufferIndex { get; }

    /// <summary>setup.compression → 像素格式。TCM_None 只支持真彩（TRF_TrueColor）。</summary>
    public static TextureFormat? FormatOf(string? compression, string? rawFormat) => compression switch
    {
        "TCM_QualityColor" => TextureFormat.Bc7,
        "TCM_DXTNoAlpha" => TextureFormat.Bc1,
        "TCM_DXTAlpha" or "TCM_DXTAlphaLinear" => TextureFormat.Bc3,
        "TCM_QualityR" => TextureFormat.Bc4,
        "TCM_QualityRG" or "TCM_Normalmap" => TextureFormat.Bc5,
        null or "TCM_None" => rawFormat is null or "TRF_TrueColor" ? TextureFormat.Rgba8 : null,
        _ => null
    };

    public static XbmTexture Parse(Cr2wFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.ExportCount == 0)
            throw new InvalidDataException("贴图没有导出对象。");
        var bitmap = file.ReadExport(0);
        var setup = bitmap.GetStruct("setup");
        var format = FormatOf(setup?.GetName("compression"), setup?.GetName("rawFormat"))
            ?? throw new InvalidDataException($"不支持的贴图压缩：{setup?.GetName("compression")}");
        var resource = bitmap.GetStruct("renderTextureResource");
        var handle = resource?["renderResourceBlobPC"] is Cr2wHandle value ? value.ExportIndex : -1;
        if (handle < 0 || handle >= file.ExportCount)
            throw new InvalidDataException("贴图缺少 renderResourceBlobPC。");
        var blob = file.ReadExport(handle);
        var header = blob.GetStruct("header") ?? throw new InvalidDataException("贴图缺少 header。");
        var size = header.GetStruct("sizeInfo");
        var width = (int)(size?.GetInt("width") ?? 0);
        var height = (int)(size?.GetInt("height") ?? 0);
        if (width <= 0 || height <= 0)
        {
            width = (int)bitmap.GetInt("width");
            height = (int)bitmap.GetInt("height");
        }

        if (width <= 0 || height <= 0 || width > 16384 || height > 16384)
            throw new InvalidDataException($"贴图尺寸无效：{width}×{height}");
        var mips = header.GetArray("mipMapInfo");
        var mip0 = mips.Count > 0 ? mips[0] as Cr2wStruct : null;
        var rowPitch = (int)(mip0?.GetStruct("layout")?.GetInt("rowPitch") ?? 0);
        if (rowPitch <= 0)
            rowPitch = BcDecoder.RowPitch(format, width);
        var placement = mip0?.GetStruct("placement");
        var mipOffset = (int)(placement?.GetInt("offset") ?? 0);
        var rows = BcDecoder.IsBlockCompressed(format) ? (height + 3) / 4 : height;
        var mipSize = (int)(placement?.GetInt("size") ?? 0);
        if (mipSize <= 0)
            mipSize = rowPitch * rows;
        if (rowPitch < BcDecoder.RowPitch(format, width) || (long)rowPitch * rows > mipSize)
            throw new InvalidDataException("贴图 mip 0 的布局和尺寸不一致。");
        var buffer = blob["textureData"] is Cr2wBufferRef reference ? reference.Index : 0;
        if (buffer <= 0)
            throw new InvalidDataException("贴图缺少 textureData 缓冲。");
        return new XbmTexture(width, height, format, rowPitch, mipOffset, mipSize, buffer);
    }
}
