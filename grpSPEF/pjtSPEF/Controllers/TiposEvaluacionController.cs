using System;
using System.Data.Entity;
using System.Linq;
using System.Web.Mvc;
using pjtSPEF.Data;
using pjtSPEF.Models.Entities;
using pjtSPEF.Models.ViewModels;
using pjtSPEF.Services;

namespace pjtSPEF.Controllers
{
    public class TiposEvaluacionController : Controller
    {
        private readonly SpefDbContext _db;
        private readonly ICurrentUserService _currentUser;
        private readonly IFileStorageService _storage;

        public TiposEvaluacionController()
        {
            _db = new SpefDbContext();
            _currentUser = new FormsCurrentUserService(_db);
            _storage = new LocalFileStorageService();
        }

        public TiposEvaluacionController(SpefDbContext db, ICurrentUserService currentUser, IFileStorageService storage)
        {
            _db = db;
            _currentUser = currentUser;
            _storage = storage;
        }

        public ActionResult Details(int id)
        {
            var usuario = _currentUser.ObtenerUsuarioActual();
            if (usuario == null)
                return RedirectToAction("Login", "Account");

            var tipo = _db.TiposEvaluacion
                .Include(t => t.Unidad.Curso)
                .Include(t => t.ExamenesBase)
                .FirstOrDefault(t => t.Id == id && t.Unidad.Curso.UsuarioId == usuario.Id);
            if (tipo == null)
                return HttpNotFound();

            return View(tipo);
        }

        [HttpGet]
        public ActionResult Create(int unidadId)
        {
            var usuario = _currentUser.ObtenerUsuarioActual();
            if (usuario == null)
                return RedirectToAction("Login", "Account");

            var unidad = _db.Unidades
                .Include(u => u.Curso)
                .FirstOrDefault(u => u.Id == unidadId && u.Curso.UsuarioId == usuario.Id);
            if (unidad == null)
                return HttpNotFound();

            ViewBag.Unidad = unidad;
            return View(new TipoEvaluacionFormViewModel { UnidadId = unidadId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(TipoEvaluacionFormViewModel model)
        {
            var usuario = _currentUser.ObtenerUsuarioActual();
            if (usuario == null)
                return RedirectToAction("Login", "Account");

            var unidad = _db.Unidades
                .Include(u => u.Curso)
                .FirstOrDefault(u => u.Id == model.UnidadId && u.Curso.UsuarioId == usuario.Id);
            if (unidad == null)
                return HttpNotFound();

            if (!ModelState.IsValid)
            {
                ViewBag.Unidad = unidad;
                return View(model);
            }

            var tipo = new TipoEvaluacion
            {
                UnidadId = unidad.Id,
                Nombre = model.Nombre.Trim(),
                FechaCreacion = DateTime.UtcNow
            };
            _db.TiposEvaluacion.Add(tipo);
            _db.SaveChanges();

            return RedirectToAction("Details", "Unidades", new { id = unidad.Id });
        }

        [HttpGet]
        public ActionResult Edit(int id)
        {
            var usuario = _currentUser.ObtenerUsuarioActual();
            if (usuario == null)
                return RedirectToAction("Login", "Account");

            var tipo = _db.TiposEvaluacion
                .Include(t => t.Unidad.Curso)
                .FirstOrDefault(t => t.Id == id && t.Unidad.Curso.UsuarioId == usuario.Id);
            if (tipo == null)
                return HttpNotFound();

            ViewBag.Unidad = tipo.Unidad;
            return View(new TipoEvaluacionFormViewModel { Id = tipo.Id, UnidadId = tipo.UnidadId, Nombre = tipo.Nombre });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(int id, TipoEvaluacionFormViewModel model)
        {
            var usuario = _currentUser.ObtenerUsuarioActual();
            if (usuario == null)
                return RedirectToAction("Login", "Account");

            var tipo = _db.TiposEvaluacion
                .Include(t => t.Unidad.Curso)
                .FirstOrDefault(t => t.Id == id && t.Unidad.Curso.UsuarioId == usuario.Id);
            if (tipo == null)
                return HttpNotFound();

            if (!ModelState.IsValid)
            {
                model.Id = id;
                model.UnidadId = tipo.UnidadId;
                ViewBag.Unidad = tipo.Unidad;
                return View(model);
            }

            tipo.Nombre = model.Nombre.Trim();
            _db.SaveChanges();

            return RedirectToAction("Details", "Unidades", new { id = tipo.UnidadId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id)
        {
            var usuario = _currentUser.ObtenerUsuarioActual();
            if (usuario == null)
                return RedirectToAction("Login", "Account");

            var tipo = _db.TiposEvaluacion
                .Include(t => t.Unidad.Curso)
                .FirstOrDefault(t => t.Id == id && t.Unidad.Curso.UsuarioId == usuario.Id);
            if (tipo == null)
                return HttpNotFound();

            var unidadId = tipo.UnidadId;
            var archivos = _db.ExamenesBase
                .Where(e => e.TipoEvaluacionId == id && e.ArchivoRef != null)
                .Select(e => e.ArchivoRef)
                .ToList();

            _db.TiposEvaluacion.Remove(tipo);
            _db.SaveChanges();

            foreach (var archivo in archivos)
                _storage.Eliminar(archivo);

            return RedirectToAction("Details", "Unidades", new { id = unidadId });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _db.Dispose();
            base.Dispose(disposing);
        }
    }
}
