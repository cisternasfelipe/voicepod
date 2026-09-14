using VoiceFlow.Core.Models;
using Xunit;

namespace VoiceFlow.Tests;

public class HotkeyDefinitionTests
{
    [Fact]
    public void Default_IsControlAltSpace()
    {
        var hotkey = HotkeyDefinition.Default;

        Assert.True(hotkey.Modifiers.HasFlag(HotkeyModifiers.Control));
        Assert.True(hotkey.Modifiers.HasFlag(HotkeyModifiers.Alt));
        Assert.Equal(HotkeyDefinition.VkSpace, hotkey.VirtualKey);
        Assert.Equal("Ctrl + Alt + Espacio", hotkey.ToDisplayString());
    }

    [Fact]
    public void IsValid_AllowsSingleKeyAndModifiersOnly()
    {
        // Single key without modifiers (e.g. Space, F8)
        Assert.True(new HotkeyDefinition(HotkeyModifiers.None, HotkeyDefinition.VkSpace).IsValid);
        // Modifiers only (e.g. Ctrl + Alt)
        Assert.True(new HotkeyDefinition(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0).IsValid);
        // Standard combination (Ctrl + A)
        Assert.True(new HotkeyDefinition(HotkeyModifiers.Control, 0x41).IsValid);
        // Completely empty is invalid
        Assert.False(new HotkeyDefinition(HotkeyModifiers.None, 0).IsValid);
    }

    [Fact]
    public void ModifiersOnly_ToDisplayString_HasNoTrailingPlus()
    {
        var hotkey = new HotkeyDefinition(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0);
        Assert.Equal("Ctrl + Alt", hotkey.ToDisplayString());
    }

    [Theory]
    [InlineData(0x41, "A")]
    [InlineData(0x70, "F1")]
    [InlineData(0x87, "F24")]
    [InlineData(0x62, "Num 2")]
    [InlineData(0x1B, "Esc")]
    public void VirtualKeyNames_CoverCommonKeys(int virtualKey, string expected) =>
        Assert.Equal(expected, VirtualKeyNames.GetName(virtualKey));
}

public class PromptProfileTests
{
    [Fact]
    public void Seed_ContainsTheFourBuiltInProfiles()
    {
        var profiles = BuiltInProfiles.CreateSeed();

        Assert.Equal(4, profiles.Count);
        Assert.Collection(
            profiles.Select(p => p.Name),
            name => Assert.Equal("Crudo", name),
            name => Assert.Equal("Limpieza", name),
            name => Assert.Equal("Correo formal", name),
            name => Assert.Equal("Prompt para agente", name));
    }

    [Fact]
    public void RawProfile_DoesNotCallTheLlm()
    {
        var raw = BuiltInProfiles.CreateSeed().Single(p => p.BuiltInKey == BuiltInProfiles.RawKey);

        Assert.False(raw.UsesLlm);
        Assert.Empty(raw.SystemPrompt);
    }

    [Fact]
    public void UsesTranscriptPlaceholder_DetectsTheVariable()
    {
        var profile = new PromptProfile { SystemPrompt = "Corrige esto: {transcript}" };

        Assert.True(profile.UsesTranscriptPlaceholder);
        Assert.False(new PromptProfile { SystemPrompt = "Corrige el dictado" }.UsesTranscriptPlaceholder);
    }

    [Fact]
    public void GetActiveProfile_FallsBackToTheFirstProfile()
    {
        var settings = new AppSettings { ActiveProfileId = "does-not-exist" };

        Assert.Equal(settings.Profiles[0].Name, settings.GetActiveProfile().Name);
    }
}

public class LlmReasoningTests
{
    [Theory]
    [InlineData("<think>Pensando en cómo corregir el texto...</think>Hola mundo", "Hola mundo")]
    [InlineData("<think>\nMultiples líneas\nde pensamiento\n</think>\nTexto final con acentos.", "Texto final con acentos.")]
    [InlineData("<thought>Análisis detallado</thought>Resultado limpio", "Resultado limpio")]
    [InlineData("Texto normal sin etiquetas de razonamiento", "Texto normal sin etiquetas de razonamiento")]
    [InlineData("<think>Solo pensamiento</think>", "")]
    public void CleanReasoningTags_StripsTagsCorrectly(string input, string expected)
    {
        var cleaned = VoiceFlow.Llm.OpenAiCompatibleClient.CleanReasoningTags(input);
        Assert.Equal(expected, cleaned);
    }

    [Fact]
    public void LlmSettings_DefaultsToReasoningDisabledWithLowEffort()
    {
        var settings = new LlmSettings();
        Assert.False(settings.EnableReasoning);
        Assert.Equal("low", settings.ReasoningEffort);
    }
}
