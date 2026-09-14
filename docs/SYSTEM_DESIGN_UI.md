# Diseño del Sistema y Experiencia de Usuario (UI/UX) - VoiceFlow

Este documento describe la filosofía visual, el sistema de diseño (*design system*), las especificaciones de pantalla y las pautas de experiencia de usuario para la interfaz moderna de **VoiceFlow** (React + WebView2).

---

## 1. Filosofía de Diseño y Principios de Experiencia (UX)

1. **Claridad para Cualquier Persona**:
   - Eliminar la jerga técnica incomprensible. Cada control que pueda generar dudas (como *Temperatura*, *Tokens* o *Thinking*) debe incluir explicaciones contextuales claras y tranquilizadoras (ej. *"No afecta textos cortos ni gasta saldo adicional"*).
2. **Estética Premium y Modo Oscuro Nativo**:
   - Interfaz con paleta oscura refinada (evitar negros puros `0x000000`, usar tonos carbón y pizarra profundos con bordes sutiles y efectos de elevación).
3. **Micro-interacciones y Feedback Inmediato**:
   - Todo cambio de estado debe comunicarse visualmente: ondas reactivas al volumen de voz, transiciones suaves al deslizar sliders, estados activos evidentes al pulsar botones de preajuste y mensajes de confirmación no intrusivos.
4. **Respeto al Foco del Usuario**:
   - La aplicación es una herramienta de productividad. La ventana de configuración debe ser espaciosa y ordenada, y el widget de dictado debe ser minimalista para no distraer de la tarea principal.

---

## 2. Tokens de Diseño (Design Tokens)

### 2.1. Paleta de Colores

| Token | Valor Hex | Uso Principal |
| :--- | :--- | :--- |
| `--bg-base` | `#0B0F17` | Fondo principal de ventanas |
| `--bg-surface` | `#111827` | Paneles laterales, barras de navegación y tarjetas base |
| `--bg-surface-alt` | `#1F2937` | Inputs, campos de texto, sliders y áreas anidadas |
| `--bg-surface-hover` | `#374151` | Hover en filas, botones secundarios y elementos clickeables |
| `--border-subtle` | `#1F293D` | Bordes suaves de separación |
| `--border-focus` | `#3B82F6` | Borde al enfocar inputs o elementos seleccionados |
| `--text-primary` | `#F9FAFB` | Títulos, textos principales y valores numéricos destacados |
| `--text-secondary` | `#9CA3AF` | Etiquetas, descripciones generales y textos explicativos |
| `--text-muted` | `#6B7280` | Pistas (*placeholders*), atajos y textos de ayuda secundaria |
| `--accent` | `#2563EB` | Botones de acción primaria, sliders activos y acentos |
| `--accent-hover` | `#1D4ED8` | Hover de botones de acción primaria |
| `--accent-glow` | `rgba(37, 99, 235, 0.25)` | Sombra exterior en elementos activos o grabando |
| `--success` | `#10B981` | Conexión exitosa, dictado guardado, checks |
| `--danger` | `#EF4444` | Errores de API, eliminación, micrófono desconectado |

### 2.2. Tipografía y Escalas

- **Fuente principal**: `Segoe UI Variable`, `Inter`, o `-apple-system, system-ui, sans-serif`.
- **Tamaños**:
  - `Title`: `20px` / SemiBold (Títulos de ventana).
  - `Section`: `14px` / SemiBold (Títulos de tarjeta o encabezados de grupo).
  - `Body`: `13px` / Regular (Textos normales, labels de formulario).
  - `Caption / Muted`: `12px` / Regular (Explicaciones pedagógicas, pistas).
  - `Badge / Counter`: `11px` / Bold (Contadores de tokens, etiquetas de versión).

### 2.3. Elevación y Bordes
- **Bordes redondeados (`border-radius`)**:
  - Botones y campos: `6px` a `8px`.
  - Tarjetas (*Cards*): `10px` a `12px`.
  - Píldora del Widget Flotante: `24px` (completamente redondeada).
- **Sombras**:
  - Tarjetas: `0 4px 12px rgba(0, 0, 0, 0.35)`.
  - Widget Flotante: `0 8px 24px rgba(0, 0, 0, 0.5)`.

---

## 3. Especificación Pantalla por Pantalla

### Pantalla 1: Ventana de Configuración (Settings Window)

La ventana principal de configuración utiliza una navegación por pestañas laterales (o superiores) organizada por dominios claros:

#### Pestaña A: Inteligencia Artificial (LLM)
- **Proveedor y API Key**:
  - Selector de proveedor (OpenRouter, OpenAI compatible, Ollama local).
  - Campo de API Key con enmascarado de contraseña (`••••••••`) y botón para abrir la web del proveedor en el navegador.
- **Selector de Modelo**:
  - Campo de texto editable con botón `"Buscar modelos..."` para explorar el catálogo de OpenRouter.
- **Tarjeta de Temperatura**:
  - Encabezado con badge del valor numérico actual (ej. `0.3`).
  - Grupo de botones de preajuste: `Preciso (0.1)`, `Equilibrado (0.3)`, `Creativo (0.7)`.
  - Slider interactivo con saltos suaves de `0.05`.
  - Texto dinámico explicativo que orienta al usuario sobre el resultado que obtendrá.
- **Tarjeta de Tope Máximo de Longitud del Texto**:
  - Título tranquilizador: *"Tope máximo de longitud del texto"*.
  - Badge numérico con estimación en palabras: `1024 tokens (≈ hasta 768 palabras)`.
  - Preajustes rápidos: `Hasta 512`, `Hasta 1024`, `Hasta 2048`, `Hasta 4096`.
  - Slider de 256 a 4096.
  - Mensaje tranquilizador: *"No afecta textos cortos (la IA responderá solo lo que hables, sin alargar nada). Ojo: no te preocupes por el saldo, solo se cobran los tokens que el modelo realmente genera."*
- **Tarjeta de Razonamiento del Modelo (Thinking)**:
  - Interruptor Toggle para activar/desactivar pensamiento previo (Chain of Thought).
  - Selector de nivel de esfuerzo: `Bajo (Rápido)`, `Medio`, `Alto (Profundo)`.
  - Aclaración de que al desactivarlo, el dictado responde al instante sin esperas internas.
- **Prueba de Conexión**:
  - Botón `"Probar conexión"` que valida la API Key y muestra saldo disponible y modelo en verde.

#### Pestaña B: Atajos de Teclado
- **Capturador de Teclas en Vivo**:
  - Botón interactivo con estado de escucha: al hacer clic cambia a *"Presiona tus teclas..."*.
  - Muestra en tiempo real las teclas que el usuario mantiene presionadas (ej. `[Ctrl + Alt]`).
  - Admite modificadores puros o teclas compuestas (`Ctrl + Alt + Space`, `F8`, `Alt + Shift`, etc.).
- **Modo de Activación**:
  - Selector entre modo **Toggle** (un toque para empezar a grabar, otro toque para terminar) y **Hold** (mantener presionado mientras hablas).

#### Pestaña C: Audio y Dispositivos
- Selector desplegable del micrófono de entrada.
- Barra medidora de volumen en tiempo real (vúmetro) para que el usuario verifique que su micrófono funciona antes de dictar.

#### Pestaña D: Transcripción (STT Offline)
- Selector de modelo local (Parakeet TDT int8 recomendado).
- Indicador de estado del modelo (Descargado / Listo / En memoria).
- Selector de hilos de CPU para la inferencia.

#### Pestaña E: Historial
- Tabla o lista de dictados recientes con fecha, duración, texto transcrito y botones de acción (Copiar, Borrar).

---

### Pantalla 2: Widget Flotante de Dictado (Floating Pill)

Un widget minimalista y no intrusivo que aparece flotando sobre la pantalla activa (`TopMost`) cuando el usuario presiona el atajo de voz:

```text
┌─────────────────────────────────────────────────────────────┐
│  🎙️  |||||||||||||||||||||||   00:04   [ Detener (Ctrl+Alt) ] │
└─────────────────────────────────────────────────────────────┘
```

#### Estados del Widget:
1. **Grabando (`Recording`)**:
   - Icono de micrófono brillando en azul/rojo.
   - Visualizador de ondas de audio reactivo en tiempo real al volumen del micrófono.
   - Contador de tiempo transcurrido (`00:03`).
2. **Transcribiendo (`Transcribing`)**:
   - Indicador de progreso pulsante con texto: *"Transcribiendo con Parakeet..."*.
3. **Refinando con IA (`Refining`)**:
   - Brillo sutil degradado con texto: *"Refinando texto..."*.
4. **Completado (`Done`)**:
   - Checkmark verde sutil, pegado del texto en la ventana del usuario y desvanecimiento suave (*fade-out* en 400ms).

---

### Pantalla 3: Menú de la Bandeja del Sistema (System Tray)

El icono de VoiceFlow en la bandeja de notificación de Windows ofrece un menú contextual accesible con clic derecho:
- **Abrir Ajustes** (abre la ventana principal).
- **Ver Historial** (acceso directo a transcripciones pasadas).
- **Pausar Atajo** (desactiva temporalmente el hook global sin cerrar la app).
- **Separador**.
- **Salir de VoiceFlow** (cierra la aplicación de forma limpia).
