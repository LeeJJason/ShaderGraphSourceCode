using System.Collections.Generic;
using System.Linq;
using UnityEditor.Experimental;
using UnityEditor.Experimental.Graph;
using UnityEngine;

namespace UnityEditor.MaterialGraph
{
    public class MaterialGraphDataSource : ICanvasDataSource
    {
        public MaterialGraph graph { get; set; }

        public CanvasElement[] FetchElements()
        {
            var drawableNodes = new List<DrawableMaterialNode>();
            Debug.Log("trying to convert");
            var pixelGraph = graph.currentGraph;
            foreach (var node in pixelGraph.nodes)
            {
                // add the nodes
                var bmn = node as BaseMaterialNode;
                drawableNodes.Add(new DrawableMaterialNode(bmn, (bmn is PixelShaderNode) ? 600.0f : 200.0f, typeof(Vector4), this));
            }

            // Add the edges now
            var drawableEdges = new List<Edge<NodeAnchor>>();
            foreach (var drawableMaterialNode in drawableNodes)
            {
                var baseNode = drawableMaterialNode.m_Node;
                foreach (var slot in baseNode.outputSlots)
                {
                    var sourceAnchor =  (NodeAnchor)drawableMaterialNode.Children().FirstOrDefault(x => x is NodeAnchor && ((NodeAnchor) x).m_Slot == slot);

                    foreach (var edge in slot.edges)
                    {
                        var targetNode = drawableNodes.FirstOrDefault(x => x.m_Node == edge.toSlot.node);
                        var targetAnchor = (NodeAnchor)targetNode.Children().FirstOrDefault(x => x is NodeAnchor && ((NodeAnchor) x).m_Slot == edge.toSlot);
                        drawableEdges.Add(new Edge<NodeAnchor>(this, sourceAnchor, targetAnchor));
                    }
                }
            }
            
            var toReturn = new List<CanvasElement>();
            toReturn.AddRange(drawableNodes.Select(x => (CanvasElement)x));
            toReturn.AddRange(drawableEdges.Select(x => (CanvasElement)x));
            
            Debug.LogFormat("REturning {0} nodes", toReturn.Count);
            return toReturn.ToArray();
        }

        public void DeleteElement(CanvasElement e)
        {
            Debug.Log("Trying to delete " + e);
            if (e is DrawableMaterialNode)
            {
                Debug.Log("Deleting node " + e + " " + ((DrawableMaterialNode) e).m_Node);
                graph.currentGraph.RemoveNode(((DrawableMaterialNode) e).m_Node);
            }
            else if (e is Edge<NodeAnchor>)
            {
                //find the edge
                var localEdge = (Edge<NodeAnchor>) e;
                var edge = graph.currentGraph.edges.FirstOrDefault(x => x.fromSlot == localEdge.Left.m_Slot && x.toSlot == localEdge.Right.m_Slot);
                graph.currentGraph.RemoveEdge(edge);
            }

            e.ParentCanvas().ReloadData();
        }

        public void Connect(NodeAnchor a, NodeAnchor b)
        {
            Debug.Log("Connecting: " + a + " " + b);
            var pixelGraph = graph.currentGraph;
            pixelGraph.Connect(a.m_Slot, b.m_Slot);
            //m_Elements.Add();
        }
    }
}
