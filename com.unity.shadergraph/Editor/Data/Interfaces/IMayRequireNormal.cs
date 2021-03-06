using UnityEditor.Graphing;

namespace UnityEditor.ShaderGraph
{
    public interface IMayRequireNormal
    {
        NeededCoordinateSpace RequiresNormal();
    }

    public static class MayRequireNormalExtensions
    {
        public static NeededCoordinateSpace RequiresNormal(this ISlot slot)
        {
            var mayRequireNormal = slot as IMayRequireNormal;
            return mayRequireNormal != null ? mayRequireNormal.RequiresNormal() : NeededCoordinateSpace.None;
        }
    }
}
