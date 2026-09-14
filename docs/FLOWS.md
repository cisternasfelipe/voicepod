# Diagramas de Flujo del Sistema (Mermaid) - VoiceFlow

Este documento contiene la representación visual y técnica de los flujos de ejecución más importantes de **VoiceFlow** utilizando diagramas de Mermaid.

---

## 1. Pipeline Completo de Dictado por Voz

Describe el recorrido que hace el audio desde que el usuario presiona el atajo de teclado hasta que el texto refinado es inyectado en su aplicación activa.

```mermaid
sequenceDiagram
    autonumber
    actor Usuario
    participant Hook as LowLevelKeyboardHook (WH_KEYBOARD_LL)
    participant HotkeySvc as GlobalHotkeyService
    participant Pipeline as DictationPipeline
    participant Audio as AudioCaptureService (WASAPI)
    participant STT as SherpaTranscriptionService (Parakeet)
    participant LLM as OpenAiCompatibleClient (OpenRouter)
    participant Interop as WindowsTextInjector (SendInput)
    participant DB as SqliteHistoryRepository

    Usuario->>Hook: Presiona atajo global (ej. Ctrl + Alt)
    Hook->>HotkeySvc: Detecta coincidencia (KeyDown / KeyUp)
    HotkeySvc->>Pipeline: Dispara evento HotkeyPressed

    alt Modo Toggle o Hold (Inicia Grabación)
        Pipeline->>Audio: StartRecording()
        Audio-->>Pipeline: Stream de audio (PCM 16kHz Mono)
        Pipeline-->>Usuario: Feedback sonoro / Notificación visual de inicio
    end

    Usuario->>Hook: Suelta o vuelve a presionar atajo
    Hook->>HotkeySvc: Atajo finalizado
    HotkeySvc->>Pipeline: Dispara evento Stop

    Pipeline->>Audio: StopRecordingAsync()
    Audio-->>Pipeline: Buffer de audio completo (byte[])

    Pipeline->>STT: TranscribeAsync(audioBuffer)
    Note over STT: Inferencia local offline con Parakeet TDT 0.6B int8
    STT-->>Pipeline: Texto crudo ("hola que tal nos vemos hoy")

    alt Refinamiento con LLM Habilitado
        Pipeline->>LLM: RefineTextAsync(textoCrudo, prompt, settings)
        Note over LLM: Envía Temperature, MaxTokens, Reasoning (low/none)<br/>Limpia etiquetas <think>...</think>
        LLM-->>Pipeline: Texto pulido ("Hola, ¿qué tal? Nos vemos hoy.")
    else LLM Deshabilitado
        Pipeline->>Pipeline: Usa texto crudo
    end

    Pipeline->>Interop: InjectText(textoFinal)
    Note over Interop: Inyecta en la ventana activa del usuario vía SendInput o Portapapeles

    Pipeline->>DB: SaveAsync(HistoryItem)
    Note over DB: Guarda en %LOCALAPPDATA%\VoiceFlow\history.db
    Pipeline-->>Usuario: Feedback sonoro de éxito
```

---

## 2. Arquitectura de Comunicación IPC (WebView2 React <-> .NET Host)

Describe cómo interactúa la interfaz de usuario en React con el motor central de .NET a través del puente de WebView2.

```mermaid
sequenceDiagram
    autonumber
    participant UI as Componente React (Vite)
    participant BridgeClient as bridge.ts (Frontend IPC Facade)
    participant WebView2 as Microsoft.Web.WebView2
    participant BridgeHost as WebView2BridgeController (.NET)
    participant Dispatcher as MessageDispatcher (.NET)
    participant Services as Servicios del Sistema (Audio, LLM, Config)

    rect rgb(20, 30, 45)
    note over UI, Services: Patrón 1: Petición / Respuesta Asíncrona (Request - Response)
    UI->>BridgeClient: bridge.settings.save(newSettings)
    BridgeClient->>WebView2: window.chrome.webview.postMessage({ id: "req-1", action: "settings:save", payload: newSettings })
    WebView2->>BridgeHost: Evento WebMessageReceived
    BridgeHost->>Dispatcher: Enrutar acción "settings:save"
    Dispatcher->>Services: ISettingsService.SaveAsync(newSettings)
    Services-->>Dispatcher: Resultado exitoso
    Dispatcher->>BridgeHost: Respuesta estructurada
    BridgeHost->>WebView2: PostWebMessageAsJson({ id: "req-1", success: true, data: null })
    WebView2->>BridgeClient: Evento window.chrome.webview.addEventListener('message')
    BridgeClient-->>UI: Resuelve Promise(void)
    end

    rect rgb(25, 40, 30)
    note over UI, Services: Patrón 2: Flujo de Eventos Push en Tiempo Real (Event Stream)
    Services->>BridgeHost: Evento OnAudioLevelChanged(rms: 0.72)
    BridgeHost->>WebView2: PostWebMessageAsJson({ event: "audio:level", data: 0.72 })
    WebView2->>BridgeClient: Dispara listener registrado "audio:level"
    BridgeClient-->>UI: Actualiza estado reactivo / Anima visualizador de ondas
    end
```

---

## 3. Captura y Registro de Atajos Globales de Teclado

Detalla cómo se registran atajos en la interfaz (permitiendo combinaciones complejas como `Ctrl + Alt` sin conflicto) y cómo se vinculan con el hook global del sistema operativo.

```mermaid
flowchart TD
    Start(["Usuario hace clic en 'Capturar atajo'"]) --> Listening["Modo de escucha activo en Settings"]
    Listening --> KeyDownEvent["Usuario presiona tecla(s)"]
    KeyDownEvent --> TrackKeys["Agregar tecla a _captureActiveKeys"]
    TrackKeys --> ShowPreview["Mostrar en botón: 'Presiona tus teclas... [Ctrl + Alt]'"]
    ShowPreview --> CheckRelease{"¿Se soltaron todas las teclas?"}
    
    CheckRelease -- No --> KeyDownEvent
    CheckRelease -- Sí --> AnalyzeKeys["Analizar teclas capturadas"]
    
    AnalyzeKeys --> IsPureModifier{"¿Son solo modificadores?<br/>(Ej: Ctrl + Alt)"}
    IsPureModifier -- Sí --> SetModifierOnly["VirtualKey = 0<br/>Modifiers = Alt | Control"]
    IsPureModifier -- No --> SetStandard["VirtualKey = Tecla primaria<br/>Modifiers = Modificadores activos"]
    
    SetModifierOnly --> SaveConfig["Guardar HotkeyDefinition en SettingsViewModel"]
    SetStandard --> SaveConfig
    
    SaveConfig --> ClickSave["Usuario presiona 'Guardar'"]
    ClickSave --> UpdateHook["GlobalHotkeyService.UpdateHotkey()"]
    UpdateHook --> Rehook["Re-registrar comparador en LowLevelKeyboardHook (WH_KEYBOARD_LL)"]
    Rehook --> Ready(["Atajo activo globalmente en Windows"])
```

---

## 4. Refinamiento LLM y Sanitización de Razonamiento (Thinking)

Muestra cómo se procesan las opciones de temperatura, límite máximo de longitud y cómo se filtran los pensamientos internos de modelos como DeepSeek R1 o GLM-5.

```mermaid
flowchart TD
    RawText["Texto crudo de voz transcrito por Parakeet"] --> CheckApiKey{"¿Hay API Key configurada?"}
    
    CheckApiKey -- No --> ReturnRaw["Retornar texto crudo sin modificar"]
    CheckApiKey -- Sí --> BuildPayload["Construir ChatCompletionRequest"]
    
    BuildPayload --> SetTemp["Asignar Temperature (ej. 0.3)"]
    SetTemp --> SetTokens["Asignar MaxTokens (Tope de seguridad, ej. 1024)"]
    
    SetTokens --> CheckReasoning{"¿EnableReasoning activo?"}
    CheckReasoning -- Sí --> AddThinkingParams["ReasoningEffort = effort ('low' | 'medium' | 'high')<br/>Reasoning = { Effort: effort, Exclude: false }"]
    CheckReasoning -- No --> DisableThinking["ReasoningEffort = 'none'<br/>Reasoning = { Effort: 'none', MaxTokens: 0 }"]
    
    AddThinkingParams --> SendHttp["Enviar POST a https://openrouter.ai/api/v1/chat/completions"]
    DisableThinking --> SendHttp
    
    SendHttp --> ReceiveResponse["Recibir respuesta HTTP"]
    ReceiveResponse --> CleanTags["Ejecutar CleanReasoningTags(response)"]
    
    CleanTags --> RegexThink["Eliminar bloques &lt;think&gt;...&lt;/think&gt; y &lt;thought&gt;...&lt;/thought&gt;"]
    RegexThink --> TrimText["Trim() y limpieza de espacios redundantes"]
    TrimText --> FinalOutput(["Texto final limpio listo para inyectar"])
```

---

## 5. Ciclo de Vida de la Aplicación y Manejo del System Tray

Representa el arranque de VoiceFlow, la protección de instancia única con Mutex y el manejo de ventanas.

```mermaid
flowchart TD
    Launch(["Inicio de VoiceFlow.exe"]) --> CheckMutex{"¿Existe VoiceFlow_SingleInstance_Mutex?"}
    
    CheckMutex -- Sí --> SendToFront["Notificar instancia existente y cerrar nueva"]
    SendToFront --> ExitAlreadyRunning(["Terminar proceso"])
    
    CheckMutex -- No --> InitHost["Iniciar Microsoft.Extensions.Hosting (IHost)"]
    InitHost --> InitServices["Registrar Servicios:<br/>- Settings (DPAPI + JSON)<br/>- Audio (WASAPI)<br/>- STT (Sherpa Parakeet)<br/>- Hotkey Hook (WH_KEYBOARD_LL)<br/>- Storage (SQLite)"]
    
    InitServices --> CheckModel{"¿Existe modelo Parakeet en disco?"}
    CheckModel -- No --> DownloadModel["Descargar modelo automáticamente a %LOCALAPPDATA%"]
    CheckModel -- Sí --> LoadModel["Cargar modelo en memoria (Sherpa-ONNX)"]
    DownloadModel --> LoadModel
    
    LoadModel --> InitTray["Crear TrayIconController en la bandeja del sistema"]
    InitTray --> HookGlobalExceptions["Conectar DispatcherUnhandledException (Toast de error no fatal)"]
    
    HookGlobalExceptions --> UserAction{"Interacción del Usuario"}
    UserAction -- Doble clic en Tray o atajo --> ShowMainWindow["Mostrar Ventana de Ajustes / UI"]
    UserAction -- Atajo de dictado --> RunPipeline["Ejecutar Pipeline de Dictado"]
    UserAction -- Clic en 'Salir' de la bandeja --> Shutdown["Cierre ordenado: Dispose de audio, hooks y base de datos"]
    Shutdown --> ExitSuccess(["Fin del proceso"])
```
