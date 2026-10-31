using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
#if NET
using System.Runtime.Versioning;
#endif

namespace LicenceManager
{
    /// <summary>
    /// Code poste : empreinte de 64 bits de la machine, pour lier une licence a un poste
    /// (FORMAT.md section 5). Calcule a l'identique par le validateur C++.
    /// </summary>
    public static class MachineCode
    {
        internal const int Size = 8;
        private const int GroupSize = 4;
        private const string Domain = "LicenceManager.Machine/1:";

        /// <summary>
        /// Code du poste courant (ex. <c>WSZ6-G7W2-KQCT-CD3Q</c>), ou null si aucun identifiant
        /// n'est disponible (systeme autre que Windows, acces refuse).
        /// Source : UUID de la carte mere (SMBIOS), sinon MachineGuid de Windows.
        /// </summary>
        public static string Current
        {
            get
            {
                byte[] id = CurrentId();
                return id == null ? null : Format(id);
            }
        }

        /// <summary>
        /// Valide un code saisi (tolerant : minuscules, espaces, O/I/L) et le remet en forme.
        /// </summary>
        public static bool TryNormalize(string text, out string code)
        {
            code = null;
            if (!TryParse(text, out byte[] id)) return false;
            code = Format(id);
            return true;
        }

        internal static bool TryParse(string text, out byte[] id)
        {
            id = null;
            if (!LicenceKeyCodec.TryDecode(text, out byte[] raw) || raw.Length != Size || IsZero(raw)) return false;
            id = raw;
            return true;
        }

        internal static string Format(byte[] id) => LicenceKeyCodec.Encode(id, GroupSize);

        internal static byte[] CurrentId()
        {
#if NET
            if (!OperatingSystem.IsWindows()) return null;
#endif

            byte[] uuid = ReadSmbiosUuid();
            if (uuid != null) return Derive("A:", uuid);

            string guid = ReadMachineGuid();
            return guid != null ? DeriveFromMachineGuid(guid) : null;
        }

        internal static byte[] DeriveFromMachineGuid(string machineGuid)
        {
            string guid = machineGuid.Trim().ToLowerInvariant();
            return guid.Length == 0 ? null : Derive("B:", Encoding.UTF8.GetBytes(guid));
        }

        // SHA-256("LicenceManager.Machine/1:" + "A:" ou "B:" + valeur), tronque a 8 octets.
        internal static byte[] Derive(string source, byte[] value)
        {
            byte[] prefix = Encoding.ASCII.GetBytes(Domain + source);
            byte[] input = new byte[prefix.Length + value.Length];
            Buffer.BlockCopy(prefix, 0, input, 0, prefix.Length);
            Buffer.BlockCopy(value, 0, input, prefix.Length, value.Length);

            byte[] hash;
            using (SHA256 sha = SHA256.Create()) hash = sha.ComputeHash(input);
            byte[] id = new byte[Size];
            Buffer.BlockCopy(hash, 0, id, 0, Size);
            return IsZero(id) ? null : id; // 0 est reserve a "licence non liee"
        }

        // UUID systeme (structure SMBIOS de type 1, octets bruts tels que stockes), ou null s'il
        // est absent ou s'il s'agit d'une valeur bouche-trou (que des 0, que des FF, ou la valeur
        // par defaut de certains fabricants).
        internal static byte[] ParseSmbiosUuid(byte[] rawSmbios)
        {
            // En-tete RawSMBIOSData : methode(1), version majeure(1), mineure(1), revision DMI(1), longueur(4)
            if (rawSmbios == null || rawSmbios.Length < 8) return null;
            int major = rawSmbios[1], minor = rawSmbios[2];
            if (major < 2 || (major == 2 && minor < 1)) return null; // UUID apparu en SMBIOS 2.1
            long length = BitConverter.ToUInt32(rawSmbios, 4);
            int end = (int)Math.Min(rawSmbios.Length, 8 + length);

            int p = 8;
            while (p + 4 <= end)
            {
                int type = rawSmbios[p], len = rawSmbios[p + 1];
                if (len < 4 || p + len > end) return null;
                if (type == 1 && len >= 0x19)
                {
                    byte[] uuid = new byte[16];
                    Buffer.BlockCopy(rawSmbios, p + 8, uuid, 0, 16);
                    return IsPlaceholder(uuid) ? null : uuid;
                }
                if (type == 127) return null;

                // Zone de chaines : terminee par deux octets nuls.
                int q = p + len;
                while (q + 1 < end && (rawSmbios[q] != 0 || rawSmbios[q + 1] != 0)) q++;
                p = q + 2;
            }
            return null;
        }

        private static readonly byte[] DefaultOemUuid =
            { 0x00, 0x02, 0x00, 0x03, 0x00, 0x04, 0x00, 0x05, 0x00, 0x06, 0x00, 0x07, 0x00, 0x08, 0x00, 0x09 };

        private static bool IsPlaceholder(byte[] uuid)
        {
            bool allZero = true, allFf = true;
            foreach (byte b in uuid)
            {
                allZero &= b == 0x00;
                allFf &= b == 0xFF;
            }
            if (allZero || allFf) return true;
            for (int i = 0; i < 16; i++)
                if (uuid[i] != DefaultOemUuid[i]) return false;
            return true;
        }

        private static bool IsZero(byte[] b)
        {
            foreach (byte x in b)
                if (x != 0) return false;
            return true;
        }

        private const uint Rsmb = 0x52534D42; // 'RSMB'

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetSystemFirmwareTable(uint firmwareTableProviderSignature, uint firmwareTableId,
            [Out] byte[] firmwareTableBuffer, uint bufferSize);

        private static byte[] ReadSmbiosUuid()
        {
            try
            {
                uint size = GetSystemFirmwareTable(Rsmb, 0, null, 0);
                if (size == 0) return null;
                byte[] buffer = new byte[size];
                if (GetSystemFirmwareTable(Rsmb, 0, buffer, size) != size) return null;
                return ParseSmbiosUuid(buffer);
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
            {
                return null;
            }
        }

#if NET
        [SupportedOSPlatform("windows")]
#endif
        // Vue 64 bits du registre : un processus 32 bits lirait sinon une autre cle.
        private static string ReadMachineGuid()
        {
            try
            {
                using (RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                {
                    return key?.GetValue("MachineGuid") as string;
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException || ex is System.IO.IOException)
            {
                return null;
            }
        }
    }
}
