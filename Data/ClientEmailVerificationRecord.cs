using System;
using System.Data.Linq.Mapping;

namespace autodealer.dev.Data {
    [Table(Name = "dbo.ClientEmailVerifications")]
    internal sealed class ClientEmailVerificationRecord {
        [Column(IsPrimaryKey = true, IsDbGenerated = true, AutoSync = AutoSync.OnInsert)]
        public long VerificationId { get; set; }

        [Column]
        public long ClientId { get; set; }

        [Column]
        public string TokenHash { get; set; }

        [Column]
        public bool CreatedByAdmin { get; set; }

        [Column]
        public DateTime ExpiresUtc { get; set; }

        [Column]
        public DateTime CreatedUtc { get; set; }

        [Column(CanBeNull = true)]
        public DateTime? UsedUtc { get; set; }

        [Column(CanBeNull = true)]
        public DateTime? CredentialsSentUtc { get; set; }
    }
}
