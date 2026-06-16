using System;
using System.Linq;
using System.Web.Mvc;
using pjtSPEF.Data;
using pjtSPEF.Models.Entities;
using pjtSPEF.Models.ViewModels;
using pjtSPEF.Services;

namespace pjtSPEF.Controllers
{
    // Raíz de la jerarquía: los periodos académicos del docente (año + tipo I/II/REC).
    // De cada periodo cuelgan los cursos.
    public class PeriodosController : Controller
    {
        private readonly GoogleCurrentUserService _currentUser;
        private readonly SpefSheetStore _store;
        private readonly DriveStorageService _storage;

        public PeriodosController()
        {
            _currentUser = new GoogleCurrentUserService();
            _store = new SpefSheetStore(_currentUser);
            _storage = new DriveStorageService(_currentUser);
        }

        public ActionResult Index()
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var periodos = _store.Periodos()
                .OrderByDescending(p => p.Anio)
                .ThenBy(p => p.Tipo)
                .ToList();
            return View(periodos);
        }

        public ActionResult Details(int id)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var periodo = _store.Periodo(id);
            if (periodo == null)
                return HttpNotFound();

            periodo.Cursos = _store.CursosDePeriodo(id);
            return View(periodo);
        }

        [HttpGet]
        public ActionResult Create()
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            return View(new PeriodoFormViewModel { Anio = DateTime.Now.Year, Tipo = "I" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(PeriodoFormViewModel model)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            if (!ModelState.IsValid)
                return View(model);

            var tipo = TipoPeriodoExtensions.Parsear(model.Tipo);
            if (_store.ExistePeriodo(model.Anio.Value, tipo))
            {
                ModelState.AddModelError("", "Ya existe ese periodo (" + model.Anio.Value + "-" + tipo.Sufijo() + ").");
                return View(model);
            }

            var periodo = _store.AgregarPeriodo(new Periodo
            {
                Anio = model.Anio.Value,
                Tipo = tipo,
                FechaCreacion = DateTime.UtcNow
            });

            return RedirectToAction("Details", new { id = periodo.Id });
        }

        [HttpGet]
        public ActionResult Edit(int id)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var periodo = _store.Periodo(id);
            if (periodo == null)
                return HttpNotFound();

            return View(new PeriodoFormViewModel { Id = periodo.Id, Anio = periodo.Anio, Tipo = periodo.Tipo.Sufijo() });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(int id, PeriodoFormViewModel model)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var periodo = _store.Periodo(id);
            if (periodo == null)
                return HttpNotFound();

            if (!ModelState.IsValid)
            {
                model.Id = id;
                return View(model);
            }

            var tipo = TipoPeriodoExtensions.Parsear(model.Tipo);
            if (_store.ExistePeriodo(model.Anio.Value, tipo, id))
            {
                ModelState.AddModelError("", "Ya existe ese periodo (" + model.Anio.Value + "-" + tipo.Sufijo() + ").");
                model.Id = id;
                return View(model);
            }

            periodo.Anio = model.Anio.Value;
            periodo.Tipo = tipo;
            _store.ActualizarPeriodo(periodo);

            return RedirectToAction("Details", new { id = periodo.Id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var periodo = _store.Periodo(id);
            if (periodo == null)
                return HttpNotFound();

            // Borra en cascada cursos → unidades → tipos → exámenes → preguntas/entregas/respuestas;
            // los PDFs de esos exámenes y entregas se eliminan aparte del almacenamiento.
            var archivos = _store.EliminarPeriodo(id);
            foreach (var archivo in archivos)
                _storage.Eliminar(archivo);

            return RedirectToAction("Index");
        }
    }
}
