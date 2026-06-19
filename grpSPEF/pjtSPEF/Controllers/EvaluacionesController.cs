using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using pjtSPEF.Data;
using pjtSPEF.Models.Entities;
using pjtSPEF.Models.ViewModels;
using pjtSPEF.Services;

namespace pjtSPEF.Controllers
{
    // Entregas de los estudiantes para un examen base ya calibrado: subir los PDFs,
    // calificarlos con IA contra la clave y revisar el detalle por pregunta.
    public class EvaluacionesController : Controller
    {
        private readonly GoogleCurrentUserService _currentUser;
        private readonly SpefSheetStore _store;
        private readonly DriveStorageService _storage;
        private readonly PdfPigValidationService _pdfValidator;
        private readonly PdfSelloService _sello;

        public EvaluacionesController()
        {
            _currentUser = new GoogleCurrentUserService();
            _store = new SpefSheetStore(_currentUser);
            _storage = new DriveStorageService(_currentUser);
            _pdfValidator = new PdfPigValidationService();
            _sello = new PdfSelloService();
        }

        // Cada entrega se califica con el proveedor que el usuario eligió (botón Gemini/Claude).
        private static ICalificadorService CrearCalificador(ProveedorIa proveedor)
        {
            switch (proveedor)
            {
                case ProveedorIa.Claude:
                    return new ClaudeCalificadorService();
                default:
                    return new GeminiCalificadorService();
            }
        }

        // Lista de entregas de un examen base.
        public ActionResult Index(int examenBaseId)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var examen = CargarExamen(examenBaseId);
            if (examen == null)
                return HttpNotFound();

            examen.Evaluaciones = _store.EvaluacionesDeExamen(examenBaseId);
            return View(examen);
        }

        [HttpGet]
        public ActionResult Subir(int examenBaseId)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var examen = CargarExamen(examenBaseId);
            if (examen == null)
                return HttpNotFound();

            ViewBag.Examen = examen;
            return View(new SubirEvaluacionesViewModel { ExamenBaseId = examen.Id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Subir(SubirEvaluacionesViewModel model)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var examen = CargarExamen(model.ExamenBaseId);
            if (examen == null)
                return HttpNotFound();

            var archivos = (model.Archivos ?? Enumerable.Empty<System.Web.HttpPostedFileBase>())
                .Where(a => a != null && a.ContentLength > 0)
                .ToList();

            if (archivos.Count == 0)
                ModelState.AddModelError("Archivos", "Sube al menos un PDF.");

            if (!ModelState.IsValid)
            {
                ViewBag.Examen = examen;
                return View(model);
            }

            int subidos = 0, conError = 0;
            foreach (var archivo in archivos)
            {
                var validacion = _pdfValidator.Validar(archivo.InputStream);
                if (!validacion.EsValido)
                {
                    conError++;
                    continue;
                }

                string archivoRef;
                try
                {
                    archivoRef = _storage.Guardar(archivo.InputStream, RutaStorage.Entregas(examen), ".pdf");
                }
                catch (DriveNoAutorizadoException ex)
                {
                    // Sin Drive no se puede guardar nada: cortar y mostrar el motivo.
                    ModelState.AddModelError("", ex.Message);
                    ViewBag.Examen = examen;
                    return View(model);
                }

                try
                {
                    _store.AgregarEvaluacion(new EvaluacionEstudiante
                    {
                        ExamenBaseId = examen.Id,
                        NombreEstudiante = Path.GetFileNameWithoutExtension(archivo.FileName),
                        ArchivoRef = archivoRef,
                        ArchivoNombreOriginal = Path.GetFileName(archivo.FileName),
                        TotalPaginas = validacion.TotalPaginas,
                        Estado = EstadoEvaluacion.Pendiente,
                        FechaCreacion = DateTime.UtcNow
                    });
                }
                catch
                {
                    // Compensación: no dejar el archivo huérfano si no se registró.
                    _storage.Eliminar(archivoRef);
                    throw;
                }

                subidos++;
            }

            TempData["Exito"] = ConstruirResumen(subidos, conError);
            return RedirectToAction("Index", new { examenBaseId = examen.Id });
        }

        public ActionResult Details(int id)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var evaluacion = CargarEvaluacion(id);
            if (evaluacion == null)
                return HttpNotFound();

            return View(evaluacion);
        }

        // (Re)califica una entrega ya subida.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Calificar(int id, string proveedor)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var evaluacion = CargarEvaluacion(id);
            if (evaluacion == null)
                return HttpNotFound();

            var examen = evaluacion.ExamenBase;
            if (examen.PreguntasClave == null || !examen.PreguntasClave.Any())
            {
                TempData["Error"] = "El examen base no tiene clave calibrada. Calíbrala antes de calificar.";
                return RedirectToAction("Details", new { id });
            }

            var ia = ProveedorIaExtensions.Parsear(proveedor);
            var calificada = await CalificarEvaluacion(evaluacion, examen, ia);

            // Una (re)calificación de la IA deja la entrega pendiente de revisión del docente:
            // el PDF permanece (o vuelve) en entregas/ hasta que se pulse GUARDAR. Recién ahí
            // se archiva en calificados/ (ver GuardarCalificacion).
            MoverEntrega(evaluacion, RutaStorage.Entregas(examen));

            if (calificada)
            {
                if (examen.Estado == EstadoExamen.Calibrado)
                {
                    examen.Estado = EstadoExamen.Activo;
                    examen.FechaModificacion = DateTime.UtcNow;
                    _store.ActualizarExamen(examen);
                }
                TempData["Exito"] = "Entrega calificada con " + ia.Nombre() + ". Revisa el detalle y pulsa Guardar para archivarla.";
            }
            else
            {
                TempData["Error"] = evaluacion.MensajeError ?? "No se pudo calificar la entrega.";
            }

            return RedirectToAction("Details", new { id });
        }

        // (Re)califica UNA entrega y devuelve JSON. Lo usa el bucle de calificación en lote
        // de la vista Index (un request por entrega, con barra de progreso en el navegador),
        // para no meter decenas de llamadas a la IA en un solo request y evitar timeouts.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> CalificarUna(int id, string proveedor)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return Json(new { ok = false, error = "Sesión expirada. Vuelve a iniciar sesión." });

            var evaluacion = CargarEvaluacion(id);
            if (evaluacion == null)
                return Json(new { ok = false, error = "Entrega no encontrada." });

            var examen = evaluacion.ExamenBase;
            if (examen.PreguntasClave == null || !examen.PreguntasClave.Any())
                return Json(new { ok = false, error = "El examen base no tiene clave calibrada." });

            var ia = ProveedorIaExtensions.Parsear(proveedor);
            bool calificada;
            try
            {
                calificada = await CalificarEvaluacion(evaluacion, examen, ia);
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, id, error = ex.Message });
            }

            // Igual que en Calificar: la entrega queda (o vuelve) en entregas/ pendiente de
            // revisión; recién al pulsar Guardar se archiva en calificados/.
            MoverEntrega(evaluacion, RutaStorage.Entregas(examen));

            if (calificada && examen.Estado == EstadoExamen.Calibrado)
            {
                examen.Estado = EstadoExamen.Activo;
                examen.FechaModificacion = DateTime.UtcNow;
                _store.ActualizarExamen(examen);
            }

            return Json(new
            {
                ok = calificada,
                id,
                estado = evaluacion.Estado.ToString(),
                nota = evaluacion.NotaTotal,
                notaMaxima = examen.NotaMaxima,
                nombre = evaluacion.NombreEstudiante,
                error = calificada ? null : (evaluacion.MensajeError ?? "No se pudo calificar la entrega.")
            });
        }

        // Corrige el nombre del estudiante (el que detectó la IA puede venir mal leído).
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EditarNombre(int id, string nombreEstudiante)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var evaluacion = _store.Evaluacion(id);
            if (evaluacion == null)
                return HttpNotFound();

            nombreEstudiante = (nombreEstudiante ?? string.Empty).Trim();
            if (nombreEstudiante.Length == 0)
            {
                TempData["Error"] = "El nombre no puede quedar vacío.";
                return RedirectToAction("Details", new { id });
            }

            evaluacion.NombreEstudiante = nombreEstudiante;
            _store.ActualizarEvaluacion(evaluacion);
            TempData["Exito"] = "Nombre actualizado.";
            return RedirectToAction("Details", new { id });
        }

        // Guarda en bloque las correcciones del docente (transcripción + puntaje de cada pregunta)
        // y recién aquí archiva la entrega: el PDF pasa de entregas/ a calificados/ en Drive.
        // El puntaje de cada pregunta se valida en servidor contra su PuntajeMaximo (clave calibrada).
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult GuardarCalificacion(GuardarCalificacionViewModel model)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var evaluacion = CargarEvaluacion(model.EvaluacionId);
            if (evaluacion == null)
                return HttpNotFound();

            var respuestas = _store.RespuestasDeEvaluacion(model.EvaluacionId);
            if (respuestas.Count == 0)
            {
                TempData["Error"] = "No hay detalle que guardar. Califica la entrega primero.";
                return RedirectToAction("Details", new { id = model.EvaluacionId });
            }

            var porId = respuestas.ToDictionary(r => r.Id);
            var ediciones = model.Respuestas ?? new List<RespuestaEditViewModel>();

            // 1) Validar todos los puntajes antes de tocar nada (no se guarda parcialmente).
            foreach (var edit in ediciones)
            {
                if (!porId.TryGetValue(edit.Id, out var actual))
                    continue;
                if (edit.PuntajeObtenido < 0 || edit.PuntajeObtenido > actual.PuntajeMaximo)
                {
                    TempData["Error"] = string.Format(
                        "El puntaje de la pregunta {0} debe estar entre 0 y {1} (lo permitido por la clave del examen).",
                        actual.Numero, actual.PuntajeMaximo.ToString("0.##"));
                    return RedirectToAction("Details", new { id = model.EvaluacionId });
                }
            }

            // 2) Aplicar las ediciones sobre el detalle existente.
            foreach (var edit in ediciones)
            {
                if (!porId.TryGetValue(edit.Id, out var actual))
                    continue;
                actual.RespuestaTexto = (edit.RespuestaTexto ?? string.Empty).Trim();
                actual.PuntajeObtenido = edit.PuntajeObtenido;
            }

            // 3) Persistir el detalle y recalcular la nota total. Al guardar, el docente
            // confirma la calificación: la entrega pasa a Revisada.
            _store.ReemplazarRespuestas(model.EvaluacionId, respuestas);
            evaluacion.NotaTotal = respuestas.Sum(r => r.PuntajeObtenido);
            evaluacion.Estado = EstadoEvaluacion.Revisada;
            _store.ActualizarEvaluacion(evaluacion);

            // 4) Sellar la nota + los ✔/✗ sobre el PDF y archivarlo: entregas/ -> calificados/.
            EstamparNota(evaluacion, respuestas);
            MoverEntrega(evaluacion, RutaStorage.Calificados(evaluacion.ExamenBase));

            TempData["Exito"] = "Calificación guardada y archivada en Drive.";
            return RedirectToAction("Details", new { id = model.EvaluacionId });
        }

        // Abre en Drive la carpeta del examen (contiene el PDF base + entregas/ + calificados/).
        // Botón "Abrir en Drive" de la lista de entregas.
        public ActionResult AbrirCarpetaDrive(int examenBaseId)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var examen = CargarExamen(examenBaseId);
            if (examen == null)
                return HttpNotFound();

            try
            {
                var url = _storage.EnlaceCarpeta(RutaStorage.ExamenBase(examen.TipoEvaluacion, examen.Titulo));
                if (string.IsNullOrEmpty(url))
                {
                    TempData["Error"] = "No se pudo abrir la carpeta en Drive.";
                    return RedirectToAction("Index", new { examenBaseId });
                }
                return Redirect(url);
            }
            catch (DriveNoAutorizadoException ex)
            {
                TempData["Error"] = ex.Message;
                return RedirectToAction("Index", new { examenBaseId });
            }
        }

        // Abre en Drive el PDF de una entrega concreta. Botón "Abrir en Drive" del detalle.
        public ActionResult AbrirEnDrive(int id)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var evaluacion = _store.Evaluacion(id);
            if (evaluacion == null || string.IsNullOrEmpty(evaluacion.ArchivoRef))
                return HttpNotFound();

            try
            {
                var url = _storage.EnlaceArchivo(evaluacion.ArchivoRef);
                if (string.IsNullOrEmpty(url))
                {
                    TempData["Error"] = "No se pudo abrir el PDF en Drive.";
                    return RedirectToAction("Details", new { id });
                }
                return Redirect(url);
            }
            catch (DriveNoAutorizadoException ex)
            {
                TempData["Error"] = ex.Message;
                return RedirectToAction("Details", new { id });
            }
        }

        public ActionResult Descargar(int id)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var evaluacion = _store.Evaluacion(id);
            if (evaluacion == null || string.IsNullOrEmpty(evaluacion.ArchivoRef))
                return HttpNotFound();

            var nombreDescarga = string.IsNullOrEmpty(evaluacion.ArchivoNombreOriginal)
                ? "entrega.pdf"
                : evaluacion.ArchivoNombreOriginal;
            return File(_storage.Abrir(evaluacion.ArchivoRef), "application/pdf", nombreDescarga);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var evaluacion = _store.Evaluacion(id);
            if (evaluacion == null)
                return HttpNotFound();

            var examenBaseId = evaluacion.ExamenBaseId;
            var archivos = _store.EliminarEvaluacion(id);
            foreach (var archivo in archivos)
                _storage.Eliminar(archivo);

            return RedirectToAction("Index", new { examenBaseId });
        }

        // Llama a la IA con el PDF del alumno y la clave, y persiste el resultado en la entrega.
        // Devuelve true si quedó calificada; en caso de fallo deja Estado = Error con el motivo.
        private async Task<bool> CalificarEvaluacion(EvaluacionEstudiante evaluacion, ExamenBase examen, ProveedorIa proveedor)
        {
            var calificador = CrearCalificador(proveedor);
            var clave = examen.PreguntasClave.ToList();
            ResultadoCalificacion resultado;
            try
            {
                using (var pdf = _storage.Abrir(evaluacion.ArchivoRef))
                {
                    resultado = await calificador.CalificarAsync(pdf, clave);
                }
            }
            catch (DriveNoAutorizadoException ex)
            {
                resultado = ResultadoCalificacion.Fallo(ex.Message);
            }

            if (!resultado.Exito)
            {
                // Se descarta el detalle anterior (re-calificación fallida).
                _store.ReemplazarRespuestas(evaluacion.Id, new List<RespuestaEstudiante>());
                evaluacion.Estado = EstadoEvaluacion.Error;
                evaluacion.MensajeError = resultado.Error;
                evaluacion.NotaTotal = null;
                evaluacion.FechaCalificacion = null;
                _store.ActualizarEvaluacion(evaluacion);
                return false;
            }

            var clavePorNumero = clave.ToDictionary(p => p.Numero);
            var nuevas = new List<RespuestaEstudiante>();
            decimal notaTotal = 0;
            foreach (var r in resultado.Respuestas)
            {
                clavePorNumero.TryGetValue(r.Numero, out var pregunta);
                nuevas.Add(new RespuestaEstudiante
                {
                    EvaluacionEstudianteId = evaluacion.Id,
                    Numero = r.Numero,
                    Enunciado = pregunta?.Enunciado ?? ("Pregunta " + r.Numero),
                    RespuestaTexto = r.RespuestaTexto,
                    PuntajeMaximo = pregunta?.Puntaje ?? 0,
                    PuntajeObtenido = r.PuntajeObtenido,
                    Comentario = r.Comentario,
                    Pagina = r.Pagina >= 1 ? r.Pagina : (int?)null,
                    MarcaX = r.MarcaX,
                    MarcaY = r.MarcaY,
                    Dudoso = r.Dudoso,
                    FechaCreacion = DateTime.UtcNow
                });
                notaTotal += r.PuntajeObtenido;
            }
            // La última corrida manda: se reemplaza el detalle por completo.
            _store.ReemplazarRespuestas(evaluacion.Id, nuevas);

            // Si la IA detectó el nombre y el actual sigue siendo el placeholder del archivo,
            // usar el detectado. Nunca pisamos un nombre que el docente haya editado a mano.
            var placeholderArchivo = Path.GetFileNameWithoutExtension(evaluacion.ArchivoNombreOriginal ?? string.Empty);
            var nombreEsPlaceholder = string.IsNullOrWhiteSpace(evaluacion.NombreEstudiante)
                || string.Equals(evaluacion.NombreEstudiante.Trim(), placeholderArchivo.Trim(), StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(resultado.NombreDetectado) && nombreEsPlaceholder)
                evaluacion.NombreEstudiante = resultado.NombreDetectado;

            evaluacion.NotaTotal = notaTotal;
            evaluacion.Estado = EstadoEvaluacion.Calificada;
            evaluacion.MensajeError = null;
            evaluacion.FechaCalificacion = DateTime.UtcNow;
            _store.ActualizarEvaluacion(evaluacion);
            return true;
        }

        // Estampa la nota (obtenido / máximo) y los ✔/✗ por pregunta sobre el PDF de la entrega,
        // conservando su Id en Drive. Best-effort: si falla (PDF ilegible, Drive caído, etc.)
        // no rompe el guardado de la nota.
        private void EstamparNota(EvaluacionEstudiante evaluacion, IList<RespuestaEstudiante> respuestas)
        {
            if (string.IsNullOrEmpty(evaluacion.ArchivoRef) || evaluacion.ExamenBase == null)
                return;
            try
            {
                var texto = string.Format(CultureInfo.InvariantCulture, "{0:0.##} / {1:0.##}",
                    evaluacion.NotaTotal ?? 0m, evaluacion.ExamenBase.NotaMaxima);

                var marcas = ConstruirMarcas(respuestas);

                byte[] sellado;
                using (var original = _storage.Abrir(evaluacion.ArchivoRef))
                {
                    sellado = _sello.Estampar(original, texto, marcas);
                }
                using (var ms = new MemoryStream(sellado))
                {
                    _storage.ReemplazarContenido(evaluacion.ArchivoRef, ms);
                }
            }
            catch
            {
                // El sello es secundario; la calificación ya quedó guardada.
            }
        }

        // Arma las marcas a dibujar: solo las que tienen ubicación válida y NO son dudosas
        // (las marcas múltiples/ambiguas no se dibujan; solo se avisan en pantalla).
        private static List<MarcaVisto> ConstruirMarcas(IList<RespuestaEstudiante> respuestas)
        {
            var marcas = new List<MarcaVisto>();
            foreach (var r in respuestas)
            {
                if (r.Dudoso || !r.Pagina.HasValue || r.Pagina.Value < 1 || !r.MarcaX.HasValue || !r.MarcaY.HasValue)
                    continue;

                VistoEstado estado;
                if (r.PuntajeMaximo > 0 && r.PuntajeObtenido >= r.PuntajeMaximo)
                    estado = VistoEstado.Correcto;
                else if (r.PuntajeObtenido <= 0)
                    estado = VistoEstado.Incorrecto;
                else
                    estado = VistoEstado.Parcial;

                marcas.Add(new MarcaVisto
                {
                    Pagina = r.Pagina.Value,
                    X = (double)r.MarcaX.Value,
                    Y = (double)r.MarcaY.Value,
                    Estado = estado
                });
            }
            return marcas;
        }

        // Mueve el PDF de la entrega a la carpeta destino (entregas/ o calificados/) en Drive.
        // Best-effort: cualquier fallo se ignora para no romper el flujo de calificación.
        private void MoverEntrega(EvaluacionEstudiante evaluacion, string categoriaDestino)
        {
            if (string.IsNullOrEmpty(evaluacion.ArchivoRef))
                return;
            try
            {
                _storage.Mover(evaluacion.ArchivoRef, categoriaDestino);
            }
            catch
            {
                // El movimiento de carpeta es secundario; la calificación ya quedó registrada.
            }
        }

        private static string ConstruirResumen(int subidos, int conError)
        {
            var resumen = string.Format("{0} entrega(s) subida(s) como pendientes.", subidos);
            if (conError > 0)
                resumen += string.Format(" {0} PDF con problemas (no se subieron).", conError);
            return resumen;
        }

        // Examen con su cadena (para breadcrumb) y su clave (para saber si se puede calificar).
        private ExamenBase CargarExamen(int id)
        {
            var examen = _store.ExamenConCadena(id);
            if (examen == null || examen.TipoEvaluacion == null
                || examen.TipoEvaluacion.Unidad == null || examen.TipoEvaluacion.Unidad.Curso == null)
                return null;
            examen.PreguntasClave = _store.PreguntasDeExamen(id);
            return examen;
        }

        // Entrega con su examen (cadena + clave) y su detalle por pregunta.
        private EvaluacionEstudiante CargarEvaluacion(int id)
        {
            var evaluacion = _store.EvaluacionConCadena(id);
            if (evaluacion == null || evaluacion.ExamenBase == null
                || evaluacion.ExamenBase.TipoEvaluacion == null
                || evaluacion.ExamenBase.TipoEvaluacion.Unidad == null
                || evaluacion.ExamenBase.TipoEvaluacion.Unidad.Curso == null)
                return null;
            return evaluacion;
        }
    }
}
