using System;
using System.Web.Mvc;
using pjtSPEF.Data;
using pjtSPEF.Models.Entities;
using pjtSPEF.Models.ViewModels;
using pjtSPEF.Services;

namespace pjtSPEF.Controllers
{
    // Cursos/materias dentro de un periodo. El curso ya no guarda su periodo: cuelga de uno.
    public class CursosController : Controller
    {
        private readonly GoogleCurrentUserService _currentUser;
        private readonly SpefSheetStore _store;
        private readonly DriveStorageService _storage;

        public CursosController()
        {
            _currentUser = new GoogleCurrentUserService();
            _store = new SpefSheetStore(_currentUser);
            _storage = new DriveStorageService(_currentUser);
        }

        public ActionResult Details(int id)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var curso = _store.CursoConPeriodo(id);
            if (curso == null)
                return HttpNotFound();

            curso.Unidades = _store.UnidadesDeCurso(id);
            return View(curso);
        }

        [HttpGet]
        public ActionResult Create(int periodoId)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var periodo = _store.Periodo(periodoId);
            if (periodo == null)
                return HttpNotFound();

            ViewBag.Periodo = periodo;
            return View(new CursoFormViewModel { PeriodoId = periodoId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(CursoFormViewModel model)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var periodo = _store.Periodo(model.PeriodoId);
            if (periodo == null)
                return HttpNotFound();

            if (!ModelState.IsValid)
            {
                ViewBag.Periodo = periodo;
                return View(model);
            }

            var curso = _store.AgregarCurso(new Curso
            {
                PeriodoId = periodo.Id,
                Nombre = model.Nombre.Trim(),
                FechaCreacion = DateTime.UtcNow
            });

            return RedirectToAction("Details", new { id = curso.Id });
        }

        [HttpGet]
        public ActionResult Edit(int id)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var curso = _store.CursoConPeriodo(id);
            if (curso == null)
                return HttpNotFound();

            ViewBag.Periodo = curso.Periodo;
            return View(new CursoFormViewModel { Id = curso.Id, PeriodoId = curso.PeriodoId, Nombre = curso.Nombre });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(int id, CursoFormViewModel model)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var curso = _store.CursoConPeriodo(id);
            if (curso == null)
                return HttpNotFound();

            if (!ModelState.IsValid)
            {
                model.Id = id;
                model.PeriodoId = curso.PeriodoId;
                ViewBag.Periodo = curso.Periodo;
                return View(model);
            }

            curso.Nombre = model.Nombre.Trim();
            _store.ActualizarCurso(curso);

            return RedirectToAction("Details", new { id = curso.Id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var curso = _store.Curso(id);
            if (curso == null)
                return HttpNotFound();

            var periodoId = curso.PeriodoId;

            // Cascada: unidades → tipos → exámenes → preguntas/entregas/respuestas. Los PDFs aparte.
            var archivos = _store.EliminarCurso(id);
            foreach (var archivo in archivos)
                _storage.Eliminar(archivo);

            return RedirectToAction("Details", "Periodos", new { id = periodoId });
        }
    }
}
