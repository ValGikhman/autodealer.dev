namespace autodealer.dev.Services {
    public interface ICredentialEmailService {
        bool Send(string businessName, string firstName, string lastName, string email, string phone, string clientNumber, string apiKey, string planCode);
    }
}
