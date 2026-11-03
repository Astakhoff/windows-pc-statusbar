namespace PCStatusBar;

public record PanelLayout(bool Compact, int[] Widths, bool[] Visible, int OverflowWidth, int ContentWidth);

public static class ResponsiveLayout
{
    public static PanelLayout Calculate(int available, int[] natural, int[] compact, bool[] enabled, int[] hideOrder, int gap, int endPadding, int tickerReserve, int moreWidth, int outerPadding = 16)
    {
        int budget = Math.Max(0, available - Math.Min(tickerReserve, available / 3) - outerPadding);
        var visible = (bool[])enabled.Clone();
        var widths = (int[])natural.Clone();
        bool hidden = false;
        int Total() => visible.Select((v, i) => v ? widths[i] + 2 * gap : 0).Sum() + endPadding + (hidden ? moreWidth + 2 * gap : 0);
        bool useCompact = Total() > budget;
        if (useCompact) widths = (int[])compact.Clone();
        foreach (int index in hideOrder)
        {
            if (Total() <= budget) break;
            if (visible[index]) { visible[index] = false; hidden = true; }
        }
        int more = hidden ? Math.Min(moreWidth, Math.Max(0, budget - endPadding - 2 * gap)) : 0;
        int total = visible.Select((v, i) => v ? widths[i] + 2 * gap : 0).Sum() + (more > 0 ? more + 2 * gap : 0);
        total = total > 0 ? Math.Min(budget, total + endPadding) : 0;
        for (int i = 0; i < widths.Length; i++) if (!visible[i]) widths[i] = 0;
        return new(useCompact, widths, visible, more, total);
    }
}
