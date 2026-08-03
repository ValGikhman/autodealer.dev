using autodealer.dev.Models;

namespace autodealer.dev.Services {
    public interface IClientAccountService {
        AccountCreatedViewModel Create(AccountRegistrationViewModel model);
        AccountDashboardViewModel Authenticate(string email, string password);
        AccountDashboardViewModel GetDashboard(string email);
    }
}
