using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace LicenceManager.Tests
{
    public class MachineCodeTests
    {
        private static readonly LicenceSigningKey Key = LicenceSigningKey.Create();
        private static readonly LicenceIssuer Issuer = new LicenceIssuer(Key);
        private static readonly LicenceValidator Validator = new LicenceValidator(Key.PublicKey);
        private static readonly DateTime Today = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

        private static readonly byte[] Uuid = Enumerable.Range(1, 16).Select(i => (byte)(i * 7)).ToArray();
        private static readonly string PosteA = MachineCode.Format(MachineCode.Derive("A:", Uuid));
        private static readonly string PosteB = MachineCode.Format(MachineCode.DeriveFromMachineGuid(" 4C4C4544-0042-3510-8051-B4C04F4E4D32 "));

        [Fact]
        public void Format_du_code_poste()
        {
            Assert.Matches("^[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}$", PosteA);
            Assert.NotEqual(PosteA, PosteB);
            Assert.True(MachineCode.TryNormalize(" " + PosteA.ToLowerInvariant().Replace("-", " ").Replace('0', 'o') + " ", out string normalized));
            Assert.Equal(PosteA, normalized);
        }

        [Theory]
        [InlineData("")]
        [InlineData("ABCD")]
        [InlineData("0000-0000-0000-0000")]
        public void Code_poste_invalide(string text)
        {
            Assert.False(MachineCode.TryNormalize(text, out _));
        }

        [Fact]
        public void Faute_de_frappe_dans_le_code_poste_est_detectee()
        {
            string typo = (PosteA[0] == 'A' ? "B" : "A") + PosteA.Substring(1);
            Assert.False(MachineCode.TryNormalize(typo, out _));
            Assert.Throws<ArgumentException>(() => Issuer.Issue(new LicenceData { ProductId = 1, MachineCode = typo }));
        }

        [Fact]
        public void Licence_liee_valide_uniquement_sur_son_poste()
        {
            string key = Issuer.Issue(new LicenceData { ProductId = 1000, MachineCode = PosteA.ToLowerInvariant() });

            LicenceCheck ok = Validator.Validate(key, 1000, Today, PosteA);
            Assert.Equal(LicenceStatus.Valid, ok.Status);
            Assert.Equal(PosteA, ok.Data.MachineCode);
            Assert.Equal(LicenceStatus.WrongMachine, Validator.Validate(key, 1000, Today, PosteB).Status);
            Assert.Equal(LicenceStatus.WrongMachine, Validator.Validate(key, 1000, Today, null).Status);
            // Le produit est controle avant le poste.
            Assert.Equal(LicenceStatus.WrongProduct, Validator.Validate(key, 2000, Today, PosteB).Status);
        }

        [Fact]
        public void Licence_non_liee_valide_sur_tout_poste()
        {
            string key = Issuer.Issue(new LicenceData { ProductId = 1000 });

            LicenceCheck check = Validator.Validate(key, 1000, Today, PosteB);
            Assert.True(check.IsValid);
            Assert.Null(check.Data.MachineCode);
            Assert.True(Validator.Validate(key, 1000, Today, null).IsValid);
        }

        [Fact]
        public void Poste_modifie_avec_crc_recalcule_est_refuse_par_la_signature()
        {
            Assert.True(LicenceKeyCodec.TryDecode(Issuer.Issue(new LicenceData { ProductId = 1, MachineCode = PosteA }), out byte[] raw));
            raw[25] ^= 0x01;

            Assert.Equal(LicenceStatus.InvalidSignature, Validator.Validate(LicenceKeyCodec.Encode(raw), 1, Today, PosteA).Status);
        }

        // --- Lecture SMBIOS ------------------------------------------------------------

        private static byte[] Smbios(byte major, byte minor, params byte[][] structures)
        {
            var table = structures.SelectMany(x => x).ToArray();
            var header = new List<byte> { 0, major, minor, 0 };
            header.AddRange(BitConverter.GetBytes((uint)table.Length));
            return header.Concat(table).ToArray();
        }

        // Structure de type 0 (BIOS) avec deux chaines, pour verifier le saut de la zone de chaines.
        private static readonly byte[] Bios =
            new byte[] { 0, 0x18, 0, 0 }.Concat(new byte[0x18 - 4]).Concat(Encoding.ASCII.GetBytes("Vendor\0Version\0\0")).ToArray();

        private static byte[] SystemInfo(byte[] uuid)
        {
            byte[] s = new byte[0x1B];
            s[0] = 1;
            s[1] = 0x1B;
            Buffer.BlockCopy(uuid, 0, s, 8, 16);
            return s.Concat(new byte[] { 0, 0 }).ToArray(); // aucune chaine
        }

        private static readonly byte[] End = { 127, 4, 0, 0, 0, 0 };

        [Fact]
        public void Uuid_lu_apres_une_structure_avec_chaines()
        {
            Assert.Equal(Uuid, MachineCode.ParseSmbiosUuid(Smbios(3, 4, Bios, SystemInfo(Uuid), End)));
        }

        [Fact]
        public void Uuid_bouche_trou_ou_absent_est_ignore()
        {
            byte[] oem = { 0x00, 0x02, 0x00, 0x03, 0x00, 0x04, 0x00, 0x05, 0x00, 0x06, 0x00, 0x07, 0x00, 0x08, 0x00, 0x09 };
            Assert.Null(MachineCode.ParseSmbiosUuid(Smbios(3, 4, SystemInfo(new byte[16]), End)));
            Assert.Null(MachineCode.ParseSmbiosUuid(Smbios(3, 4, SystemInfo(Enumerable.Repeat((byte)0xFF, 16).ToArray()), End)));
            Assert.Null(MachineCode.ParseSmbiosUuid(Smbios(3, 4, SystemInfo(oem), End)));
            Assert.Null(MachineCode.ParseSmbiosUuid(Smbios(3, 4, Bios, End)));
            Assert.Null(MachineCode.ParseSmbiosUuid(Smbios(2, 0, SystemInfo(Uuid), End)));
        }

        [Fact]
        public void Table_smbios_tronquee_ou_vide_ne_plante_pas()
        {
            byte[] full = Smbios(3, 4, Bios, SystemInfo(Uuid), End);
            for (int n = 0; n < full.Length; n++)
                MachineCode.ParseSmbiosUuid(full.Take(n).ToArray());
            Assert.Null(MachineCode.ParseSmbiosUuid(null));
        }

        [Fact]
        public void Poste_courant_coherent_avec_la_plateforme()
        {
            string current = MachineCode.Current;
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                Assert.NotNull(current);
                Assert.True(MachineCode.TryNormalize(current, out _));
                Assert.Equal(current, MachineCode.Current); // stable
            }
            else
            {
                Assert.Null(current);
            }
        }
    }
}
