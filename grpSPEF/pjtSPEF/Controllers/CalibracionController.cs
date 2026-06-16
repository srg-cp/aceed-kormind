using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Mvc;
using pjtSPEF.Data;
using pjtSPEF.Models.Entities;
using pjtSPEF.Models.ViewModels;
using pjtSPEF.Services;

namespace pjtSPEF.Controllers
{
    public class CalibracionController : Controller
    {
        private readonly GoogleCurrentUserService _currentUser;
        private readonly SpefSheetStore _store;
        private readonly DriveStorageService _storage;
        private readonly GeminiExtractorClaveService _extractor;

        public CalibracionController()
        {
            _currentUser = new GoogleCurrentUserService();
            _store = new SpefSheetStore(_currentUser);
            _storage = new DriveStorageService(_currentUser);
            _extractor = new GeminiExtractorClaveService();
        }

        [HttpGet]
        public ActionResult Calibrar(int id)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var examen = CargarExamen(id);
            if (examen == null)
                return HttpNotFound();

            var model = new CalibracionViewModel
            {
                ExamenBaseId = examen.Id,
                Preguntas = AGrilla(examen.PreguntasClave)
            };

            ViewBag.Examen = examen;
            return View(model);
        }

        // Llama a Gemini con el PDF del examen y muestra la clave extraída
        // en la grilla para que el docente la revise antes de guardar.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Extraer(int id)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var examen = CargarExamen(id);
            if (examen == null)
                return HttpNotFound();

            if (string.IsNullOrEmpty(examen.ArchivoRef))
            {
                TempData["Error"] = "El examen no tiene PDF; súbelo antes de extraer la clave.";
                return RedirectToAction("Calibrar", new { id });
            }

            ResultadoExtraccionClave resultado;
            try
            {
                using (var pdf = _storage.Abrir(examen.ArchivoRef))
                {
                    resultado = await _extractor.ExtraerClaveAsync(pdf, examen.NotaMaxima);
                }
            }
            catch (DriveNoAutorizadoException ex)
            {
                TempData["Error"] = ex.Message;
                return RedirectToAction("Calibrar", new { id });
            }

            var model = new CalibracionViewModel { ExamenBaseId = examen.Id };
            if (!resultado.Exito)
            {
                TempData["Error"] = resultado.Error;
                // Conservar lo que ya hubiera guardado para no perder trabajo del docente.
                model.Preguntas = AGrilla(examen.PreguntasClave);
            }
            else
            {
                TempData["Exito"] = string.Format(
                    "Gemini extrajo {0} pregunta(s). Revisa enunciados, respuestas y puntajes antes de guardar.",
                    resultado.Preguntas.Count);
                model.Preguntas = resultado.Preguntas
                    .Select(p => new PreguntaClaveFormViewModel
                    {
                        Numero = p.Numero,
                        Enunciado = p.Enunciado,
                        RespuestaEsperada = p.RespuestaEsperada,
                        Puntaje = p.Puntaje
                    })
                    .ToList();
            }

            ViewBag.Examen = examen;
            return View("Calibrar", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Guardar(CalibracionViewModel model)
        {
            if (_currentUser.ObtenerUsuarioActual() == null)
                return RedirectToAction("Login", "Account");

            var examen = CargarExamen(model.ExamenBaseId);
            if (examen == null)
                return HttpNotFound();

            var preguntas = (model.Preguntas ?? new List<PreguntaClaveFormViewModel>())
                .Where(p => p != null)
                .ToList();

            if (preguntas.Count == 0)
                ModelState.AddModelError("", "Agrega al menos una pregunta antes de guardar la clave.");

            if (ModelState.IsValid)
            {
                var suma = preguntas.Sum(p => p.Puntaje.Value);
                if (suma != examen.NotaMaxima)
                    ModelState.AddModelError("", string.Format(
                        "Los puntajes suman {0:0.##} pero la nota máxima del examen es {1:0.##}. Ajusta los puntajes (o la nota máxima en Editar).",
                        suma, examen.NotaMaxima));
            }

            if (!ModelState.IsValid)
            {
                for (var i = 0; i < preguntas.Count; i++)
                    preguntas[i].Numero = i + 1;
                model.Preguntas = preguntas;
                ViewBag.Examen = examen;
                return View("Calibrar", model);
            }

            // La clave se reemplaza completa: lo que está en la grilla es la verdad.
            var nuevas = new List<PreguntaClave>();
            for (var i = 0; i < preguntas.Count; i++)
            {
                nuevas.Add(new PreguntaClave
                {
                    ExamenBaseId = examen.Id,
                    Numero = i + 1,
                    Enunciado = preguntas[i].Enunciado.Trim(),
                    RespuestaEsperada = (preguntas[i].RespuestaEsperada ?? string.Empty).Trim(),
                    Puntaje = preguntas[i].Puntaje.Value,
                    FechaCreacion = DateTime.UtcNow
                });
            }
            _store.ReemplazarClave(examen.Id, nuevas);

            if (examen.Estado == EstadoExamen.Borrador)
                examen.Estado = EstadoExamen.Calibrado;
            examen.FechaModificacion = DateTime.UtcNow;
            _store.ActualizarExamen(examen);

            TempData["Exito"] = "Clave guardada. El examen quedó calibrado.";
            return RedirectToAction("Details", "ExamenesBase", new { id = examen.Id });
        }

        // Carga el examen con su cadena (para el breadcrumb) y su clave actual.
        private ExamenBase CargarExamen(int id)
        {
            var examen = _store.ExamenConCadena(id);
            if (examen == null || examen.TipoEvaluacion == null
                || examen.TipoEvaluacion.Unidad == null || examen.TipoEvaluacion.Unidad.Curso == null)
                return null;
            examen.PreguntasClave = _store.PreguntasDeExamen(id);
            return examen;
        }

        private static List<PreguntaClaveFormViewModel> AGrilla(IEnumerable<PreguntaClave> preguntas)
        {
            return (preguntas ?? Enumerable.Empty<PreguntaClave>())
                .OrderBy(p => p.Numero)
                .Select(p => new PreguntaClaveFormViewModel
                {
                    Numero = p.Numero,
                    Enunciado = p.Enunciado,
                    RespuestaEsperada = p.RespuestaEsperada,
                    Puntaje = p.Puntaje
                })
                .ToList();
        }
    }
}
