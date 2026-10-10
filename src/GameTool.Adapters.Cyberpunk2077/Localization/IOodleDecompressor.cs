namespace GameTool.Adapters.Cyberpunk2077.Localization;

public interface IOodleDecompressor
{
    byte[] Decompress(ReadOnlySpan<byte> compressed, int uncompressedSize);
}
