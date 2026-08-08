using autodealer.dev.Models;

namespace autodealer.dev.Services {
    public interface IAdminService {
        bool Authenticate(string userId, string password);
        AdminDashboardViewModel GetDashboard();
        System.Collections.Generic.IReadOnlyList<AdminClientEmailViewModel> GetClientEmails(long clientId);
        AdminCustomerAccountDetailViewModel GetClientAccountDetails(long clientId);
        AdminClientCreateViewModel GetNewClientDefaults();
        AdminClientEditViewModel GetClientForEdit(long clientId);
        AdminClientEditViewModel UpdateClient(AdminClientEditViewModel model);
        string DeleteClient(long clientId);
        string DeleteDemoRequest(System.Guid requestId);
        AdminDemoRequestEditViewModel GetNewDemoRequestDefaults();
        AdminDemoRequestEditViewModel GetDemoRequestForEdit(System.Guid requestId);
        AdminDemoRequestEditViewModel SaveDemoRequest(AdminDemoRequestEditViewModel model, bool create);
        AdminApiKeyEditViewModel GetApiKeyForEdit(long apiKeyId);
        AdminApiKeyEditViewModel UpdateApiKey(AdminApiKeyEditViewModel model);
        AdminSubscriptionEditViewModel GetNewSubscriptionDefaults(long clientId);
        AdminSubscriptionEditViewModel CreateSubscription(AdminSubscriptionEditViewModel model);
        AdminSubscriptionEditViewModel GetSubscriptionForEdit(long subscriptionId);
        AdminSubscriptionEditViewModel UpdateSubscription(AdminSubscriptionEditViewModel model);
    }
}
