namespace TarodevController
{
    // Any movement script that honors this will ignore input when true (without killing momentum).
    public interface IInputLockable
    {
        bool InputLocked { get; set; }
    }
}
