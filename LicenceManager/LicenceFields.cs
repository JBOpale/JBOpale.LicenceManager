using System;
using System.Collections.Generic;
using System.Text;

namespace LicenceManager
{
    /// <summary>
    /// Champs personnalises d'une licence (FORMAT.md section 6) : valeurs identifiees par un numero
    /// de 1 a 255 dont la signification appartient au produit (meme principe que les bits de
    /// <see cref="LicenceData.Features"/>). Seules les valeurs sont dans la cle, pas les noms.
    /// </summary>
    public sealed class LicenceFields
    {
        /// <summary>Taille maximale de l'ensemble des champs dans la cle (numeros et tailles compris).</summary>
        public const int MaxTotalBytes = 64;

        /// <summary>Taille maximale d'un champ texte, en octets UTF-8.</summary>
        public const int TextMaxBytes = 32;

        private static readonly DateTime Epoch = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private readonly SortedDictionary<byte, byte[]> _values = new SortedDictionary<byte, byte[]>();

        /// <summary>Numeros des champs presents, par ordre croissant.</summary>
        public IEnumerable<byte> Ids => _values.Keys;

        /// <summary>Nombre de champs presents.</summary>
        public int Count => _values.Count;

        /// <summary>Vrai si le champ est present.</summary>
        public bool Contains(byte id) => _values.ContainsKey(id);

        /// <summary>Retire un champ (sans effet s'il est absent).</summary>
        public void Remove(byte id) => _values.Remove(id);

        /// <summary>Entier signe 32 bits.</summary>
        public void SetInt(byte id, int value) => Set(id, BitConverterLe((uint)value, 4));

        /// <summary>Oui/non.</summary>
        public void SetBool(byte id, bool value) => Set(id, new[] { value ? (byte)1 : (byte)0 });

        /// <summary>Date (jour UTC, l'heure est ignoree), du 02/01/2000 a 2179.</summary>
        public void SetDate(byte id, DateTime value)
        {
            double days = (value.Date - Epoch).TotalDays;
            if (days < 1 || days > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(value), "Date hors de la plage 02/01/2000 - 2179.");
            Set(id, BitConverterLe((ulong)days, 2));
        }

        /// <summary>Texte, au plus <see cref="TextMaxBytes"/> octets en UTF-8.</summary>
        public void SetText(byte id, string value)
        {
            byte[] b = Encoding.UTF8.GetBytes(value ?? string.Empty);
            if (b.Length > TextMaxBytes)
                throw new ArgumentException($"Texte du champ {id} : {TextMaxBytes} octets UTF-8 au maximum.");
            Set(id, b);
        }

        /// <summary>Masque de 64 bits (jeu d'options).</summary>
        public void SetMask(byte id, ulong value) => Set(id, BitConverterLe(value, 8));

        /// <summary>Lit un entier ; false si le champ est absent ou d'un autre type.</summary>
        public bool TryGetInt(byte id, out int value)
        {
            value = 0;
            if (!TryGet(id, 4, out byte[] b)) return false;
            value = (int)(uint)ReadLe(b);
            return true;
        }

        /// <summary>Lit un oui/non ; false si le champ est absent ou d'un autre type.</summary>
        public bool TryGetBool(byte id, out bool value)
        {
            value = false;
            if (!TryGet(id, 1, out byte[] b) || b[0] > 1) return false;
            value = b[0] == 1;
            return true;
        }

        /// <summary>Lit une date (UTC) ; false si le champ est absent ou d'un autre type.</summary>
        public bool TryGetDate(byte id, out DateTime value)
        {
            value = default(DateTime);
            if (!TryGet(id, 2, out byte[] b)) return false;
            ulong days = ReadLe(b);
            if (days == 0) return false;
            value = Epoch.AddDays(days);
            return true;
        }

        /// <summary>Lit un texte ; false si le champ est absent ou trop long pour etre un texte.</summary>
        public bool TryGetText(byte id, out string value)
        {
            value = null;
            if (!_values.TryGetValue(id, out byte[] b) || b.Length > TextMaxBytes) return false;
            value = Encoding.UTF8.GetString(b);
            return true;
        }

        /// <summary>Lit un masque de 64 bits ; false si le champ est absent ou d'un autre type.</summary>
        public bool TryGetMask(byte id, out ulong value)
        {
            value = 0;
            if (!TryGet(id, 8, out byte[] b)) return false;
            value = ReadLe(b);
            return true;
        }

        /// <summary>Octets bruts du champ (copie), ou null s'il est absent.</summary>
        public byte[] GetRaw(byte id) => _values.TryGetValue(id, out byte[] b) ? (byte[])b.Clone() : null;

        // Taille du bloc dans la cle : (numero, taille, valeur) par champ, hors octet de longueur du bloc.
        internal int EncodedSize
        {
            get
            {
                int size = 0;
                foreach (byte[] v in _values.Values) size += 2 + v.Length;
                return size;
            }
        }

        internal void WriteTo(byte[] b, int offset)
        {
            int size = EncodedSize;
            if (size > MaxTotalBytes)
                throw new ArgumentException($"Champs personnalises trop volumineux : {size} octets, {MaxTotalBytes} au maximum.");
            b[offset++] = (byte)size;
            foreach (KeyValuePair<byte, byte[]> kv in _values)
            {
                b[offset++] = kv.Key;
                b[offset++] = (byte)kv.Value.Length;
                Buffer.BlockCopy(kv.Value, 0, b, offset, kv.Value.Length);
                offset += kv.Value.Length;
            }
        }

        // Numeros strictement croissants (encodage unique) et tailles coherentes avec le bloc.
        internal static bool TryParse(byte[] b, int offset, int count, out LicenceFields fields)
        {
            fields = new LicenceFields();
            if (count > MaxTotalBytes) return false;
            int end = offset + count, previous = 0;
            while (offset < end)
            {
                if (end - offset < 2) return false;
                byte id = b[offset];
                int length = b[offset + 1];
                offset += 2;
                if (id <= previous || end - offset < length) return false;
                byte[] value = new byte[length];
                Buffer.BlockCopy(b, offset, value, 0, length);
                fields._values[id] = value;
                offset += length;
                previous = id;
            }
            return true;
        }

        private void Set(byte id, byte[] value)
        {
            if (id == 0) throw new ArgumentOutOfRangeException(nameof(id), "Les numeros de champ vont de 1 a 255.");
            _values[id] = value;
        }

        private bool TryGet(byte id, int size, out byte[] value) =>
            _values.TryGetValue(id, out value) && value.Length == size;

        private static byte[] BitConverterLe(ulong value, int size)
        {
            byte[] b = new byte[size];
            for (int i = 0; i < size; i++) b[i] = (byte)(value >> (8 * i));
            return b;
        }

        private static ulong ReadLe(byte[] b)
        {
            ulong v = 0;
            for (int i = 0; i < b.Length; i++) v |= (ulong)b[i] << (8 * i);
            return v;
        }
    }
}
