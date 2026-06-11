using System;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Web.Mvc;
using pjtSPEF.Data;
using pjtSPEF.Models.Entities;
using pjtSPEF.Models.ViewModels;
using pjtSPEF.Services;

namespace pjtSPEF.Controllers
{
    public class ExamenesBaseController : Controller
    {
        private const string CategoriaStorage = "examenes-base";

        private readonly SpefDbContext _db;
        private readonly ICurrentUserService _currentUser;
        private readonly IFileStorageService _storage;
        private readonly IPdfValidationService _pdfValidator;

        public ExamenesBaseController()
        {
            _db = new SpefDbContext();
            _currentUser = new FormsCurrentUserService(_db);
            _storage = new LocalFileStorageService();
            _pdfValidator = new PdfPigValidationService();
        }

        public ExamenesBaseController(SpefDbContext db, ICurrentUserService currentUser,
            IFileStorageService storage, IPdfValidationService pdfValidator)
        {
            _db = db;
            _currentUser = currentUser;
            _storage = storage;
            _pdfValidator = pdfValidator;
        }

        public ActionResult Details(int id)
        {
            var usuario = _currentUser.ObtenerUsuarioActual();
            if (usuario == null)
                return RedirectToAction("Login", "Account");

            var examen = BuscarExamenDelUsuario(id, usuario.Id);
            if (examen == null)
                return HttpNotFound();

            return View(examen);
        }

        [HttpGet]
        public ActionResult Create(int tipoEvaluacionId)
        {
            var usuario = _currentUser.ObtenerUsuarioActual();
            if (usuario == null)
                return RedirectToAction("Login", "Account");

            var tipo = BuscarTipoDelUsuario(tipoEvaluacionId, usuario.Id);
            if (tipo == null)
                return HttpNotFound();

            ViewBag.TipoEvaluacion = tipo;
            return View(new ExamenBaseFormViewModel { TipoEvaluacionId = tipoEvaluacionId, NotaMaxima = 20 });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(ExamenBaseFormViewModel model)
        {
            var usuario = _currentUser.ObtenerUsuarioActual();
            if (usuario == null)
                return RedirectToAction("Login", "Account");

            var tipo = BuscarTipoDelUsuario(model.TipoEvaluacionId, usuario.Id);
            if (tipo == null)
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

            var archivoRef = _storage.Guardar(model.ArchivoPdf.InputStream, CategoriaStorage, ".pdf");
            try
            {
                var examen = new ExamenBase
                {
                    TipoEvaluacionId = tipo.Id,
                    Titulo = model.Titulo.Trim(),
                    NotaMaxima = model.NotaMaxima.Value,
                    ArchivoRef = archivoRef,
                    ArchivoNombreOriginal = Path.GetFileName(model.ArchivoPdf.FileName),
                    TotalPaginas = totalPaginas,
                    Estado = EstadoExamen.Borrador,
                    FechaCreacion = DateTime.UtcNow
                };
                _db.ExamenesBase.Add(examen);
                _db.SaveChanges();

                return RedirectToAction("Details", new { id = examen.Id });
            }
            catch
            {
                // Compensación: si no se pudo registrar en BD, no dejar el archivo huérfano.
                _storage.Eliminar(archivoRef);
                throw;
            }
        }

        [HttpGet]
        public ActionResult Edit(int id)
        {
            var usuario = _currentUser.ObtenerUsuarioActual();
            if (usuario == null)
                return RedirectToAction("Login", "Account");

            var examen = BuscarExamenDelUsuario(id, usuario.Id);
            if (examen == null)
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
            var usuario = _currentUser.ObtenerUsuarioActual();
            if (usuario == null)
                return RedirectToAction("Login", "Account");

            var examen = BuscarExamenDelUsuario(id, usuario.Id);
            if (examen == null)
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

            string archivoAnterior = null;
            if (hayArchivoNuevo)
            {
                archivoAnterior = examen.ArchivoRef;
                examen.ArchivoRef = _storage.Guardar(model.ArchivoPdf.InputStream, CategoriaStorage, ".pdf");
                examen.ArchivoNombreOriginal = Path.GetFileName(model.ArchivoPdf.FileName);
                examen.TotalPaginas = totalPaginas;
            }

            examen.Titulo = model.Titulo.Trim();
            examen.NotaMaxima = model.NotaMaxima.Value;
            examen.FechaModificacion = DateTime.UtcNow;
            _db.SaveChanges();

            if (archivoAnterior != null)
                _storage.Eliminar(archivoAnterior);

            return RedirectToAction("Details", new { id = examen.Id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id)
        {
            var usuario = _currentUser.ObtenerUsuarioActual();
            if (usuario == null)
                return RedirectToAction("Login", "Account");

            var examen = BuscarExamenDelUsuario(id, usuario.Id);
            if (examen == null)
                return HttpNotFound();

            var tipoEvaluacionId = examen.TipoEvaluacionId;
            var archivo = examen.ArchivoRef;

            _db.ExamenesBase.Remove(examen);
            _db.SaveChanges();
            _storage.Eliminar(archivo);

            return RedirectToAction("Details", "TiposEvaluacion", new { id = tipoEvaluacionId });
        }

        public ActionResult Descargar(int id)
        {
            var usuario = _currentUser.ObtenerUsuarioActual();
            if (usuario == null)
                return RedirectToAction("Login", "Account");

            var examen = BuscarExamenDelUsuario(id, usuario.Id);
            if (examen == null || string.IsNullOrEmpty(examen.ArchivoRef))
                return HttpNotFound();

            var nombreDescarga = string.IsNullOrEmpty(examen.ArchivoNombreOriginal)
                ? examen.Titulo + ".pdf"
                : examen.ArchivoNombreOriginal;
            return File(_storage.Abrir(examen.ArchivoRef), "application/pdf", nombreDescarga);
        }

        private ExamenBase BuscarExamenDelUsuario(int id, int usuarioId)
        {
            return _db.ExamenesBase
                .Include(e => e.TipoEvaluacion.Unidad.Curso)
                .FirstOrDefault(e => e.Id == id && e.TipoEvaluacion.Unidad.Curso.UsuarioId == usuarioId);
        }

        private TipoEvaluacion BuscarTipoDelUsuario(int id, int usuarioId)
        {
            return _db.TiposEvaluacion
                .Include(t => t.Unidad.Curso)
                .FirstOrDefault(t => t.Id == id && t.Unidad.Curso.UsuarioId == usuarioId);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _db.Dispose();
            base.Dispose(disposing);
        }
    }
}
