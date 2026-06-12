using System;
using System.Data.Entity;
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
        private readonly SpefDbContext _db;
        private readonly GoogleCurrentUserService _currentUser;
        private readonly DriveStorageService _storage;
        private readonly PdfPigValidationService _pdfValidator;

        public EvaluacionesController()
        {
            _db = new SpefDbContext();
            _currentUser = new GoogleCurrentUserService(_db);
            _storage = new DriveStorageService(_currentUser);
            _pdfValidator = new PdfPigValidationService();
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
            var usuario = _currentUser.ObtenerUsuarioActual();
            if (usuario == null)
                return RedirectToAction("Login", "Account");

            var examen = BuscarExamenDelUsuario(examenBaseId, usuario.Id);
            if (examen == null)
                return HttpNotFound();

            return View(examen);
        }

        [HttpGet]
        public ActionResult Subir(int examenBaseId)
        {
            var usuario = _currentUser.ObtenerUsuarioActual();
            if (usuario == null)
                return RedirectToAction("Login", "Account");

            var examen = BuscarExamenDelUsuario(examenBaseId, usuario.Id);
            if (examen == null)
                return HttpNotFound();

            ViewBag.Examen = examen;
            return View(new SubirEvaluacionesViewModel { ExamenBaseId = examen.Id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Subir(SubirEvaluacionesViewModel model)
        {
            var usuario = _currentUser.ObtenerUsuarioActual();
            if (usuario == null)
                return RedirectToAction("Login", "Account");

            var examen = BuscarExamenDelUsuario(model.ExamenBaseId, usuario.Id);
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

                var evaluacion = new EvaluacionEstudiante
                {
                    ExamenBaseId = examen.Id,
                    NombreEstudiante = Path.GetFileNameWithoutExtension(archivo.FileName),
                    ArchivoRef = archivoRef,
                    ArchivoNombreOriginal = Path.GetFileName(archivo.FileName),
                    TotalPaginas = validacion.TotalPaginas,
                    Estado = EstadoEvaluacion.Pendiente,
                    FechaCreacion = DateTime.UtcNow
                };

                try
                {
                    _db.EvaluacionesEstudiante.Add(evaluacion);
                    _db.SaveChanges();
                }
                catch
                {
                    // Compensación: no dejar el archivo huérfano si no se registró en BD.
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
            var usuario = _currentUser.ObtenerUsuarioActual();
            if (usuario == null)
                return RedirectToAction("Login", "Account");

            var evaluacion = BuscarEvaluacionDelUsuario(id, usuario.Id);
            if (evaluacion == null)
                return HttpNotFound();

            return View(evaluacion);
        }

        // (Re)califica una entrega ya subida.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Calificar(int id, string proveedor)
        {
            var usuario = _currentUser.ObtenerUsuarioActual();
            if (usuario == null)
                return RedirectToAction("Login", "Account");

            var evaluacion = BuscarEvaluacionDelUsuario(id, usuario.Id);
            if (evaluacion == null)
                return HttpNotFound();

            var examen = evaluacion.ExamenBase;
            if (!examen.PreguntasClave.Any())
            {
                TempData["Error"] = "El examen base no tiene clave calibrada. Calíbrala antes de calificar.";
                return RedirectToAction("Details", new { id });
            }

            var ia = ProveedorIaExtensions.Parsear(proveedor);
            if (await CalificarEvaluacion(evaluacion, examen, ia))
            {
                if (examen.Estado == EstadoExamen.Calibrado)
                {
                    examen.Estado = EstadoExamen.Activo;
                    examen.FechaModificacion = DateTime.UtcNow;
                    _db.SaveChanges();
                }
                TempData["Exito"] = "Entrega calificada con " + ia.Nombre() + ".";
            }
            else
            {
                TempData["Error"] = evaluacion.MensajeError ?? "No se pudo calificar la entrega.";
            }

            return RedirectToAction("Details", new { id });
        }

        // Corrige el nombre del estudiante (el que detectó la IA puede venir mal leído).
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EditarNombre(int id, string nombreEstudiante)
        {
            var usuario = _currentUser.ObtenerUsuarioActual();
            if (usuario == null)
                return RedirectToAction("Login", "Account");

            var evaluacion = BuscarEvaluacionDelUsuario(id, usuario.Id);
            if (evaluacion == null)
                return HttpNotFound();

            nombreEstudiante = (nombreEstudiante ?? string.Empty).Trim();
            if (nombreEstudiante.Length == 0)
            {
                TempData["Error"] = "El nombre no puede quedar vacío.";
                return RedirectToAction("Details", new { id });
            }

            evaluacion.NombreEstudiante = nombreEstudiante;
            _db.SaveChanges();
            TempData["Exito"] = "Nombre actualizado.";
            return RedirectToAction("Details", new { id });
        }

        public ActionResult Descargar(int id)
        {
            var usuario = _currentUser.ObtenerUsuarioActual();
            if (usuario == null)
                return RedirectToAction("Login", "Account");

            var evaluacion = BuscarEvaluacionDelUsuario(id, usuario.Id);
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
            var usuario = _currentUser.ObtenerUsuarioActual();
            if (usuario == null)
                return RedirectToAction("Login", "Account");

            var evaluacion = BuscarEvaluacionDelUsuario(id, usuario.Id);
            if (evaluacion == null)
                return HttpNotFound();

            var examenBaseId = evaluacion.ExamenBaseId;
            var archivo = evaluacion.ArchivoRef;

            _db.EvaluacionesEstudiante.Remove(evaluacion);
            _db.SaveChanges();
            _storage.Eliminar(archivo);

            return RedirectToAction("Index", new { examenBaseId });
        }

        // Llama a la IA con el PDF del alumno y la clave, y persiste el resultado en la entrega.
        // Devuelve true si quedó calificada; en caso de fallo deja Estado = Error con el motivo.
        private async Task<bool> CalificarEvaluacion(EvaluacionEstudiante evaluacion, ExamenBase examen, ProveedorIa proveedor)
        {
            var calificador = CrearCalificador(proveedor);
            ResultadoCalificacion resultado;
            try
            {
                using (var pdf = _storage.Abrir(evaluacion.ArchivoRef))
                {
                    resultado = await calificador.CalificarAsync(pdf, examen.PreguntasClave.ToList());
                }
            }
            catch (DriveNoAutorizadoException ex)
            {
                resultado = ResultadoCalificacion.Fallo(ex.Message);
            }

            // Se reemplaza el detalle anterior (re-calificación): la última corrida manda.
            if (evaluacion.Respuestas != null && evaluacion.Respuestas.Count > 0)
                _db.RespuestasEstudiante.RemoveRange(evaluacion.Respuestas);

            if (!resultado.Exito)
            {
                evaluacion.Estado = EstadoEvaluacion.Error;
                evaluacion.MensajeError = resultado.Error;
                evaluacion.NotaTotal = null;
                evaluacion.FechaCalificacion = null;
                _db.SaveChanges();
                return false;
            }

            var clavePorNumero = examen.PreguntasClave.ToDictionary(p => p.Numero);
            decimal notaTotal = 0;
            foreach (var r in resultado.Respuestas)
            {
                clavePorNumero.TryGetValue(r.Numero, out var pregunta);
                _db.RespuestasEstudiante.Add(new RespuestaEstudiante
                {
                    EvaluacionEstudianteId = evaluacion.Id,
                    Numero = r.Numero,
                    Enunciado = pregunta?.Enunciado ?? ("Pregunta " + r.Numero),
                    RespuestaTexto = r.RespuestaTexto,
                    PuntajeMaximo = pregunta?.Puntaje ?? 0,
                    PuntajeObtenido = r.PuntajeObtenido,
                    Comentario = r.Comentario,
                    FechaCreacion = DateTime.UtcNow
                });
                notaTotal += r.PuntajeObtenido;
            }

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
            _db.SaveChanges();
            return true;
        }

        private static string ConstruirResumen(int subidos, int conError)
        {
            var resumen = string.Format("{0} entrega(s) subida(s) como pendientes.", subidos);
            if (conError > 0)
                resumen += string.Format(" {0} PDF con problemas (no se subieron).", conError);
            return resumen;
        }

        private ExamenBase BuscarExamenDelUsuario(int id, int usuarioId)
        {
            return _db.ExamenesBase
                .Include(e => e.TipoEvaluacion.Unidad.Curso)
                .Include(e => e.PreguntasClave)
                .Include(e => e.Evaluaciones)
                .FirstOrDefault(e => e.Id == id && e.TipoEvaluacion.Unidad.Curso.UsuarioId == usuarioId);
        }

        private EvaluacionEstudiante BuscarEvaluacionDelUsuario(int id, int usuarioId)
        {
            return _db.EvaluacionesEstudiante
                .Include(e => e.Respuestas)
                .Include(e => e.ExamenBase.PreguntasClave)
                .Include(e => e.ExamenBase.TipoEvaluacion.Unidad.Curso)
                .FirstOrDefault(e => e.Id == id && e.ExamenBase.TipoEvaluacion.Unidad.Curso.UsuarioId == usuarioId);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _db.Dispose();
            base.Dispose(disposing);
        }
    }
}
