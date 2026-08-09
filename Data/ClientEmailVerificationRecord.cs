using System;
using System.Data.Linq.Mapping;

namespace autodealer.dev.Data {
    [Table(Name = "dbo.ClientEmailVerifications")]
    internal sealed class ClientEmailVerificationRecord {
        [Column(
            IsPrimaryKey = true,
            IsDbGenerated = true,
            AutoSync = AutoSync.OnInsert,
            DbType = "BigInt NOT NULL IDENTITY",
            UpdateCheck = UpdateCheck.Never)]
        public long VerificationId { get; set; }

        [Column(DbType = "BigInt NOT NULL", UpdateCheck = UpdateCheck.Never)]
        public long ClientId { get; set; }

        [Column(DbType = "Char(64) NOT NULL", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public string TokenHash { get; set; }

        [Column(DbType = "Bit NOT NULL", UpdateCheck = UpdateCheck.Never)]
        public bool CreatedByAdmin { get; set; }

        [Column(DbType = "DateTime2(3) NOT NULL", UpdateCheck = UpdateCheck.Never)]
        public DateTime ExpiresUtc { get; set; }

        [Column(DbType = "DateTime2(3) NOT NULL", UpdateCheck = UpdateCheck.Never)]
        public DateTime CreatedUtc { get; set; }

        [Column(DbType = "DateTime2(3)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        public DateTime? UsedUtc { get; set; }

        [Column(DbType = "DateTime2(3)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        public DateTime? CredentialsSentUtc { get; set; }
    }
}
