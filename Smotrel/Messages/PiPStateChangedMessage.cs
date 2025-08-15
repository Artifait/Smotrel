
namespace Smotrel.Messages
{
    public sealed class PiPStateChangedMessage
    {
        public bool IsActive { get; }
        public PiPStateChangedMessage(bool isActive) => IsActive = isActive;
    }
}
