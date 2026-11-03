namespace KeyToSoundNeo;

public sealed class SoundProfile
{
    public string Name { get; set; } = "New profile";
    public string KeyboardLayout { get; set; } = "JIS";
    public double Volume { get; set; } = 0.82;
    public double PitchVariation { get; set; } = 0.012;
    public double SpeedVolume { get; set; } = 0.3;
    public List<KeySound> Keys { get; set; } = [];
}

public sealed class AppPreferences
{
    public string ProcessPriority { get; set; } = "Normal";
    public bool BackgroundKeySoundsEnabled { get; set; }
}

public sealed class KeySound
{
    public string Key { get; set; } = "";
    public string Zone { get; set; } = "Center";
    public List<string> PressSounds { get; set; } = [];
    public List<string> ReleaseSounds { get; set; } = [];
}

public sealed class KsnManifest
{
    public int FormatVersion { get; set; } = 1;
    public SoundProfile Profile { get; set; } = new();
    public List<string> Sounds { get; set; } = [];
}

public static class KeyNames
{
    public static IReadOnlyList<(string Key, string Label, string Zone)[]> AnsiRows { get; } =
    [
        [("Escape", "Esc", "Special"), ("D1", "1", "Edge"), ("D2", "2", "Center"), ("D3", "3", "Center"),
         ("D4", "4", "Center"), ("D5", "5", "Center"), ("D6", "6", "Center"), ("D7", "7", "Center"),
         ("D8", "8", "Center"), ("D9", "9", "Center"), ("D0", "0", "Center"), ("OemMinus", "-", "Center"),
         ("OemPlus", "=", "Center"), ("Back", "⌫", "Special")],
        [("Tab", "Tab", "Special"), ("Q", "Q", "Edge"), ("W", "W", "Center"), ("E", "E", "Center"),
         ("R", "R", "Center"), ("T", "T", "Center"), ("Y", "Y", "Center"), ("U", "U", "Center"),
         ("I", "I", "Center"), ("O", "O", "Center"), ("P", "P", "Center"), ("OemOpenBrackets", "[", "Center"),
         ("Oem6", "]", "Center"), ("Return", "Enter", "Special")],
        [("Capital", "Caps", "Special"), ("A", "A", "Edge"), ("S", "S", "Center"), ("D", "D", "Center"),
         ("F", "F", "Center"), ("G", "G", "Center"), ("H", "H", "Center"), ("J", "J", "Center"),
         ("K", "K", "Center"), ("L", "L", "Center"), ("OemSemicolon", ";", "Center"), ("OemQuotes", "'", "Center"),
         ("Oem5", "\\", "Special")],
        [("LeftShift", "Shift", "Special"), ("Z", "Z", "Edge"), ("X", "X", "Center"), ("C", "C", "Center"),
         ("V", "V", "Center"), ("B", "B", "Center"), ("N", "N", "Center"), ("M", "M", "Center"),
         ("OemComma", ",", "Center"), ("OemPeriod", ".", "Center"), ("OemQuestion", "/", "Center"),
         ("RightShift", "Shift", "Special")],
        [("LeftCtrl", "Ctrl", "Special"), ("LeftAlt", "Alt", "Special"), ("Space", "Space", "Special"),
         ("RightAlt", "Alt", "Special"), ("RightCtrl", "Ctrl", "Special")]
    ];

    public static IReadOnlyList<(string Key, string Label, string Zone)[]> JisRows { get; } =
    [
        [("Escape", "Esc", "Special"), ("D1", "1", "Edge"), ("D2", "2", "Center"), ("D3", "3", "Center"),
         ("D4", "4", "Center"), ("D5", "5", "Center"), ("D6", "6", "Center"), ("D7", "7", "Center"),
         ("D8", "8", "Center"), ("D9", "9", "Center"), ("D0", "0", "Center"), ("OemMinus", "-", "Center"),
         ("OemPlus", "^", "Center"), ("Oem5", "¥", "Center"), ("Back", "⌫", "Special")],
        [("Tab", "Tab", "Special"), ("Q", "Q", "Edge"), ("W", "W", "Center"), ("E", "E", "Center"),
         ("R", "R", "Center"), ("T", "T", "Center"), ("Y", "Y", "Center"), ("U", "U", "Center"),
         ("I", "I", "Center"), ("O", "O", "Center"), ("P", "P", "Center"), ("Oem3", "@", "Center"),
         ("OemOpenBrackets", "[", "Center"), ("Return", "Enter", "Special")],
        [("Capital", "Caps", "Special"), ("A", "A", "Edge"), ("S", "S", "Center"), ("D", "D", "Center"),
         ("F", "F", "Center"), ("G", "G", "Center"), ("H", "H", "Center"), ("J", "J", "Center"),
         ("K", "K", "Center"), ("L", "L", "Center"), ("Oem1", ";", "Center"), ("OemQuotes", ":", "Center"),
         ("Oem6", "]", "Center")],
        [("LeftShift", "Shift", "Special"), ("OemBackslash", "\\", "Special"), ("Z", "Z", "Edge"), ("X", "X", "Center"),
         ("C", "C", "Center"), ("V", "V", "Center"), ("B", "B", "Center"), ("N", "N", "Center"),
         ("M", "M", "Center"), ("OemComma", ",", "Center"), ("OemPeriod", ".", "Center"),
         ("OemQuestion", "/", "Center"), ("RightShift", "Shift", "Special")],
        [("LeftCtrl", "Ctrl", "Special"), ("LeftAlt", "Alt", "Special"), ("ImeNonConvert", "無変換", "Special"),
         ("Space", "Space", "Special"), ("ImeConvert", "変換", "Special"), ("KanaMode", "かな", "Special"),
         ("RightCtrl", "Ctrl", "Special")]
    ];

    public static string Display(string key)
    {
        if (key.Length == 2 && key[0] == 'D' && char.IsDigit(key[1]))
            return key[1].ToString();
        return key switch
        {
            "Space" => "Space",
            "Return" => "Enter",
            "Back" => "Backspace",
            "Capital" => "Caps",
            "OemMinus" => "-",
            "OemPlus" => "=",
            "Oem1" => ";",
            "OemComma" => ",",
            "OemPeriod" => ".",
            "OemQuestion" => "/",
            "OemSemicolon" => ";",
            "OemQuotes" => "'",
            "Oem4" => "[",
            "OemOpenBrackets" => "[",
            "Oem6" => "]",
            "Oem7" => "'",
            "Oem5" => "\\",
            "Oem3" => "`",
            "ImeNonConvert" => "無変換",
            "ImeConvert" => "変換",
            "KanaMode" => "かな",
            "Oem102" => "\\",
            "OemBackslash" => "\\",
            "LeftShift" or "RightShift" => "Shift",
            "LeftCtrl" or "RightCtrl" => "Ctrl",
            "LeftAlt" or "RightAlt" => "Alt",
            _ => key
        };
    }

    public static string DefaultZone(string key, string layout)
    {
        key = CanonicalKey(key);
        var rows = layout == "ANSI" ? AnsiRows : JisRows;
        return rows.SelectMany(row => row).FirstOrDefault(item => item.Key == key).Zone ?? "Center";
    }

    public static string CanonicalKey(string key) => key switch
    {
        "Oem4" => "OemOpenBrackets",
        "Oem7" => "OemQuotes",
        "Oem102" => "OemBackslash",
        _ => key
    };
}
