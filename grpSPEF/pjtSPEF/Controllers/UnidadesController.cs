using System;
using System.Web.Mvc;
using pjtSPEF.Data;
using pjtSPEF.Models.Entities;
using pjtSPEF.Models.ViewModels;
using pjtSPEF.Services;

namespace pjtSPEF.Controllers
{
    public class UnidadesController : Controller
    {
        private readonly GoogleCurrentUserService _currentUser;
        private readonly SpefSheetStore _store;
        private readonly DriveStorageService _storage;

        public UnidadesController()
        {
            _currentUser = new GoogleCurrentUserService();
            _store = new SpefSheetStore(_currentUser);
            _storage = new DriveStorageService(_currentUser);
        }

        public ActionResult Details(int id)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var unidad = _store.UnidadConCadena(id);
            if (unidad == null || unidad.Curso == null)
                return HttpNotFound();

            unidad.TiposEvaluacion = _store.TiposDeUnidad(id);
            return View(unidad);
        }

        [HttpGet]
        public ActionResult Create(int cursoId)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var curso = _store.CursoConPeriodo(cursoId);
            if (curso == null)
                return HttpNotFound();

            var unidades = _store.UnidadesDeCurso(cursoId);
            var siguienteNumero = unidades.Count == 0 ? 1 : unidades[unidades.Count - 1].Numero + 1;
            ViewBag.Curso = curso;
            return View(new UnidadFormViewModel { CursoId = cursoId, Numero = siguienteNumero });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(UnidadFormViewModel model)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var curso = _store.CursoConPeriodo(model.CursoId);
            if (curso == null)
                return HttpNotFound();

            ValidarNumeroUnico(model);
            if (!ModelState.IsValid)
            {
                ViewBag.Curso = curso;
                return View(model);
            }

            _store.AgregarUnidad(new Unidad
            {
                CursoId = curso.Id,
                Numero = model.Numero.Value,
                Nombre = model.Nombre.Trim(),
                FechaCreacion = DateTime.UtcNow
            });

            return RedirectToAction("Details", "Cursos", new { id = curso.Id });
        }

        [HttpGet]
        public ActionResult Edit(int id)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var unidad = _store.UnidadConCadena(id);
            if (unidad == null || unidad.Curso == null)
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
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var unidad = _store.UnidadConCadena(id);
            if (unidad == null || unidad.Curso == null)
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
            _store.ActualizarUnidad(unidad);

            return RedirectToAction("Details", "Cursos", new { id = unidad.CursoId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var unidad = _store.Unidad(id);
            if (unidad == null)
                return HttpNotFound();

            var cursoId = unidad.CursoId;
            var archivos = _store.EliminarUnidad(id);
            foreach (var archivo in archivos)
                _storage.Eliminar(archivo);

            return RedirectToAction("Details", "Cursos", new { id = cursoId });
        }

        // El número de unidad debe ser único dentro del curso; se valida aquí para dar
        // un mensaje claro en el formulario en lugar de un error genérico.
        private void ValidarNumeroUnico(UnidadFormViewModel model)
        {
            if (!model.Numero.HasValue)
                return;

            if (_store.ExisteUnidadNumero(model.CursoId, model.Numero.Value, model.Id))
                ModelState.AddModelError("Numero", "Ya existe una unidad con ese número en este curso.");
        }
    }
}
