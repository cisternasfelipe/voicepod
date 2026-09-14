using System.Text;
using System.Text.Json.Serialization;

namespace VoiceFlow.Core.Models;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Win = 0x0008
}

public enum HotkeyMode
{
    /// <summary>Records while the combination is held down; releasing it stops the recording.</summary>
    PushToTalk,

    /// <summary>First press starts the recording, second press stops it.</summary>
    Toggle
}

/// <summary>
/// A global hotkey supporting standard modifier + key combinations, modifier-only shortcuts,
/// and arbitrary multi-key combinations (>4 keys, distinguishing Left vs Right modifiers).
/// </summary>
public sealed record HotkeyDefinition
{
    public const int VkSpace = 0x20;

    public HotkeyModifiers Modifiers { get; init; }
    public int VirtualKey { get; init; }

    /// <summary>
    /// Exact virtual keys that constitute this hotkey combination.
    /// Allows combinations of 4, 5 or more keys, and distinguishes Left vs Right modifiers
    /// (e.g. Left Ctrl + Left Alt + Right Ctrl + Right Alt).
    /// </summary>
    public int[] Keys { get; init; } = Array.Empty<int>();

    public static HotkeyDefinition Default { get; } =
        new(HotkeyModifiers.Control | HotkeyModifiers.Alt, VkSpace);

    public HotkeyDefinition()
    {
    }

    public HotkeyDefinition(HotkeyModifiers modifiers, int virtualKey)
    {
        Modifiers = modifiers;
        VirtualKey = virtualKey;
        Keys = BuildKeysFromModifiers(modifiers, virtualKey);
    }

    [JsonConstructor]
    public HotkeyDefinition(HotkeyModifiers modifiers, int virtualKey, int[]? keys)
    {
        Modifiers = modifiers;
        VirtualKey = virtualKey;
        if (keys is { Length: > 0 })
        {
            Keys = keys.Where(k => k != 0).ToArray();
            if (Modifiers == HotkeyModifiers.None && Keys.Length > 0)
            {
                Modifiers = DeriveModifiers(Keys);
            }
            if (VirtualKey == 0 && Keys.Length > 0)
            {
                VirtualKey = DeriveVirtualKey(Keys);
            }
        }
        else
        {
            Keys = BuildKeysFromModifiers(modifiers, virtualKey);
        }
    }

    public HotkeyDefinition(params int[] keys)
    {
        var cleaned = keys.Where(k => k != 0).ToArray();
        Keys = cleaned;
        Modifiers = DeriveModifiers(cleaned);
        VirtualKey = DeriveVirtualKey(cleaned);
    }

    [JsonIgnore]
    public bool IsValid => Keys.Length > 0 || VirtualKey != 0 || Modifiers != HotkeyModifiers.None;

    public string ToDisplayString()
    {
        if (Keys.Length > 0)
        {
            return string.Join(" + ", Keys.Select(VirtualKeyNames.GetName));
        }

        var text = new StringBuilder();
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) text.Append("Ctrl + ");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) text.Append("Alt + ");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) text.Append("Shift + ");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) text.Append("Win + ");

        if (VirtualKey != 0)
        {
            text.Append(VirtualKeyNames.GetName(VirtualKey));
        }
        else if (text.Length >= 3)
        {
            text.Length -= 3;
        }

        return text.ToString();
    }

    public override string ToString() => ToDisplayString();

    private static int[] BuildKeysFromModifiers(HotkeyModifiers modifiers, int virtualKey)
    {
        var list = new List<int>();
        if (modifiers.HasFlag(HotkeyModifiers.Control)) list.Add(0x11);
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) list.Add(0x12);
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) list.Add(0x10);
        if (modifiers.HasFlag(HotkeyModifiers.Win)) list.Add(0x5B);
        if (virtualKey != 0 && !list.Contains(virtualKey)) list.Add(virtualKey);
        return list.ToArray();
    }

    private static HotkeyModifiers DeriveModifiers(IEnumerable<int> keys)
    {
        var mods = HotkeyModifiers.None;
        foreach (var k in keys)
        {
            if (k is 0x11 or 0xA2 or 0xA3) mods |= HotkeyModifiers.Control;
            else if (k is 0x12 or 0xA4 or 0xA5) mods |= HotkeyModifiers.Alt;
            else if (k is 0x10 or 0xA0 or 0xA1) mods |= HotkeyModifiers.Shift;
            else if (k is 0x5B or 0x5C) mods |= HotkeyModifiers.Win;
        }
        return mods;
    }

    private static int DeriveVirtualKey(IEnumerable<int> keys)
    {
        foreach (var k in keys)
        {
            if (k is not (0x11 or 0xA2 or 0xA3 or 0x12 or 0xA4 or 0xA5 or 0x10 or 0xA0 or 0xA1 or 0x5B or 0x5C))
            {
                return k;
            }
        }
        return 0;
    }
}

/// <summary>Display names for the virtual key codes a user is likely to bind.</summary>
public static class VirtualKeyNames
{
    private static readonly Dictionary<int, string> Names = new()
    {
        [0x08] = "Backspace", [0x09] = "Tab", [0x0D] = "Enter", [0x13] = "Pause",
        [0x14] = "Bloq Mayús", [0x1B] = "Esc", [0x20] = "Espacio", [0x21] = "Re Pág",
        [0x22] = "Av Pág", [0x23] = "Fin", [0x24] = "Inicio", [0x25] = "Izquierda",
        [0x26] = "Arriba", [0x27] = "Derecha", [0x28] = "Abajo", [0x2C] = "Impr Pant",
        [0x2D] = "Insert", [0x2E] = "Supr", [0xBA] = "Ñ", [0xBB] = "+", [0xBC] = ",",
        [0xBD] = "-", [0xBE] = ".", [0xBF] = "Ç", [0xC0] = "'", [0xDB] = "`",
        [0xDC] = "Ç", [0xDD] = "+", [0xDE] = "´", [0xE2] = "<",
        // Modifiers and specific left/right variants
        [0x10] = "Shift", [0xA0] = "Shift", [0xA1] = "Shift",
        [0x11] = "Ctrl", [0xA2] = "Ctrl", [0xA3] = "Ctrl",
        [0x12] = "Alt", [0xA4] = "Alt", [0xA5] = "Alt",
        [0x5B] = "Win", [0x5C] = "Win", [0x5D] = "Menu"
    };

    public static string GetName(int virtualKey)
    {
        if (Names.TryGetValue(virtualKey, out var name))
        {
            return name;
        }

        if (virtualKey is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A)
        {
            return ((char)virtualKey).ToString();
        }

        if (virtualKey is >= 0x70 and <= 0x87)
        {
            return "F" + (virtualKey - 0x6F);
        }

        if (virtualKey is >= 0x60 and <= 0x69)
        {
            return "Num " + (virtualKey - 0x60);
        }

        return $"0x{virtualKey:X2}";
    }
}
