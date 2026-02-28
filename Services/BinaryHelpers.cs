using System.Text;

namespace Mewgenics.SaveFileViewer.Services {
    public static class BinaryHelpers {
        public static ushort ReadU16LE(byte[] data, int offset) {
            if (offset + 1 >= data.Length) return 0;
            return (ushort)(data[offset] | (data[offset + 1] << 8));
        }

        public static uint ReadU32LE(byte[] data, int offset) {
            if (offset + 3 >= data.Length) return 0;
            return (uint)(data[offset] | (data[offset + 1] << 8) |
                         (data[offset + 2] << 16) | (data[offset + 3] << 24));
        }

        public static int ReadI32LE(byte[] data, int offset) {
            return (int)ReadU32LE(data, offset);
        }

        public static ulong ReadU64LE(byte[] data, int offset) {
            if (offset + 7 >= data.Length) return 0;
            return (ulong)ReadU32LE(data, offset) | ((ulong)ReadU32LE(data, offset + 4) << 32);
        }

        public static long ReadI64LE(byte[] data, int offset) {
            return (long)ReadU64LE(data, offset);
        }

        public static float ReadF32LE(byte[] data, int offset) {
            if (offset + 3 >= data.Length) return 0;
            return BitConverter.ToSingle(data, offset);
        }

        public static double ReadF64LE(byte[] data, int offset) {
            if (offset + 7 >= data.Length) return 0;
            return BitConverter.ToDouble(data, offset);
        }

        public static string ReadUtf16LE(byte[] data, int offset, int length) {
            if (offset + length * 2 > data.Length) return string.Empty;
            return Encoding.Unicode.GetString(data, offset, length * 2);
        }

        public static string ReadAscii(byte[] data, int offset, int length) {
            if (offset + length > data.Length) return string.Empty;
            return Encoding.ASCII.GetString(data, offset, length);
        }

        public static bool IsAsciiIdent(byte[] data, int offset, int length) {
            if (offset + length > data.Length) return false;
            for (int i = 0; i < length; i++) {
                byte b = data[offset + i];
                if (b < 0x20 || b >= 0x7F) return false;
            }
            return true;
        }

        public static int FindBytes(byte[] data, byte[] pattern, int startOffset = 0) {
            int end = data.Length - pattern.Length;
            for (int i = startOffset; i <= end; i++) {
                bool found = true;
                for (int j = 0; j < pattern.Length; j++) {
                    if (data[i + j] != pattern[j]) {
                        found = false;
                        break;
                    }
                }
                if (found) return i;
            }
            return -1;
        }

        public static bool SliceBytesEqual(byte[] data, int offset, byte[] pattern, int length) {
            if (offset + length > data.Length) return false;
            for (int i = 0; i < length; i++) {
                if (data[offset + i] != pattern[i]) return false;
            }
            return true;
        }
    }
}