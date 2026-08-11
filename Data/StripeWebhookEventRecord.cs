using System;
using System.Data.Linq.Mapping;

namespace autodealer.dev.Data {
    [Table(Name = "dbo.StripeWebhookEvents")]
    internal sealed class StripeWebhookEventRecord {
        [Column(IsPrimaryKey = true, DbType = "NVarChar(255) NOT NULL", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public string StripeEventId { get; set; }

        [Column(DbType = "NVarChar(100) NOT NULL", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public string EventType { get; set; }

        [Column(DbType = "DateTime2(3) NOT NULL", UpdateCheck = UpdateCheck.Never)]
        public DateTime EventCreatedUtc { get; set; }

        [Column(DbType = "DateTime2(3) NOT NULL", UpdateCheck = UpdateCheck.Never)]
        public DateTime ProcessedUtc { get; set; }
    }
}
