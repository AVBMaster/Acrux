using SkiaSharp;

namespace UpBrowser.Rendering.DevTools;

public class DevToolsTheme
{
    public bool IsLight { get; }
    public string Name { get; }

    public SKColor PanelBg { get; }
    public SKColor InputBg { get; }
    public SKColor TabBarBg { get; }
    public SKColor TabActiveBg { get; }
    public SKColor TabHoverBg { get; }
    public SKColor TabActiveText { get; }
    public SKColor TabInactiveText { get; }
    public SKColor TabIndicator { get; }

    public SKColor CloseBtnText { get; }
    public SKColor CloseBtnHoverBg { get; }
    public SKColor CloseBtnHoverText { get; }

    public SKColor TextPrimary { get; }
    public SKColor TextSecondary { get; }

    public SKColor AccentBlue { get; }
    public SKColor AccentOrange { get; }
    public SKColor AccentYellow { get; }
    public SKColor AccentGreen { get; }
    public SKColor AccentRed { get; }

    public SKColor SelectionBg { get; }
    public SKColor InfoBg { get; }
    public SKColor ScrollbarThumb { get; }
    public SKColor LineHighlight { get; }
    public SKColor LineNumberText { get; }
    public SKColor CursorColor { get; }
    public SKColor Separator { get; }
    public SKColor DragHandle { get; }
    public SKColor DragLine { get; }

    public SKColor BtnDefault { get; }
    public SKColor BtnHoverBg { get; }
    public SKColor BtnActiveBg { get; }

    public DevToolsTheme(bool isLight, string name,
        SKColor panelBg, SKColor inputBg,
        SKColor tabBarBg, SKColor tabActiveBg, SKColor tabHoverBg,
        SKColor tabActiveText, SKColor tabInactiveText, SKColor tabIndicator,
        SKColor closeBtnText, SKColor closeBtnHoverBg, SKColor closeBtnHoverText,
        SKColor textPrimary, SKColor textSecondary,
        SKColor accentBlue, SKColor accentOrange, SKColor accentYellow,
        SKColor accentGreen, SKColor accentRed,
        SKColor selectionBg, SKColor infoBg, SKColor scrollbarThumb,
        SKColor lineHighlight, SKColor lineNumberText, SKColor cursorColor,
        SKColor separator, SKColor dragHandle, SKColor dragLine,
        SKColor btnDefault, SKColor btnHoverBg, SKColor btnActiveBg)
    {
        IsLight = isLight;
        Name = name;
        PanelBg = panelBg;
        InputBg = inputBg;
        TabBarBg = tabBarBg;
        TabActiveBg = tabActiveBg;
        TabHoverBg = tabHoverBg;
        TabActiveText = tabActiveText;
        TabInactiveText = tabInactiveText;
        TabIndicator = tabIndicator;
        CloseBtnText = closeBtnText;
        CloseBtnHoverBg = closeBtnHoverBg;
        CloseBtnHoverText = closeBtnHoverText;
        TextPrimary = textPrimary;
        TextSecondary = textSecondary;
        AccentBlue = accentBlue;
        AccentOrange = accentOrange;
        AccentYellow = accentYellow;
        AccentGreen = accentGreen;
        AccentRed = accentRed;
        SelectionBg = selectionBg;
        InfoBg = infoBg;
        ScrollbarThumb = scrollbarThumb;
        LineHighlight = lineHighlight;
        LineNumberText = lineNumberText;
        CursorColor = cursorColor;
        Separator = separator;
        DragHandle = dragHandle;
        DragLine = dragLine;
        BtnDefault = btnDefault;
        BtnHoverBg = btnHoverBg;
        BtnActiveBg = btnActiveBg;
    }

    public static DevToolsTheme Dark { get; } = new DevToolsTheme(
        isLight: false,
        name: "暗色",
        panelBg: SKColor.Parse("#1E1E1E"),
        inputBg: SKColor.Parse("#2D2D2D"),
        tabBarBg: SKColor.Parse("#1E1E1E"),
        tabActiveBg: SKColor.Parse("#2D2D2D"),
        tabHoverBg: SKColor.Parse("#2A2D2E"),
        tabActiveText: SKColor.Parse("#FFFFFF"),
        tabInactiveText: SKColor.Parse("#808080"),
        tabIndicator: SKColor.Parse("#1A73E8"),
        closeBtnText: SKColor.Parse("#808080"),
        closeBtnHoverBg: SKColor.Parse("#3C3C3C"),
        closeBtnHoverText: SKColor.Parse("#FFFFFF"),
        textPrimary: SKColor.Parse("#D4D4D4"),
        textSecondary: SKColor.Parse("#808080"),
        accentBlue: SKColor.Parse("#569CD6"),
        accentOrange: SKColor.Parse("#CE9178"),
        accentYellow: SKColor.Parse("#D7BA7D"),
        accentGreen: SKColor.Parse("#6A9955"),
        accentRed: SKColor.Parse("#F44747"),
        selectionBg: SKColor.Parse("#264F78"),
        infoBg: SKColor.Parse("#252526"),
        scrollbarThumb: new SKColor(80, 80, 80),
        lineHighlight: SKColor.Parse("#2A2D2E"),
        lineNumberText: SKColor.Parse("#858585"),
        cursorColor: SKColors.White,
        separator: SKColor.Parse("#3C3C3C"),
        dragHandle: SKColor.Parse("#252526"),
        dragLine: SKColor.Parse("#555555"),
        btnDefault: SKColor.Parse("#808080"),
        btnHoverBg: SKColor.Parse("#3C3C3C"),
        btnActiveBg: SKColor.Parse("#505050")
    );

    public static DevToolsTheme Light { get; } = new DevToolsTheme(
        isLight: true,
        name: "亮色",
        panelBg: SKColor.Parse("#FFFFFF"),
        inputBg: SKColor.Parse("#F1F3F4"),
        tabBarBg: SKColor.Parse("#F1F3F4"),
        tabActiveBg: SKColor.Parse("#FFFFFF"),
        tabHoverBg: SKColor.Parse("#E8EAED"),
        tabActiveText: SKColor.Parse("#202124"),
        tabInactiveText: SKColor.Parse("#5F6368"),
        tabIndicator: SKColor.Parse("#1A73E8"),
        closeBtnText: SKColor.Parse("#5F6368"),
        closeBtnHoverBg: SKColor.Parse("#E8EAED"),
        closeBtnHoverText: SKColor.Parse("#202124"),
        textPrimary: SKColor.Parse("#202124"),
        textSecondary: SKColor.Parse("#5F6368"),
        accentBlue: SKColor.Parse("#1A73E8"),
        accentOrange: SKColor.Parse("#C85A17"),
        accentYellow: SKColor.Parse("#B8860B"),
        accentGreen: SKColor.Parse("#188038"),
        accentRed: SKColor.Parse("#D93025"),
        selectionBg: SKColor.Parse("#E8F0FE"),
        infoBg: SKColor.Parse("#F8F9FA"),
        scrollbarThumb: new SKColor(180, 180, 180),
        lineHighlight: SKColor.Parse("#E8EAED"),
        lineNumberText: SKColor.Parse("#5F6368"),
        cursorColor: SKColor.Parse("#202124"),
        separator: SKColor.Parse("#DADCE0"),
        dragHandle: SKColor.Parse("#E8EAED"),
        dragLine: SKColor.Parse("#BDC1C6"),
        btnDefault: SKColor.Parse("#5F6368"),
        btnHoverBg: SKColor.Parse("#E8EAED"),
        btnActiveBg: SKColor.Parse("#D2E3FC")
    );

    public static DevToolsTheme Current { get; set; } = Dark;

    public void Apply()
    {
        Current = this;
    }

    public DevToolsTheme Toggle()
    {
        return IsLight ? Dark : Light;
    }
}
