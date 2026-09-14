using System.Text;
using VoiceFlow.App.Resources;
using VoiceFlow.Core.Models;

namespace VoiceFlow.App.Services;

/// <summary>
/// Renders a hotkey for the UI in the interface language. Core keeps a neutral table;
/// this adds the wording for the keys a user is most likely to bind.
/// </summary>
public static class HotkeyFormatter
{
    private static readonly Dictionary<int, Func<string>> LocalisedKeys = new()
    {
        [0x08] = () => Strings.KeyBackspace,
        [0x09] = () => Strings.KeyTab,
        [0x0D] = () => Strings.KeyEnter,
        [0x1B] = () => Strings.KeyEsc,
        [0x20] = () => Strings.KeySpace,
        [0x21] = () => Strings.KeyPageUp,
        [0x22] = () => Strings.KeyPageDown,
        [0x23] = () => Strings.KeyEnd,
        [0x24] = () => Strings.KeyHome,
        [0x25] = () => Strings.KeyLeft,
        [0x26] = () => Strings.KeyUp,
        [0x27] = () => Strings.KeyRight,
        [0x28] = () => Strings.KeyDown,
        [0x2D] = () => Strings.KeyInsert,
        [0x2E] = () => Strings.KeyDelete
    };

    public static string Describe(HotkeyDefinition definition)
    {
        if (definition.Keys is { Length: > 0 })
        {
            return string.Join(" + ", definition.Keys.Select(DescribeKey));
        }

        var text = new StringBuilder();

        if (definition.Modifiers.HasFlag(HotkeyModifiers.Control)) text.Append("Ctrl + ");
        if (definition.Modifiers.HasFlag(HotkeyModifiers.Alt)) text.Append("Alt + ");
        if (definition.Modifiers.HasFlag(HotkeyModifiers.Shift)) text.Append("Shift + ");
        if (definition.Modifiers.HasFlag(HotkeyModifiers.Win)) text.Append("Win + ");

        if (definition.VirtualKey != 0)
        {
            text.Append(DescribeKey(definition.VirtualKey));
        }
        else if (text.Length >= 3)
        {
            text.Length -= 3;
        }

        return text.ToString();
    }

    private static string DescribeKey(int virtualKey) =>
        LocalisedKeys.TryGetValue(virtualKey, out var resolve)
            ? resolve()
            : VirtualKeyNames.GetName(virtualKey);
}
