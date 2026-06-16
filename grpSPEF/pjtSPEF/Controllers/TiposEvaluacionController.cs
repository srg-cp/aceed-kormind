using System;
using System.Web.Mvc;
using pjtSPEF.Data;
using pjtSPEF.Models.Entities;
using pjtSPEF.Models.ViewModels;
using pjtSPEF.Services;

namespace pjtSPEF.Controllers
{
    public class TiposEvaluacionController : Controller
    {
        private readonly GoogleCurrentUserService _currentUser;
        private readonly SpefSheetStore _store;
        private readonly DriveStorageService _storage;

        public TiposEvaluacionController()
        {
            _currentUser = new GoogleCurrentUserService();
            _store = new SpefSheetStore(_currentUser);
            _storage = new DriveStorageService(_currentUser);
        }

        public ActionResult Details(int id)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var tipo = _store.TipoConCadena(id);
            if (tipo == null || tipo.Unidad == null || tipo.Unidad.Curso == null)
                return HttpNotFound();

            tipo.ExamenesBase = _store.ExamenesDeTipo(id);
            return View(tipo);
        }

        [HttpGet]
        public ActionResult Create(int unidadId)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var unidad = _store.UnidadConCadena(unidadId);
            if (unidad == null || unidad.Curso == null)
                return HttpNotFound();

            ViewBag.Unidad = unidad;
            return View(new TipoEvaluacionFormViewModel { UnidadId = unidadId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(TipoEvaluacionFormViewModel model)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var unidad = _store.UnidadConCadena(model.UnidadId);
            if (unidad == null || unidad.Curso == null)
                return HttpNotFound();

            if (!ModelState.IsValid)
            {
                ViewBag.Unidad = unidad;
                return View(model);
            }

            _store.AgregarTipo(new TipoEvaluacion
            {
                UnidadId = unidad.Id,
                Nombre = model.Nombre.Trim(),
                FechaCreacion = DateTime.UtcNow
            });

            return RedirectToAction("Details", "Unidades", new { id = unidad.Id });
        }

        [HttpGet]
        public ActionResult Edit(int id)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var tipo = _store.TipoConCadena(id);
            if (tipo == null || tipo.Unidad == null || tipo.Unidad.Curso == null)
                return HttpNotFound();

            ViewBag.Unidad = tipo.Unidad;
            return View(new TipoEvaluacionFormViewModel { Id = tipo.Id, UnidadId = tipo.UnidadId, Nombre = tipo.Nombre });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(int id, TipoEvaluacionFormViewModel model)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var tipo = _store.TipoConCadena(id);
            if (tipo == null || tipo.Unidad == null || tipo.Unidad.Curso == null)
                return HttpNotFound();

            if (!ModelState.IsValid)
            {
                model.Id = id;
                model.UnidadId = tipo.UnidadId;
                ViewBag.Unidad = tipo.Unidad;
                return View(model);
            }

            tipo.Nombre = model.Nombre.Trim();
            _store.ActualizarTipo(tipo);

            return RedirectToAction("Details", "Unidades", new { id = tipo.UnidadId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var tipo = _store.Tipo(id);
            if (tipo == null)
                return HttpNotFound();

            var unidadId = tipo.UnidadId;
            var archivos = _store.EliminarTipo(id);
            foreach (var archivo in archivos)
                _storage.Eliminar(archivo);

            return RedirectToAction("Details", "Unidades", new { id = unidadId });
        }
    }
}
