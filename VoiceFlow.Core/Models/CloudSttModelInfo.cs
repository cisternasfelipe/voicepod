namespace VoiceFlow.Core.Models;

public sealed record CloudSttModelInfo(
    string Id,
    string DisplayName,
    string Provider,
    string Description,
    string Pricing,
    bool IsDefault = false
);

public static class CloudSttCatalog
{
    public const string DefaultModelId = "microsoft/mai-transcribe-2";

    public static readonly IReadOnlyList<CloudSttModelInfo> Models =
    [
        new(
            "microsoft/mai-transcribe-2",
            "MAI-Transcribe 2",
            "Microsoft AI",
            "#1 en FLEURS multilingüe. 60 idiomas, alta velocidad, code-switching y diarización.",
            "$0.10 / hora",
            IsDefault: true),

        new(
            "openai/whisper-large-v3-turbo",
            "Whisper Large v3 Turbo",
            "OpenAI",
            "Optimizado para velocidad y costo. 99+ idiomas, velocidad 216x en tiempo real.",
            "$0.000003 / seg (~$0.01 / hora)"),

        new(
            "openai/whisper-large-v3",
            "Whisper Large v3",
            "OpenAI",
            "1.55B parámetros. Máxima robustez ante ruido de fondo y modismos complejos.",
            "$0.000008 / seg (~$0.03 / hora)"),

        new(
            "meta/muse-voice-transcribe-1.0",
            "Muse Voice Transcribe 1.0",
            "Meta",
            "Baja latencia, ideal para push-to-talk, detección de idioma y sesgo de palabras clave.",
            "$0.18 / hora"),

        new(
            "nvidia/nemotron-3.5-asr-streaming-multilingual-0.6b",
            "Nemotron 3.5 ASR Streaming 0.6B",
            "NVIDIA",
            "Arquitectura FastConformer-RNNT para streaming multilingüe de ultrabaja latencia.",
            "$0.000003 / seg"),

        new(
            "mistralai/voxtral-small-24b-2507-stt",
            "Voxtral Small 24B",
            "Mistral AI",
            "Modelo de 24B parámetros para transcripción avanzada y comprensión auditiva.",
            "$0.00005 / seg"),

        new(
            "mistralai/voxtral-mini-3b-2507",
            "Voxtral Mini 3B",
            "Mistral AI",
            "Modelo compacto y veloz de Mistral para procesamiento de audio.",
            "$0.000017 / seg"),

        new(
            "qwen/qwen3-asr-1.7b",
            "Qwen3 ASR 1.7B",
            "Alibaba Qwen",
            "Identificación multilingüe en 30 idiomas, marcas temporales por palabra y alta fidelidad.",
            "$0.000008 / seg"),

        new(
            "qwen/qwen3-asr-0.6b",
            "Qwen3 ASR 0.6B",
            "Alibaba Qwen",
            "Versión compacta de Qwen3 para transcripciones rápidas y ligeras.",
            "$0.000003 / seg"),

        new(
            "qwen/qwen3-asr-flash-2026-02-10",
            "Qwen3 ASR Flash",
            "Alibaba Qwen",
            "Entrenado en decenas de millones de horas. Filtra ruido acústico difícil y música de fondo.",
            "$0.000035 / seg"),

        new(
            "openai/gpt-4o-mini-transcribe",
            "GPT-4o Mini Transcribe",
            "OpenAI",
            "Capacidades de audio de GPT-4o Mini con tarificación por tokens.",
            "$1.25 / M tokens"),

        new(
            "openai/gpt-transcribe",
            "GPT Transcribe",
            "OpenAI",
            "Alta precisión, contexto libre y soporte de múltiples pistas de idioma.",
            "$0.0045 / minuto"),

        new(
            "deepgram/nova-3",
            "Nova-3",
            "Deepgram",
            "Transcripción multilingüe de propósito general, rápida y económica.",
            "$0.0043 / minuto"),

        new(
            "google/chirp-3",
            "Chirp 3",
            "Google",
            "24 idiomas GA y 77+ preview, reductor de ruido integrado y puntuación automática.",
            "$0.016 / minuto"),

        new(
            "x-ai/grok-stt-1.0",
            "Grok STT 1.0",
            "xAI / SpaceX",
            "Transcripción con marcas de tiempo por palabra y diarización de interlocutores.",
            "$0.10 / hora"),

        new(
            "fish-audio/transcribe-1",
            "Transcribe 1",
            "Fish Audio",
            "Detección automática de idioma y segmentos alineados por palabra.",
            "$0.0001 / seg")
    ];
}
