using System.Collections.Generic;
using System.Linq;
using UnityEditor.Experimental;
using UnityEditorInternal.Experimental;
using UnityEditor.Experimental.Graph;
using UnityEditor.Graphs;
using UnityEngine;

namespace UnityEditor.MaterialGraph
{
    public class MaterialGraphDataSource : ICanvasDataSource
    {
        readonly List<DrawableMaterialNode> m_DrawableNodes = new List<DrawableMaterialNode>();
        
        public MaterialGraph graph { get; set; }

        public ICollection<DrawableMaterialNode> lastGeneratedNodes
        {
            get { return m_DrawableNodes; }
        }

        public CanvasElement[] FetchElements()
        {
            m_DrawableNodes.Clear();
            Debug.Log("trying to convert");
            var pixelGraph = graph.currentGraph;
            foreach (var node in pixelGraph.nodes)
            {
                // add the nodes
                var bmn = node as BaseMaterialNode;
                m_DrawableNodes.Add(new DrawableMaterialNode(bmn, (bmn is PixelShaderNode) ? 600.0f : 200.0f, this));
            }

            // Add the edges now
            var drawableEdges = new List<Edge<NodeAnchor>>();
            foreach (var drawableMaterialNode in m_DrawableNodes)
            {
                var baseNode = drawableMaterialNode.m_Node;
                foreach (var slot in baseNode.outputSlots)
                {
                    var sourceAnchor =  (NodeAnchor)drawableMaterialNode.Children().FirstOrDefault(x => x is NodeAnchor && ((NodeAnchor) x).m_Slot == slot);

                    foreach (var edge in slot.edges)
                    {
                        var targetNode = m_DrawableNodes.FirstOrDefault(x => x.m_Node == edge.toSlot.node);
                        var targetAnchor = (NodeAnchor)targetNode.Children().FirstOrDefault(x => x is NodeAnchor && ((NodeAnchor) x).m_Slot == edge.toSlot);
                        drawableEdges.Add(new Edge<NodeAnchor>(this, sourceAnchor, targetAnchor));
                    }
                }
            }
            
            // Add proxy inputs for when edges are not connect
            var nullInputSlots = new List<NullInputProxy>();
            foreach (var drawableMaterialNode in m_DrawableNodes)
            {
                var baseNode = drawableMaterialNode.m_Node;
                // grab the input slots where there are no edges
                foreach (var slot in baseNode.GetDrawableInputProxies())
                {
                    // if there is no anchor, continue
                    // this can happen if we are in collapsed mode
                    var sourceAnchor = (NodeAnchor)drawableMaterialNode.Children().FirstOrDefault(x => x is NodeAnchor && ((NodeAnchor)x).m_Slot == slot);
                    if (sourceAnchor == null)
                        continue;

                    nullInputSlots.Add(new NullInputProxy(slot, sourceAnchor));
                }
            }

            var toReturn = new List<CanvasElement>();
            toReturn.AddRange(m_DrawableNodes.Select(x => (CanvasElement)x));
            toReturn.AddRange(drawableEdges.Select(x => (CanvasElement)x));
            toReturn.AddRange(nullInputSlots.Select(x => (CanvasElement)x));

            // find the highest z-index
            // and make all comment boxes have indexes lower than that.
            int highestZIndex = int.MinValue;
            foreach ( CanvasElement e in toReturn )
            {
                if ( e.zIndex > highestZIndex )
                {
                    highestZIndex = e.zIndex;
                }
            }

            // Add comment boxes
            DrawableCommentBox.registerCommentBoxMoveEvent(CommentBoxMoved);
            var commentBoxes = new List<DrawableCommentBox>();
            Debug.Log(pixelGraph.commentBoxes);
            foreach (var commentBox in pixelGraph.commentBoxes)
            {
                DrawableCommentBox toAdd = new DrawableCommentBox(commentBox);
                toAdd.zIndex = highestZIndex + 1;
                commentBoxes.Add(toAdd);
            }

            toReturn.AddRange(commentBoxes.Select(x => (CanvasElement)x));

            //toReturn.Add(new FloatingPreview(new Rect(Screen.width - 300, Screen.height - 300, 300, 300), pixelGraph.nodes.FirstOrDefault(x => x is PixelShaderNode)));

            Debug.LogFormat("Returning {0} nodes", toReturn.Count);
            return toReturn.ToArray();
        }

        public void DeleteElement(CanvasElement e)
        {
            // do nothing here, we want to use delete elements.
            // delete elements ensures that edges are deleted before nodes.
        }

        public void DeleteElements(List<CanvasElement> elements)
        {
            // delete selected edges first
            foreach (var e in elements.Where(x => x is Edge<NodeAnchor>))
            {
                //find the edge
                var localEdge = (Edge<NodeAnchor>) e;
                var edge = graph.currentGraph.edges.FirstOrDefault(x => x.fromSlot == localEdge.Left.m_Slot && x.toSlot == localEdge.Right.m_Slot);

                Debug.Log("Deleting edge " + edge);
                graph.currentGraph.RemoveEdgeNoRevalidate(edge);
            }

            // now delete edges that the selected nodes use
            foreach (var e in elements.Where(x => x is DrawableMaterialNode))
            {
                var node = ((DrawableMaterialNode) e).m_Node;
                if (!node.canDeleteNode)
                    continue;

                foreach (var slot in node.slots)
                {
                    for (int index = slot.edges.Count -1; index >= 0; --index)
                    {
                        var edge = slot.edges[index];
                        Debug.Log("Deleting edge " + edge);
                        graph.currentGraph.RemoveEdgeNoRevalidate(edge);
                    }
                }
            }

            // now delete the nodes
            foreach (var e in elements.Where(x => x is DrawableMaterialNode))
            {
                var node = ((DrawableMaterialNode)e).m_Node;
                if (!node.canDeleteNode)
                    continue;

                Debug.Log("Deleting node " + e + " " + node);
                graph.currentGraph.RemoveNode(node);
            }

            // now delete commentboxes
            foreach (var e in elements.Where(x => x is DrawableCommentBox))
            {
                var commentBox = (DrawableCommentBox)e;
                Debug.Log("Deleting comment box " + commentBox);
                graph.currentGraph.RemoveCommentBox(commentBox.m_CommentBox);
            }

            graph.currentGraph.RevalidateGraph();
        }

        // For detecting movement of commentboxes
        private void CommentBoxMoved(DrawableCommentBox commentbox, Vector2 motion)
        {
            foreach (var node in m_DrawableNodes)
            {
                if ( RectUtils.Contains(commentbox.m_CommentBox.m_Rect, node.boundingRect) )
                {
                    Vector3 tx = node.translation;
                    tx.x += motion.x;
                    tx.y += motion.y;
                    node.translation = tx;
                    node.UpdateModel(UpdateType.Candidate);
                }
            }
        }

        public void Connect(NodeAnchor a, NodeAnchor b)
        {
            var pixelGraph = graph.currentGraph;
            pixelGraph.Connect(a.m_Slot, b.m_Slot);
        }

        private string m_LastPath;
        public void Export(bool quickExport)
        {
            var path = quickExport ? m_LastPath : EditorUtility.SaveFilePanelInProject("Export shader to file...", "shader.shader", "shader", "Enter file name");
            m_LastPath = path; // For quick exporting
            if (!string.IsNullOrEmpty(path))
                graph.ExportShader(path);
            else
                EditorUtility.DisplayDialog("Export Shader Error", "Cannot export shader", "Ok");
        }
    }

    public class FloatingPreview : CanvasElement
    {
        private BaseMaterialNode m_Node;

        public FloatingPreview(Rect position, Node node)
        {
            m_Node = node as BaseMaterialNode;
            m_Translation = new Vector2(position.x, position.y);
            m_Scale = new Vector3(position.width, position.height, 1);
            m_Caps |= Capabilities.Floating | Capabilities.Unselectable;
        }

        public override void Render(Rect parentRect, Canvas2D canvas)
        {
            var drawArea = new Rect(0, 0, scale.x, scale.y);
            Color backgroundColor = new Color(0.0f, 0.0f, 0.0f, 0.7f);
            EditorGUI.DrawRect(drawArea, backgroundColor);

            drawArea.width -= 10;
            drawArea.height -= 10;
            drawArea.x += 5;
            drawArea.y += 5;

            GL.sRGBWrite = (QualitySettings.activeColorSpace == ColorSpace.Linear);
            GUI.DrawTexture(drawArea, m_Node.RenderPreview(new Rect(0, 0, drawArea.width, drawArea.height)), ScaleMode.StretchToFill, false);
            GL.sRGBWrite = false;

            Invalidate();
            canvas.Repaint();
        }
    }
}
