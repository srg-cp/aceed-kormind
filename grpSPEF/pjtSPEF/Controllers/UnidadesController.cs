using System;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Web.Mvc;
using pjtSPEF.Data;
using pjtSPEF.Models.Entities;
using pjtSPEF.Models.ViewModels;
using pjtSPEF.Services;

namespace pjtSPEF.Controllers
{
    public class UnidadesController : Controller
    {
        private readonly SpefDbContext _db;
        private readonly GoogleCurrentUserService _currentUser;
        private readonly DriveStorageService _storage;

        public UnidadesController()
        {
            _db = new SpefDbContext();
            _currentUser = new GoogleCurrentUserService(_db);
            _storage = new DriveStorageService(_currentUser);
        }

        public ActionResult Details(int id)
        {
            var usuario = _currentUser.ObtenerUsuarioActual();
            if (usuario == null)
                return RedirectToAction("Login", "Account");

            var unidad = _db.Unidades
                .Include(u => u.Curso)
                .Include(u => u.TiposEvaluacion)
                .FirstOrDefault(u => u.Id == id && u.Curso.UsuarioId == usuario.Id);
            if (unidad == null)
                return HttpNotFound();

            return View(unidad);
        }

        [HttpGet]
        public ActionResult Create(int cursoId)
        {
            var usuario = _currentUser.ObtenerUsuarioActual();
            if (usuario == null)
                return RedirectToAction("Login", "Account");

            var curso = _db.Cursos.FirstOrDefault(c => c.Id == cursoId && c.UsuarioId == usuario.Id);
            if (curso == null)
                return HttpNotFound();

            var siguienteNumero = _db.Unidades.Where(u => u.CursoId == cursoId).Select(u => (int?)u.Numero).Max() ?? 0;
            ViewBag.Curso = curso;
            return View(new UnidadFormViewModel { CursoId = cursoId, Numero = siguienteNumero + 1 });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(UnidadFormViewModel model)
        {
            var usuario = _currentUser.ObtenerUsuarioActual();
            if (usuario == null)
                return RedirectToAction("Login", "Account");

            var curso = _db.Cursos.FirstOrDefault(c => c.Id == model.CursoId && c.UsuarioId == usuario.Id);
            if (curso == null)
                return HttpNotFound();

            ValidarNumeroUnico(model);
            if (!ModelState.IsValid)
            {
                ViewBag.Curso = curso;
                return View(model);
            }

            var unidad = new Unidad
            {
                CursoId = curso.Id,
                Numero = model.Numero.Value,
                Nombre = model.Nombre.Trim(),
                FechaCreacion = DateTime.UtcNow
            };
            _db.Unidades.Add(unidad);
            _db.SaveChanges();

            return RedirectToAction("Details", "Cursos", new { id = curso.Id });
        }

        [HttpGet]
        public ActionResult Edit(int id)
        {
            var usuario = _currentUser.ObtenerUsuarioActual();
            if (usuario == null)
                return RedirectToAction("Login", "Account");

            var unidad = _db.Unidades
                .Include(u => u.Curso)
                .FirstOrDefault(u => u.Id == id && u.Curso.UsuarioId == usuario.Id);
            if (unidad == null)
                return HttpNotFound();

            ViewBag.Curso = unidad.Curso;
            return View(new UnidadFormViewModel
            {
                Id = unidad.Id,
                CursoId = unidad.CursoId,
                Numero = unidad.Numero,
                Nombre = unidad.Nombre
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(int id, UnidadFormViewModel model)
        {
            var usuario = _currentUser.ObtenerUsuarioActual();
            if (usuario == null)
                return RedirectToAction("Login", "Account");

            var unidad = _db.Unidades
                .Include(u => u.Curso)
                .FirstOrDefault(u => u.Id == id && u.Curso.UsuarioId == usuario.Id);
            if (unidad == null)
                return HttpNotFound();

            model.Id = id;
            model.CursoId = unidad.CursoId;
            ValidarNumeroUnico(model);
            if (!ModelState.IsValid)
            {
                ViewBag.Curso = unidad.Curso;
                return View(model);
            }

            unidad.Numero = model.Numero.Value;
            unidad.Nombre = model.Nombre.Trim();
            _db.SaveChanges();

            return RedirectToAction("Details", "Cursos", new { id = unidad.CursoId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id)
        {
            var usuario = _currentUser.ObtenerUsuarioActual();
            if (usuario == null)
                return RedirectToAction("Login", "Account");

            var unidad = _db.Unidades
                .Include(u => u.Curso)
                .FirstOrDefault(u => u.Id == id && u.Curso.UsuarioId == usuario.Id);
            if (unidad == null)
                return HttpNotFound();

            var cursoId = unidad.CursoId;
            var archivos = _db.ExamenesBase
                .Where(e => e.TipoEvaluacion.UnidadId == id && e.ArchivoRef != null)
                .Select(e => e.ArchivoRef)
                .ToList();

            _db.Unidades.Remove(unidad);
            _db.SaveChanges();

            foreach (var archivo in archivos)
                _storage.Eliminar(archivo);

            return RedirectToAction("Details", "Cursos", new { id = cursoId });
        }

        // La restricción UNIQUE(curso_id, numero) existe en la BD; se valida aquí
        // para dar un mensaje claro en el formulario en lugar de un error 500.
        private void ValidarNumeroUnico(UnidadFormViewModel model)
        {
            if (!model.Numero.HasValue)
                return;

            var duplicado = _db.Unidades.Any(u =>
                u.CursoId == model.CursoId &&
                u.Numero == model.Numero.Value &&
                (!model.Id.HasValue || u.Id != model.Id.Value));

            if (duplicado)
                ModelState.AddModelError("Numero", "Ya existe una unidad con ese número en este curso.");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _db.Dispose();
            base.Dispose(disposing);
        }
    }
}
