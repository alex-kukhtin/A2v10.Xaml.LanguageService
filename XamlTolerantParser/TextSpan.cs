namespace XamlTolerantParser;

public readonly record struct TextSpan(int Start, int Length)
{
    public int End => Start + Length;

    public bool Contains(int position) => position >= Start && position < End;

    public override string ToString() => $"[{Start}..{End})";
}
