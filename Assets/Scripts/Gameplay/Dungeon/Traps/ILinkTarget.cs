namespace Gameplay.Dungeon.Traps
{
    /// <summary>압력판(PressurePlate)이 제어하는 대상(문/다트/가시).</summary>
    public interface ILinkTarget
    {
        void Activate();
        void Deactivate();
    }
}
