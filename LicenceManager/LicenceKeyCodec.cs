using System;
using System.Text;

namespace LicenceManager
{
    // Texte de la cle : octets + CRC-16 (CCITT-FALSE, big-endian), encodes en Base32 Crockford
    // (bits de poids fort d'abord), en groupes de 6 caracteres separes par des tirets.
    // Le CRC ne protege que contre les erreurs de copie : l'authenticite vient de la signature.
    internal static class LicenceKeyCodec
    {
        internal const int GroupSize = 6;
        private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

        internal static string Encode(byte[] data, int groupSize = GroupSize)
        {
            byte[] withCrc = new byte[data.Length + 2];
            Buffer.BlockCopy(data, 0, withCrc, 0, data.Length);
            ushort crc = Crc16(data, data.Length);
            withCrc[data.Length] = (byte)(crc >> 8);
            withCrc[data.Length + 1] = (byte)crc;

            var sb = new StringBuilder();
            int buffer = 0, bits = 0, chars = 0;
            foreach (byte b in withCrc)
            {
                buffer = (buffer << 8) | b;
                bits += 8;
                while (bits >= 5)
                {
                    Append(sb, Alphabet[(buffer >> (bits - 5)) & 31], groupSize, ref chars);
                    bits -= 5;
                }
                buffer &= (1 << bits) - 1;
            }
            if (bits > 0)
                Append(sb, Alphabet[(buffer << (5 - bits)) & 31], groupSize, ref chars);
            return sb.ToString();
        }

        // Tolerant a la saisie : minuscules, espaces, retours a la ligne et tirets ignores,
        // O lu comme 0, I et L lus comme 1. Retourne false si un caractere est invalide, si les
        // bits de remplissage ne sont pas nuls ou si le CRC ne correspond pas.
        internal static bool TryDecode(string text, out byte[] data)
        {
            data = null;
            if (string.IsNullOrEmpty(text)) return false;

            var bytes = new System.Collections.Generic.List<byte>(text.Length * 5 / 8);
            int buffer = 0, bits = 0;
            foreach (char raw in text)
            {
                if (raw == '-' || char.IsWhiteSpace(raw)) continue;
                int v = DecodeChar(raw);
                if (v < 0) return false;
                buffer = (buffer << 5) | v;
                bits += 5;
                if (bits >= 8)
                {
                    bytes.Add((byte)(buffer >> (bits - 8)));
                    bits -= 8;
                }
                buffer &= (1 << bits) - 1;
            }
            if (bits >= 5 || buffer != 0) return false; // remplissage invalide
            if (bytes.Count < 3) return false;

            byte[] all = bytes.ToArray();
            int n = all.Length - 2;
            ushort expected = (ushort)((all[n] << 8) | all[n + 1]);
            if (Crc16(all, n) != expected) return false;

            data = new byte[n];
            Buffer.BlockCopy(all, 0, data, 0, n);
            return true;
        }

        private static void Append(StringBuilder sb, char c, int groupSize, ref int chars)
        {
            if (chars > 0 && chars % groupSize == 0) sb.Append('-');
            sb.Append(c);
            chars++;
        }

        private static int DecodeChar(char c)
        {
            c = char.ToUpperInvariant(c);
            if (c == 'O') c = '0';
            else if (c == 'I' || c == 'L') c = '1';
            return Alphabet.IndexOf(c);
        }

        // CRC-16/CCITT-FALSE : polynome 0x1021, valeur initiale 0xFFFF, sans reflexion.
        internal static ushort Crc16(byte[] data, int count)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < count; i++)
            {
                crc ^= (ushort)(data[i] << 8);
                for (int j = 0; j < 8; j++)
                    crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
            }
            return crc;
        }
    }
}
