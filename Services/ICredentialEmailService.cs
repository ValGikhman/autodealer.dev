namespace autodealer.dev.Services {
    public interface ICredentialEmailService {
        bool Send(string firstName, string email, string clientNumber, string apiKey, string planCode);
    }
}
