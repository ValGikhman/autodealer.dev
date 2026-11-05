using System.Data.Linq.Mapping;

namespace autodealer.dev.Data {
    public partial class ApiKey {
        private bool quotaUnlimited;
        [Column(Storage="quotaUnlimited",DbType="Bit NOT NULL",CanBeNull=false,UpdateCheck=UpdateCheck.Never)]
        public bool IsQuotaUnlimited {
            get { return quotaUnlimited; }
            set {
                if(quotaUnlimited==value)return;
                SendPropertyChanging();
                quotaUnlimited=value;
                SendPropertyChanged("IsQuotaUnlimited");
            }
        }
    }
}
