using System;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Web.Mvc;
using pjtSPEF.Data;
using pjtSPEF.Models.Entities;
using pjtSPEF.Models.ViewModels;
using pjtSPEF.Services;

namespace pjtSPEF.Controllers
{
    public class CursosController : Controller
    {
        private readonly SpefDbContext _db;
        private readonly ICurrentUserService _currentUser;
        private readonly IFileStorageService _storage;

        public CursosController()
        {
            _db = new SpefDbContext();
            _currentUser = new FormsCurrentUserService(_db);
            _storage = new LocalFileStorageService();
        }

        public CursosController(SpefDbContext db, ICurrentUserService currentUser, IFileStorageService storage)
        {
            _db = db;
            _currentUser = currentUser;
            _storage = storage;
        }

        public ActionResult Index()
        {
            var usuario = _currentUser.ObtenerUsuarioActual();
            if (usuario == null)
                return RedirectToAction("Login", "Account");

            var cursos = _db.Cursos
                .Where(c => c.UsuarioId == usuario.Id)
                .OrderBy(c => c.Nombre)
                .ToList();
            return View(cursos);
        }

        public ActionResult Details(int id)
        {
            var usuario = _currentUser.ObtenerUsuarioActual();
            if (usuario == null)
                return RedirectToAction("Login", "Account");

            var curso = _db.Cursos
                .Include(c => c.Unidades)
                .FirstOrDefault(c => c.Id == id && c.UsuarioId == usuario.Id);
            if (curso == null)
                return HttpNotFound();

            return View(curso);
        }

        [HttpGet]
        public ActionResult Create()
        {
            return View(new CursoFormViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(CursoFormViewModel model)
        {
            var usuario = _currentUser.ObtenerUsuarioActual();
            if (usuario == null)
                return RedirectToAction("Login", "Account");

            if (!ModelState.IsValid)
                return View(model);

            var curso = new Curso
            {
                UsuarioId = usuario.Id,
                Nombre = model.Nombre.Trim(),
                Periodo = string.IsNullOrWhiteSpace(model.Periodo) ? null : model.Periodo.Trim(),
                FechaCreacion = DateTime.UtcNow
            };
            _db.Cursos.Add(curso);
            _db.SaveChanges();

            return RedirectToAction("Details", new { id = curso.Id });
        }

        [HttpGet]
        public ActionResult Edit(int id)
        {
            var usuario = _currentUser.ObtenerUsuarioActual();
            if (usuario == null)
                return RedirectToAction("Login", "Account");

            var curso = _db.Cursos.FirstOrDefault(c => c.Id == id && c.UsuarioId == usuario.Id);
            if (curso == null)
                return HttpNotFound();

            return View(new CursoFormViewModel { Id = curso.Id, Nombre = curso.Nombre, Periodo = curso.Periodo });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(int id, CursoFormViewModel model)
        {
            var usuario = _currentUser.ObtenerUsuarioActual();
            if (usuario == null)
                return RedirectToAction("Login", "Account");

            var curso = _db.Cursos.FirstOrDefault(c => c.Id == id && c.UsuarioId == usuario.Id);
            if (curso == null)
                return HttpNotFound();

            if (!ModelState.IsValid)
            {
                model.Id = id;
                return View(model);
            }

            curso.Nombre = model.Nombre.Trim();
            curso.Periodo = string.IsNullOrWhiteSpace(model.Periodo) ? null : model.Periodo.Trim();
            _db.SaveChanges();

            return RedirectToAction("Details", new { id = curso.Id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id)
        {
            var usuario = _currentUser.ObtenerUsuarioActual();
            if (usuario == null)
                return RedirectToAction("Login", "Account");

            var curso = _db.Cursos.FirstOrDefault(c => c.Id == id && c.UsuarioId == usuario.Id);
            if (curso == null)
                return HttpNotFound();

            // La BD elimina en cascada unidades → tipos → exámenes; los PDFs
            // de esos exámenes hay que borrarlos aparte del almacenamiento.
            var archivos = _db.ExamenesBase
                .Where(e => e.TipoEvaluacion.Unidad.CursoId == id && e.ArchivoRef != null)
                .Select(e => e.ArchivoRef)
                .ToList();

            _db.Cursos.Remove(curso);
            _db.SaveChanges();

            foreach (var archivo in archivos)
                _storage.Eliminar(archivo);

            return RedirectToAction("Index");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _db.Dispose();
            base.Dispose(disposing);
        }
    }
}
