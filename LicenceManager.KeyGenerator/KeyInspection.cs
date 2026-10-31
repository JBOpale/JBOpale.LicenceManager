using System;

namespace LicenceManager.KeyGeneratorApp
{
    // Contenu d'une cle relue par Inspect() (signature verifiee avec la paire de cles chargee).
    public sealed class KeyInspection
    {
        public ProductProfile Profile { get; set; }
        public int ProductId { get; set; }
        public string Client { get; set; }
        public DateTime? Expiration { get; set; } // null = jamais
        public bool Expired { get; set; }
        public ulong? Features { get; set; }      // null si le profil n'a pas de Features
        public ulong LicenceId { get; set; }
        public ushort Seats { get; set; }         // 0 = non limite
        public string MachineCode { get; set; }   // null = tout poste
        public LicenceFields Fields { get; set; } // a decrire avec Profile.Fields
    }
}
