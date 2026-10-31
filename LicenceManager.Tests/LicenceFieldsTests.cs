using System;
using System.Linq;
using Xunit;

namespace LicenceManager.Tests
{
    public class LicenceFieldsTests
    {
        private static readonly LicenceSigningKey Key = LicenceSigningKey.Create();
        private static readonly LicenceIssuer Issuer = new LicenceIssuer(Key);
        private static readonly LicenceValidator Validator = new LicenceValidator(Key.PublicKey);
        private static readonly DateTime Today = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

        private static LicenceData WithFields(Action<LicenceFields> fill)
        {
            var data = new LicenceData { ProductId = 2001, Client = "ACME" };
            fill(data.Fields);
            return data;
        }

        private static LicenceData RoundTrip(LicenceData data)
        {
            LicenceCheck check = Validator.Validate(Issuer.Issue(data), data.ProductId, Today);
            Assert.True(check.IsValid, check.Status.ToString());
            return check.Data;
        }

        [Fact]
        public void Aller_retour_de_tous_les_types()
        {
            LicenceData read = RoundTrip(WithFields(f =>
            {
                f.SetText(1, "Édition Pro");
                f.SetInt(2, -123456);
                f.SetDate(3, new DateTime(2028, 6, 30));
                f.SetBool(4, true);
                f.SetMask(5, ulong.MaxValue);
                f.SetBool(200, false);
            }));

            Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 200 }, read.Fields.Ids.ToArray());
            Assert.True(read.Fields.TryGetText(1, out string text) && text == "Édition Pro");
            Assert.True(read.Fields.TryGetInt(2, out int n) && n == -123456);
            Assert.True(read.Fields.TryGetDate(3, out DateTime d) && d == new DateTime(2028, 6, 30));
            Assert.True(read.Fields.TryGetBool(4, out bool yes) && yes);
            Assert.True(read.Fields.TryGetMask(5, out ulong mask) && mask == ulong.MaxValue);
            Assert.True(read.Fields.TryGetBool(200, out bool no) && !no);
        }

        [Fact]
        public void Champ_absent_ou_d_un_autre_type()
        {
            LicenceData read = RoundTrip(WithFields(f => f.SetInt(2, 5)));

            Assert.False(read.Fields.TryGetInt(9, out _));
            Assert.False(read.Fields.TryGetBool(2, out _));
            Assert.False(read.Fields.TryGetDate(2, out _));
            Assert.False(read.Fields.TryGetMask(2, out _));
            Assert.Equal(new byte[] { 5, 0, 0, 0 }, read.Fields.GetRaw(2));
            Assert.Null(read.Fields.GetRaw(9));
        }

        [Fact]
        public void Sans_champ_la_cle_ne_grossit_que_d_un_octet()
        {
            LicenceData read = RoundTrip(new LicenceData { ProductId = 1000 });
            Assert.Equal(0, read.Fields.Count);
        }

        [Fact]
        public void Limites_de_taille()
        {
            Assert.Throws<ArgumentException>(() => new LicenceFields().SetText(1, new string('x', LicenceFields.TextMaxBytes + 1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LicenceFields().SetInt(0, 1));

            // 64 octets exactement : 2 textes de 30 octets (2 x 32) passent, un octet de plus non.
            LicenceData ok = WithFields(f => { f.SetText(1, new string('a', 30)); f.SetText(2, new string('b', 30)); });
            Assert.Equal(new string('b', 30), RoundTrip(ok).Fields.TryGetText(2, out string t) ? t : null);

            LicenceData tooBig = WithFields(f => { f.SetText(1, new string('a', 30)); f.SetText(2, new string('b', 31)); });
            Assert.Throws<ArgumentException>(() => Issuer.Issue(tooBig));
        }

        [Fact]
        public void Champ_modifie_avec_crc_recalcule_est_refuse_par_la_signature()
        {
            var data = WithFields(f => f.SetInt(2, 10));
            Assert.True(LicenceKeyCodec.TryDecode(Issuer.Issue(data), out byte[] raw));
            raw[LicenceData.FixedSize + 4 + 2] ^= 0x01; // valeur du champ 2 (client "ACME" = 4 octets)

            Assert.Equal(LicenceStatus.InvalidSignature, Validator.Validate(LicenceKeyCodec.Encode(raw), 2001, Today).Status);
        }

        [Theory]
        [InlineData(new byte[] { 2, 1, 0, 1, 1, 0 })] // numeros non croissants
        [InlineData(new byte[] { 1, 1, 0, 1, 1, 0 })] // numero en double
        [InlineData(new byte[] { 0, 1, 0 })]          // numero 0 reserve
        [InlineData(new byte[] { 1, 5, 0 })]          // taille au-dela du bloc
        [InlineData(new byte[] { 1 })]                // entete incomplete
        public void Bloc_de_champs_incoherent_est_refuse(byte[] block)
        {
            Assert.False(LicenceFields.TryParse(block, 0, block.Length, out _));
        }

        [Fact]
        public void Bloc_de_plus_de_64_octets_est_refuse()
        {
            byte[] block = new byte[65];
            block[0] = 1;
            block[1] = 63;
            Assert.False(LicenceFields.TryParse(block, 0, block.Length, out _));
        }
    }
}
