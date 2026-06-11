# Plan de Implementación
## Solución de Puntuación de Exámenes Físicos (SPEF)
### ASP.NET MVC · .NET Framework 4.8 · Google OAuth/Drive · Gemini Flash

---

## 1. Resumen Ejecutivo

Se desarrollará una aplicación web en **ASP.NET MVC sobre .NET Framework 4.8** que automatiza la corrección de exámenes escritos a mano. El flujo real del docente es el siguiente:

1. El docente posee el **examen con respuestas correctas en PDF** (100% legible, es el documento base que usa para imprimir).
2. Imprime ese mismo examen **sin las respuestas** y lo reparte a los estudiantes.
3. Los estudiantes resuelven el examen **a mano** y lo entregan.
4. El docente **escanea cada examen individualmente**: 1 PDF = 1 estudiante.
5. Sube al sistema: (a) el examen base con respuestas, una sola vez por "apartado de examen", y (b) los PDFs escaneados de los estudiantes correspondientes a ese examen base.
6. El sistema compara, califica y entrega la **nota de forma inmediata**, con **feedback explicativo** en los casos donde la IA no tiene certeza total (caligrafía ambigua, paráfrasis, respuestas parciales).

**Restricciones y reglas de negocio confirmadas:**

| Regla | Detalle |
|---|---|
| Tamaño máximo de examen | **10 hojas** (aplica tanto al examen base como al del estudiante, son estructuralmente idénticos) |
| Formato de entrada | PDF en ambos casos (clave y entregas) |
| Relación PDF–alumno | 1 PDF = 1 alumno (el docente escanea examen por examen) |
| Examen base | Siempre 100% legible (documento digital de origen) |
| Examen del alumno | Enunciado impreso legible; **respuestas manuscritas** con caligrafía variable |
| Apartados de examen | Un docente maneja múltiples exámenes base; cada lote de entregas se asocia a su examen base correspondiente |
| Motor de IA | **Gemini Flash** (API REST, visión multimodal) |

**Principio de diseño central:** ningún motor de reconocimiento de manuscrito es infalible. La robustez del sistema no proviene de un OCR perfecto, sino de la arquitectura: *evaluación con niveles de confianza + comparación semántica (no literal) + feedback explicativo + bandeja de revisión humana solo para casos dudosos*. El docente interviene por excepción (típicamente 5–10% de las respuestas), no revisa todo.

---

## 2. Alcance

### 2.1 Incluido (MVP)

- Login con Google OAuth 2.0 y permiso de Google Drive del usuario.
- Gestión de **Exámenes Base** (apartados): subir PDF con respuestas, calibración de la clave, activación.
- Subida de entregas de alumnos (individual o en lote: N archivos PDF = N alumnos).
- Pipeline asíncrono de corrección: rasterización → evaluación con Gemini Flash → consolidación de nota.
- **Sistema de feedback por respuesta**: transcripción, veredicto, justificación y nivel de confianza.
- Bandeja de revisión para respuestas de baja confianza (decisión del docente en un clic).
- Publicación de notas y exportación de reportes (Excel/PDF).
- Almacenamiento de todos los archivos en el Google Drive del docente.

### 2.2 Excluido (fuera del MVP)

- Separación automática de múltiples alumnos dentro de un mismo PDF (regla: 1 PDF = 1 alumno).
- Entrenamiento de modelos propios de HTR/OCR.
- Portal para estudiantes (el sistema es de uso exclusivo del docente en el MVP; puede ser trabajo futuro).
- Exámenes con contenido no textual complejo como respuesta (dibujos, planos, partituras). Fórmulas matemáticas simples sí están dentro del alcance.

---

## 3. Stack Tecnológico

| Capa | Tecnología | Notas |
|---|---|---|
| Web | ASP.NET MVC 5, C#, .NET Framework 4.8, IIS | Requisito del proyecto |
| Autenticación | Google OAuth 2.0 vía OWIN (`Microsoft.Owin.Security.Google`) | Scopes: `openid email profile` + `https://www.googleapis.com/auth/drive.file` |
| Almacenamiento de archivos | Google Drive API v3 (`Google.Apis.Drive.v3`) | Compatible con .NET 4.8. Scope `drive.file` (solo archivos creados por la app) → verificación de Google más simple |
| Base de datos | SQL Server + Entity Framework 6 | Persistencia de metadatos, claves, resultados |
| Rasterización PDF | PDFium (PdfiumViewer) o Ghostscript | PDF → PNG **300 DPI** (calidad crítica para lectura de manuscrito) |
| Extracción de texto de la clave | PdfPig o iText 7 | Texto nativo del PDF base (digital) — precisión total, sin OCR |
| IA de evaluación | **Gemini Flash** vía API REST | Visión multimodal: transcribe y evalúa en un solo paso. Consumo con `HttpClient` (POST JSON + imagen base64), sin SDK necesario |
| Jobs en background | Hangfire | Compatible con .NET 4.8. El procesamiento de un lote no puede vivir en un request HTTP |
| Cifrado de tokens | DPAPI / `MachineKey` | El refresh token de Google se almacena cifrado |

---

## 4. Arquitectura de Componentes

```
[Navegador del docente]
        │
        ▼
[ASP.NET MVC 5 + OWIN (Google OAuth)]
        │
        ├── Capa de Servicios
        │     ├── DriveStorageService      → Google Drive API (carpetas, subida, descarga)
        │     ├── PdfTextService           → extrae texto nativo del examen base (PdfPig/iText)
        │     ├── PdfRasterService         → PDF → PNG 300 DPI (máx. 10 páginas)
        │     ├── ExamTemplateService      → estructura de la clave: preguntas, respuestas, puntajes
        │     ├── GeminiGradingService     → llamadas REST a Gemini Flash (transcripción + evaluación + feedback)
        │     └── ScoringService           → consolidación, umbrales de confianza, nota final
        │
        ├── Hangfire
        │     ├── ProcesarEntregaJob       → pipeline completo de 1 examen de alumno
        │     └── ProcesarLoteJob          → encola N entregas
        │
        └── EF6 → SQL Server
```

**Estructura de carpetas en Drive (autogenerada):**

```
/CorreccionExamenes/
   └── {Curso}/
        └── {ExamenBase}/
             ├── clave/        ← PDF con respuestas
             └── entregas/     ← 1 PDF por alumno
```

---

## 5. Modelo de Datos (núcleo)

| Entidad | Campos principales |
|---|---|
| `Usuarios` | GoogleId, email, nombre, refresh_token (cifrado) |
| `Cursos` | nombre, docente (FK Usuario) |
| `ExamenesBase` | curso (FK), título, DriveFileId de la clave, total de páginas (≤10), estado: *Borrador → Calibrado → Activo* |
| `PreguntasClave` | examen base (FK), número, enunciado, respuesta correcta, tipo (*opción múltiple / respuesta corta / desarrollo*), puntaje, página, criterios de puntaje parcial (opcional) |
| `EntregasAlumno` | examen base (FK), nombre/código del alumno, DriveFileId, estado: *En cola → Procesando → Calificado / Requiere revisión / Error*, nota final, fecha |
| `RespuestasEvaluadas` | entrega (FK), pregunta (FK), transcripción, veredicto (*correcta / parcial / incorrecta / ilegible*), puntaje sugerido, puntaje final, confianza_lectura (0–1), confianza_evaluación (0–1), **feedback** (texto explicativo), requiere_revisión (bool), revisado_por, fecha_revisión |
| `LogProcesamiento` | entrega (FK), etapa, resultado, duración, error si aplica |

> Nota de diseño: la identificación del alumno puede capturarse de dos formas: (a) el docente la escribe al subir el PDF, o (b) Gemini transcribe el nombre manuscrito de la cabecera del examen y el docente solo lo confirma. Implementar (a) en el MVP y (b) como mejora.

---

## 6. Pipeline de Corrección (detalle técnico)

### 6.1 Calibración del examen base (una vez por apartado)

1. Docente sube el PDF con respuestas → se guarda en Drive (`/clave/`).
2. `PdfTextService` extrae el texto nativo (el PDF es digital, precisión total).
3. Una llamada a Gemini Flash con el texto completo estructura la clave: lista de preguntas con `{numero, enunciado, respuesta_correcta, puntaje_sugerido, tipo, pagina}` en JSON.
4. **Vista de confirmación**: el docente revisa la lista extraída, corrige enunciados/respuestas/puntajes si hace falta, define el puntaje real de cada pregunta y los criterios de puntaje parcial si los hay.
5. El examen pasa a estado **Activo** y queda listo para recibir entregas.

> Esta vista de confirmación es la garantía de calidad de la mitad del problema: la clave queda validada por el humano antes de calificar a nadie.

### 6.2 Procesamiento de cada entrega (job Hangfire)

1. **Validación de entrada**: PDF ≤ 10 páginas, resolución suficiente (advertir si el escaneo es < 200 DPI efectivos).
2. **Rasterización**: cada página → PNG 300 DPI.
3. **Evaluación con Gemini Flash**, página por página (o varias páginas por llamada). El prompt incluye:
   - Las preguntas de esa página con su enunciado, respuesta correcta, puntaje y criterios (texto ya validado de la clave).
   - La imagen de la página del alumno.
   - Instrucción de responder **únicamente JSON estricto**.
4. **Consolidación** (`ScoringService`): suma de puntajes, aplicación de umbrales de confianza, marcado de respuestas para revisión.
5. **Resultado**: nota preliminar inmediata + detalle por pregunta con feedback.

### 6.3 Contrato JSON de salida de Gemini (por pregunta)

```json
{
  "numero": 3,
  "transcripcion": "La fotosintesis es el proseso por el cual las plantas convierten luz en energia quimica",
  "veredicto": "correcta",
  "puntaje_sugerido": 2.0,
  "puntaje_maximo": 2.0,
  "confianza_lectura": 0.93,
  "confianza_evaluacion": 0.97,
  "feedback": "La respuesta es conceptualmente correcta aunque está parafraseada respecto a la clave y contiene errores ortográficos ('proseso'), que no afectan el contenido. Coincide en los elementos esenciales: conversión de luz en energía química por las plantas."
}
```

### 6.4 Manejo de paráfrasis y respuestas no literales — el sistema de feedback

Este es el corazón de la flexibilidad solicitada. La comparación **nunca es literal** (no se usa matching de strings ni distancia de edición como criterio principal): Gemini evalúa **equivalencia semántica** entre lo que escribió el alumno y la respuesta correcta, usando el enunciado como contexto. Las instrucciones del prompt del evaluador cubren explícitamente estos casos:

| Caso | Comportamiento del sistema |
|---|---|
| **Paráfrasis** ("las plantas transforman la luz solar en alimento" vs. la clave textual) | Veredicto `correcta` si los conceptos esenciales coinciden. El feedback explica qué elementos de la clave están presentes |
| **Equivalencias formales** ("2π" vs "6.28", "H₂O" vs "agua") | Correcta, con nota en el feedback de la forma equivalente usada |
| **Errores ortográficos** que no alteran el significado | No penalizan (configurable por el docente por examen) |
| **Respuesta parcial** (menciona 2 de 3 elementos pedidos) | Veredicto `parcial`, puntaje proporcional según criterios de la clave, feedback indicando qué faltó |
| **Caligrafía ambigua** (confianza_lectura baja) | Veredicto provisional + `requiere_revision = true`. El feedback describe la ambigüedad: *"La palabra clave podría leerse como 'mitosis' o 'meiosis'; el resto de la respuesta sugiere mitosis"* |
| **Equivalencia dudosa** (confianza_evaluacion baja: la paráfrasis es lejana o incompleta) | `requiere_revision = true` con feedback argumentando ambas lecturas posibles, para que el docente decida con contexto |
| **Ilegible o en blanco** | Veredicto `ilegible`/`incorrecta` según el caso, siempre a revisión si es ilegible |

**Reglas de decisión del `ScoringService` (umbrales configurables):**

- `confianza_lectura ≥ 0.85` **y** `confianza_evaluacion ≥ 0.85` → puntaje automático.
- Cualquiera de las dos por debajo del umbral → **bandeja de revisión**: el docente ve la imagen de la respuesta, la transcripción, el feedback de la IA y aprueba/corrige el puntaje en un clic.
- Preguntas de tipo *desarrollo largo* → siempre pasan por revisión en el MVP (auto-calificación solo para opción múltiple y respuesta corta al inicio; se relaja según resultados del piloto).

El **feedback se conserva siempre**, incluso en respuestas auto-calificadas: es el registro de *por qué* el sistema asignó ese puntaje, sirve como justificación ante reclamos de estudiantes y es evidencia documental para la validación del sistema.

### 6.5 Esqueleto del prompt del evaluador

```
Eres un asistente de corrección de exámenes universitarios. Recibes la imagen
de una página de examen resuelta a mano por un estudiante y la clave de
respuestas de esa página.

CLAVE DE LA PÁGINA:
[Pregunta 3 | Puntaje máx: 2.0 | Tipo: respuesta corta]
Enunciado: ¿Qué es la fotosíntesis?
Respuesta correcta: Proceso mediante el cual las plantas convierten la energía
lumínica en energía química.
Criterios de puntaje parcial: 1.0 si menciona conversión de energía sin
especificar tipos.

INSTRUCCIONES:
1. Localiza cada pregunta en la imagen y transcribe fielmente la respuesta
   manuscrita del estudiante, sin corregirla.
2. Evalúa EQUIVALENCIA SEMÁNTICA con la respuesta correcta. Acepta paráfrasis,
   sinónimos y formas equivalentes. Los errores ortográficos no penalizan si
   no alteran el significado.
3. Asigna puntaje según los criterios. Usa "parcial" cuando corresponda.
4. Reporta confianza_lectura (qué tan seguro estás de haber leído bien la
   caligrafía) y confianza_evaluacion (qué tan seguro estás del veredicto)
   como valores entre 0 y 1. Sé conservador: ante ambigüedad real, reporta
   confianza baja y explica la ambigüedad en el feedback.
5. El feedback debe justificar el puntaje en 1-3 oraciones, en español,
   dirigido al docente.
6. Responde ÚNICAMENTE con el arreglo JSON, sin texto adicional ni markdown.
```

---

## 7. Fases de Implementación (≈ 14 semanas)

### Fase 0 — Setup (Semana 1)
- Proyecto ASP.NET MVC 5 (.NET 4.8), repositorio Git, estructura de capas.
- Google Cloud Console: proyecto, pantalla de consentimiento OAuth, credenciales, habilitar Drive API; cuenta y API key de Gemini.
- SQL Server + EF6 (migraciones), instalación de Hangfire.
- **Entregable:** proyecto base desplegable con CI básico.

### Fase 1 — Autenticación y Drive (Semanas 2–3)
- Login con Google OAuth (OWIN), captura y cifrado del refresh token.
- `DriveStorageService`: creación de estructura de carpetas, subida/descarga, manejo de renovación de tokens.
- CRUD de Cursos y Exámenes Base (apartados).
- **Entregable:** el docente inicia sesión y sube PDFs que quedan organizados en su Drive desde la app.

### Fase 2 — Calibración del examen base (Semanas 4–5)
- Extracción de texto nativo del PDF clave (PdfPig/iText).
- Estructuración de la clave con Gemini (preguntas/respuestas/puntajes en JSON).
- **Vista de confirmación/edición de la clave** (la pieza de UX más importante de esta fase).
- Estados del examen: Borrador → Calibrado → Activo.
- **Entregable:** examen base calibrado y validado por el docente, listo para recibir entregas.

### Fase 3 — Pipeline de corrección (Semanas 6–8) ⚠️ *Fase crítica*
- Subida de entregas (individual y lote; validación ≤ 10 páginas y calidad de escaneo).
- `PdfRasterService` (300 DPI) + `GeminiGradingService` (REST, reintentos, rate limiting) + `ScoringService` (umbrales, consolidación).
- Jobs Hangfire con estados visibles en UI y log por etapa.
- Preprocesamiento básico de imagen: enderezado (deskew) si la rotación es notoria, ajuste de contraste.
- **Entregable:** entra un PDF escaneado → sale nota preliminar con feedback por pregunta.

### Fase 4 — Bandeja de revisión, feedback y publicación (Semanas 9–10)
- Bandeja de revisión: imagen de la respuesta + transcripción + feedback de la IA lado a lado; el docente aprueba o corrige puntaje en un clic.
- Vista de detalle de cada entrega: nota, desglose por pregunta, feedback completo.
- Publicación de notas, exportación a Excel y PDF.
- Reporte analítico por pregunta (qué preguntas falló más el grupo).
- **Entregable:** ciclo completo cerrado de corrección con supervisión por excepción.

### Fase 5 — Endurecimiento (Semanas 11–12)
- Manejo de escaneos deficientes (oscuros, torcidos, baja resolución): validaciones, advertencias al docente, guía de escaneo.
- Telemetría de precisión: % de respuestas auto-calificadas vs. enviadas a revisión, distribución de confianzas, costo por examen.
- Pruebas con exámenes reales de distintos cursos y caligrafías.
- Seguridad: revisión de cifrado de tokens, autorización por recurso, manejo de errores de API.
- **Entregable:** sistema estable bajo condiciones reales adversas.

### Fase 6 — Piloto y validación (Semanas 13–14)
- Corrida en paralelo: el sistema califica un lote real y el docente califica el mismo lote a mano.
- Medición de **concordancia** (precisión del sistema vs. corrección humana), % de intervención manual, tiempo ahorrado, costo por examen.
- Ajuste fino de umbrales de confianza con datos reales.
- **Entregable:** métricas cuantitativas de validación (insumo directo para la documentación/sustentación del proyecto).

---

## 8. Riesgos y Mitigaciones

| # | Riesgo | Impacto | Mitigación |
|---|---|---|---|
| 1 | **Calidad del escaneo** (el riesgo #1, mayor que la caligrafía: a baja resolución todo motor falla) | Alto | Validar resolución al subir, advertencias inmediatas, guía de escaneo para el docente, preprocesamiento (deskew, contraste) |
| 2 | Caligrafía extremadamente difícil | Medio | Diseñado en la arquitectura: confianza baja → revisión humana con feedback explicativo. El sistema nunca "adivina en silencio" |
| 3 | Respuestas de desarrollo largo | Medio | Siempre a revisión en el MVP; el feedback de la IA acelera la decisión del docente. Auto-calificación gradual según resultados del piloto |
| 4 | Verificación OAuth de Google | Medio | Scope `drive.file` (restringido) en lugar del scope completo de Drive → proceso de verificación leve, sin auditoría costosa |
| 5 | Cambios/límites de la API de Gemini | Medio | Capa `GeminiGradingService` aislada tras interfaz → el motor es intercambiable. Reintentos con backoff y rate limiting |
| 6 | Costos de API | Bajo | Gemini Flash cuesta centavos por examen de 10 páginas. Registrar costo por examen como métrica del piloto |
| 7 | Examen base mal estructurado (numeración irregular, formato atípico) | Medio | La vista de confirmación de la Fase 2 obliga validación humana de la clave antes de activar el examen |
| 8 | Alucinación del modelo (transcribir algo que no está) | Medio | Prompt conservador (instrucción explícita de reportar baja confianza ante ambigüedad), umbrales estrictos al inicio, feedback siempre auditable junto a la imagen original |

---

## 9. Métricas de Éxito (para el piloto)

- **Concordancia** con la corrección manual del docente (objetivo: ≥ 90% en respuesta corta y opción múltiple).
- **% de auto-calificación** (objetivo: ≥ 85% de respuestas sin intervención manual).
- **Tiempo de corrección por examen** vs. corrección manual (objetivo: reducción ≥ 90% del tiempo del docente).
- **Costo por examen** (objetivo documentado: < USD 0.05 por examen de 10 páginas con Gemini Flash).
- **Tasa de reclamos sostenidos**: respuestas auto-calificadas que el docente revierte tras reclamo del estudiante (objetivo: < 2%).

---

## 10. Decisiones de Diseño Deliberadas (qué NO se hará y por qué)

1. **No entrenar un modelo propio de HTR/OCR**: meses de trabajo, dataset enorme requerido y resultado inferior a los modelos multimodales actuales.
2. **No comparar respuestas por matching de strings ni distancia Levenshtein** como criterio principal: se rompe con sinónimos, paráfrasis y errores ortográficos. La equivalencia semántica vía LLM es el mecanismo central.
3. **No usar transcripción y evaluación en dos pasos separados**: Gemini transcribe y evalúa en una sola llamada viendo la respuesta en contexto del enunciado, lo que mejora la lectura de caligrafía ambigua.
4. **No prometer "corrección 100% automática sin intervención"**: la promesa correcta y defendible es *"reducción superior al 90% del tiempo de corrección, con supervisión por excepción y feedback auditable en cada respuesta"*. Es medible, honesta y técnicamente sostenible.
5. **No aceptar PDFs con múltiples alumnos**: la regla 1 PDF = 1 alumno (máx. 10 hojas) elimina el problema complejo de segmentar exámenes dentro de un archivo y simplifica todo el pipeline.

---

*Fin del plan de implementación.*
