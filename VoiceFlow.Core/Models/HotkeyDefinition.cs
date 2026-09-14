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
/// A global hotkey expressed with Win32 semantics: modifier flags plus a virtual key code,
/// which is exactly what RegisterHotKey expects.
/// </summary>
public sealed record HotkeyDefinition(HotkeyModifiers Modifiers, int VirtualKey)
{
    public const int VkSpace = 0x20;

    public static HotkeyDefinition Default { get; } =
        new(HotkeyModifiers.Control | HotkeyModifiers.Alt, VkSpace);

    [JsonIgnore]
    public bool IsValid => VirtualKey != 0 || Modifiers != HotkeyModifiers.None;

    public string ToDisplayString()
    {
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
        [0xDC] = "Ç", [0xDD] = "+", [0xDE] = "´", [0xE2] = "<"
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
