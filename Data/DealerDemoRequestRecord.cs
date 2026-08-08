using System;
using System.Data.Linq.Mapping;

namespace autodealer.dev.Data {
    [Table(Name = "dbo.DealerDemoRequests")]
    internal sealed class DealerDemoRequestRecord {
        [Column(IsPrimaryKey = true, CanBeNull = false)]
        public Guid RequestId { get; set; }

        [Column(CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public string BusinessName { get; set; }

        [Column(CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public string ContactName { get; set; }

        [Column(CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public string Email { get; set; }

        [Column(CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        public string Phone { get; set; }

        [Column(CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        public string CurrentWebsite { get; set; }

        [Column(CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        public int? LocationCount { get; set; }

        [Column(CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public string InventorySize { get; set; }

        [Column(CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public string PrimaryGoal { get; set; }

        [Column(CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public string PreferredContact { get; set; }

        [Column(CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public string Message { get; set; }

        [Column(CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public string Status { get; set; }

        [Column(CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public DateTime CreatedUtc { get; set; }

        [Column(CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        public DateTime? OwnerNotificationSentUtc { get; set; }

        [Column(CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        public DateTime? CustomerConfirmationSentUtc { get; set; }
    }
}
