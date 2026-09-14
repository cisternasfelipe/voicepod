# VoicePod

Dictado por voz inteligente para Windows 10/11 con **transcripción local offline** (NVIDIA Parakeet TDT 0.6B v3
INT8 sobre sherpa-onnx) y **refinamiento opcional con LLMs** (OpenRouter / OpenAI) con control de temperatura,
tope de longitud y razonamiento (Thinking / CoT). Pulsas un atajo global (ej. `Ctrl + Alt`), hablas, y el texto final
se pega automáticamente en la ventana que tenías delante.

- El audio **nunca sale del equipo**: la transcripción se ejecuta en tu CPU.
- Al LLM solo se envía el texto transcrito, y únicamente si el perfil activo lo usa.
- Si el LLM falla o no hay internet, se pega la transcripción cruda y el fallo queda
  anotado en el historial.

---

## Requisitos

| | |
|---|---|
| Sistema | Windows 10 21H2 o superior / Windows 11, x64 |
| Runtime | Ninguno: la publicación es *self-contained* (incluye .NET 8) |
| Para compilar | .NET SDK 8 o superior |
| Disco | ~180 MB de aplicación + ~670 MB del modelo |
| RAM | ~1 GB con el modelo cargado |
| Micrófono | Cualquier dispositivo de captura WASAPI |
| Opcional | Clave de API de un servicio compatible con OpenAI (o un servidor local) |
| Opcional | [Inno Setup 6](https://jrsoftware.org/isdl.php) para generar el instalador |

---

## Atajos y controles

| Acción | Atajo / control | Notas |
|---|---|---|
| Iniciar / detener dictado | **Ctrl + Alt + Espacio** | Reasignable en Ajustes → Atajo |
| Dictar manteniendo pulsado | El mismo atajo en modo *Push to talk* | Se elige en Ajustes → Atajo |
| Abrir la ventana principal | Clic en el icono de la bandeja | Doble clic también |
| Cambiar de perfil | Clic derecho en la bandeja → *Perfil activo* | Sin reiniciar |
| Abrir historial / ajustes | Clic derecho en la bandeja | |
| Cerrar a la bandeja | Botón **X** de la ventana | El atajo sigue activo |
| Salir de verdad | Clic derecho en la bandeja → *Salir* | |
| Cancelar el dictado en curso | Botón *Cancelar* de la ventana principal | Descarta el audio |

El overlay de estado aparece abajo a la derecha mientras dura el dictado y **nunca roba el
foco** a la ventana de destino.

---

## Cómo obtener el modelo

En el primer arranque, VoiceFlow abre un asistente que descarga el modelo a:

```
%LOCALAPPDATA%\VoiceFlow\models\parakeet-tdt-0.6b-v3-int8\
```

Son cuatro archivos (~670 MB en total), descargados uno a uno con barra de progreso,
verificación de tamaño y SHA256, reintentos y reanudación si se corta:

| Archivo | Tamaño |
|---|---|
| `encoder.int8.onnx` | 652 MB |
| `decoder.int8.onnx` | 11,8 MB |
| `joiner.int8.onnx` | 6,4 MB |
| `tokens.txt` | 92 KB |

Origen por defecto (configurable en `settings.json` → `Stt.ModelBaseUrl`):

```
https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8/resolve/main
```

### Si ya tienes el modelo descargado

En el asistente, pulsa **«Usar carpeta local...»** y selecciona la carpeta que contenga esos
cuatro archivos. También puedes cambiar la ruta en Ajustes → Transcripción.

### Descarga manual (alternativa)

```powershell
$dir = "$env:LOCALAPPDATA\VoiceFlow\models\parakeet-tdt-0.6b-v3-int8"
New-Item -ItemType Directory -Force $dir | Out-Null
$base = "https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8/resolve/main"
foreach ($f in "encoder.int8.onnx","decoder.int8.onnx","joiner.int8.onnx","tokens.txt") {
    curl.exe -L -o "$dir\$f" "$base/$f"
}
```

---

## Cómo compilar

```powershell
git clone <este-repositorio> VoiceFlow
cd VoiceFlow
dotnet build VoiceFlow.sln -c Debug
dotnet run --project VoiceFlow.App
```

Pruebas:

```powershell
dotnet test VoiceFlow.Tests/VoiceFlow.Tests.csproj
```

Las pruebas de integración que necesitan el modelo se **omiten solas** si el modelo no está
descargado, así que la suite funciona en una máquina limpia.

---

## Cómo publicar

```powershell
dotnet publish VoiceFlow.App\VoiceFlow.App.csproj -c Release -r win-x64 --self-contained true -o publish
```

Resultado: `publish\VoiceFlow.exe` (~171 MB, incluye .NET 8, WPF y las bibliotecas nativas
de sherpa-onnx / onnxruntime para win-x64). El modelo **no** se incluye.

### Instalador

```powershell
# Requiere Inno Setup 6
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\VoiceFlow.iss
```

Genera `dist\VoiceFlow-0.1.0-setup.exe`, que instala en `Program Files`, crea accesos
directos, ofrece la casilla «iniciar con Windows» y, al desinstalar, **pregunta** antes de
borrar el historial, los ajustes y el modelo (por defecto los conserva).

---

## Dónde se guardan las cosas

```
%LOCALAPPDATA%\VoiceFlow\
├─ settings.json   Configuración (la ApiKey va cifrada con DPAPI)
├─ history.db      Historial de dictados (SQLite)
├─ models\         Modelo de transcripción
├─ audio\          WAV de cada dictado (solo si activas la opción)
└─ logs\           Registro diario, se conservan 7 días
```

La clave de API se cifra con DPAPI (ámbito del usuario actual) y **nunca** se escribe en
claro ni aparece en los logs.

---

## Perfiles de prompt

Un perfil define qué le pide VoiceFlow al LLM sobre tu dictado. Vienen cuatro:

| Perfil | Qué hace |
|---|---|
| **Crudo** | No llama al LLM: pega la transcripción tal cual |
| **Limpieza** | Corrige puntuación, muletillas y errores de dictado sin cambiar el contenido |
| **Correo formal** | Reescribe el dictado como correo profesional en español |
| **Prompt para agente** | Reescribe el dictado como instrucción técnica estructurada |

Se editan en Ajustes → Perfiles: nombre, prompt, modelo y temperatura propios, con
«restaurar por defecto» para los perfiles de serie. Si el prompt contiene la variable
`{transcript}`, se sustituye por la transcripción y se envía como mensaje de usuario; si no,
el prompt va como mensaje `system` y la transcripción como mensaje `user`.

El cambio de perfil surte efecto en el **siguiente dictado**, sin reiniciar.

---

## Configurar la IA

Ajustes → IA. Funciona con cualquier endpoint compatible con OpenAI Chat Completions;
solo cambia la `BaseUrl`:

| Servicio | BaseUrl |
|---|---|
| OpenAI | `https://api.openai.com/v1` |
| OpenRouter | `https://openrouter.ai/api/v1` |
| Groq | `https://api.groq.com/openai/v1` |
| LM Studio (local) | `http://localhost:1234/v1` |
| Ollama (local) | `http://localhost:11434/v1` |

El botón **«Probar conexión»** consulta `/models` y, si el servidor no lo implementa, hace
una petición mínima de chat.

---

## Idioma de la interfaz

Ajustes → General permite elegir **Español** (por defecto) o **Inglés**; el cambio se aplica
al reiniciar la aplicación. Los textos viven en
[`VoiceFlow.App/Resources/Strings.resx`](VoiceFlow.App/Resources/Strings.resx) (español,
cultura neutra) y `Strings.en.resx` (inglés, ensamblado satélite `en/`).

Tras añadir o renombrar una clave hay que regenerar la clase de acceso:

```powershell
pwsh tools/generate-strings.ps1
```

Nota: unos pocos detalles técnicos de error que genera el cliente de IA, el descargador del
modelo y el pegado (por ejemplo «Petición cancelada» o «La ventana de destino ya no existe»)
se muestran siempre en español, anexados al mensaje traducido.

---

## Arquitectura

```
VoiceFlow.sln
├─ VoiceFlow.App       WPF: ventanas, ViewModels (MVVM), bandeja, DI (Generic Host)
├─ VoiceFlow.Core      Dominio: pipeline de estados, modelos, interfaces
├─ VoiceFlow.Audio     NAudio/WASAPI: captura, mezcla a mono, resampleo a 16 kHz, nivel
├─ VoiceFlow.Stt       sherpa-onnx: carga del modelo, transcripción, descargador
├─ VoiceFlow.Llm       Cliente HTTP compatible con OpenAI y gestión de perfiles
├─ VoiceFlow.Interop   Win32: RegisterHotKey, SendInput, portapapeles, foreground window
├─ VoiceFlow.Storage   SQLite (Dapper), settings.json, DPAPI
└─ VoiceFlow.Tests     xUnit
```

Solo `VoiceFlow.App` conoce WPF; el resto son bibliotecas sin interfaz. `VoiceFlow.Core` no
contiene textos de interfaz: el pipeline publica *tipos* de mensaje
(`DictationMessageKind`) y la capa de presentación los traduce.

El pipeline recorre `Idle → Recording → Transcribing → Processing → Pasting → Idle`, con
`Error` alcanzable desde cualquier punto. Todo el I/O, el STT y el HTTP van fuera del hilo
de UI.

**Latencia del atajo.** Inicializar un cliente WASAPI cuesta ~400 ms, así que VoiceFlow abre
e inicializa el dispositivo de captura al arrancar y lo reutiliza (`AudioClient.Reset` tras
cada grabación). Pulsar el atajo solo ejecuta `AudioClient.Start`: medido en **~90 ms** desde
la pulsación hasta que la captura está grabando, con la aplicación en la bandeja.

Tener el dispositivo preparado **no** mantiene el micrófono abierto: Windows solo marca el
micro como «en uso» mientras dura el dictado (comprobado en el registro de
`CapabilityAccessManager`).

---

## Solución de problemas

| Síntoma | Causa habitual |
|---|---|
| «El atajo ya está en uso por otra aplicación» | Otra app tiene registrada esa combinación: cámbiala en Ajustes → Atajo |
| «Texto copiado — no se pudo pegar» | La ventana de destino se cerró o bloquea el pegado: prueba el modo «escribir carácter por carácter» |
| El modelo tarda en estar listo al arrancar | Se carga en segundo plano (2-4 s en CPU); el atajo ya funciona antes |
| La IA falla siempre | Revisa BaseUrl, clave y modelo con «Probar conexión»; mientras tanto se pega el texto crudo |
| Dictados muy cortos que no hacen nada | Por debajo de 400 ms se descartan (configurable) |
| Nada en el historial | Mira `logs\voiceflow-*.log`; un fallo de la base de datos no interrumpe el dictado |

---

## Licencia de los componentes

VoiceFlow usa sherpa-onnx (Apache-2.0), NAudio (MIT), H.NotifyIcon (MIT),
CommunityToolkit.Mvvm (MIT), Serilog (Apache-2.0), Dapper (Apache-2.0) y el modelo
NVIDIA Parakeet TDT 0.6B v3 (CC-BY-4.0). Revisa cada licencia antes de redistribuir.
