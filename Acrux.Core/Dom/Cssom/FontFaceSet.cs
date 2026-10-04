namespace Acrux.Core.Dom.Cssom;

public class FontFaceSet
{
    public int Size { get; }
    public bool Ready { get; } = true;
    public string Status { get; set; } = "loaded";
}
