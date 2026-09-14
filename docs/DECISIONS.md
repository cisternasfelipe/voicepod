# Registro de Decisiones de Arquitectura (ADR) - VoiceFlow

Este documento registra las decisiones arquitectónicas clave tomadas a lo largo de la evolución de **VoiceFlow**, su contexto, justificación y consecuencias.

---

## ADR-001: Arquitectura Híbrida Desacoplada (.NET 8 Core + React/WebView2)

- **Estado**: Aprobado / En proceso de migración
- **Fecha**: 2026-09-14
- **Contexto**:
  La aplicación inició con una interfaz clásica de escritorio en WPF (.NET 8). Si bien WPF permite una interacción directa con la API de Windows, crear interfaces fluidas, modernas, responsivas y con micro-interacciones avanzadas resulta verboso y costoso de mantener en XAML.
- **Decisión**:
  Separar el sistema en dos capas claramente delimitadas:
  1. **Backend (.NET 8 Core Engine)**: Responsable exclusivo de las operaciones de bajo nivel del sistema operativo: captura de audio WASAPI, inferencia local de voz con Sherpa-ONNX, registro de hooks de teclado de bajo nivel (`SetWindowsHookEx`), persistencia SQLite, cifrado DPAPI y cliente HTTP para APIs de LLM.
  2. **Frontend (React + Vite + TypeScript en Microsoft.Web.WebView2)**: Responsable de la experiencia de usuario, diseño visual, animaciones y gestión de estados de interfaz.
  3. **Canal de Comunicación (Typed IPC)**: Puente asíncrono bidireccional mediante mensajes JSON (`postMessage` / `WebMessageReceived`).
- **Consecuencias**:
  - *Positivas*: Desarrollo de UI mucho más rápido y vistoso, reutilización de ecosistemas modernos de diseño web (Tailwind, Lucide, Framer Motion), desacoplamiento total de la lógica de negocio.
  - *Mitigación*: Se empaquetan los assets compilados de React (`dist/`) en el instalador para que no se requiera Node.js en la máquina del usuario final.

---

## ADR-002: Hook Global de Teclado de Bajo Nivel con Soporte para Modificadores Puros (`Ctrl + Alt`)

- **Estado**: Aprobado e Implementado
- **Fecha**: 2026-09-14
- **Contexto**:
  La API estándar `RegisterHotKey` de Windows tiene limitaciones estrictas:
  - Falla si otra aplicación registró el mismo atajo.
  - No permite atajos compuestos únicamente por teclas modificadoras (por ejemplo, presionar y soltar `Ctrl + Alt` sin una letra adicional).
  - El capturador de teclado en la UI cerraba prematuramente la captura en el primer evento `KeyDown`.
- **Decisión**:
  Implementar un hook global de bajo nivel utilizando `SetWindowsHookEx` con `WH_KEYBOARD_LL` (`LowLevelKeyboardHook.cs`):
  1. Soporte explícito para `VirtualKey = 0` cuando el atajo consiste exclusivamente en modificadores (`Modifiers != None`).
  2. Detección en el evento `KeyUp`: cuando el usuario presiona `Ctrl + Alt` y luego suelta cualquiera de los dos modificadores, el hook dispara el evento de activación.
  3. En la interfaz de configuración, el capturador rastrea el conjunto de teclas activas (`_captureActiveKeys`) y solo consolida el atajo cuando el usuario **suelta todas las teclas**.
- **Consecuencias**:
  - *Positivas*: Soporte para combinaciones populares y ergonómicas como `Ctrl + Alt`, `Shift + Alt`, o cualquier tecla secundaria; cero conflictos con `RegisterHotKey`.

---

## ADR-003: Motor Local de STT (Transcripción Offline) con Sherpa-ONNX

- **Estado**: Aprobado e Implementado
- **Fecha**: 2026-09-14
- **Contexto**:
  El dictado por voz debe ser veloz, privado y de costo cero para la transcripción base. Depender exclusivamente de APIs en la nube agrega latencia inaceptable y costos recurrentes por cada segundo hablado.
- **Decisión**:
  Integrar **Sherpa-ONNX** ejecutando modelos offline optimizados para CPU/DirectML:
  - Modelo principal: **Parakeet TDT 0.6B v3 int8** de NeMo (altísima precisión en español e inglés, consumo moderado de memoria y baja latencia).
  - Fallback a modelos Whisper ONNX en caso de configuraciones alternativas.
  - Descarga y verificación bajo demanda en `%LOCALAPPDATA%\VoiceFlow\models`.
- **Consecuencias**:
  - *Positivas*: Transcripción 100% offline, gratuita, privada y en tiempo real.

---

## ADR-004: Refinamiento con LLMs (OpenRouter/OpenAI) con Control de Temperatura, Tope de Longitud y Razonamiento (Thinking)

- **Estado**: Aprobado e Implementado
- **Fecha**: 2026-09-14
- **Contexto**:
  La transcripción cruda de voz contiene muletillas, pausas, puntuación deficiente y errores gramaticales. Se requiere un LLM para corregir el texto sin alterar la intención. Sin embargo, modelos avanzados como `z-ai/glm-5.3-flash`, `deepseek-r1` u `o3-mini` poseen capacidades de razonamiento (Chain of Thought) que pueden demorar la respuesta innecesariamente o filtrar etiquetas `<think>` en la salida.
- **Decisión**:
  1. **Temperatura intuitiva**: Sliders con preajustes (`Preciso (0.1)`, `Equilibrado (0.3)`, `Creativo (0.7)`) y descripciones en tiempo real.
  2. **Tope de longitud de seguridad (`max_tokens`)**: Aclarar explícitamente al usuario que no alarga textos cortos ni cobra de más, sino que actúa como disyuntor de seguridad para evitar cortes en dictados largos.
  3. **Control de Razonamiento (Thinking)**:
     - Interruptor para activar/desactivar y selector de esfuerzo (`low`, `medium`, `high`).
     - Al desactivarse, envía `effort: "none"` y `max_tokens: 0` a la API de OpenRouter para desactivar el pensamiento interno y forzar respuesta instantánea.
     - **Sanitización de salida**: Se implementó una limpieza automática por expresión regular (`CleanReasoningTags`) para suprimir cualquier etiqueta `<think>...</think>` o `<thought>...</thought>` antes de pegar el texto en la aplicación activa del usuario.
- **Consecuencias**:
  - *Positivas*: Máxima velocidad en dictados cotidianos y salida completamente limpia de razonamientos internos.

---

## ADR-005: Seguridad y Almacenamiento Local (DPAPI + SQLite)

- **Estado**: Aprobado e Implementado
- **Fecha**: 2026-09-14
- **Contexto**:
  La aplicación requiere almacenar la clave de API (OpenRouter/OpenAI), configuraciones de usuario y el historial de dictados realizados.
- **Decisión**:
  1. **Claves de API**: Se cifran a nivel de sistema operativo utilizando **Windows DPAPI** (`ProtectedData.Protect` con `DataProtectionScope.CurrentUser`). Nunca se almacenan en texto plano en el archivo de configuración.
  2. **Historial de dictados**: Se gestiona mediante **SQLite** (`Microsoft.Data.Sqlite`) en `%LOCALAPPDATA%\VoiceFlow\history.db`, permitiendo búsqueda rápida por texto, filtros por fecha y re-copiado de transcripciones anteriores.
- **Consecuencias**:
  - *Positivas*: Seguridad nativa en Windows sin requerir contraseñas maestras; persistencia eficiente y aislada para cada usuario.

---

## ADR-006: Lanzamiento de Procesos en Escritorio Interactivo de Windows (`WinSta0\Default`)

- **Estado**: Aprobado e Implementado
- **Fecha**: 2026-09-14
- **Contexto**:
  Cuando agentes o herramientas de automatización lanzan procesos en segundo plano o entornos de servicio, Windows puede instanciar el proceso en un escritorio no interactivo o sesión aislada (`WinSta0\Desktop: exebox-...`). Esto provocaba que VoiceFlow se ejecutara pero fuera invisible para el usuario y colisionara con el Mutex de instancia única (`VoiceFlow_SingleInstance_Mutex`).
- **Decisión**:
  Crear una utilidad nativa (`tools/launch-app.ps1`) que utiliza `CreateProcess` de la API de Windows con `STARTUPINFO.lpDesktop = @"WinSta0\Default"`.
- **Consecuencias**:
  - *Positivas*: La aplicación siempre se inicia en la pantalla visible del usuario, garantizando interacción fluida tanto desde scripts de compilación como desde el acceso directo de Windows.
