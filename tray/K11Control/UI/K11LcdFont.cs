namespace K11Control.UI;

/// <summary>
/// 14-segment glyph table aligned to FiiO K11 R2R hardware VA LCD cells.
/// Bit index = byteIdx * 2 + bitIdx across the 7 segment bytes written per display cell.
/// Stock M79 table (0x0800DB68) keeps exact firmware masks for compatible ASCII strings;
/// direct mask command (0x07) extends the font on PC with fixed 'J', Cyrillic, and symbols.
/// </summary>
public static class K11LcdFont
{
    public const string StockM79Glyphs = " -/0123456789<ABCDEFGHIJKLMNOPQRSTUVWXYZ\\di";
    public const string StockM79NonBlankGlyphs = " -/0123456789<ABCDEFGHIKLMNOPQRSTUVWXYZ\\di";

    public const int SegLeftTop = 0;      // 0.0 (F)
    public const int SegLeftBottom = 1;   // 0.1 (E)
    public const int SegTop = 2;          // 1.0 (A)
    public const int SegMidLeft = 3;      // 1.1 (G1)
    public const int SegDiagTopLeft = 4;  // 2.0 (\ upper-left -> center)
    public const int SegDiagBotLeft = 5;  // 2.1 (/ center -> lower-left)
    public const int SegCenterTop = 6;    // 3.0 (center vertical top)
    public const int SegBottom = 7;       // 3.1 (D)
    public const int SegDiagTopRight = 8; // 4.0 (/ upper-right -> center)
    public const int SegCenterBot = 9;    // 4.1 (center vertical bottom)
    public const int SegMidRight = 10;    // 5.0 (G2)
    public const int SegDiagBotRight = 11;// 5.1 (\ center -> lower-right)
    public const int SegRightTop = 12;    // 6.0 (B)
    public const int SegRightBottom = 13; // 6.1 (C)

    public const ushort AllSegmentsMask = 0x3FFF;

    // Exact 42 non-blank hardware glyphs from M79 0x0800DB68
    private static readonly Dictionary<char, ushort> StockMasks = new()
    {
        [' ']  = Pack(0, 0, 0, 0, 0, 0, 0),
        ['-']  = Pack(0, 2, 0, 0, 0, 1, 0),
        ['/']  = Pack(0, 0, 2, 0, 1, 0, 0),
        ['0']  = Pack(3, 1, 0, 2, 0, 0, 3),
        ['1']  = Pack(0, 0, 0, 1, 2, 0, 0),
        ['2']  = Pack(2, 3, 0, 2, 0, 1, 1),
        ['3']  = Pack(0, 3, 0, 2, 0, 1, 3),
        ['4']  = Pack(1, 2, 0, 0, 0, 1, 3),
        ['5']  = Pack(1, 3, 0, 2, 0, 1, 2),
        ['6']  = Pack(3, 3, 0, 2, 0, 1, 2),
        ['7']  = Pack(0, 1, 0, 0, 0, 0, 3),
        ['8']  = Pack(3, 3, 0, 2, 0, 1, 3),
        ['9']  = Pack(1, 3, 0, 2, 0, 1, 3),
        ['<']  = Pack(0, 0, 0, 0, 1, 2, 0),
        ['A']  = Pack(3, 3, 0, 0, 0, 1, 3),
        ['B']  = Pack(3, 2, 0, 2, 0, 1, 2),
        ['C']  = Pack(3, 1, 0, 2, 0, 0, 0),
        ['D']  = Pack(0, 1, 0, 3, 2, 0, 3),
        ['E']  = Pack(3, 3, 0, 2, 0, 1, 0),
        ['F']  = Pack(3, 3, 0, 0, 0, 1, 0),
        ['G']  = Pack(3, 1, 0, 2, 0, 1, 2),
        ['H']  = Pack(3, 2, 0, 0, 0, 1, 3),
        ['I']  = Pack(0, 1, 0, 3, 2, 0, 0),
        ['K']  = Pack(3, 2, 0, 0, 1, 2, 0),
        ['L']  = Pack(3, 0, 0, 2, 0, 0, 0),
        ['M']  = Pack(3, 0, 1, 0, 1, 0, 3),
        ['N']  = Pack(3, 0, 1, 0, 0, 2, 3),
        ['O']  = Pack(3, 1, 0, 2, 0, 0, 3),
        ['P']  = Pack(3, 3, 0, 0, 0, 1, 1),
        ['Q']  = Pack(3, 1, 0, 2, 0, 2, 3),
        ['R']  = Pack(3, 3, 0, 0, 0, 3, 1),
        ['S']  = Pack(1, 3, 0, 2, 0, 1, 2),
        ['T']  = Pack(0, 1, 0, 1, 2, 0, 0),
        ['U']  = Pack(3, 0, 0, 2, 0, 0, 3),
        ['V']  = Pack(3, 0, 2, 0, 1, 0, 0),
        ['W']  = Pack(3, 0, 2, 0, 0, 2, 3),
        ['X']  = Pack(0, 0, 3, 0, 1, 2, 0),
        ['Y']  = Pack(0, 0, 1, 0, 3, 0, 0),
        ['Z']  = Pack(0, 1, 2, 2, 1, 0, 0),
        ['\\'] = Pack(0, 0, 1, 0, 0, 2, 0),
        ['d']  = Pack(2, 2, 0, 2, 0, 1, 3),
        ['i']  = Pack(0, 0, 0, 0, 0, 0, 3),
    };

    // Extended glyphs rendered via Variant B direct segment masks (cmd 0x07)
    private static readonly Dictionary<char, ushort> ExtendedMasks = new()
    {
        // Fixed 'J' + readable geometric symbols (no dot-based punctuation)
        ['J']  = Pack(2, 0, 0, 2, 0, 0, 3),
        ['+']  = Pack(0, 2, 0, 1, 2, 1, 0),
        ['=']  = Pack(0, 2, 0, 2, 0, 1, 0),
        ['_']  = Pack(0, 0, 0, 2, 0, 0, 0),
        ['>']  = Pack(0, 0, 3, 0, 0, 0, 0),
        ['*']  = Pack(0, 2, 3, 1, 3, 3, 0),
        ['[']  = Pack(3, 1, 0, 2, 0, 0, 0),
        [']']  = Pack(0, 1, 0, 2, 0, 0, 3),
        ['(']  = Pack(0, 0, 0, 0, 1, 2, 0),
        [')']  = Pack(0, 0, 3, 0, 0, 0, 0),
        ['|']  = Pack(0, 0, 0, 1, 2, 0, 0),

        // Cyrillic (Russian uppercase; lowercase resolves identically via ToUpperInvariant)
        ['А']  = Pack(3, 3, 0, 0, 0, 1, 3),
        ['Б']  = Pack(3, 3, 0, 2, 0, 1, 2),
        ['В']  = Pack(3, 3, 0, 2, 0, 1, 3),
        ['Г']  = Pack(3, 1, 0, 0, 0, 0, 0),
        ['Д']  = Pack(0, 1, 2, 3, 0, 0, 3),
        ['Е']  = Pack(3, 3, 0, 2, 0, 1, 0),
        ['Ё']  = Pack(3, 3, 0, 2, 0, 1, 0),
        ['Ж']  = Pack(0, 0, 3, 1, 3, 2, 0),
        ['З']  = Pack(0, 3, 0, 2, 0, 1, 3),
        ['И']  = Pack(3, 0, 2, 0, 1, 0, 3),
        ['Й']  = Pack(3, 0, 2, 0, 1, 0, 3),
        ['К']  = Pack(3, 2, 0, 0, 1, 2, 0),
        ['Л']  = Pack(0, 1, 2, 1, 0, 0, 3),
        ['М']  = Pack(3, 0, 1, 0, 1, 0, 3),
        ['Н']  = Pack(3, 2, 0, 0, 0, 1, 3),
        ['О']  = Pack(3, 1, 0, 2, 0, 0, 3),
        ['П']  = Pack(3, 1, 0, 0, 0, 0, 3),
        ['Р']  = Pack(3, 3, 0, 0, 0, 1, 1),
        ['С']  = Pack(3, 1, 0, 2, 0, 0, 0),
        ['Т']  = Pack(0, 1, 0, 1, 2, 0, 0),
        ['У']  = Pack(0, 0, 3, 0, 1, 0, 0),
        ['Ф']  = Pack(1, 3, 0, 1, 2, 1, 1),
        ['Х']  = Pack(0, 0, 3, 0, 1, 2, 0),
        ['Ц']  = Pack(3, 0, 0, 3, 2, 2, 0),
        ['Ч']  = Pack(1, 2, 0, 0, 0, 1, 3),
        ['Ш']  = Pack(3, 0, 0, 3, 2, 0, 3),
        ['Щ']  = Pack(3, 0, 0, 3, 2, 2, 3),
        ['Ъ']  = Pack(0, 1, 0, 3, 2, 1, 2),
        ['Ы']  = Pack(3, 2, 0, 2, 2, 0, 3),
        ['Ь']  = Pack(3, 2, 0, 2, 0, 1, 2),
        ['Э']  = Pack(0, 1, 0, 2, 0, 1, 3),
        ['Ю']  = Pack(3, 2, 0, 3, 3, 0, 3),
        ['Я']  = Pack(1, 3, 2, 0, 0, 1, 3),
    };

    public static ushort Pack(byte b0, byte b1, byte b2, byte b3, byte b4, byte b5, byte b6) =>
        (ushort)(b0 | (b1 << 2) | (b2 << 4) | (b3 << 6) | (b4 << 8) | (b5 << 10) | (b6 << 12));

    private static bool ContainsChar(char c, Dictionary<char, ushort> dict) => dict.ContainsKey(c);

    public static char ResolveDisplayChar(char raw)
    {
        if (ContainsChar(raw, StockMasks) || ContainsChar(raw, ExtendedMasks))
            return raw;

        char upper = char.ToUpperInvariant(raw);
        if (ContainsChar(upper, StockMasks) || ContainsChar(upper, ExtendedMasks))
            return upper;

        return ' ';
    }

    public static bool RequiresDirectMaskCommand(string? formatted7)
    {
        if (string.IsNullOrEmpty(formatted7))
            return false;

        foreach (char raw in formatted7)
        {
            char mapped = ResolveDisplayChar(raw);
            if (!StockMasks.ContainsKey(mapped))
                return true;
        }
        return false;
    }

    public static ushort GetSegmentMask(char raw)
    {
        char mapped = ResolveDisplayChar(raw);
        if (StockMasks.TryGetValue(mapped, out ushort m))
            return m;
        if (ExtendedMasks.TryGetValue(mapped, out ushort ext))
            return ext;
        return 0;
    }

    public static ushort[] BuildMasksFromText(string? text)
    {
        var masks = new ushort[7];
        if (string.IsNullOrEmpty(text))
            return masks;

        for (int i = 0; i < 7 && i < text.Length; i++)
            masks[i] = (ushort)(GetSegmentMask(text[i]) & AllSegmentsMask);
        return masks;
    }

    public static ushort[] CenterTextMasks(string? text, int slotWidth)
    {
        var outMasks = new ushort[slotWidth];
        if (string.IsNullOrEmpty(text) || slotWidth <= 0)
            return outMasks;

        int take = Math.Min(slotWidth, text.Length);
        int padLeft = (slotWidth - take) / 2;
        for (int i = 0; i < take; i++)
            outMasks[padLeft + i] = (ushort)(GetSegmentMask(text[i]) & AllSegmentsMask);
        return outMasks;
    }
}

public enum SticksTemplateKind
{
    Custom = 0,
    ReelsText = 1,
    BracketsText = 2,
    Impulse = 3,
    PulseEkg = 4,
    Orbit = 5,
    Rain = 6,
    SingingFace = 7
}

public static class SticksTemplateEngine
{
    public static readonly SticksTemplateKind[] OrderedTemplates =
    [
        SticksTemplateKind.ReelsText,
        SticksTemplateKind.BracketsText,
        SticksTemplateKind.Impulse,
        SticksTemplateKind.PulseEkg,
        SticksTemplateKind.Orbit,
        SticksTemplateKind.Rain,
        SticksTemplateKind.SingingFace,
        SticksTemplateKind.Custom
    ];

    private static readonly ushort[] ReelsL =
    [
        K11LcdFont.Pack(3, 1, 0, 3, 2, 0, 3), // O + |
        K11LcdFont.Pack(3, 1, 2, 2, 1, 0, 3), // O + /
        K11LcdFont.Pack(3, 3, 0, 2, 0, 1, 3), // O + -
        K11LcdFont.Pack(3, 1, 1, 2, 0, 2, 3), // O + \
    ];

    private static readonly ushort[] ReelsR = [ReelsL[0], ReelsL[3], ReelsL[2], ReelsL[1]];

    private static readonly ushort SpinV    = K11LcdFont.Pack(0, 0, 0, 1, 2, 0, 0);
    private static readonly ushort SpinH    = K11LcdFont.Pack(0, 2, 0, 0, 0, 1, 0);
    private static readonly ushort Star     = K11LcdFont.Pack(0, 2, 3, 1, 3, 3, 0);
    private static readonly ushort ArrL     = K11LcdFont.Pack(0, 0, 0, 0, 1, 2, 0); // <
    private static readonly ushort ArrR     = K11LcdFont.Pack(0, 0, 3, 0, 0, 0, 0); // >
    private static readonly ushort BrL      = K11LcdFont.Pack(3, 1, 0, 2, 0, 0, 0); // [
    private static readonly ushort BrR      = K11LcdFont.Pack(0, 1, 0, 2, 0, 0, 3); // ]
    private static readonly ushort EkgFlat  = K11LcdFont.Pack(0, 2, 0, 0, 0, 1, 0); // ---
    private static readonly ushort EkgUp    = K11LcdFont.Pack(0, 2, 2, 0, 1, 0, 0); // _/
    private static readonly ushort EkgDn    = K11LcdFont.Pack(0, 0, 1, 0, 0, 3, 0); // \_
    private static readonly ushort DropTop  = K11LcdFont.Pack(0, 0, 0, 1, 0, 0, 0);
    private static readonly ushort DropBot  = K11LcdFont.Pack(0, 0, 0, 0, 2, 0, 0);
    private static readonly ushort DropSpl  = K11LcdFont.Pack(0, 0, 0, 2, 0, 0, 0);
    private static readonly ushort OrbTop   = K11LcdFont.Pack(0, 1, 0, 0, 0, 0, 0);
    private static readonly ushort OrbRgt   = K11LcdFont.Pack(0, 0, 0, 0, 0, 0, 3);
    private static readonly ushort OrbBot   = K11LcdFont.Pack(0, 0, 0, 2, 0, 0, 0);
    private static readonly ushort OrbLft   = K11LcdFont.Pack(3, 0, 0, 0, 0, 0, 0);
    private static readonly ushort EyeHappy = K11LcdFont.Pack(0, 0, 2, 0, 0, 2, 0); // ^
    private static readonly ushort MouthSm  = K11LcdFont.Pack(0, 0, 0, 2, 0, 0, 0); // _
    private static readonly ushort MouthLoO = K11LcdFont.Pack(2, 2, 0, 2, 0, 1, 2); // o
    private static readonly ushort MouthHiO = K11LcdFont.Pack(3, 1, 0, 2, 0, 0, 3); // O

    public static bool SupportsInnerText(SticksTemplateKind kind) =>
        kind is SticksTemplateKind.ReelsText or SticksTemplateKind.BracketsText;

    public static int DefaultStepMs(SticksTemplateKind kind) => kind switch
    {
        SticksTemplateKind.ReelsText => 250,
        SticksTemplateKind.BracketsText => 400,
        SticksTemplateKind.Impulse => 350,
        SticksTemplateKind.PulseEkg => 250,
        SticksTemplateKind.Orbit => 250,
        SticksTemplateKind.Rain => 300,
        SticksTemplateKind.SingingFace => 450,
        _ => 300
    };

    public static string DefaultInnerText(SticksTemplateKind kind) => kind switch
    {
        SticksTemplateKind.ReelsText => "R2R",
        SticksTemplateKind.BracketsText => "HI-FI",
        _ => ""
    };

    public static (ushort[][] Frames, int FrameCount, int StepMs) Build(SticksTemplateKind kind, string? innerText)
    {
        var frames = new ushort[4][];
        for (int i = 0; i < 4; i++)
            frames[i] = new ushort[7];

        switch (kind)
        {
            case SticksTemplateKind.ReelsText:
            {
                ushort[] mid = K11LcdFont.CenterTextMasks(innerText, 5);
                for (int f = 0; f < 4; f++)
                {
                    frames[f][0] = ReelsL[f];
                    Array.Copy(mid, 0, frames[f], 1, 5);
                    frames[f][6] = ReelsR[f];
                }
                return (frames, 4, DefaultStepMs(kind));
            }

            case SticksTemplateKind.BracketsText:
            {
                ushort[] mid = K11LcdFont.CenterTextMasks(innerText, 5);
                (ushort L, ushort R)[] pairs = [(BrL, BrR), (ArrL, ArrR), (ArrR, ArrL), (ArrL, ArrR)];
                for (int f = 0; f < 4; f++)
                {
                    frames[f][0] = pairs[f].L;
                    Array.Copy(mid, 0, frames[f], 1, 5);
                    frames[f][6] = pairs[f].R;
                }
                return (frames, 4, DefaultStepMs(kind));
            }

            case SticksTemplateKind.Impulse:
                frames[0] = [0, 0, 0, SpinV, 0, 0, 0];
                frames[1] = [0, 0, 0, Star, 0, 0, 0];
                frames[2] = [0, 0, ArrL, SpinH, ArrR, 0, 0];
                frames[3] = [ArrL, SpinH, 0, 0, 0, SpinH, ArrR];
                return (frames, 4, DefaultStepMs(kind));

            case SticksTemplateKind.PulseEkg:
                frames[0] = [EkgFlat, EkgFlat, EkgFlat, EkgFlat, EkgFlat, EkgFlat, EkgFlat];
                frames[1] = [EkgUp,   EkgDn,   EkgFlat, EkgFlat, EkgFlat, EkgFlat, EkgFlat];
                frames[2] = [EkgFlat, EkgFlat, EkgUp,   EkgDn,   EkgFlat, EkgFlat, EkgFlat];
                frames[3] = [EkgFlat, EkgFlat, EkgFlat, EkgFlat, EkgUp,   EkgDn,   EkgFlat];
                return (frames, 4, DefaultStepMs(kind));

            case SticksTemplateKind.Orbit:
                frames[0] = [OrbTop, OrbBot, OrbTop, OrbBot, OrbTop, OrbBot, OrbTop];
                frames[1] = [OrbRgt, OrbLft, OrbRgt, OrbLft, OrbRgt, OrbLft, OrbRgt];
                frames[2] = [OrbBot, OrbTop, OrbBot, OrbTop, OrbBot, OrbTop, OrbBot];
                frames[3] = [OrbLft, OrbRgt, OrbLft, OrbRgt, OrbLft, OrbRgt, OrbLft];
                return (frames, 4, DefaultStepMs(kind));

            case SticksTemplateKind.Rain:
                frames[0] = [DropTop, 0,       DropBot, DropSpl, DropTop, 0,       DropBot];
                frames[1] = [DropBot, DropTop, DropSpl, 0,       DropBot, DropTop, DropSpl];
                frames[2] = [DropSpl, DropBot, 0,       DropTop, DropSpl, DropBot, 0      ];
                frames[3] = [0,       DropSpl, DropTop, DropBot, 0,       DropSpl, DropTop];
                return (frames, 4, DefaultStepMs(kind));

            case SticksTemplateKind.SingingFace:
                frames[0] = [ArrL, EyeHappy, 0, MouthSm,  0, EyeHappy, ArrR];
                frames[1] = [ArrL, EyeHappy, 0, MouthLoO, 0, EyeHappy, ArrR];
                frames[2] = [ArrL, EyeHappy, 0, MouthHiO, 0, EyeHappy, ArrR];
                frames[3] = [ArrL, EyeHappy, 0, MouthLoO, 0, EyeHappy, ArrR];
                return (frames, 4, DefaultStepMs(kind));

            default:
                return (frames, 1, DefaultStepMs(kind));
        }
    }
}
