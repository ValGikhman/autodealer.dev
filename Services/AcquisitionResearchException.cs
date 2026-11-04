using System.IO;
namespace autodealer.dev.Services {
    public sealed class AcquisitionResearchException : IOException {
        public string Code { get; private set; }
        public AcquisitionResearchException(string code, string message) : base(message) { Code = code; }
    }
}
