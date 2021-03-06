using UnityEditor.Graphing;

namespace UnityEditor.ShaderGraph
{
    public interface IMayRequireScreenPosition
    {
        bool RequiresScreenPosition();
    }

    public static class MayRequireScreenPositionExtensions
    {
        public static bool RequiresScreenPosition(this ISlot slot)
        {
            var mayRequireScreenPosition = slot as IMayRequireScreenPosition;
            return mayRequireScreenPosition != null && mayRequireScreenPosition.RequiresScreenPosition();
        }
    }
}
