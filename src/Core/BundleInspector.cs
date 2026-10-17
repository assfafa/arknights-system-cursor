using System;
using System.IO;
using System.Text;

namespace ArknightsSystemCursor
{
    // Read only. Supports the standard UnityFS LZ4 blocks and Arknights' token/offset variant.
    // No game assets, Unity libraries, or executable patches are included.
    public static class BundleInspector
    {
        private const int MaximumBundleSize = 4 * 1024 * 1024;
        private const int MaximumDecodedSize = 16 * 1024 * 1024;
        private static readonly byte[] Marker = Encoding.ASCII.GetBytes("dyn/mouse/pc_mouse_mgr.prefab");

        public static bool IsMouseBundle(string path)
        {
            FileInfo file = new FileInfo(path);
            if (file.Length > 0 && file.Length <= MaximumBundleSize)
            {
                try
                {
                    return ContainsMousePrefab(File.ReadAllBytes(path));
                }
                catch (InvalidDataException) { return false; }
                catch (EndOfStreamException) { return false; }
                catch (OverflowException) { return false; }
                catch (ArgumentException) { return false; }
            }
            else
            {
                return false;
            }
        }

        public static bool ContainsMousePrefab(byte[] file)
        {
            using (MemoryStream stream = new MemoryStream(file, false))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                string signature = ReadCString(reader);
                if (signature == "UnityFS")
                {
                    uint version = ReadUInt32BE(reader);
                    ReadCString(reader);
                    ReadCString(reader);
                    long declaredSize = ReadInt64BE(reader);
                    uint compressedInfoSize = ReadUInt32BE(reader);
                    uint decodedInfoSize = ReadUInt32BE(reader);
                    uint flags = ReadUInt32BE(reader);
                    if (declaredSize == file.Length && version >= 6 && version <= 8)
                    {
                        if (version >= 7)
                        {
                            Align(stream);
                        }
                        long dataStart = stream.Position;
                        if ((flags & 128) != 0)
                        {
                            stream.Position = file.Length - compressedInfoSize;
                        }
                        byte[] info = Decode(ReadBytes(reader, compressedInfoSize), decodedInfoSize, (int)(flags & 63));
                        if ((flags & 128) == 0)
                        {
                            dataStart = stream.Position;
                        }
                        stream.Position = dataStart;
                        if ((flags & 512) != 0)
                        {
                            Align(stream);
                        }
                        using (MemoryStream infoStream = new MemoryStream(info, false))
                        using (BinaryReader infoReader = new BinaryReader(infoStream))
                        using (MemoryStream decoded = new MemoryStream())
                        {
                            ReadBytes(infoReader, 16);
                            uint blockCount = ReadUInt32BE(infoReader);
                            if (blockCount > 0 && blockCount <= 4096)
                            {
                                for (uint index = 0; index < blockCount; index++)
                                {
                                    uint outputSize = ReadUInt32BE(infoReader);
                                    uint inputSize = ReadUInt32BE(infoReader);
                                    int blockFlags = ReadUInt16BE(infoReader);
                                    if (decoded.Length + outputSize <= MaximumDecodedSize)
                                    {
                                        byte[] block = Decode(ReadBytes(reader, inputSize), outputSize, blockFlags & 63);
                                        decoded.Write(block, 0, block.Length);
                                    }
                                    else
                                    {
                                        throw new InvalidDataException("Bundle exceeds the cursor inspection size limit.");
                                    }
                                }
                                return ContainsSerializedMarker(decoded.ToArray());
                            }
                            else
                            {
                                throw new InvalidDataException("Invalid block count.");
                            }
                        }
                    }
                    else
                    {
                        throw new InvalidDataException("Unsupported UnityFS header.");
                    }
                }
                else
                {
                    return false;
                }
            }
        }

        private static bool ContainsSerializedMarker(byte[] data)
        {
            for (int start = 4; start <= data.Length - Marker.Length; start++)
            {
                if (data[start] == Marker[0] && BitConverter.ToInt32(data, start - 4) == Marker.Length)
                {
                    bool matches = true;
                    for (int index = 1; index < Marker.Length; index++)
                    {
                        if (data[start + index] != Marker[index])
                        {
                            matches = false;
                        }
                    }
                    if (matches)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public static byte[] Decode(byte[] input, uint expectedSize, int compression)
        {
            if (expectedSize > MaximumDecodedSize)
            {
                throw new InvalidDataException("Invalid decoded block size.");
            }
            if (compression == 0)
            {
                if (input.Length == expectedSize)
                {
                    return input;
                }
                else
                {
                    throw new InvalidDataException("Uncompressed block size mismatch.");
                }
            }
            else if (compression >= 2 && compression <= 5)
            {
                bool custom = compression >= 4;
                byte[] output = new byte[checked((int)expectedSize)];
                int source = 0;
                int destination = 0;
                while (source < input.Length)
                {
                    int token = input[source++];
                    int literalLength;
                    int matchLength;
                    if (custom)
                    {
                        literalLength = token & 15;
                        matchLength = token >> 4;
                    }
                    else
                    {
                        literalLength = token >> 4;
                        matchLength = token & 15;
                    }
                    literalLength = ReadLength(input, ref source, literalLength);
                    if (literalLength <= input.Length - source && literalLength <= output.Length - destination)
                    {
                        Buffer.BlockCopy(input, source, output, destination, literalLength);
                        source += literalLength;
                        destination += literalLength;
                    }
                    else
                    {
                        throw new InvalidDataException("Invalid literal length.");
                    }
                    if (source < input.Length)
                    {
                        if (source + 2 <= input.Length)
                        {
                            int distance;
                            if (custom)
                            {
                                distance = (input[source] << 8) | input[source + 1];
                            }
                            else
                            {
                                distance = input[source] | (input[source + 1] << 8);
                            }
                            source += 2;
                            matchLength = checked(ReadLength(input, ref source, matchLength) + 4);
                            if (distance > 0 && distance <= destination && matchLength <= output.Length - destination)
                            {
                                for (int index = 0; index < matchLength; index++)
                                {
                                    output[destination] = output[destination - distance];
                                    destination++;
                                }
                            }
                            else
                            {
                                throw new InvalidDataException("Invalid match offset or length.");
                            }
                        }
                        else
                        {
                            throw new InvalidDataException("Truncated match offset.");
                        }
                    }
                }
                if (destination == output.Length)
                {
                    return output;
                }
                else
                {
                    throw new InvalidDataException("Decoded block size mismatch.");
                }
            }
            else
            {
                throw new InvalidDataException("Unsupported compression.");
            }
        }

        private static int ReadLength(byte[] data, ref int position, int length)
        {
            if (length == 15)
            {
                bool reading = true;
                while (reading)
                {
                    if (position < data.Length)
                    {
                        int extension = data[position++];
                        length = checked(length + extension);
                        reading = extension == 255;
                    }
                    else
                    {
                        throw new InvalidDataException("Truncated length extension.");
                    }
                }
            }
            return length;
        }

        private static string ReadCString(BinaryReader reader)
        {
            StringBuilder value = new StringBuilder();
            bool reading = true;
            while (reading && value.Length <= 128)
            {
                byte next = reader.ReadByte();
                if (next == 0)
                {
                    reading = false;
                }
                else
                {
                    value.Append((char)next);
                }
            }
            if (reading)
            {
                throw new InvalidDataException("Unterminated bundle header string.");
            }
            return value.ToString();
        }

        private static byte[] ReadBytes(BinaryReader reader, uint count)
        {
            if (count <= MaximumDecodedSize && count <= reader.BaseStream.Length - reader.BaseStream.Position)
            {
                byte[] bytes = reader.ReadBytes(checked((int)count));
                if (bytes.Length == count)
                {
                    return bytes;
                }
                else
                {
                    throw new EndOfStreamException();
                }
            }
            else
            {
                throw new InvalidDataException("Block size exceeds input bounds.");
            }
        }

        private static uint ReadUInt32BE(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(4);
            if (bytes.Length == 4)
            {
                return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
            }
            else
            {
                throw new EndOfStreamException();
            }
        }

        private static int ReadUInt16BE(BinaryReader reader)
        {
            return (reader.ReadByte() << 8) | reader.ReadByte();
        }

        private static long ReadInt64BE(BinaryReader reader)
        {
            ulong high = ReadUInt32BE(reader);
            ulong low = ReadUInt32BE(reader);
            return checked((long)((high << 32) | low));
        }

        private static void Align(Stream stream)
        {
            stream.Position = (stream.Position + 15) & ~15L;
        }
    }
}
