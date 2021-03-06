using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Graphing;
using UnityEditor.ShaderGraph.Drawing.Slots;
using UnityEngine;
using UnityEngine.Experimental.UIElements;

namespace UnityEditor.ShaderGraph
{
	[Serializable]
	public class DynamicValueMaterialSlot : MaterialSlot, IMaterialSlotHasValue<Matrix4x4>
	{
		[SerializeField]
		private Matrix4x4 m_Value;

		[SerializeField]
		private Matrix4x4 m_DefaultValue;

	    static readonly string[] k_Labels = {"X", "Y", "Z", "W"};

		private ConcreteSlotValueType m_ConcreteValueType = ConcreteSlotValueType.Vector4;

		public DynamicValueMaterialSlot()
		{
		}

		public DynamicValueMaterialSlot(
			int slotId,
			string displayName,
			string shaderOutputName,
			SlotType slotType,
			Matrix4x4 value,
			ShaderStage shaderStage = ShaderStage.Dynamic,
			bool hidden = false)
			: base(slotId, displayName, shaderOutputName, slotType, shaderStage, hidden)
		{
			m_Value = value;
		}

		public Matrix4x4 defaultValue { get { return m_DefaultValue; } }

		public Matrix4x4 value
		{
			get { return m_Value; }
			set { m_Value = value; }
		}

		public override VisualElement InstantiateControl()
		{
		    var labels = k_Labels.Take(concreteValueType.GetChannelCount()).ToArray();
			return new MultiFloatSlotControlView(owner, labels, () => value.GetRow(0), (newValue) => value = new Matrix4x4(newValue, value.GetRow(1), value.GetRow(2), value.GetRow(3)));
		}

		public override SlotValueType valueType { get { return SlotValueType.Dynamic; } }

		public override ConcreteSlotValueType concreteValueType
		{
			get { return m_ConcreteValueType; }
		}

		public void SetConcreteType(ConcreteSlotValueType valueType)
		{
			m_ConcreteValueType = valueType;
		}

		public override void GetPreviewProperties(List<PreviewProperty> properties, string name)
        {
            var propType = ConvertConcreteSlotValueTypeToPropertyType(concreteValueType);
			var pp = new PreviewProperty(propType) { name = name };
			if (propType == PropertyType.Vector1)
				pp.floatValue = value.m00;
			else
				pp.vector4Value = new Vector4(value.m00, value.m01, value.m02, value.m03);
            properties.Add(pp);
        }

		protected override string ConcreteSlotValueAsVariable(AbstractMaterialNode.OutputPrecision precision)
		{
			var channelCount = SlotValueHelper.GetChannelCount(concreteValueType);
			string values = NodeUtils.FloatToShaderValue(value.m00);
			if (channelCount == 1)
				return values;
			for (var i = 1; i < channelCount; i++)
				values += ", " + value.GetRow(0)[i];
			return string.Format("{0}{1}({2})", precision, channelCount, values);
		}

		public override void AddDefaultProperty(PropertyCollector properties, GenerationMode generationMode)
		{
			if (!generationMode.IsPreview())
				return;

			var matOwner = owner as AbstractMaterialNode;
			if (matOwner == null)
				throw new Exception(string.Format("Slot {0} either has no owner, or the owner is not a {1}", this, typeof(AbstractMaterialNode)));

			IShaderProperty property;
			switch (concreteValueType)
			{
				case ConcreteSlotValueType.Vector4:
					property = new Vector4ShaderProperty();
					break;
				case ConcreteSlotValueType.Vector3:
					property = new Vector3ShaderProperty();
					break;
				case ConcreteSlotValueType.Vector2:
					property = new Vector2ShaderProperty();
					break;
				case ConcreteSlotValueType.Vector1:
					property = new Vector1ShaderProperty();
					break;
				case ConcreteSlotValueType.Matrix4:
					property = new Matrix4ShaderProperty();
					break;
				case ConcreteSlotValueType.Matrix3:
					property = new Matrix3ShaderProperty();
					break;
				case ConcreteSlotValueType.Matrix2:
					property = new Matrix2ShaderProperty();
					break;
				default:
					throw new ArgumentOutOfRangeException();
			}

			property.overrideReferenceName = matOwner.GetVariableNameForSlot(id);
			property.generatePropertyBlock = false;
			properties.AddShaderProperty(property);
		}

		public override void CopyValuesFrom(MaterialSlot foundSlot)
		{
			var slot = foundSlot as DynamicValueMaterialSlot;
			if (slot != null)
				value = slot.value;
		}
	}
}
