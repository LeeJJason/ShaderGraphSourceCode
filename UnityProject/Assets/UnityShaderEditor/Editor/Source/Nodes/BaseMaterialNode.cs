using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditorInternal;
using UnityEngine;

namespace UnityEditor.MaterialGraph
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Event | AttributeTargets.Parameter | AttributeTargets.ReturnValue)]
    public class TitleAttribute : Attribute
    {
        public string m_Title;
        public TitleAttribute(string title) { m_Title = title; }
    }

    public enum Precision
    {
        Default = 0, // half
        Full = 1,
        Fixed = 2,
    }

    public enum PropertyType
    {
        Color,
        Texture2D,
        Float,
        Vector2,
        Vector3,
        Vector4
    }

    public class PreviewProperty
    {
        public string m_Name;
        public PropertyType m_PropType;

        public Color m_Color;
        public Texture2D m_Texture;
        public Vector4 m_Vector4;
        public float m_Float;
    }

    public enum PreviewMode
    {
        Preview2D,
        Preview3D
    }

    public enum DrawMode
    {
        Full,
        Collapsed
    }

    public abstract class BaseMaterialNode : IGenerateProperties, ISerializationCallbackReceiver
    {
        #region Fields
        private const int kPreviewWidth = 64;
        private const int kPreviewHeight = 64;
        
        [NonSerialized]
        private GUID m_GUID;

        [SerializeField]
        private string m_GUIDSerialzied;

        public GUID guid
        {
            get
            {
                return m_GUID;
            }
        }

        private string m_Name;
        public string name
        {
            get { return name; }
            set { name = value; }
        }

        public string precision
        {
            get { return "half"; }
        }

        public BaseMaterialGraph owner { protected get; set; }

        [SerializeField]
        private List<Slot> m_Slots = new List<Slot>();

        protected IEnumerable<Slot> inputSlots
        {
            get { return m_Slots.Where(x => x.isInputSlot); }
        }

        private IEnumerable<Slot> outputSlots
        {
            get { return m_Slots.Where(x => x.isOutputSlot); }
        }

        [SerializeField]
        private DrawMode m_DrawMode = DrawMode.Full;

        public delegate void NeedsRepaint();
        public NeedsRepaint onNeedsRepaint;
        public void ExecuteRepaint()
        {
            if (onNeedsRepaint != null)
                onNeedsRepaint();
        } 

        [NonSerialized]
        private Dictionary<string, SlotValueType> m_SlotValueTypes = new Dictionary<string, SlotValueType>();

        [NonSerialized]
        private Dictionary<string, ConcreteSlotValueType> m_ConcreteInputSlotValueTypes = new Dictionary<string, ConcreteSlotValueType>();

        [NonSerialized]
        private Dictionary<string, ConcreteSlotValueType> m_ConcreteOutputSlotValueTypes = new Dictionary<string, ConcreteSlotValueType>();
        #endregion

        #region Properties
        // Nodes that want to have a preview area can override this and return true
        public virtual bool hasPreview { get { return false; } }
        public virtual PreviewMode previewMode { get { return PreviewMode.Preview2D; } }

        public DrawMode drawMode
        {
            get { return m_DrawMode; }
            set { m_DrawMode = value; }
        }
        
        protected virtual bool generateDefaultInputs { get { return true; } }
        public virtual bool canDeleteNode { get { return true; } }

        [SerializeField]
        private string m_LastShader;

        [SerializeField]
        private Shader m_PreviewShader;

        [SerializeField]
        private Material m_PreviewMaterial;

        public Material previewMaterial
        {
            get
            {
                ValidateNode();
                if (m_PreviewMaterial == null)
                {
                    UpdatePreviewShader();
                    m_PreviewMaterial = new Material(m_PreviewShader) {hideFlags = HideFlags.HideInHierarchy};
                    m_PreviewMaterial.hideFlags = HideFlags.HideInHierarchy;
                    AssetDatabase.AddObjectToAsset(m_PreviewMaterial, this);
                    
                }
                return m_PreviewMaterial;
            }
        }

        protected PreviewMode m_GeneratedShaderMode = PreviewMode.Preview2D;
        
        protected virtual int previewWidth
        {
            get { return kPreviewWidth; }
        }
        
        protected virtual int previewHeight
        {
            get { return kPreviewHeight; }
        }

        [NonSerialized]
        private bool m_NodeNeedsValidation= true;
        public bool nodeNeedsValidation
        {
            get { return m_NodeNeedsValidation; }
        }

        public void InvalidateNode()
        {
            m_NodeNeedsValidation = true;
        }

        [NonSerialized]
        private bool m_HasError;
        public bool hasError
        {
            get
            {
                if (nodeNeedsValidation)
                    ValidateNode();
                return m_HasError;
            }
            protected set
            {
                if (m_HasError != value)
                {
                    m_HasError = value;
                    ExecuteRepaint();
                }
            }
        }

        public Dictionary<string, ConcreteSlotValueType> concreteInputSlotValueTypes
        {
            get
            {
                if (nodeNeedsValidation)
                    ValidateNode(); 
                return m_ConcreteInputSlotValueTypes;
            }
        }

        public Dictionary<string, ConcreteSlotValueType> concreteOutputSlotValueTypes
        {
            get
            {
                if (nodeNeedsValidation)
                    ValidateNode();
                return m_ConcreteOutputSlotValueTypes;
            }
        }

        #endregion

        public virtual float GetNodeUIHeight(float width)
        {
            return 0;
        }

        public virtual GUIModificationType NodeUI(Rect drawArea)
        {
            return GUIModificationType.None;
        }
        
        protected virtual void OnPreviewGUI()
        {
            if (!ShaderUtil.hardwareSupportsRectRenderTexture)
                return;

            GUILayout.BeginHorizontal(GUILayout.MinWidth(previewWidth + 10), GUILayout.MinWidth(previewHeight + 10));
            GUILayout.FlexibleSpace();
            var rect = GUILayoutUtility.GetRect(previewWidth, previewHeight, GUILayout.ExpandWidth(false));
            var preview = RenderPreview(rect);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GL.sRGBWrite = (QualitySettings.activeColorSpace == ColorSpace.Linear);
            GUI.DrawTexture(rect, preview, ScaleMode.StretchToFill, false);
            GL.sRGBWrite = false;
        }

        #region Nodes
        // CollectDependentNodes looks at the current node and calculates
        // which nodes further up the tree (parents) would be effected if this node was changed
        // it also includes itself in this list
        public IEnumerable<BaseMaterialNode> CollectDependentNodes()
        {
            var nodeList = new List<BaseMaterialNode>();
            NodeUtils.CollectDependentNodes(nodeList, this);
            return nodeList;
        }

        // CollectDependentNodes looks at the current node and calculates
        // which child nodes it depends on for it's calculation.
        // Results are returned depth first so by processing each node in
        // order you can generate a valid code block.
        public List<BaseMaterialNode> CollectChildNodesByExecutionOrder(List<BaseMaterialNode> nodeList, Slot slotToUse = null, bool includeSelf = true)
        {
            if (slotToUse != null && !m_Slots.Contains(slotToUse))
            {
                Debug.LogError("Attempting to collect nodes by execution order with an invalid slot on: " + name);
                return nodeList;
            }

            NodeUtils.CollectChildNodesByExecutionOrder(nodeList, this, slotToUse);

            if (!includeSelf)
                nodeList.Remove(this);

            return nodeList;
        }

        #endregion

        #region Previews

        protected virtual bool UpdatePreviewShader()
        {
            if (hasError)
                return false;

            var resultShader = ShaderGenerator.GeneratePreviewShader(this, out m_GeneratedShaderMode);
            return InternalUpdatePreviewShader(resultShader);
        }

        private static bool ShaderHasError(Shader shader)
        {
            var hasErrorsCall = typeof(ShaderUtil).GetMethod("GetShaderErrorCount", BindingFlags.Static | BindingFlags.NonPublic);
            var result = hasErrorsCall.Invoke(null, new object[] { shader });
            return (int)result != 0;
        }

        protected bool InternalUpdatePreviewShader(string resultShader)
        {
            MaterialWindow.DebugMaterialGraph("RecreateShaderAndMaterial : " + name + "_" + guid + "\n" + resultShader);

            // workaround for some internal shader compiler weirdness
            // if we are in error we sometimes to not properly clean 
            // clean out the error flags and will stay in error, even
            // if we are now valid
            if (m_PreviewShader && ShaderHasError(m_PreviewShader))
            {
                UnityEngine.Object.DestroyImmediate(m_PreviewShader, true);
                UnityEngine.Object.DestroyImmediate(m_PreviewMaterial, true);
                m_PreviewShader = null;
                m_PreviewMaterial = null;
            }

            if (m_PreviewShader == null)
            {
                m_PreviewShader = ShaderUtil.CreateShaderAsset(resultShader);
                m_PreviewShader.hideFlags = HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(m_PreviewShader, this);
                m_LastShader = resultShader;
            }
            else
            {
                if (string.CompareOrdinal(resultShader, m_LastShader) != 0)
                {
                    ShaderUtil.UpdateShaderAsset(m_PreviewShader, resultShader);
                    m_LastShader = resultShader;
                }
            }

            return !ShaderHasError(m_PreviewShader);
        }

        private static Mesh[] s_Meshes = { null, null, null, null };
        
        /// <summary>
        /// RenderPreview gets called in OnPreviewGUI. Nodes can override
        /// RenderPreview and do their own rendering to the render texture
        /// </summary>
        public Texture RenderPreview(Rect targetSize)
        {
            UpdatePreviewProperties();

            if (s_Meshes[0] == null)
            {
                GameObject handleGo = (GameObject)EditorGUIUtility.LoadRequired("Previews/PreviewMaterials.fbx");
                // @TODO: temp workaround to make it not render in the scene
                handleGo.SetActive(false);
                foreach (Transform t in handleGo.transform)
                {
                    switch (t.name)
                    {
                        case "sphere":
                            s_Meshes[0] = ((MeshFilter)t.GetComponent("MeshFilter")).sharedMesh;
                            break;
                        case "cube":
                            s_Meshes[1] = ((MeshFilter)t.GetComponent("MeshFilter")).sharedMesh;
                            break;
                        case "cylinder":
                            s_Meshes[2] = ((MeshFilter)t.GetComponent("MeshFilter")).sharedMesh;
                            break;
                        case "torus":
                            s_Meshes[3] = ((MeshFilter)t.GetComponent("MeshFilter")).sharedMesh;
                            break;
                        default:
                            Debug.Log("Something is wrong, weird object found: " + t.name);
                            break;
                    }
                }
            }

            var previewUtil = owner.previewUtility;
            previewUtil.BeginPreview(targetSize, GUIStyle.none);

            if (m_GeneratedShaderMode == PreviewMode.Preview3D)
            {
                previewUtil.m_Camera.transform.position = -Vector3.forward * 5;
                previewUtil.m_Camera.transform.rotation = Quaternion.identity;
                EditorUtility.SetCameraAnimateMaterialsTime(previewUtil.m_Camera, Time.realtimeSinceStartup);
                var amb = new Color(.2f, .2f, .2f, 0);
                previewUtil.m_Light[0].intensity = 1.0f;
                previewUtil.m_Light[0].transform.rotation = Quaternion.Euler (50f, 50f, 0);
                previewUtil.m_Light[1].intensity = 1.0f;

                InternalEditorUtility.SetCustomLighting(previewUtil.m_Light, amb);
                previewUtil.DrawMesh(s_Meshes[0], Vector3.zero, Quaternion.Euler(-20, 0, 0) * Quaternion.Euler(0, 0, 0), previewMaterial, 0);
                bool oldFog = RenderSettings.fog;
                Unsupported.SetRenderSettingsUseFogNoDirty(false);
                previewUtil.m_Camera.Render();
                Unsupported.SetRenderSettingsUseFogNoDirty(oldFog);
                InternalEditorUtility.RemoveCustomLighting();
            }
            else
            {
               EditorUtility.UpdateGlobalShaderProperties(Time.realtimeSinceStartup);
               Graphics.Blit(null, previewMaterial);
            }
            return previewUtil.EndPreview();
        }

        private static void SetPreviewMaterialProperty(PreviewProperty previewProperty, Material mat)
        {
            switch (previewProperty.m_PropType)
            {
                case PropertyType.Texture2D:
                    mat.SetTexture(previewProperty.m_Name, previewProperty.m_Texture);
                    break;
                case PropertyType.Color:
                    mat.SetColor(previewProperty.m_Name, previewProperty.m_Color);
                    break;
                case PropertyType.Vector2:
                    mat.SetVector(previewProperty.m_Name, previewProperty.m_Vector4);
                    break;
                case PropertyType.Vector3:
                    mat.SetVector(previewProperty.m_Name, previewProperty.m_Vector4);
                    break;
                case PropertyType.Vector4:
                    mat.SetVector(previewProperty.m_Name, previewProperty.m_Vector4);
                    break;
                case PropertyType.Float:
                    mat.SetFloat(previewProperty.m_Name, previewProperty.m_Float);
                    break;
            }
        }
        
        protected virtual void CollectPreviewMaterialProperties (List<PreviewProperty> properties)
        {
            var validSlots = inputSlots.ToArray();

            for (int index = 0; index < validSlots.Length; index++)
            {
                var s = validSlots[index];
                var edges = owner.GetEdges(s);
                if (edges.Any())
                    continue;

                var pp = new PreviewProperty
                {
                    m_Name = s.GetInputName(this),
                    m_PropType = PropertyType.Vector4,
                    m_Vector4 = s.currentValue
                };
                properties.Add(pp);
            }
        }

        public static void UpdateMaterialProperties(BaseMaterialNode target, Material material)
        {
            var childNodes = ListPool<BaseMaterialNode>.Get();
            target.CollectChildNodesByExecutionOrder(childNodes);

            var pList = ListPool<PreviewProperty>.Get();
            for (int index = 0; index < childNodes.Count; index++)
            {
                var node = childNodes[index];
                node.CollectPreviewMaterialProperties(pList);
            }

            foreach (var prop in pList)
                SetPreviewMaterialProperty(prop, material);

            ListPool<BaseMaterialNode>.Release(childNodes);
            ListPool<PreviewProperty>.Release(pList);
        }

        public void UpdatePreviewProperties()
        {
            if (!hasPreview)
                return;

            UpdateMaterialProperties(this, previewMaterial);
        }

        #endregion

        #region Slots

        public virtual string GetOutputVariableNameForSlot(Slot s, GenerationMode generationMode)
        {
            if (s.isInputSlot) Debug.LogError("Attempting to use input slot (" + s + ") for output!");
            if (!m_Slots.Contains(s)) Debug.LogError("Attempting to use slot (" + s + ") for output on a node that does not have this slot!");

            return GetOutputVariableNameForNode() + "_" + s.name;
        }

        public virtual string GetOutputVariableNameForNode()
        {
            return name + "_" + guid;
        }
        public virtual Vector4 GetNewSlotDefaultValue(SlotValueType type)
        {
            return Vector4.one;
        }

        public void AddSlot(Slot slot)
        {
            if (slot == null)
                return;

            // new slot, just add it, we cool
            if (!m_Slots.Contains(slot))
            {
                m_Slots.Add(slot);
                return;
            }

            // old slot found
            // update the default value, and the slotType!
            var foundSlots = m_Slots.FindAll(x => x.name == slot.name);

            // if we are in a bad state (> 1 slot with same name, just reset).
            if (foundSlots.Count > 1)
            {
                Debug.LogWarningFormat("Node {0} has more than one slot with the same name, removing.");
                foundSlots.ForEach(x => m_Slots.Remove(x));
                m_Slots.Add(slot);
                return;
            }

            var foundSlot = foundSlots[0];

            // if the defualt and current are the same, change the current
            // to the new default.
            if (foundSlot.defaultValue == foundSlot.currentValue)
                foundSlot.currentValue = slot.defaultValue;

            foundSlot.defaultValue = slot.defaultValue;
            foundSlot.valueType = slot.valueType;
        }
        
        public void RemoveSlot(string name)
        {
            m_Slots.RemoveAll(x => x.name == name);
        }

        public string GenerateSlotName(Slot.SlotType type)
        {
            var slotsToCheck = type == Slot.SlotType.Input ? inputSlots.ToArray() : outputSlots.ToArray();
            string format = type == Slot.SlotType.Input ? "I{0:00}" : "O{0:00}";
            int index = slotsToCheck.Count();
            var slotName = string.Format(format, index);
            if (slotsToCheck.All(x => x.name != slotName))
                return slotName;
            index = 0;
            do
            {
                slotName = string.Format(format, index++);
            }
            while (slotsToCheck.Any(x => x.name == slotName));

            return slotName;
        }

        #endregion

        public virtual void GeneratePropertyBlock(PropertyGenerator visitor, GenerationMode generationMode)
        {}

        public virtual void GeneratePropertyUsages(ShaderGenerator visitor, GenerationMode generationMode, ConcreteSlotValueType slotValueType)
        {
            if (!generateDefaultInputs)
                return;

            if (!generationMode.IsPreview())
                return;

            foreach (var inputSlot in inputSlots)
            {
                var edges = owner.GetEdges(inputSlot);
                if (edges.Any())
                    continue;
                
                inputSlot.GeneratePropertyUsages(visitor, generationMode, concreteInputSlotValueTypes[inputSlot.name], this);
            }
        }

        public Slot FindInputSlot(string name)
        {
            var slot = m_Slots.FirstOrDefault(x => x.isInputSlot && x.name == name);
            if (slot == null)
                Debug.LogError("Input slot: " + name + " could be found on node " + GetOutputVariableNameForNode());
            return slot;
        }

        public Slot FindOutputSlot(string name)
        {
            var slot = m_Slots.FirstOrDefault(x => x.isOutputSlot && x.name == name);
            if (slot == null)
                Debug.LogError("Output slot: " + name + " could be found on node " + GetOutputVariableNameForNode());
            return slot;
        }

        protected string GetSlotValue(Slot inputSlot, GenerationMode generationMode)
        {
            var edges = owner.GetEdges(inputSlot).ToArray();

            if (edges.Length > 0)
            {
                var fromSocketRef = edges[0].outputSlot;
                var fromNode = owner.GetNodeFromGUID(fromSocketRef.nodeGuid);
                var slot = fromNode.FindOutputSlot(fromSocketRef.slotName);

                return ShaderGenerator.AdaptNodeOutput(slot, generationMode, concreteInputSlotValueTypes[inputSlot.name]);
            }

            return inputSlot.GetDefaultValue(generationMode, concreteInputSlotValueTypes[inputSlot.name], this);
        }

        public void RemoveSlotsNameNotMatching(string[] slotNames)
        {
            var invalidSlots = m_Slots.Select(x => x.name).Except(slotNames);

            foreach (var invalidSlot in invalidSlots.ToList())
            {
                Debug.LogWarningFormat("Removing Invalid Slot: {0}", invalidSlot);
                RemoveSlot(invalidSlot);
            }
        }
        
        public ConcreteSlotValueType GetConcreteOutputSlotValueType(Slot slot)
        {
            if (concreteOutputSlotValueTypes.ContainsKey(slot.name))
                return concreteOutputSlotValueTypes[slot.name];

            return ConcreteSlotValueType.Error;
        }

        public ConcreteSlotValueType GetConcreteInputSlotValueType(Slot slot)
        {
            if (concreteInputSlotValueTypes.ContainsKey(slot.name))
                return concreteInputSlotValueTypes[slot.name];

            return ConcreteSlotValueType.Error;
        }

        private ConcreteSlotValueType FindCommonChannelType(ConcreteSlotValueType @from, ConcreteSlotValueType to)
        {
            if (ImplicitConversionExists(@from, to))
                return to;

            return ConcreteSlotValueType.Error;
        }

        private static ConcreteSlotValueType ToConcreteType(SlotValueType svt)
        {
            switch (svt)
            {
                case SlotValueType.Vector1:
                    return ConcreteSlotValueType.Vector1;
                case SlotValueType.Vector2:
                    return ConcreteSlotValueType.Vector2;
                case SlotValueType.Vector3:
                    return ConcreteSlotValueType.Vector3;
                case SlotValueType.Vector4:
                    return ConcreteSlotValueType.Vector4;
            }
            return ConcreteSlotValueType.Error;
        }

        private static bool ImplicitConversionExists (ConcreteSlotValueType from, ConcreteSlotValueType to)
        {
            return from >= to || from == ConcreteSlotValueType.Vector1;
        }

        protected virtual ConcreteSlotValueType ConvertDynamicInputTypeToConcrete(IEnumerable<ConcreteSlotValueType> inputTypes)
        {
            var concreteSlotValueTypes = inputTypes as IList<ConcreteSlotValueType> ?? inputTypes.ToList();
            if (concreteSlotValueTypes.Any(x => x == ConcreteSlotValueType.Error))
                return ConcreteSlotValueType.Error;

            var inputTypesDistinct = concreteSlotValueTypes.Distinct().ToList();
            switch (inputTypesDistinct.Count)
            {
                case 0:
                    return ConcreteSlotValueType.Vector1;
                case 1:
                    return inputTypesDistinct.FirstOrDefault();
                default:
                    // find the 'minumum' channel width excluding 1 as it can promote
                    inputTypesDistinct.RemoveAll(x => x == ConcreteSlotValueType.Vector1);
                    var ordered = inputTypesDistinct.OrderBy(x => x);
                    if (ordered.Any())
                        return ordered.FirstOrDefault();
                    break;
            }
            return ConcreteSlotValueType.Error;
        }
        
        public void ValidateNode()
        {
            if (!nodeNeedsValidation)
                return;

            bool isInError = false;

            // all children nodes needs to be updated first
            // so do that here
            foreach (var inputSlot in inputSlots)
            {
                var edges = owner.GetEdges(inputSlot);
                foreach (var edge in edges)
                {
                    var fromSocketRef = edge.outputSlot;
                    var outputNode = owner.GetNodeFromGUID(fromSocketRef.nodeGuid);
                    
                    outputNode.ValidateNode();
                    if (outputNode.hasError)
                        isInError = true;
                }
            }

            m_ConcreteInputSlotValueTypes.Clear(); 
            m_ConcreteOutputSlotValueTypes.Clear();

            var dynamicInputSlotsToCompare = new Dictionary<string, ConcreteSlotValueType>();
            var skippedDynamicSlots = new List<Slot>();
            
            // iterate the input slots
            foreach (var inputSlot in inputSlots)
            {
                var inputType = m_SlotValueTypes[inputSlot.name];
                // if there is a connection
                var edges = owner.GetEdges(inputSlot).ToList();
                if (!edges.Any())
                {
                    if (inputType != SlotValueType.Dynamic)
                        m_ConcreteInputSlotValueTypes.Add(inputSlot.name, ToConcreteType(inputType));
                    else
                        skippedDynamicSlots.Add(inputSlot);
                    continue;
                }

                // get the output details
                var outputSlotRef = edges[0].outputSlot;
                var outputNode = owner.GetNodeFromGUID(outputSlotRef.nodeGuid);
                var outputSlot = outputNode.FindOutputSlot(outputSlotRef.slotName);
                var outputConcreteType = outputNode.GetConcreteOutputSlotValueType(outputSlot);

                // if we have a standard connection... just check the types work!
                if (inputType != SlotValueType.Dynamic)
                {
                    var inputConcreteType = ToConcreteType(inputType);
                    m_ConcreteInputSlotValueTypes.Add(inputSlot.name, FindCommonChannelType (outputConcreteType, inputConcreteType));
                    continue;
                }

                // dynamic input... depends on output from other node.
                // we need to compare ALL dynamic inputs to make sure they
                // are compatable.
                dynamicInputSlotsToCompare.Add(inputSlot.name, outputConcreteType);
            }

            // we can now figure out the dynamic slotType
            // from here set all the 
            var dynamicType = ConvertDynamicInputTypeToConcrete(dynamicInputSlotsToCompare.Values);
            foreach (var dynamicKvP in dynamicInputSlotsToCompare)
                m_ConcreteInputSlotValueTypes.Add(dynamicKvP.Key, dynamicType);
            foreach (var skippedSlot in skippedDynamicSlots)
                m_ConcreteInputSlotValueTypes.Add(skippedSlot.name, dynamicType);

            bool inputError = m_ConcreteInputSlotValueTypes.Any(x => x.Value == ConcreteSlotValueType.Error);

            // configure the output slots now
            // their slotType will either be the default output slotType
            // or the above dynanic slotType for dynamic nodes
            // or error if there is an input error
            foreach (var outputSlot in outputSlots)
            {
                if (inputError)
                {
                    m_ConcreteOutputSlotValueTypes.Add(outputSlot.name, ConcreteSlotValueType.Error);
                    continue;
                }

                if (m_SlotValueTypes[outputSlot.name] == SlotValueType.Dynamic)
                {
                    m_ConcreteOutputSlotValueTypes.Add(outputSlot.name, dynamicType);
                    continue;
                }

                m_ConcreteOutputSlotValueTypes.Add(outputSlot.name, ToConcreteType(m_SlotValueTypes[outputSlot.name]));
            }
            
            isInError |= inputError;
            isInError |= m_ConcreteOutputSlotValueTypes.Values.Any(x => x == ConcreteSlotValueType.Error);
            isInError |= CalculateNodeHasError();
            m_NodeNeedsValidation = false;
            hasError = isInError;

            if (!hasError)
            {
                bool valid = UpdatePreviewShader();
                if (!valid)
                    hasError = true;
            }
        }

        //True if error
        protected virtual bool CalculateNodeHasError()
        {
            return false;
        }

        public static string ConvertConcreteSlotValueTypeToString(ConcreteSlotValueType slotValue)
        {
            switch (slotValue)
            {
                case ConcreteSlotValueType.Vector1:
                    return string.Empty;
                case ConcreteSlotValueType.Vector2:
                    return "2";
                case ConcreteSlotValueType.Vector3:
                    return "3";
                case ConcreteSlotValueType.Vector4:
                    return "4";
                default:
                    return "Error";
            }
        }

        public virtual bool OnGUI()
        {
            GUILayout.Label("Slot Defaults", EditorStyles.boldLabel);
            bool modified = false;
            foreach (var slot in inputSlots)
            {
                if(!owner.GetEdges(slot).Any())
                    modified |= DoSlotUI(this, slot);
            }

            return modified;
        }

        public static bool DoSlotUI(BaseMaterialNode node, Slot slot)
        {
            GUILayout.BeginHorizontal(/*EditorStyles.inspectorBig*/);
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Slot " + slot.name, EditorStyles.largeLabel);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            return slot.OnGUI();
        }
        
        public virtual bool DrawSlotDefaultInput(Rect rect, Slot inputSlot)
        {
            var inputSlotType = GetConcreteInputSlotValueType(inputSlot);
            return inputSlot.OnGUI(rect, inputSlotType);
        }

        public virtual IEnumerable<Slot> GetDrawableInputProxies()
        {
            return inputSlots.Where(x => !owner.GetEdges(x).Any());
        }

        public abstract void OnBeforeSerialize();
        public abstract void OnAfterDeserialize();
    }

    public enum GUIModificationType
    {
        None,
        Repaint,
        ModelChanged
    }
}
