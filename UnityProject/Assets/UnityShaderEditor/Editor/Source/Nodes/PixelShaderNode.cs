using System;
using UnityEngine;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor.Graphs;

namespace UnityEditor.MaterialGraph
{
    [Title("Output/Pixel Shader")]
    class PixelShaderNode : BaseMaterialNode, IGeneratesBodyCode
    {
        public const string kAlbedoSlotName = "Albedo";
        public const string kSpecularSlotName = "Specular";
        public const string kNormalSlotName = "Normal";
        public const string kEmissionSlotName = "Emission";
        public const string kMetallicSlotName = "Metallic";
        public const string kSmoothnessSlotName = "Smoothness";
        public const string kOcclusion = "Occlusion";
        public const string kAlphaSlotName = "Alpha";

        [SerializeField]
        private string m_LightFunctionClassName;

        private static List<BaseLightFunction> s_LightFunctions;

        protected override int previewWidth
        {
            get { return 300; }
        }
        
        protected override int previewHeight
        {
            get { return 300; }
        }

        public override void OnEnable()
        {
            base.OnEnable();

            AddSlot(new MaterialGraphSlot(new Slot(SlotType.InputSlot, kAlbedoSlotName), SlotValueType.Vector3));
            AddSlot(new MaterialGraphSlot(new Slot(SlotType.InputSlot, kNormalSlotName), SlotValueType.Vector3));
            AddSlot(new MaterialGraphSlot(new Slot(SlotType.InputSlot, kSpecularSlotName), SlotValueType.Vector3));
            AddSlot(new MaterialGraphSlot(new Slot(SlotType.InputSlot, kEmissionSlotName), SlotValueType.Vector3));
            AddSlot(new MaterialGraphSlot(new Slot(SlotType.InputSlot, kMetallicSlotName), SlotValueType.Vector1));
            AddSlot(new MaterialGraphSlot(new Slot(SlotType.InputSlot, kSmoothnessSlotName), SlotValueType.Vector1));
            AddSlot(new MaterialGraphSlot(new Slot(SlotType.InputSlot, kOcclusion), SlotValueType.Vector1));
            AddSlot(new MaterialGraphSlot(new Slot(SlotType.InputSlot, kAlphaSlotName), SlotValueType.Vector1));

            // clear out slot names that do not match the slots 
            // we support
            RemoveSlotsNameNotMatching(
                new[]
                {
                    kAlbedoSlotName, 
                    kNormalSlotName, 
                    kSpecularSlotName, 
                    kEmissionSlotName, 
                    kMetallicSlotName, 
                    kSmoothnessSlotName, 
                    kOcclusion, 
                    kAlphaSlotName
                });
        }

        public override void OnCreate()
        {
            name = "PixelMaster";
            base.OnCreate();
        }

        private static List<BaseLightFunction> GetLightFunctions()
        {
            if (s_LightFunctions == null)
            {
                s_LightFunctions = new List<BaseLightFunction>();

                foreach (Type type in Assembly.GetAssembly(typeof(BaseLightFunction)).GetTypes())
                {
                    if (type.IsClass && !type.IsAbstract && (type.IsSubclassOf(typeof(BaseLightFunction))))
                    {
                        var func = Activator.CreateInstance(type) as BaseLightFunction;
                        s_LightFunctions.Add(func);
                    }
                }
            }
            return s_LightFunctions;
        }

        private BaseLightFunction GetLightFunction()
        {
            var lightFunctions = GetLightFunctions();
            var lightFunction = lightFunctions.FirstOrDefault(x => x.GetType().ToString() == m_LightFunctionClassName);

            if (lightFunction == null && lightFunctions.Count > 0)
                lightFunction = lightFunctions[0];

            return lightFunction;
        }

        public virtual void GenerateLightFunction(ShaderGenerator visitor)
        {
            var lightFunction = GetLightFunction();
            lightFunction.GenerateLightFunctionName(visitor);
            lightFunction.GenerateLightFunctionBody(visitor);
        }

        public void GenerateSurfaceOutput(ShaderGenerator visitor)
        {
            var lightFunction = GetLightFunction();
            lightFunction.GenerateSurfaceOutputStructureName(visitor);
        }

        public virtual IEnumerable<Slot> FilterSlotsForLightFunction()
        {
            var lightFunction = GetLightFunction();
            return lightFunction.FilterSlots(slots);
        }

        public void GenerateNodeCode(ShaderGenerator shaderBody, GenerationMode generationMode)
        {
            // do the normal slot first so that it can be used later in the shader :)
            var normal = FindInputSlot(kNormalSlotName);
            var nodes = new List<BaseMaterialNode>();
            CollectChildNodesByExecutionOrder(nodes, normal, false);

            foreach (var node in nodes)
            {
                if (node is IGeneratesBodyCode)
                    (node as IGeneratesBodyCode).GenerateNodeCode(shaderBody, generationMode);
            }

            foreach (var edge in normal.edges)
            {
                var node = edge.fromSlot.node as BaseMaterialNode;
                shaderBody.AddShaderChunk("o." + normal.name + " = " + node.GetOutputVariableNameForSlot(edge.fromSlot, generationMode) + ";", true);
            }

            // track the last index of nodes... they have already been processed :)
            int pass2StartIndex = nodes.Count;

            //Get the rest of the nodes for all the other slots
            CollectChildNodesByExecutionOrder(nodes, null, false);
            for (var i = pass2StartIndex; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (node is IGeneratesBodyCode)
                    (node as IGeneratesBodyCode).GenerateNodeCode(shaderBody, generationMode);
            }

            foreach (var slot in FilterSlotsForLightFunction())
            {
                if (slot == normal)
                    continue;

                foreach (var edge in slot.edges)
                {
                    var node = edge.fromSlot.node as BaseMaterialNode;
                    shaderBody.AddShaderChunk("o." + slot.name + " = " + node.GetOutputVariableNameForSlot(edge.fromSlot, generationMode) + ";", true);
                }
            }
        }

        public override string GetOutputVariableNameForSlot(Slot s, GenerationMode generationMode)
        {
            return GetOutputVariableNameForNode();
        }

        public override float GetNodeUIHeight(float width)
        {
            return EditorGUIUtility.singleLineHeight;
        }

        public override bool NodeUI(Rect drawArea)
        {
            var lightFunctions = GetLightFunctions();
            var lightFunction = GetLightFunction();

            int lightFuncIndex = 0;
            if (lightFunction != null)
                lightFuncIndex = lightFunctions.IndexOf(lightFunction);

            EditorGUI.BeginChangeCheck();
            lightFuncIndex = EditorGUI.Popup(new Rect(drawArea.x, drawArea.y, drawArea.width, EditorGUIUtility.singleLineHeight), lightFuncIndex, lightFunctions.Select(x => x.GetLightFunctionName()).ToArray(), EditorStyles.popup);
            m_LightFunctionClassName = lightFunctions[lightFuncIndex].GetType().ToString();
            if (EditorGUI.EndChangeCheck())
            {
                RegeneratePreviewShaders();
                return true;
            }
            return false;
        }

        public override bool hasPreview
        {
            get { return true; }
        }

        public override bool UpdatePreviewMaterial()
        {
            if (hasError)
                return false;

            var shaderName = "Hidden/PreviewShader/" + name + "_" + Math.Abs(GetInstanceID());
            Dictionary<string, Texture> defaultTextures;
            var resultShader = ShaderGenerator.GenerateSurfaceShader(pixelGraph.owner, shaderName, true, out defaultTextures);
            m_GeneratedShaderMode = PreviewMode.Preview3D;
            InternalUpdatePreviewShader(resultShader);

            //For the pixel shader node we also want to update the shader that uses it:
            defaultTextures.Clear();
            var shader = ShaderGenerator.GenerateSurfaceShader(pixelGraph.owner, pixelGraph.owner.name, false, out defaultTextures);
            pixelGraph.owner.UpdateShaderSource(shader, defaultTextures);
            return true;
        }
    }
}
