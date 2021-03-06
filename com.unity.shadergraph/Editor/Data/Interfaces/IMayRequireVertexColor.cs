using UnityEditor.Graphing;

namespace UnityEditor.ShaderGraph
{
    interface IMayRequireVertexColor
    {
        bool RequiresVertexColor();
    }

    public static class MayRequireVertexColorExtensions
    {
        public static bool RequiresVertexColor(this ISlot slot)
        {
            var mayRequireVertexColor = slot as IMayRequireVertexColor;
            return mayRequireVertexColor != null && mayRequireVertexColor.RequiresVertexColor();
        }
    }
}
