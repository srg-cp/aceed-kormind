using System;
using System.Collections.Generic;
using System.Data.Entity;
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
        private readonly SpefDbContext _db;
        private readonly GoogleCurrentUserService _currentUser;
        private readonly DriveStorageService _storage;
        private readonly GeminiExtractorClaveService _extractor;

        public CalibracionController()
        {
            _db = new SpefDbContext();
            _currentUser = new GoogleCurrentUserService(_db);
            _storage = new DriveStorageService(_currentUser);
            _extractor = new GeminiExtractorClaveService();
        }

        [HttpGet]
        public ActionResult Calibrar(int id)
        {
            var usuario = _currentUser.ObtenerUsuarioActual();
            if (usuario == null)
                return RedirectToAction("Login", "Account");

            var examen = BuscarExamenDelUsuario(id, usuario.Id);
            if (examen == null)
                return HttpNotFound();

            var model = new CalibracionViewModel
            {
                ExamenBaseId = examen.Id,
                Preguntas = examen.PreguntasClave
                    .OrderBy(p => p.Numero)
                    .Select(p => new PreguntaClaveFormViewModel
                    {
                        Numero = p.Numero,
                        Enunciado = p.Enunciado,
                        RespuestaEsperada = p.RespuestaEsperada,
                        Puntaje = p.Puntaje
                    })
                    .ToList()
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
            var usuario = _currentUser.ObtenerUsuarioActual();
            if (usuario == null)
                return RedirectToAction("Login", "Account");

            var examen = BuscarExamenDelUsuario(id, usuario.Id);
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
                model.Preguntas = examen.PreguntasClave
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
            var usuario = _currentUser.ObtenerUsuarioActual();
            if (usuario == null)
                return RedirectToAction("Login", "Account");

            var examen = BuscarExamenDelUsuario(model.ExamenBaseId, usuario.Id);
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
                // Renumerar para mostrar la grilla consistente tras el round-trip.
                for (var i = 0; i < preguntas.Count; i++)
                    preguntas[i].Numero = i + 1;
                model.Preguntas = preguntas;
                ViewBag.Examen = examen;
                return View("Calibrar", model);
            }

            // La clave se reemplaza completa: lo que está en la grilla es la verdad.
            _db.PreguntasClave.RemoveRange(examen.PreguntasClave);
            for (var i = 0; i < preguntas.Count; i++)
            {
                _db.PreguntasClave.Add(new PreguntaClave
                {
                    ExamenBaseId = examen.Id,
                    Numero = i + 1,
                    Enunciado = preguntas[i].Enunciado.Trim(),
                    RespuestaEsperada = (preguntas[i].RespuestaEsperada ?? string.Empty).Trim(),
                    Puntaje = preguntas[i].Puntaje.Value,
                    FechaCreacion = DateTime.UtcNow
                });
            }

            if (examen.Estado == EstadoExamen.Borrador)
                examen.Estado = EstadoExamen.Calibrado;
            examen.FechaModificacion = DateTime.UtcNow;
            _db.SaveChanges();

            TempData["Exito"] = "Clave guardada. El examen quedó calibrado.";
            return RedirectToAction("Details", "ExamenesBase", new { id = examen.Id });
        }

        private ExamenBase BuscarExamenDelUsuario(int id, int usuarioId)
        {
            return _db.ExamenesBase
                .Include(e => e.TipoEvaluacion.Unidad.Curso)
                .Include(e => e.PreguntasClave)
                .FirstOrDefault(e => e.Id == id && e.TipoEvaluacion.Unidad.Curso.UsuarioId == usuarioId);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _db.Dispose();
            base.Dispose(disposing);
        }
    }
}
