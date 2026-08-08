namespace autodealer.dev.Services {
    public interface ICredentialEmailService {
        bool SendVerification(long clientId, string firstName, string lastName, string email, string verificationUrl);
        bool SendCredentials(long clientId, string businessName, string firstName, string lastName, string email, string phone, string clientNumber, string apiKey, string planCode, bool createdByAdmin);
    }
}
