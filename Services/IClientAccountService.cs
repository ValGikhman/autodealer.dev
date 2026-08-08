using autodealer.dev.Models;

namespace autodealer.dev.Services {
    public interface IClientAccountService {
        AccountCreatedViewModel Create(AccountRegistrationViewModel model);
        AccountCreatedViewModel Create(AccountRegistrationViewModel model, string clientNumber, bool emailTemporaryPassword);
        bool IsEmailAvailable(string email, long? excludedClientId);
        AccountDashboardViewModel Authenticate(string email, string password);
        AccountDashboardViewModel GetDashboard(string email);
    }
}
