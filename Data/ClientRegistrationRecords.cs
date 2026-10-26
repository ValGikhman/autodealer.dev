using System;
using System.Data.Linq;
using System.Data.Linq.Mapping;

namespace autodealer.dev.Data {
    // Same GetTable<T> mapping pattern as ClientEmailVerificationRecord.
    [Table(Name = "dbo.ClientRegistrationAudit")]
    internal sealed class ClientRegistrationAuditRecord {
        [Column(IsPrimaryKey = true)] public Guid AuditId { get; set; }
        [Column(DbType = "DateTime2(3) NOT NULL")] public DateTime RecordedUtc { get; set; }
        [Column(DbType = "DateTime2(3)", CanBeNull = true)] public DateTime? CompletedUtc { get; set; }
        [Column(CanBeNull = true)] public long? ClientId { get; set; }
        [Column(DbType = "VarChar(12) NOT NULL", CanBeNull = false)] public string EventType { get; set; }
        [Column(DbType = "VarChar(12) NOT NULL", CanBeNull = false)] public string Source { get; set; }
        [Column(DbType = "VarChar(16) NOT NULL", CanBeNull = false)] public string Outcome { get; set; }
        [Column(DbType = "VarChar(45)", CanBeNull = true)] public string SourceIp { get; set; }
        [Column(DbType = "VarChar(45)", CanBeNull = true)] public string PeerIp { get; set; }
        [Column(DbType = "NVarChar(1024)", CanBeNull = true)] public string ForwardedFor { get; set; }
        [Column] public bool TrustedProxy { get; set; }
        [Column(DbType = "NVarChar(512)", CanBeNull = true)] public string UserAgent { get; set; }
        [Column(DbType = "NVarChar(256)", CanBeNull = true)] public string AcceptLanguage { get; set; }
        [Column(DbType = "Binary(32)", CanBeNull = true)] public Binary VisitorHash { get; set; }
        [Column(DbType = "Char(2)", CanBeNull = true)] public string CountryCode { get; set; }
        [Column(DbType = "NVarChar(300)", CanBeNull = true)] public string RequestPath { get; set; }
        [Column(DbType = "NVarChar(32)", CanBeNull = true)] public string PlanCode { get; set; }
        [Column(DbType = "NVarChar(254)", CanBeNull = true)] public string Actor { get; set; }
    }

    [Table(Name = "dbo.ClientRegistrationIpBlocks")]
    internal sealed class ClientRegistrationIpBlockRecord {
        [Column(IsPrimaryKey = true, DbType = "VarChar(45) NOT NULL", CanBeNull = false)] public string SourceIp { get; set; }
        [Column(DbType = "DateTime2(3) NOT NULL")] public DateTime BlockedUntilUtc { get; set; }
        [Column(DbType = "DateTime2(3) NOT NULL")] public DateTime UpdatedUtc { get; set; }
        [Column(DbType = "NVarChar(254) NOT NULL", CanBeNull = false)] public string UpdatedBy { get; set; }
    }
}
