using System.Linq;
using UnityEditor.Graphs;
using UnityEngine;
using System.Collections.Generic;

namespace UnityEditor.MaterialGraph
{
    public abstract class BaseMaterialGraph : Graph
    {

        [SerializeField]
        List<Rect> m_CommentBoxes = new List<Rect>();
        public List<Rect> commentBoxes
        {
            get
            {
                return m_CommentBoxes;
            }
        }

        private PreviewRenderUtility m_PreviewUtility;

        public PreviewRenderUtility previewUtility
        {
            get
            {
                if (m_PreviewUtility == null)
                {
                    m_PreviewUtility = new PreviewRenderUtility();
                    EditorUtility.SetCameraAnimateMaterials(m_PreviewUtility.m_Camera, true);
                }

                return m_PreviewUtility;
            }
        }

        public bool requiresRepaint
        {
            get { return isAwake && nodes.Any(x => x is IRequiresTime); }
        }
        
        public override void RemoveEdge(Edge e)
        {
            base.RemoveEdge(e);
            RevalidateGraph();
        }

        public void RemoveEdgeNoRevalidate(Edge e)
        {
            base.RemoveEdge(e);
        }

        public override void RemoveNode(Node node, bool destroyNode = false)
        {
            if (node is BaseMaterialNode)
            {
                if (!((BaseMaterialNode) node).canDeleteNode)
                    return;
            }
            base.RemoveNode(node, destroyNode);
        }

        public override Edge Connect(Slot fromSlot, Slot toSlot)
        {
            Slot outputSlot = null;
            Slot inputSlot = null;

            // output must connect to input
            if (fromSlot.isOutputSlot)
                outputSlot = fromSlot;
            else if (fromSlot.isInputSlot)
                inputSlot = fromSlot;

            if (toSlot.isOutputSlot)
                outputSlot = toSlot;
            else if (toSlot.isInputSlot)
                inputSlot = toSlot;

            if (inputSlot == null || outputSlot == null)
                return null;

            // remove any inputs that exits before adding
            foreach (var edge in inputSlot.edges.ToArray())
            {
                Debug.Log("Removing existing edge:" + edge);
                // call base here as we DO NOT want to
                // do expensive shader regeneration
                base.RemoveEdge(edge);
            }
            
            var newEdge = base.Connect(outputSlot, inputSlot);
            
            Debug.Log("Connected edge: " + newEdge);
            var toNode = inputSlot.node as BaseMaterialNode;
            var fromNode = outputSlot.node as BaseMaterialNode;

            if (fromNode == null || toNode == null)
                return newEdge;
            
            RevalidateGraph();
            return newEdge;
        }

        public virtual void RevalidateGraph()
        {
            var bmns = nodes.Where(x => x is BaseMaterialNode).Cast<BaseMaterialNode>().ToList();

            foreach (var node in bmns)
                node.InvalidateNode();

            foreach (var node in bmns)
            {
                node.ValidateNode();
            }
        }

        public override void AddNode(Node node)
        {
            base.AddNode(node);
            AssetDatabase.AddObjectToAsset(node, this);
            RevalidateGraph();
        }

        public void AddCommentBox(Rect r)
        {
            m_CommentBoxes.Add(r);
        }

        public void AddNodeNoValidate(Node node)
        {
            base.AddNode(node);
            AssetDatabase.AddObjectToAsset(node, this);
        }

        protected void AddMasterNodeNoAddToAsset(Node node)
        {
            base.AddNode(node);
        }
    }
}
