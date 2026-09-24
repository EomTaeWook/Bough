using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Bough.App.Controls
{
    internal static class AuthorImageReader
    {
        private const int MaximumBytes = 256 * 1024;
        private const int MaximumDimension = 512;
        private static readonly byte[] _pngSignature = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };

        public static async Task<byte[]> ReadAsync(HttpContent content, CancellationToken cancellationToken)
        {
            if (content.Headers.ContentLength > MaximumBytes)
            {
                return null;
            }

            string mediaType = content.Headers.ContentType?.MediaType;
            if (mediaType != "image/png" && mediaType != "image/jpeg")
            {
                return null;
            }

            await using Stream input = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using MemoryStream output = new();
            byte[] buffer = new byte[8192];
            while (true)
            {
                int count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (count == 0)
                {
                    byte[] data = output.ToArray();
                    if (HasSafeDimensions(data, mediaType) == false)
                    {
                        return null;
                    }

                    return data;
                }

                if (output.Length + count > MaximumBytes)
                {
                    return null;
                }

                output.Write(buffer, 0, count);
            }
        }

        private static bool HasSafeDimensions(byte[] data, string mediaType)
        {
            if (mediaType == "image/png")
            {
                if (data.Length < 24)
                {
                    return false;
                }

                for (int index = 0; index < _pngSignature.Length; index++)
                {
                    if (data[index] != _pngSignature[index])
                    {
                        return false;
                    }
                }

                if (data[12] != 'I' || data[13] != 'H' || data[14] != 'D' || data[15] != 'R')
                {
                    return false;
                }

                uint width = ReadBigEndian32(data, 16);
                uint height = ReadBigEndian32(data, 20);
                return IsSafeSize(width, height);
            }

            if (data.Length < 4)
            {
                return false;
            }

            if (data[0] != 0xFF || data[1] != 0xD8)
            {
                return false;
            }

            int offset = 2;
            while (offset + 3 < data.Length)
            {
                if (data[offset] != 0xFF)
                {
                    return false;
                }

                while (offset < data.Length && data[offset] == 0xFF)
                {
                    offset++;
                }

                if (offset + 2 >= data.Length)
                {
                    return false;
                }

                byte marker = data[offset++];
                if (marker == 0xD9 || marker == 0xDA)
                {
                    return false;
                }

                int segmentLength = (data[offset] << 8) | data[offset + 1];
                if (segmentLength < 2)
                {
                    return false;
                }

                if (offset + segmentLength > data.Length)
                {
                    return false;
                }

                if (IsFrameMarker(marker) == true)
                {
                    if (segmentLength < 7)
                    {
                        return false;
                    }

                    uint height = (uint)((data[offset + 3] << 8) | data[offset + 4]);
                    uint width = (uint)((data[offset + 5] << 8) | data[offset + 6]);
                    return IsSafeSize(width, height);
                }

                offset += segmentLength;
            }

            return false;
        }

        private static bool IsFrameMarker(byte marker)
        {
            if (marker >= 0xC0 && marker <= 0xC3)
            {
                return true;
            }

            if (marker >= 0xC5 && marker <= 0xC7)
            {
                return true;
            }

            if (marker >= 0xC9 && marker <= 0xCB)
            {
                return true;
            }

            return marker >= 0xCD && marker <= 0xCF;
        }

        private static bool IsSafeSize(uint width, uint height)
        {
            if (width == 0 || height == 0)
            {
                return false;
            }

            return width <= MaximumDimension && height <= MaximumDimension;
        }

        private static uint ReadBigEndian32(byte[] data, int offset)
        {
            return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];
        }
    }
}
