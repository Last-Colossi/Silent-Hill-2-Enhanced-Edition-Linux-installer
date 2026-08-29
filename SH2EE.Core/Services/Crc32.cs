namespace SH2EESetup.Services
{
    /// <summary>
    /// Standard CRC-32 (IEEE 802.3, reversed polynomial 0xEDB88320).
    ///
    /// Two unrelated things in this project need exactly this checksum: ZIP stores it per
    /// entry — which is how <see cref="ComponentFileMap"/> knows what upstream's files should
    /// look like — and Steam hashes non-Steam shortcuts with it.
    /// </summary>
    public static class Crc32
    {
        private static readonly uint[] Table = BuildTable();

        private static uint[] BuildTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[i] = c;
            }
            return table;
        }

        public static uint OfBytes(ReadOnlySpan<byte> data)
        {
            uint crc = 0xFFFFFFFFu;
            foreach (byte b in data)
                crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }

        /// <summary>Streams the file so a multi-hundred-megabyte asset isn't held in memory.</summary>
        public static uint OfFile(string path)
        {
            uint crc = 0xFFFFFFFFu;
            using var stream = File.OpenRead(path);
            var buffer = new byte[1 << 16];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i < read; i++)
                    crc = Table[(crc ^ buffer[i]) & 0xFF] ^ (crc >> 8);
            }
            return crc ^ 0xFFFFFFFFu;
        }
    }
}
