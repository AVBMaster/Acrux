using Acrux.Core.Dom;
using Acrux.Core.Layout.Geometry;

namespace Acrux.Core.Layout;

/// <summary>
/// This class manages rare data of PhysicalBoxFragment.
/// Only PhysicalBoxFragment should use this class.
/// Mirrors PhysicalFragmentRareData in physical_fragment_rare_data.h/.cc.
/// </summary>
public class PhysicalFragmentRareData
{
    public enum FieldId
    {
        ScrollableOverflow = 0,
        Borders,
        Scrollbar,
        Padding,
        InflowBounds,
        FrameSetLayoutData,
        TableGridRect,
        TableCollapsedBordersGeometry,
        TableCellColumnIndex,
        TableSectionStartRowIndex,
        TableSectionRowOffsets,
        PageName,
        Margins,
    }

    public PhysicalFragmentRareData(int numFields)
    {
        _fieldList.Capacity = numFields;
    }

    public PhysicalFragmentRareData(PhysicalRect? scrollableOverflow, PhysicalBoxStrut? borders, PhysicalBoxStrut? scrollbar,
        PhysicalBoxStrut? padding, PhysicalRect? inflowBounds, BoxFragmentBuilder builder, int numFields)
    {
        _fieldList.Capacity = numFields;

        if (scrollableOverflow.HasValue)
            SetField(FieldId.ScrollableOverflow, scrollableOverflow.Value);
        if (borders.HasValue)
            SetField(FieldId.Borders, borders.Value);
        if (scrollbar.HasValue)
            SetField(FieldId.Scrollbar, scrollbar.Value);
        if (padding.HasValue)
            SetField(FieldId.Padding, padding.Value);
        if (inflowBounds.HasValue)
            SetField(FieldId.InflowBounds, inflowBounds.Value);
    }

    public PhysicalFragmentRareData(PhysicalFragmentRareData other)
    {
        _fieldList.Capacity = other._fieldList.Capacity;
        foreach (var field in other._fieldList)
        {
            var newField = field.Clone();
            _fieldList.Add(newField);
        }
        _bitField = other._bitField;
        _tableColumnGeometries = other._tableColumnGeometries != null
            ? new List<object>(other._tableColumnGeometries)
            : null;
    }

    public RareField? GetField(FieldId fieldId)
    {
        if ((_bitField & FieldIdBit(fieldId)) != 0)
        {
            int index = GetFieldIndex(fieldId);
            if (index >= 0 && index < _fieldList.Count)
                return _fieldList[index];
        }
        return null;
    }

    public RareField EnsureField(FieldId fieldId)
    {
        var bit = FieldIdBit(fieldId);
        if ((_bitField & bit) != 0)
        {
            int index = GetFieldIndex(fieldId);
            return _fieldList[index];
        }
        _bitField |= bit;
        int newIndex = GetFieldIndex(fieldId);
        var field = new RareField(fieldId);
        _fieldList.Insert(newIndex, field);
        return _fieldList[newIndex];
    }

    public void RemoveField(FieldId fieldId)
    {
        int index = GetFieldIndex(fieldId);
        if (index >= 0)
        {
            _fieldList.RemoveAt(index);
            _bitField &= ~FieldIdBit(fieldId);
        }
    }

    public class RareField
    {
        public FieldId Type { get; }
        public object? Value { get; set; }

        public RareField(FieldId fieldId)
        {
            Type = fieldId;
        }

        public RareField Clone()
        {
            var clone = new RareField(Type);
            clone.Value = Type switch
            {
                FieldId.FrameSetLayoutData or FieldId.TableCollapsedBordersGeometry => Value,
                FieldId.TableSectionRowOffsets when Value is List<LayoutUnit> list => new List<LayoutUnit>(list),
                _ => Value,
            };
            return clone;
        }

        public T GetValue<T>() => (T)Value!;
        public void SetValue<T>(T value) => Value = value;
    }

    private uint FieldIdBit(FieldId id) => (uint)(1 << (int)id);
    private uint FieldIdLowerMask(FieldId id) => (uint)(~(~0U << (int)id));

    private int GetFieldIndex(FieldId fieldId)
    {
        uint mask = FieldIdLowerMask(fieldId);
        uint masked = _bitField & mask;
        return System.Numerics.BitOperations.PopCount(masked);
    }

    private void SetField(FieldId fieldId, object value)
    {
        var field = EnsureField(fieldId);
        field.Value = value;
    }

    private List<RareField> _fieldList = new();
    private uint _bitField = 0u;
    private List<object>? _tableColumnGeometries;
}