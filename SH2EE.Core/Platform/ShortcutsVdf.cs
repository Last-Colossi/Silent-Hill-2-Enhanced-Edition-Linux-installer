using System.Text;

namespace SH2EESetup.Platform
{
    /// <summary>
    /// Minimal reader/writer for Steam's binary VDF (the format of shortcuts.vdf).
    ///
    /// Structure: a root map "shortcuts" whose children are numerically-indexed maps, one
    /// per shortcut. Within a map, each entry is a type byte, a NUL-terminated key, then
    /// the value: 0x00 = nested map (recurse until 0x08), 0x01 = NUL-terminated string,
    /// 0x02 = little-endian int32. A 0x08 byte closes a map.
    /// </summary>
    public static class ShortcutsVdf
    {
        private const byte TypeMap = 0x00;
        private const byte TypeString = 0x01;
        private const byte TypeInt = 0x02;
        private const byte EndMap = 0x08;

        public static List<Dictionary<string, object>> Parse(byte[] bytes)
        {
            var result = new List<Dictionary<string, object>>();
            int pos = 0;

            // Expect: TypeMap, "shortcuts", then the indexed entries.
            if (bytes.Length == 0)
                return result;
            if (bytes[pos] != TypeMap)
                return result;
            pos++;
            ReadCString(bytes, ref pos); // "shortcuts"

            // Each child is TypeMap with a numeric key → one shortcut.
            while (pos < bytes.Length && bytes[pos] != EndMap)
            {
                byte type = bytes[pos++];
                string key = ReadCString(bytes, ref pos);
                if (type == TypeMap)
                {
                    result.Add(ReadMap(bytes, ref pos));
                }
                else
                {
                    // Unexpected; skip its value to stay in sync.
                    SkipValue(bytes, ref pos, type);
                    _ = key;
                }
            }

            return result;
        }

        private static Dictionary<string, object> ReadMap(byte[] bytes, ref int pos)
        {
            var map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            while (pos < bytes.Length && bytes[pos] != EndMap)
            {
                byte type = bytes[pos++];
                string key = ReadCString(bytes, ref pos);
                switch (type)
                {
                    case TypeMap:
                        map[key] = ReadMap(bytes, ref pos);
                        break;
                    case TypeString:
                        map[key] = ReadCString(bytes, ref pos);
                        break;
                    case TypeInt:
                        map[key] = BitConverter.ToInt32(bytes, pos);
                        pos += 4;
                        break;
                }
            }
            pos++; // consume EndMap
            return map;
        }

        private static void SkipValue(byte[] bytes, ref int pos, byte type)
        {
            switch (type)
            {
                case TypeString: ReadCString(bytes, ref pos); break;
                case TypeInt: pos += 4; break;
                case TypeMap: ReadMap(bytes, ref pos); break;
            }
        }

        private static string ReadCString(byte[] bytes, ref int pos)
        {
            int start = pos;
            while (pos < bytes.Length && bytes[pos] != 0)
                pos++;
            string s = Encoding.UTF8.GetString(bytes, start, pos - start);
            pos++; // consume NUL
            return s;
        }

        public static byte[] Serialize(List<Dictionary<string, object>> shortcuts)
        {
            using var ms = new MemoryStream();

            WriteByte(ms, TypeMap);
            WriteCString(ms, "shortcuts");

            for (int i = 0; i < shortcuts.Count; i++)
            {
                WriteByte(ms, TypeMap);
                WriteCString(ms, i.ToString());
                WriteMapBody(ms, shortcuts[i]);
            }

            WriteByte(ms, EndMap); // close "shortcuts"
            WriteByte(ms, EndMap); // close root
            return ms.ToArray();
        }

        private static void WriteMapBody(Stream s, Dictionary<string, object> map)
        {
            foreach (var (key, value) in map)
            {
                switch (value)
                {
                    case Dictionary<string, object> nested:
                        WriteByte(s, TypeMap);
                        WriteCString(s, key);
                        WriteMapBody(s, nested);
                        break;
                    case int i:
                        WriteByte(s, TypeInt);
                        WriteCString(s, key);
                        s.Write(BitConverter.GetBytes(i), 0, 4);
                        break;
                    case string str:
                        WriteByte(s, TypeString);
                        WriteCString(s, key);
                        WriteCString(s, str);
                        break;
                }
            }
            WriteByte(s, EndMap);
        }

        private static void WriteByte(Stream s, byte b) => s.WriteByte(b);

        private static void WriteCString(Stream s, string value)
        {
            byte[] data = Encoding.UTF8.GetBytes(value);
            s.Write(data, 0, data.Length);
            s.WriteByte(0);
        }
    }
}
