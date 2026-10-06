using System.Runtime.InteropServices;

namespace LogAnalyzer.Dfir.Windows.Native;

/// <summary>Windows-native XPRESS-Huffman decompression (used by Win10/11 "MAM" Prefetch files).</summary>
internal static partial class NtCompression
{
    private const ushort CompressionFormatXpressHuff = 4;

    [LibraryImport("ntdll.dll")]
    private static partial int RtlGetCompressionWorkSpaceSize(ushort format, out uint workSpaceSize, out uint fragmentWorkSpaceSize);

    [LibraryImport("ntdll.dll")]
    private static partial int RtlDecompressBufferEx(ushort format, byte[] uncompressed, uint uncompressedSize,
                                                    byte[] compressed, uint compressedSize, out uint finalSize, byte[] workSpace);

    /// <summary>Decompresses a MAM container ("MAM\x04" + uint32 size + XPRESS-Huffman payload). Throws on failure.</summary>
    public static byte[] DecompressMam(ReadOnlySpan<byte> file)
    {
        if (file.Length < 8 || file[0] != (byte)'M' || file[1] != (byte)'A' || file[2] != (byte)'M')
            throw new InvalidDataException("Not a MAM-compressed buffer.");
        uint size = BitConverter.ToUInt32(file[4..8]);
        if (size == 0 || size > 64 * 1024 * 1024) throw new InvalidDataException($"Implausible MAM uncompressed size {size}.");

        int st = RtlGetCompressionWorkSpaceSize(CompressionFormatXpressHuff, out uint ws, out _);
        if (st != 0) throw new InvalidOperationException($"RtlGetCompressionWorkSpaceSize NTSTATUS 0x{st:X8}");

        var output = new byte[size];
        var input = file[8..].ToArray();
        st = RtlDecompressBufferEx(CompressionFormatXpressHuff, output, size, input, (uint)input.Length, out uint final, new byte[ws]);
        if (st != 0) throw new InvalidDataException($"RtlDecompressBufferEx NTSTATUS 0x{st:X8}");
        return final == size ? output : output[..(int)final];
    }
}
