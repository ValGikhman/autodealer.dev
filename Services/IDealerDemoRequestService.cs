using autodealer.dev.Models;

namespace autodealer.dev.Services {
    public interface IDealerDemoRequestService {
        bool Send(DealerDemoRequestViewModel request);
    }
}
