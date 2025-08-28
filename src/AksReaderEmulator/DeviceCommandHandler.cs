using System.Globalization;

namespace AksReaderEmulator
{
    public class DeviceCommandHandler
    {
        public byte[] CreateCommand(ReadOnlySpan<byte> data)
        {
            byte[] commandBytes = new byte[7 + data.Length];
            commandBytes[0] = (byte)2;
            commandBytes[1] = 150;
            commandBytes[2] = 255;
            commandBytes[3] = (byte)(data.Length + 3);
            data.CopyTo(commandBytes.AsSpan(4));

            string bccStr = CreateBcc(commandBytes.AsSpan()[..(commandBytes.Length - 3)]);
            commandBytes[^3] = (byte)bccStr[0];
            commandBytes[^2] = (byte)bccStr[1];
            commandBytes[^1] = (byte)3;
            return commandBytes;
        }

        internal byte XORBytes(ReadOnlySpan<byte> bytes)
        {
            byte result = 0;
            foreach (byte b in bytes)
            {
                result ^= b;
            }
            return result;
        }
        public byte[]? GetDataPart(ReadOnlySpan<byte> buffer)
        {
            int startIndex = buffer.IndexOf((byte)2);
            if (startIndex == -1) return null;
            var remainingBuffer = buffer[(startIndex + 1)..];
            int endIndex = remainingBuffer.LastIndexOf((byte)3);
            if (endIndex == -1) return null;

            int dataLength = (int)remainingBuffer[2];

            return remainingBuffer.Slice(3, dataLength - 3).ToArray();
        }
        public string CreateBcc(ReadOnlySpan<byte> bytes)
        {
            byte xorBytes = XORBytes(bytes);
            return xorBytes.ToString("X2", CultureInfo.InvariantCulture);
        }
    }
}
