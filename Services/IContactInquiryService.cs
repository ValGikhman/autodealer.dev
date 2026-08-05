using autodealer.dev.Models;

namespace autodealer.dev.Services {
    public interface IContactInquiryService {
        void Send(ContactInquiryViewModel inquiry);
    }
}
