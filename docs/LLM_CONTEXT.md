# Contexto del Proyecto para LLMs (Handover Guide) - VoiceFlow

> **Nota para el Modelo/Agente que lea este archivo:**  
> Este documento contiene todo el contexto esencial de arquitectura, código, herramientas, convenciones y estado actual de **VoiceFlow** para que puedas retomar el desarrollo de inmediato sin perder tiempo investigando desde cero.

---

## 1. ¿Qué es VoiceFlow?
**VoiceFlow** es una aplicación de escritorio para Windows (arquitectura x64) diseñada para dictado y transcripción por voz inteligente de alta velocidad y bajo costo:
1. El usuario presiona un atajo global de teclado (por defecto `Ctrl + Alt`).
2. Se graba el audio del micrófono activo mediante WASAPI.
3. El audio se transcribe de forma local e instantánea (100% offline y gratuita) usando **Sherpa-ONNX** con el modelo **Parakeet TDT 0.6B v3 int8**.
4. La transcripción cruda se envía a un LLM (usando **OpenRouter** o cualquier endpoint compatible con OpenAI) para corregir puntuación, eliminar muletillas o reestructurar el texto según el perfil seleccionado.
5. El texto refinado se inyecta automáticamente en la ventana o cursor activo del usuario (simulando pulsaciones de teclado vía `SendInput` o mediante portapapeles).
6. El texto y sus metadatos quedan guardados en una base de datos SQLite local para consulta posterior.

---

## 2. Estructura de la Solución (`VoiceFlow.sln`)

La solución está desarrollada en **.NET 8** (`C# 12`, `Nullable` habilitado) siguiendo una arquitectura modular en capas:

```text
VoiceFlow.sln
├── VoiceFlow.Core/         # Modelos de dominio (AppSettings, HotkeyDefinition, HistoryItem), interfaces y utilidades de rutas
├── VoiceFlow.Audio/        # Captura de audio WASAPI con NAudio, buffers circulares, cálculo de volumen RMS
├── VoiceFlow.Stt/          # Inferencia offline con Sherpa-ONNX, descarga y gestión de modelos Parakeet / Whisper
├── VoiceFlow.Llm/          # Cliente HTTP OpenAI-compatible/OpenRouter, ChatCompletion, prompts de refinamiento, limpieza de etiquetas <think>
├── VoiceFlow.Storage/      # Repositorio SQLite (Microsoft.Data.Sqlite) para historial, serialización JSON de settings
├── VoiceFlow.Interop/      # P/Invoke de Windows: Hook global WH_KEYBOARD_LL, inyección SendInput, DPAPI para ApiKey
├── VoiceFlow.App/          # Host de escritorio (WPF + WebView2), ciclo de vida, Dependency Injection, System Tray, MVVM
├── VoiceFlow.Tests/        # Pruebas unitarias con xUnit y Moq (55 tests automatizados)
├── tools/                  # Scripts de automatización PowerShell (generate-strings.ps1, launch-app.ps1)
└── docs/                   # Documentación técnica modular (DECISIONS.md, LLM_CONTEXT.md, FLOWS.md, SYSTEM_DESIGN_UI.md)
```

---

## 3. Convenciones de Código y Arquitectura

- **Dependency Injection**: Se utiliza `Microsoft.Extensions.Hosting` con un `IHost` central configurado en `VoiceFlow.App/App.xaml.cs`. Todos los servicios se registran en el contenedor IoC.
- **Logging**: Implementado con **Serilog** (`Serilog.Sinks.File`).
  - Los logs se escriben en: `%LOCALAPPDATA%\VoiceFlow\logs\voiceflow-YYYYMMDD.log`.
  - Errores tempranos antes de iniciar el host van a: `%LOCALAPPDATA%\VoiceFlow\logs\startup.log` vía `BootstrapLog.cs`.
- **Localización (i18n)**:
  - Archivos de recursos: `Strings.resx` (español por defecto) y `Strings.en.resx` (inglés).
  - La clase fuertemente tipada `Strings.cs` se genera con el script: `powershell -File tools/generate-strings.ps1`.
- **Patrón MVVM**: En la capa WPF se utiliza `CommunityToolkit.Mvvm` (`[ObservableProperty]`, `[RelayCommand]`).
- **Seguridad**: Las claves de API nunca se guardan en texto plano en `settings.json`; se cifran con DPAPI vía `VoiceFlow.Interop.DpapiSecretStorage`.

---

## 4. Rutas Críticas del Sistema en Tiempo de Ejecución

Todas las rutas de datos del usuario se resuelven mediante `VoiceFlow.Core.AppPaths`:
- **Carpeta Base**: `C:\Users\<Usuario>\AppData\Local\VoiceFlow`
- **Configuración**: `...\VoiceFlow\settings.json`
- **Historial SQLite**: `...\VoiceFlow\history.db`
- **Modelos STT Offline**: `...\VoiceFlow\models\parakeet-tdt-0.6b-v3-int8\`
- **Logs de ejecución**: `...\VoiceFlow\logs\`

---

## 5. Comandos de Compilación, Prueba y Despliegue

### Compilación limpia de la solución
```powershell
dotnet build VoiceFlow.sln
```

### Ejecución de todas las pruebas unitarias
```powershell
dotnet test VoiceFlow.Tests
```
*(Actualmente cuenta con 55 tests pasando al 100%).*

### Publicación en modo Release (Auto-contenido x64)
```powershell
dotnet publish VoiceFlow.App -c Release -r win-x64 --self-contained true -o publish
```

### Lanzamiento interactivo en el escritorio de Windows
> **CRÍTICO:** Debido a que sesiones de background o sub-agentes pueden correr en escritorios no interactivos (`exebox`), para lanzar la app siempre usa:
```powershell
powershell -ExecutionPolicy Bypass -File tools/launch-app.ps1
```
*(Este script invoca `CreateProcess` con `lpDesktop = @"WinSta0\Default"` para asegurar visibilidad en pantalla).*

---

## 6. Estado Actual del Proyecto y Próximos Pasos (Roadmap)

### Lo que ya está terminado y verificado:
1. **Pipeline de Voz Completo**: Captura WASAPI + Transcripción Sherpa-ONNX Parakeet + Inyección de texto.
2. **Hook Global de Teclado**: Soporta combinaciones de modificadores puros (como `Ctrl + Alt`) detectadas en `KeyUp` mediante `WH_KEYBOARD_LL`.
3. **Parámetros de IA Intuitivos**:
   - Temperatura con slider interactivo y preajustes explicados (`Preciso`, `Equilibrado`, `Creativo`).
   - Tope de longitud de texto (`max_tokens`) con estimación en palabras (`≈ hasta X palabras`) y garantías de que no alarga textos cortos ni gasta saldo extra.
   - Control de Razonamiento (Thinking / CoT) con interruptor, selector de esfuerzo (`low`, `medium`, `high`) y sanitización automática de etiquetas `<think>...</think>`.
4. **Soporte de WebView2 agregado**: Se instaló el paquete `Microsoft.Web.WebView2` en `VoiceFlow.App`.

### Siguiente fase: Migración Progresiva de UI a React + WebView2
- **Objetivo**: Reemplazar gradualmente las vistas WPF por vistas web modernas en React alojadas en WebView2.
- **Estrategia acordada con el usuario**: Integración **pantalla por pantalla**:
  1. Crear la estructura del frontend web (React + Vite + TypeScript en una carpeta como `VoiceFlow.Web/`).
  2. Implementar el puente IPC tipado bidireccional en C# (`WebView2BridgeController`) para comunicar React con el backend de .NET.
  3. Migrar la primera pantalla indicada por el usuario (ej. Ajustes o Widget flotante de dictado).
