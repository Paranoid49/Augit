using System.Buffers.Binary;

namespace Augit.Core.Documents;

public readonly record struct ImageDimensions(int Width, int Height)
{
    public long PixelCount => (long)Width * Height;
}

public static class ImageDimensionsReader
{
    public static bool TryRead(DocumentKind kind, ReadOnlySpan<byte> content, out ImageDimensions dimensions)
    {
        return kind switch
        {
            DocumentKind.Png => TryReadPng(content, out dimensions),
            DocumentKind.Bmp => TryReadBmp(content, out dimensions),
            DocumentKind.Jpeg => TryReadJpeg(content, out dimensions),
            _ => Fail(out dimensions),
        };
    }

    private static bool TryReadPng(ReadOnlySpan<byte> content, out ImageDimensions dimensions)
    {
        if (content.Length < 24)
        {
            return Fail(out dimensions);
        }

        int width = BinaryPrimitives.ReadInt32BigEndian(content[16..20]);
        int height = BinaryPrimitives.ReadInt32BigEndian(content[20..24]);
        return Create(width, height, out dimensions);
    }

    private static bool TryReadBmp(ReadOnlySpan<byte> content, out ImageDimensions dimensions)
    {
        if (content.Length < 26)
        {
            return Fail(out dimensions);
        }

        int width = BinaryPrimitives.ReadInt32LittleEndian(content[18..22]);
        int rawHeight = BinaryPrimitives.ReadInt32LittleEndian(content[22..26]);
        if (rawHeight == int.MinValue)
        {
            return Fail(out dimensions);
        }

        return Create(width, Math.Abs(rawHeight), out dimensions);
    }

    private static bool TryReadJpeg(ReadOnlySpan<byte> content, out ImageDimensions dimensions)
    {
        int offset = 2;
        while (offset + 4 <= content.Length)
        {
            if (content[offset] != 0xFF)
            {
                offset++;
                continue;
            }

            byte marker = content[offset + 1];
            offset += 2;
            while (marker == 0xFF && offset < content.Length)
            {
                marker = content[offset++];
            }

            if (marker is 0xD8 or 0xD9 || marker is >= 0xD0 and <= 0xD7)
            {
                continue;
            }

            if (offset + 2 > content.Length)
            {
                break;
            }

            int segmentLength = BinaryPrimitives.ReadUInt16BigEndian(content[offset..(offset + 2)]);
            if (segmentLength < 2 || offset + segmentLength > content.Length)
            {
                break;
            }

            if (IsStartOfFrame(marker) && segmentLength >= 7)
            {
                int height = BinaryPrimitives.ReadUInt16BigEndian(content[(offset + 3)..(offset + 5)]);
                int width = BinaryPrimitives.ReadUInt16BigEndian(content[(offset + 5)..(offset + 7)]);
                return Create(width, height, out dimensions);
            }

            offset += segmentLength;
        }

        return Fail(out dimensions);
    }

    private static bool IsStartOfFrame(byte marker)
    {
        return marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;
    }

    private static bool Create(int width, int height, out ImageDimensions dimensions)
    {
        if (width <= 0 || height <= 0)
        {
            return Fail(out dimensions);
        }

        dimensions = new(width, height);
        return true;
    }

    private static bool Fail(out ImageDimensions dimensions)
    {
        dimensions = default;
        return false;
    }
}
