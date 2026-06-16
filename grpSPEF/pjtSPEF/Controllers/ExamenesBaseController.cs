using System;
using System.IO;
using System.Web.Mvc;
using pjtSPEF.Data;
using pjtSPEF.Models.Entities;
using pjtSPEF.Models.ViewModels;
using pjtSPEF.Services;

namespace pjtSPEF.Controllers
{
    public class ExamenesBaseController : Controller
    {
        private readonly GoogleCurrentUserService _currentUser;
        private readonly SpefSheetStore _store;
        private readonly DriveStorageService _storage;
        private readonly PdfPigValidationService _pdfValidator;

        public ExamenesBaseController()
        {
            _currentUser = new GoogleCurrentUserService();
            _store = new SpefSheetStore(_currentUser);
            _storage = new DriveStorageService(_currentUser);
            _pdfValidator = new PdfPigValidationService();
        }

        public ActionResult Details(int id)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var examen = _store.ExamenConCadena(id);
            if (!EsValido(examen))
                return HttpNotFound();

            examen.PreguntasClave = _store.PreguntasDeExamen(id);
            return View(examen);
        }

        [HttpGet]
        public ActionResult Create(int tipoEvaluacionId)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var tipo = _store.TipoConCadena(tipoEvaluacionId);
            if (tipo == null || tipo.Unidad == null || tipo.Unidad.Curso == null)
                return HttpNotFound();

            ViewBag.TipoEvaluacion = tipo;
            return View(new ExamenBaseFormViewModel { TipoEvaluacionId = tipoEvaluacionId, NotaMaxima = 20 });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(ExamenBaseFormViewModel model)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var tipo = _store.TipoConCadena(model.TipoEvaluacionId);
            if (tipo == null || tipo.Unidad == null || tipo.Unidad.Curso == null)
                return HttpNotFound();

            if (model.ArchivoPdf == null || model.ArchivoPdf.ContentLength == 0)
                ModelState.AddModelError("ArchivoPdf", "Debes subir el PDF del examen con respuestas.");

            int totalPaginas = 0;
            if (ModelState.IsValid)
            {
                var validacion = _pdfValidator.Validar(model.ArchivoPdf.InputStream);
                if (!validacion.EsValido)
                    ModelState.AddModelError("ArchivoPdf", validacion.Error);
                else
                    totalPaginas = validacion.TotalPaginas;
            }

            if (!ModelState.IsValid)
            {
                ViewBag.TipoEvaluacion = tipo;
                return View(model);
            }

            string archivoRef;
            try
            {
                archivoRef = _storage.Guardar(model.ArchivoPdf.InputStream, RutaStorage.ExamenBase(tipo, model.Titulo), ".pdf");
            }
            catch (DriveNoAutorizadoException ex)
            {
                ModelState.AddModelError("ArchivoPdf", ex.Message);
                ViewBag.TipoEvaluacion = tipo;
                return View(model);
            }

            try
            {
                var examen = _store.AgregarExamen(new ExamenBase
                {
                    TipoEvaluacionId = tipo.Id,
                    Titulo = model.Titulo.Trim(),
                    NotaMaxima = model.NotaMaxima.Value,
                    ArchivoRef = archivoRef,
                    ArchivoNombreOriginal = Path.GetFileName(model.ArchivoPdf.FileName),
                    TotalPaginas = totalPaginas,
                    Estado = EstadoExamen.Borrador,
                    FechaCreacion = DateTime.UtcNow
                });

                return RedirectToAction("Details", new { id = examen.Id });
            }
            catch
            {
                // Compensación: si no se pudo registrar, no dejar el archivo huérfano en Drive.
                _storage.Eliminar(archivoRef);
                throw;
            }
        }

        [HttpGet]
        public ActionResult Edit(int id)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var examen = _store.ExamenConCadena(id);
            if (!EsValido(examen))
                return HttpNotFound();

            ViewBag.Examen = examen;
            return View(new ExamenBaseFormViewModel
            {
                Id = examen.Id,
                TipoEvaluacionId = examen.TipoEvaluacionId,
                Titulo = examen.Titulo,
                NotaMaxima = examen.NotaMaxima
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(int id, ExamenBaseFormViewModel model)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var examen = _store.ExamenConCadena(id);
            if (!EsValido(examen))
                return HttpNotFound();

            // El PDF es opcional al editar: si llega, reemplaza al anterior.
            var hayArchivoNuevo = model.ArchivoPdf != null && model.ArchivoPdf.ContentLength > 0;
            int totalPaginas = 0;
            if (hayArchivoNuevo && ModelState.IsValid)
            {
                var validacion = _pdfValidator.Validar(model.ArchivoPdf.InputStream);
                if (!validacion.EsValido)
                    ModelState.AddModelError("ArchivoPdf", validacion.Error);
                else
                    totalPaginas = validacion.TotalPaginas;
            }

            if (!ModelState.IsValid)
            {
                model.Id = id;
                model.TipoEvaluacionId = examen.TipoEvaluacionId;
                ViewBag.Examen = examen;
                return View(model);
            }

            // Si cambió el título, renombra la carpeta del examen en Drive (arrastra el PDF base
            // y la subcarpeta de entregas).
            var tituloNuevo = model.Titulo.Trim();
            if (!string.Equals(examen.Titulo, tituloNuevo, StringComparison.Ordinal) && !string.IsNullOrEmpty(examen.ArchivoRef))
            {
                _storage.RenombrarCarpetaContenedora(
                    examen.ArchivoRef,
                    RutaStorage.NombreCarpetaExamen(examen.Titulo),
                    RutaStorage.NombreCarpetaExamen(tituloNuevo));
            }

            string archivoAnterior = null;
            if (hayArchivoNuevo)
            {
                string nuevoRef;
                try
                {
                    nuevoRef = _storage.Guardar(model.ArchivoPdf.InputStream, RutaStorage.ExamenBase(examen.TipoEvaluacion, tituloNuevo), ".pdf");
                }
                catch (DriveNoAutorizadoException ex)
                {
                    ModelState.AddModelError("ArchivoPdf", ex.Message);
                    model.Id = id;
                    model.TipoEvaluacionId = examen.TipoEvaluacionId;
                    ViewBag.Examen = examen;
                    return View(model);
                }

                archivoAnterior = examen.ArchivoRef;
                examen.ArchivoRef = nuevoRef;
                examen.ArchivoNombreOriginal = Path.GetFileName(model.ArchivoPdf.FileName);
                examen.TotalPaginas = totalPaginas;
            }

            examen.Titulo = tituloNuevo;
            examen.NotaMaxima = model.NotaMaxima.Value;
            examen.FechaModificacion = DateTime.UtcNow;
            _store.ActualizarExamen(examen);

            if (archivoAnterior != null)
                _storage.Eliminar(archivoAnterior);

            return RedirectToAction("Details", new { id = examen.Id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var examen = _store.Examen(id);
            if (examen == null)
                return HttpNotFound();

            var tipoEvaluacionId = examen.TipoEvaluacionId;
            var archivos = _store.EliminarExamen(id);
            foreach (var archivo in archivos)
                _storage.Eliminar(archivo);

            return RedirectToAction("Details", "TiposEvaluacion", new { id = tipoEvaluacionId });
        }

        public ActionResult Descargar(int id)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var examen = _store.Examen(id);
            if (examen == null || string.IsNullOrEmpty(examen.ArchivoRef))
                return HttpNotFound();

            var nombreDescarga = string.IsNullOrEmpty(examen.ArchivoNombreOriginal)
                ? examen.Titulo + ".pdf"
                : examen.ArchivoNombreOriginal;
            return File(_storage.Abrir(examen.ArchivoRef), "application/pdf", nombreDescarga);
        }

        private static bool EsValido(ExamenBase examen)
        {
            return examen != null && examen.TipoEvaluacion != null
                && examen.TipoEvaluacion.Unidad != null && examen.TipoEvaluacion.Unidad.Curso != null;
        }
    }
}
