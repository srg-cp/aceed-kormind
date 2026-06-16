using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using pjtSPEF.Models.Entities;
using pjtSPEF.Services;

namespace pjtSPEF.Data
{
    // Reemplaza a SpefDbContext (EF6/SQL). Persiste toda la jerarquía y notas del docente
    // en su Spreadsheet de Google (una pestaña por entidad). Como Sheets no tiene lazy-load,
    // transacciones ni cascadas, la navegación se puebla a mano y las cascadas se resuelven aquí.
    public class SpefSheetStore
    {
        private static readonly PeriodoMap MapPeriodos = new PeriodoMap();
        private static readonly CursoMap MapCursos = new CursoMap();
        private static readonly UnidadMap MapUnidades = new UnidadMap();
        private static readonly TipoEvaluacionMap MapTipos = new TipoEvaluacionMap();
        private static readonly ExamenBaseMap MapExamenes = new ExamenBaseMap();
        private static readonly PreguntaClaveMap MapPreguntas = new PreguntaClaveMap();
        private static readonly EvaluacionEstudianteMap MapEvaluaciones = new EvaluacionEstudianteMap();
        private static readonly RespuestaEstudianteMap MapRespuestas = new RespuestaEstudianteMap();

        // Orden en que se crean las pestañas del libro nuevo (ver CrearLibro).
        public static readonly IList<TabDef> Esquema = new ISheetMap[]
        {
            MapPeriodos, MapCursos, MapUnidades, MapTipos,
            MapExamenes, MapPreguntas, MapEvaluaciones, MapRespuestas
        }.Select(m => new TabDef(m.Tab, m.Cabeceras)).ToList();

        private readonly GoogleSheetsClient _sheets;
        private readonly GoogleCurrentUserService _currentUser;
        private readonly LocalUserStore _users;
        private string _libroId;

        public SpefSheetStore(GoogleCurrentUserService currentUser)
        {
            _currentUser = currentUser;
            _sheets = new GoogleSheetsClient(currentUser);
            _users = new LocalUserStore();
        }

        // Resuelve (creando la primera vez) el Spreadsheet del docente y cachea su Id por instancia.
        private string LibroId()
        {
            if (_libroId != null)
                return _libroId;

            var usuario = _currentUser.ObtenerUsuarioActual();
            if (usuario == null)
                throw new DriveNoAutorizadoException("No hay una sesión activa.");

            if (string.IsNullOrEmpty(usuario.SpreadsheetId))
            {
                usuario.SpreadsheetId = _sheets.CrearLibro("ACEED - " + usuario.Email, Esquema);
                _users.Guardar(usuario);
            }

            _libroId = usuario.SpreadsheetId;
            return _libroId;
        }

        // ===================== Helpers genéricos =====================

        private List<RowEnt<T>> LeerCon<T>(SheetMap<T> map)
        {
            var filas = _sheets.LeerDatos(LibroId(), map.Tab);
            var res = new List<RowEnt<T>>();
            for (var i = 0; i < filas.Count; i++)
            {
                var fila = filas[i];
                if (fila == null || fila.All(c => c == null || string.IsNullOrEmpty(c.ToString())))
                    continue; // fila en blanco
                // La fila de datos i (0-based) está en la fila real i+2 (la 1 es la cabecera).
                res.Add(new RowEnt<T>(i + 2, map.FromRow(fila)));
            }
            return res;
        }

        private List<T> Todos<T>(SheetMap<T> map)
        {
            return LeerCon(map).Select(x => x.Entidad).ToList();
        }

        private T Buscar<T>(SheetMap<T> map, int id) where T : class
        {
            return LeerCon(map).Where(x => map.GetId(x.Entidad) == id).Select(x => x.Entidad).FirstOrDefault();
        }

        private T Agregar<T>(SheetMap<T> map, T entidad)
        {
            var max = LeerCon(map).Select(x => map.GetId(x.Entidad)).DefaultIfEmpty(0).Max();
            map.SetId(entidad, max + 1);
            _sheets.Anexar(LibroId(), map.Tab, map.ToRow(entidad));
            return entidad;
        }

        private void AgregarVarias<T>(SheetMap<T> map, IList<T> entidades)
        {
            if (entidades.Count == 0)
                return;
            var max = LeerCon(map).Select(x => map.GetId(x.Entidad)).DefaultIfEmpty(0).Max();
            foreach (var e in entidades)
                map.SetId(e, ++max);
            _sheets.AnexarVarias(LibroId(), map.Tab, entidades.Select(map.ToRow).ToList());
        }

        private void Actualizar<T>(SheetMap<T> map, T entidad)
        {
            var fila = LeerCon(map).FirstOrDefault(x => map.GetId(x.Entidad) == map.GetId(entidad));
            if (fila == null)
                return;
            _sheets.ActualizarFila(LibroId(), map.Tab, fila.Fila, map.ToRow(entidad));
        }

        // Borra las filas que cumplen el filtro y devuelve las entidades borradas.
        private List<T> EliminarDonde<T>(SheetMap<T> map, Func<T, bool> filtro)
        {
            var coincidencias = LeerCon(map).Where(x => filtro(x.Entidad)).ToList();
            if (coincidencias.Count > 0)
                _sheets.BorrarFilas(LibroId(), map.Tab, coincidencias.Select(x => x.Fila));
            return coincidencias.Select(x => x.Entidad).ToList();
        }

        // ===================== Periodos =====================

        public List<Periodo> Periodos() => Todos(MapPeriodos);
        public Periodo Periodo(int id) => Buscar(MapPeriodos, id);
        public Periodo AgregarPeriodo(Periodo p) => Agregar(MapPeriodos, p);
        public void ActualizarPeriodo(Periodo p) => Actualizar(MapPeriodos, p);

        public bool ExistePeriodo(int anio, TipoPeriodo tipo, int? exceptoId = null)
        {
            return Periodos().Any(p => p.Anio == anio && p.Tipo == tipo &&
                                       (!exceptoId.HasValue || p.Id != exceptoId.Value));
        }

        // ===================== Cursos =====================

        public List<Curso> CursosDePeriodo(int periodoId) =>
            Todos(MapCursos).Where(c => c.PeriodoId == periodoId).OrderBy(c => c.Nombre).ToList();
        public Curso Curso(int id) => Buscar(MapCursos, id);
        public Curso AgregarCurso(Curso c) => Agregar(MapCursos, c);
        public void ActualizarCurso(Curso c) => Actualizar(MapCursos, c);

        public Curso CursoConPeriodo(int id)
        {
            var curso = Curso(id);
            if (curso != null)
                curso.Periodo = Periodo(curso.PeriodoId);
            return curso;
        }

        // ===================== Unidades =====================

        public List<Unidad> UnidadesDeCurso(int cursoId) =>
            Todos(MapUnidades).Where(u => u.CursoId == cursoId).OrderBy(u => u.Numero).ToList();
        public Unidad Unidad(int id) => Buscar(MapUnidades, id);
        public Unidad AgregarUnidad(Unidad u) => Agregar(MapUnidades, u);
        public void ActualizarUnidad(Unidad u) => Actualizar(MapUnidades, u);

        public bool ExisteUnidadNumero(int cursoId, int numero, int? exceptoId = null)
        {
            return Todos(MapUnidades).Any(u => u.CursoId == cursoId && u.Numero == numero &&
                                               (!exceptoId.HasValue || u.Id != exceptoId.Value));
        }

        public Unidad UnidadConCadena(int id)
        {
            var unidad = Unidad(id);
            if (unidad != null)
                unidad.Curso = CursoConPeriodo(unidad.CursoId);
            return unidad;
        }

        // ===================== Tipos de evaluación =====================

        public List<TipoEvaluacion> TiposDeUnidad(int unidadId) =>
            Todos(MapTipos).Where(t => t.UnidadId == unidadId).OrderBy(t => t.Nombre).ToList();
        public TipoEvaluacion Tipo(int id) => Buscar(MapTipos, id);
        public TipoEvaluacion AgregarTipo(TipoEvaluacion t) => Agregar(MapTipos, t);
        public void ActualizarTipo(TipoEvaluacion t) => Actualizar(MapTipos, t);

        public TipoEvaluacion TipoConCadena(int id)
        {
            var tipo = Tipo(id);
            if (tipo != null)
                tipo.Unidad = UnidadConCadena(tipo.UnidadId);
            return tipo;
        }

        // ===================== Exámenes base =====================

        public List<ExamenBase> ExamenesDeTipo(int tipoId) =>
            Todos(MapExamenes).Where(e => e.TipoEvaluacionId == tipoId).ToList();
        public ExamenBase Examen(int id) => Buscar(MapExamenes, id);
        public ExamenBase AgregarExamen(ExamenBase e) => Agregar(MapExamenes, e);
        public void ActualizarExamen(ExamenBase e) => Actualizar(MapExamenes, e);

        public ExamenBase ExamenConCadena(int id)
        {
            var examen = Examen(id);
            if (examen != null)
                examen.TipoEvaluacion = TipoConCadena(examen.TipoEvaluacionId);
            return examen;
        }

        // ===================== Preguntas clave =====================

        public List<PreguntaClave> PreguntasDeExamen(int examenId) =>
            Todos(MapPreguntas).Where(p => p.ExamenBaseId == examenId).OrderBy(p => p.Numero).ToList();

        // Reemplaza por completo la clave de un examen (lo de la grilla es la verdad).
        public void ReemplazarClave(int examenId, IList<PreguntaClave> nuevas)
        {
            EliminarDonde(MapPreguntas, p => p.ExamenBaseId == examenId);
            AgregarVarias(MapPreguntas, nuevas);
        }

        // ===================== Evaluaciones (entregas) =====================

        public List<EvaluacionEstudiante> EvaluacionesDeExamen(int examenId) =>
            Todos(MapEvaluaciones).Where(e => e.ExamenBaseId == examenId).ToList();
        public EvaluacionEstudiante Evaluacion(int id) => Buscar(MapEvaluaciones, id);
        public EvaluacionEstudiante AgregarEvaluacion(EvaluacionEstudiante e) => Agregar(MapEvaluaciones, e);
        public void ActualizarEvaluacion(EvaluacionEstudiante e) => Actualizar(MapEvaluaciones, e);

        public EvaluacionEstudiante EvaluacionConCadena(int id)
        {
            var eval = Evaluacion(id);
            if (eval != null)
            {
                eval.ExamenBase = ExamenConCadena(eval.ExamenBaseId);
                if (eval.ExamenBase != null)
                    eval.ExamenBase.PreguntasClave = PreguntasDeExamen(eval.ExamenBaseId);
                eval.Respuestas = RespuestasDeEvaluacion(id);
            }
            return eval;
        }

        // ===================== Respuestas =====================

        public List<RespuestaEstudiante> RespuestasDeEvaluacion(int evalId) =>
            Todos(MapRespuestas).Where(r => r.EvaluacionEstudianteId == evalId).OrderBy(r => r.Numero).ToList();

        public void ReemplazarRespuestas(int evalId, IList<RespuestaEstudiante> nuevas)
        {
            EliminarDonde(MapRespuestas, r => r.EvaluacionEstudianteId == evalId);
            AgregarVarias(MapRespuestas, nuevas);
        }

        // ===================== Borrado en cascada =====================
        // Cada método devuelve los ArchivoRef (PDFs en Drive) que el llamador debe eliminar
        // del almacenamiento tras borrar las filas. Se borra de las hojas hoja→raíz.

        public List<string> EliminarEvaluacion(int id)
        {
            var evals = EliminarDonde(MapEvaluaciones, e => e.Id == id);
            EliminarDonde(MapRespuestas, r => r.EvaluacionEstudianteId == id);
            return evals.Where(e => !string.IsNullOrEmpty(e.ArchivoRef)).Select(e => e.ArchivoRef).ToList();
        }

        public List<string> EliminarExamen(int id) => EliminarExamenes(new[] { id });

        public List<string> EliminarTipo(int id) => EliminarTipos(new[] { id });

        public List<string> EliminarUnidad(int id) => EliminarUnidades(new[] { id });

        public List<string> EliminarCurso(int id) => EliminarCursos(new[] { id });

        public List<string> EliminarPeriodo(int id)
        {
            var cursoIds = Todos(MapCursos).Where(c => c.PeriodoId == id).Select(c => c.Id);
            var archivos = EliminarCursos(cursoIds);
            EliminarDonde(MapPeriodos, p => p.Id == id);
            return archivos;
        }

        private List<string> EliminarCursos(IEnumerable<int> cursoIds)
        {
            var ids = new HashSet<int>(cursoIds);
            if (ids.Count == 0)
                return new List<string>();
            var unidadIds = Todos(MapUnidades).Where(u => ids.Contains(u.CursoId)).Select(u => u.Id);
            var archivos = EliminarUnidades(unidadIds);
            EliminarDonde(MapCursos, c => ids.Contains(c.Id));
            return archivos;
        }

        private List<string> EliminarUnidades(IEnumerable<int> unidadIds)
        {
            var ids = new HashSet<int>(unidadIds);
            if (ids.Count == 0)
                return new List<string>();
            var tipoIds = Todos(MapTipos).Where(t => ids.Contains(t.UnidadId)).Select(t => t.Id);
            var archivos = EliminarTipos(tipoIds);
            EliminarDonde(MapUnidades, u => ids.Contains(u.Id));
            return archivos;
        }

        private List<string> EliminarTipos(IEnumerable<int> tipoIds)
        {
            var ids = new HashSet<int>(tipoIds);
            if (ids.Count == 0)
                return new List<string>();
            var examenIds = Todos(MapExamenes).Where(e => ids.Contains(e.TipoEvaluacionId)).Select(e => e.Id);
            var archivos = EliminarExamenes(examenIds);
            EliminarDonde(MapTipos, t => ids.Contains(t.Id));
            return archivos;
        }

        private List<string> EliminarExamenes(IEnumerable<int> examenIds)
        {
            var ids = new HashSet<int>(examenIds);
            if (ids.Count == 0)
                return new List<string>();

            var evals = EliminarDonde(MapEvaluaciones, ev => ids.Contains(ev.ExamenBaseId));
            var evalIds = new HashSet<int>(evals.Select(e => e.Id));
            EliminarDonde(MapRespuestas, r => evalIds.Contains(r.EvaluacionEstudianteId));
            EliminarDonde(MapPreguntas, p => ids.Contains(p.ExamenBaseId));
            var examenes = EliminarDonde(MapExamenes, e => ids.Contains(e.Id));

            return examenes.Select(e => e.ArchivoRef)
                .Concat(evals.Select(e => e.ArchivoRef))
                .Where(a => !string.IsNullOrEmpty(a))
                .ToList();
        }

        private sealed class RowEnt<T>
        {
            public int Fila { get; }
            public T Entidad { get; }
            public RowEnt(int fila, T entidad) { Fila = fila; Entidad = entidad; }
        }
    }

    // ===================== Mapeadores fila <-> entidad =====================

    internal interface ISheetMap
    {
        string Tab { get; }
        string[] Cabeceras { get; }
    }

    internal abstract class SheetMap<T> : ISheetMap
    {
        public abstract string Tab { get; }
        public abstract string[] Cabeceras { get; }
        public abstract IList<object> ToRow(T e);
        public abstract T FromRow(IList<object> r);
        public abstract int GetId(T e);
        public abstract void SetId(T e, int id);
    }

    internal sealed class PeriodoMap : SheetMap<Periodo>
    {
        public override string Tab => "Periodos";
        public override string[] Cabeceras => new[] { "id", "anio", "tipo", "fecha_creacion" };
        public override int GetId(Periodo e) => e.Id;
        public override void SetId(Periodo e, int id) => e.Id = id;
        public override IList<object> ToRow(Periodo e) => new List<object>
            { Cel.Num(e.Id), Cel.Num(e.Anio), Cel.Num((int)e.Tipo), Cel.Fecha(e.FechaCreacion) };
        public override Periodo FromRow(IList<object> r) => new Periodo
        {
            Id = Cel.I(r, 0),
            Anio = Cel.I(r, 1),
            Tipo = (TipoPeriodo)(byte)Cel.I(r, 2),
            FechaCreacion = Cel.DT(r, 3)
        };
    }

    internal sealed class CursoMap : SheetMap<Curso>
    {
        public override string Tab => "Cursos";
        public override string[] Cabeceras => new[] { "id", "periodo_id", "nombre", "fecha_creacion" };
        public override int GetId(Curso e) => e.Id;
        public override void SetId(Curso e, int id) => e.Id = id;
        public override IList<object> ToRow(Curso e) => new List<object>
            { Cel.Num(e.Id), Cel.Num(e.PeriodoId), Cel.Txt(e.Nombre), Cel.Fecha(e.FechaCreacion) };
        public override Curso FromRow(IList<object> r) => new Curso
        {
            Id = Cel.I(r, 0),
            PeriodoId = Cel.I(r, 1),
            Nombre = Cel.S(r, 2),
            FechaCreacion = Cel.DT(r, 3)
        };
    }

    internal sealed class UnidadMap : SheetMap<Unidad>
    {
        public override string Tab => "Unidades";
        public override string[] Cabeceras => new[] { "id", "curso_id", "numero", "nombre", "fecha_creacion" };
        public override int GetId(Unidad e) => e.Id;
        public override void SetId(Unidad e, int id) => e.Id = id;
        public override IList<object> ToRow(Unidad e) => new List<object>
            { Cel.Num(e.Id), Cel.Num(e.CursoId), Cel.Num(e.Numero), Cel.Txt(e.Nombre), Cel.Fecha(e.FechaCreacion) };
        public override Unidad FromRow(IList<object> r) => new Unidad
        {
            Id = Cel.I(r, 0),
            CursoId = Cel.I(r, 1),
            Numero = Cel.I(r, 2),
            Nombre = Cel.S(r, 3),
            FechaCreacion = Cel.DT(r, 4)
        };
    }

    internal sealed class TipoEvaluacionMap : SheetMap<TipoEvaluacion>
    {
        public override string Tab => "TiposEvaluacion";
        public override string[] Cabeceras => new[] { "id", "unidad_id", "nombre", "fecha_creacion" };
        public override int GetId(TipoEvaluacion e) => e.Id;
        public override void SetId(TipoEvaluacion e, int id) => e.Id = id;
        public override IList<object> ToRow(TipoEvaluacion e) => new List<object>
            { Cel.Num(e.Id), Cel.Num(e.UnidadId), Cel.Txt(e.Nombre), Cel.Fecha(e.FechaCreacion) };
        public override TipoEvaluacion FromRow(IList<object> r) => new TipoEvaluacion
        {
            Id = Cel.I(r, 0),
            UnidadId = Cel.I(r, 1),
            Nombre = Cel.S(r, 2),
            FechaCreacion = Cel.DT(r, 3)
        };
    }

    internal sealed class ExamenBaseMap : SheetMap<ExamenBase>
    {
        public override string Tab => "ExamenesBase";
        public override string[] Cabeceras => new[]
        {
            "id", "tipo_evaluacion_id", "titulo", "archivo_ref", "archivo_nombre_original",
            "total_paginas", "nota_maxima", "estado", "fecha_creacion", "fecha_modificacion"
        };
        public override int GetId(ExamenBase e) => e.Id;
        public override void SetId(ExamenBase e, int id) => e.Id = id;
        public override IList<object> ToRow(ExamenBase e) => new List<object>
        {
            Cel.Num(e.Id), Cel.Num(e.TipoEvaluacionId), Cel.Txt(e.Titulo), Cel.Txt(e.ArchivoRef),
            Cel.Txt(e.ArchivoNombreOriginal), Cel.NumN(e.TotalPaginas), Cel.Num(e.NotaMaxima),
            Cel.Num((int)e.Estado), Cel.Fecha(e.FechaCreacion), Cel.FechaN(e.FechaModificacion)
        };
        public override ExamenBase FromRow(IList<object> r) => new ExamenBase
        {
            Id = Cel.I(r, 0),
            TipoEvaluacionId = Cel.I(r, 1),
            Titulo = Cel.S(r, 2),
            ArchivoRef = Cel.S(r, 3),
            ArchivoNombreOriginal = Cel.S(r, 4),
            TotalPaginas = Cel.IN(r, 5),
            NotaMaxima = Cel.D(r, 6),
            Estado = (EstadoExamen)(byte)Cel.I(r, 7),
            FechaCreacion = Cel.DT(r, 8),
            FechaModificacion = Cel.DTN(r, 9)
        };
    }

    internal sealed class PreguntaClaveMap : SheetMap<PreguntaClave>
    {
        public override string Tab => "PreguntasClave";
        public override string[] Cabeceras => new[]
            { "id", "examen_base_id", "numero", "enunciado", "respuesta_esperada", "puntaje", "fecha_creacion" };
        public override int GetId(PreguntaClave e) => e.Id;
        public override void SetId(PreguntaClave e, int id) => e.Id = id;
        public override IList<object> ToRow(PreguntaClave e) => new List<object>
        {
            Cel.Num(e.Id), Cel.Num(e.ExamenBaseId), Cel.Num(e.Numero), Cel.Txt(e.Enunciado),
            Cel.Txt(e.RespuestaEsperada), Cel.Num(e.Puntaje), Cel.Fecha(e.FechaCreacion)
        };
        public override PreguntaClave FromRow(IList<object> r) => new PreguntaClave
        {
            Id = Cel.I(r, 0),
            ExamenBaseId = Cel.I(r, 1),
            Numero = Cel.I(r, 2),
            Enunciado = Cel.S(r, 3),
            RespuestaEsperada = Cel.S(r, 4),
            Puntaje = Cel.D(r, 5),
            FechaCreacion = Cel.DT(r, 6)
        };
    }

    internal sealed class EvaluacionEstudianteMap : SheetMap<EvaluacionEstudiante>
    {
        public override string Tab => "Evaluaciones";
        public override string[] Cabeceras => new[]
        {
            "id", "examen_base_id", "nombre_estudiante", "archivo_ref", "archivo_nombre_original",
            "total_paginas", "nota_total", "estado", "mensaje_error", "fecha_creacion", "fecha_calificacion"
        };
        public override int GetId(EvaluacionEstudiante e) => e.Id;
        public override void SetId(EvaluacionEstudiante e, int id) => e.Id = id;
        public override IList<object> ToRow(EvaluacionEstudiante e) => new List<object>
        {
            Cel.Num(e.Id), Cel.Num(e.ExamenBaseId), Cel.Txt(e.NombreEstudiante), Cel.Txt(e.ArchivoRef),
            Cel.Txt(e.ArchivoNombreOriginal), Cel.NumN(e.TotalPaginas), Cel.NumN(e.NotaTotal),
            Cel.Num((int)e.Estado), Cel.Txt(e.MensajeError), Cel.Fecha(e.FechaCreacion), Cel.FechaN(e.FechaCalificacion)
        };
        public override EvaluacionEstudiante FromRow(IList<object> r) => new EvaluacionEstudiante
        {
            Id = Cel.I(r, 0),
            ExamenBaseId = Cel.I(r, 1),
            NombreEstudiante = Cel.S(r, 2),
            ArchivoRef = Cel.S(r, 3),
            ArchivoNombreOriginal = Cel.S(r, 4),
            TotalPaginas = Cel.IN(r, 5),
            NotaTotal = Cel.DN(r, 6),
            Estado = (EstadoEvaluacion)(byte)Cel.I(r, 7),
            MensajeError = Cel.S(r, 8),
            FechaCreacion = Cel.DT(r, 9),
            FechaCalificacion = Cel.DTN(r, 10)
        };
    }

    internal sealed class RespuestaEstudianteMap : SheetMap<RespuestaEstudiante>
    {
        public override string Tab => "Respuestas";
        public override string[] Cabeceras => new[]
        {
            "id", "evaluacion_estudiante_id", "numero", "enunciado", "respuesta_texto",
            "puntaje_maximo", "puntaje_obtenido", "comentario", "fecha_creacion",
            "pagina", "marca_x", "marca_y", "dudoso"
        };
        public override int GetId(RespuestaEstudiante e) => e.Id;
        public override void SetId(RespuestaEstudiante e, int id) => e.Id = id;
        public override IList<object> ToRow(RespuestaEstudiante e) => new List<object>
        {
            Cel.Num(e.Id), Cel.Num(e.EvaluacionEstudianteId), Cel.Num(e.Numero), Cel.Txt(e.Enunciado),
            Cel.Txt(e.RespuestaTexto), Cel.Num(e.PuntajeMaximo), Cel.Num(e.PuntajeObtenido),
            Cel.Txt(e.Comentario), Cel.Fecha(e.FechaCreacion),
            Cel.NumN(e.Pagina), Cel.NumN(e.MarcaX), Cel.NumN(e.MarcaY), Cel.Bool(e.Dudoso)
        };
        public override RespuestaEstudiante FromRow(IList<object> r) => new RespuestaEstudiante
        {
            Id = Cel.I(r, 0),
            EvaluacionEstudianteId = Cel.I(r, 1),
            Numero = Cel.I(r, 2),
            Enunciado = Cel.S(r, 3),
            RespuestaTexto = Cel.S(r, 4),
            PuntajeMaximo = Cel.D(r, 5),
            PuntajeObtenido = Cel.D(r, 6),
            Comentario = Cel.S(r, 7),
            FechaCreacion = Cel.DT(r, 8),
            Pagina = Cel.IN(r, 9),
            MarcaX = Cel.DN(r, 10),
            MarcaY = Cel.DN(r, 11),
            Dudoso = Cel.B(r, 12)
        };
    }

    // Conversión de celdas <-> texto. Se persiste todo como texto (ValueInputOption RAW) en cultura
    // invariante para que decimales y fechas hagan round-trip igual en cualquier servidor.
    internal static class Cel
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Txt(string v) => v ?? string.Empty;
        public static string Num(int v) => v.ToString(Inv);
        public static string Num(decimal v) => v.ToString(Inv);
        public static string NumN(int? v) => v.HasValue ? v.Value.ToString(Inv) : string.Empty;
        public static string NumN(decimal? v) => v.HasValue ? v.Value.ToString(Inv) : string.Empty;
        public static string Fecha(DateTime v) => v.ToString("o", Inv);
        public static string FechaN(DateTime? v) => v.HasValue ? v.Value.ToString("o", Inv) : string.Empty;
        public static string Bool(bool v) => v ? "1" : "0";

        public static string S(IList<object> r, int i) =>
            (r != null && r.Count > i && r[i] != null) ? r[i].ToString() : null;

        public static int I(IList<object> r, int i) =>
            int.TryParse(S(r, i), NumberStyles.Any, Inv, out var v) ? v : 0;

        public static int? IN(IList<object> r, int i)
        {
            var s = S(r, i);
            return !string.IsNullOrEmpty(s) && int.TryParse(s, NumberStyles.Any, Inv, out var v) ? v : (int?)null;
        }

        public static decimal D(IList<object> r, int i) =>
            decimal.TryParse(S(r, i), NumberStyles.Any, Inv, out var v) ? v : 0m;

        public static decimal? DN(IList<object> r, int i)
        {
            var s = S(r, i);
            return !string.IsNullOrEmpty(s) && decimal.TryParse(s, NumberStyles.Any, Inv, out var v) ? v : (decimal?)null;
        }

        public static bool B(IList<object> r, int i)
        {
            var s = S(r, i);
            return s == "1" || string.Equals(s, "true", StringComparison.OrdinalIgnoreCase);
        }

        public static DateTime DT(IList<object> r, int i) =>
            DateTime.TryParse(S(r, i), Inv, DateTimeStyles.RoundtripKind, out var v) ? v : DateTime.MinValue;

        public static DateTime? DTN(IList<object> r, int i)
        {
            var s = S(r, i);
            return !string.IsNullOrEmpty(s) && DateTime.TryParse(s, Inv, DateTimeStyles.RoundtripKind, out var v)
                ? v : (DateTime?)null;
        }
    }
}
