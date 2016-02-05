using System.Collections.Generic;

namespace UnityEditor.Experimental.Graph.Examples
{
    internal class AnimationDataSource : ICanvasDataSource
    {
        List<CanvasElement> m_Elements;

        public AnimationDataSource(List<CanvasElement> m_Data)
        {
            m_Elements = m_Data;
        }

        public CanvasElement[] FetchElements()
        {
            return m_Elements.ToArray();
        }

        public void DeleteElement(CanvasElement e)
        {
            m_Elements.Remove(e);
        }

        public void AddElement(CanvasElement e)
        {
            m_Elements.Add(e);
        }

        public void Connect(NodeAnchor a, NodeAnchor b)
        {
            m_Elements.Add(new Edge<NodeAnchor>(this, a, b));
        }
    }
}
