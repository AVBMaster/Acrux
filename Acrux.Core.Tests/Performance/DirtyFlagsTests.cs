using Acrux.Core.Performance;
using Acrux.Core.Dom;
using Xunit;

namespace Acrux.Core.Tests.Performance;

public class DirtyFlagsTests
{
    [Fact]
    public void FreshElement_IsClean()
    {
        var el = new HtmlElement("div");
        Assert.True(DirtyState.IsClean(el));
    }

    [Fact]
    public void AddingFlags_MakesElementDirty()
    {
        var el = new HtmlElement("div");
        DirtyState.AddSelf(el, DirtyFlags.Style);
        Assert.False(DirtyState.IsClean(el));
        Assert.Equal(DirtyFlags.Style, DirtyState.GetSelf(el));
    }

    [Fact]
    public void AddSelf_AccumulatesFlags()
    {
        var el = new HtmlElement("div");
        DirtyState.AddSelf(el, DirtyFlags.Style);
        DirtyState.AddSelf(el, DirtyFlags.Layout);
        Assert.Equal(DirtyFlags.Style | DirtyFlags.Layout, DirtyState.GetSelf(el));
    }

    [Fact]
    public void ChildrenAndSelf_AreDistinctFields()
    {
        var el = new HtmlElement("div");
        DirtyState.AddSelf(el, DirtyFlags.Style);
        DirtyState.AddChildren(el, DirtyFlags.Layout);
        Assert.Equal(DirtyFlags.Style, DirtyState.GetSelf(el));
        Assert.Equal(DirtyFlags.Layout, DirtyState.GetChildren(el));
    }

    [Fact]
    public void ClearAll_ResetsBothFields()
    {
        var el = new HtmlElement("div");
        DirtyState.AddSelf(el, DirtyFlags.AllLayout);
        DirtyState.AddChildren(el, DirtyFlags.AllStyle);
        DirtyState.ClearAll(el);
        Assert.True(DirtyState.IsClean(el));
    }

    [Fact]
    public void LayoutVersion_IncrementsOnBump()
    {
        var el = new HtmlElement("div");
        long v0 = DirtyState.GetLayoutVersion(el);
        long v1 = DirtyState.BumpLayoutVersion(el);
        Assert.Equal(v0 + 1, v1);
    }

    [Fact]
    public void DirtySet_UnionOperator()
    {
        var a = DirtySet.Of(DirtyFlags.Style);
        var b = a | DirtyFlags.Layout;
        Assert.True(b.Has(DirtyFlags.Style));
        Assert.True(b.Has(DirtyFlags.Layout));
        Assert.False(b.IsEmpty);
    }

    [Fact]
    public void DirtySet_HasAny_PartialMatch()
    {
        var s = DirtySet.Of(DirtyFlags.Style | DirtyFlags.Layout);
        Assert.True(s.HasAny(DirtyFlags.Style | DirtyFlags.Paint));
        Assert.True(s.HasAny(DirtyFlags.Layout));
        Assert.False(s.HasAny(DirtyFlags.Paint));
    }
}

