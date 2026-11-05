using System.Data.Linq.Mapping;
namespace autodealer.dev.Data {
    public partial class ApiKey {
        [Column(DbType = "NVarChar(MAX)", CanBeNull = true, UpdateCheck = UpdateCheck.Never)]
        public string ProtectedCredential { get; set; }
    }
}
