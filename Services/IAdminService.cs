using autodealer.dev.Models;

namespace autodealer.dev.Services {
    public interface IAdminService {
        bool Authenticate(string userId, string password);
        AdminDashboardViewModel GetDashboard();
        System.Collections.Generic.IReadOnlyList<AdminClientEmailViewModel> GetClientEmails(long clientId);
    }
}
