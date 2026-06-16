using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;

namespace pjtSPEF.Services
{
    // Definición de una pestaña al crear el libro: nombre + cabeceras de la fila 1.
    public class TabDef
    {
        public string Nombre { get; }
        public string[] Cabeceras { get; }

        public TabDef(string nombre, string[] cabeceras)
        {
            Nombre = nombre;
            Cabeceras = cabeceras;
        }
    }

    // Cliente de bajo nivel sobre Google Sheets API v4, en nombre del docente autenticado
    // (scope spreadsheets). Convención de cada pestaña: fila 1 = cabeceras; filas 2..N = datos,
    // una entidad por fila. Espeja el patrón de credencial/caché de DriveStorageService.
    public class GoogleSheetsClient
    {
        // El SheetsService (y su access token) se reutiliza por refresh token del usuario.
        private static readonly ConcurrentDictionary<string, SheetsService> _cache =
            new ConcurrentDictionary<string, SheetsService>();

        // spreadsheetId -> (título de pestaña -> sheetId/gid). El gid es estable mientras exista la pestaña.
        private static readonly ConcurrentDictionary<string, Dictionary<string, int>> _gidCache =
            new ConcurrentDictionary<string, Dictionary<string, int>>();

        private readonly GoogleCurrentUserService _currentUser;
        private readonly string _clientId;
        private readonly string _clientSecret;

        public GoogleSheetsClient(GoogleCurrentUserService currentUser)
        {
            _currentUser = currentUser;
            _clientId = ConfigurationManager.AppSettings["GoogleAuth:ClientId"];
            _clientSecret = ConfigurationManager.AppSettings["GoogleAuth:ClientSecret"];
        }

        // Crea un Spreadsheet nuevo con una pestaña por entidad y sus cabeceras. Devuelve el spreadsheetId.
        public string CrearLibro(string titulo, IList<TabDef> tabs)
        {
            var svc = Crear();
            var ss = new Spreadsheet
            {
                Properties = new SpreadsheetProperties { Title = titulo },
                Sheets = tabs.Select(t => new Sheet
                {
                    Properties = new SheetProperties { Title = t.Nombre }
                }).ToList()
            };
            var creado = svc.Spreadsheets.Create(ss).Execute();

            var data = tabs.Select(t => new ValueRange
            {
                Range = t.Nombre + "!A1",
                Values = new List<IList<object>> { t.Cabeceras.Cast<object>().ToList() }
            }).ToList();
            svc.Spreadsheets.Values.BatchUpdate(
                new BatchUpdateValuesRequest { ValueInputOption = "RAW", Data = data },
                creado.SpreadsheetId).Execute();

            return creado.SpreadsheetId;
        }

        // Devuelve las filas de datos (sin la cabecera). Filas/celdas vacías al final pueden venir recortadas.
        public IList<IList<object>> LeerDatos(string spreadsheetId, string tab)
        {
            var svc = Crear();
            var resp = svc.Spreadsheets.Values.Get(spreadsheetId, tab + "!A2:ZZ").Execute();
            return resp.Values ?? new List<IList<object>>();
        }

        public void Anexar(string spreadsheetId, string tab, IList<object> fila)
        {
            AnexarVarias(spreadsheetId, tab, new List<IList<object>> { fila });
        }

        public void AnexarVarias(string spreadsheetId, string tab, IList<IList<object>> filas)
        {
            if (filas == null || filas.Count == 0)
                return;

            var svc = Crear();
            var req = svc.Spreadsheets.Values.Append(new ValueRange { Values = filas }, spreadsheetId, tab + "!A1");
            req.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.RAW;
            req.InsertDataOption = SpreadsheetsResource.ValuesResource.AppendRequest.InsertDataOptionEnum.INSERTROWS;
            req.Execute();
        }

        // filaSheet es el número de fila real (1-based; los datos empiezan en 2).
        public void ActualizarFila(string spreadsheetId, string tab, int filaSheet, IList<object> fila)
        {
            var svc = Crear();
            var req = svc.Spreadsheets.Values.Update(
                new ValueRange { Values = new List<IList<object>> { fila } },
                spreadsheetId, tab + "!A" + filaSheet);
            req.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
            req.Execute();
        }

        // Borra filas por número de fila real (1-based). Se ordenan descendente para que el borrado
        // de una fila no descuadre los índices de las siguientes dentro del mismo batch.
        public void BorrarFilas(string spreadsheetId, string tab, IEnumerable<int> filasSheet)
        {
            var filas = filasSheet.Distinct().OrderByDescending(f => f).ToList();
            if (filas.Count == 0)
                return;

            var svc = Crear();
            var gid = ObtenerGid(svc, spreadsheetId, tab);
            var requests = filas.Select(f => new Request
            {
                DeleteDimension = new DeleteDimensionRequest
                {
                    Range = new DimensionRange
                    {
                        SheetId = gid,
                        Dimension = "ROWS",
                        StartIndex = f - 1, // 0-based, inclusivo
                        EndIndex = f        // exclusivo
                    }
                }
            }).ToList();

            svc.Spreadsheets.BatchUpdate(
                new BatchUpdateSpreadsheetRequest { Requests = requests }, spreadsheetId).Execute();
        }

        private int ObtenerGid(SheetsService svc, string spreadsheetId, string tab)
        {
            var mapa = _gidCache.GetOrAdd(spreadsheetId, _ =>
            {
                var get = svc.Spreadsheets.Get(spreadsheetId);
                get.Fields = "sheets.properties(sheetId,title)";
                return get.Execute().Sheets.ToDictionary(
                    s => s.Properties.Title, s => s.Properties.SheetId ?? 0);
            });
            return mapa.TryGetValue(tab, out var gid) ? gid : 0;
        }

        private SheetsService Crear()
        {
            if (string.IsNullOrWhiteSpace(_clientId) || string.IsNullOrWhiteSpace(_clientSecret))
                throw new DriveNoAutorizadoException(
                    "Faltan las credenciales de Google (GoogleAuth:ClientId / ClientSecret) en Secrets.config.");

            var usuario = _currentUser.ObtenerUsuarioActual();
            if (usuario == null)
                throw new DriveNoAutorizadoException("No hay una sesión activa para acceder a Google Sheets.");

            var refreshToken = TokenProtector.Desproteger(usuario.RefreshToken);
            if (string.IsNullOrEmpty(refreshToken))
                throw new DriveNoAutorizadoException(
                    "Tu cuenta no tiene autorización de Google. Cierra sesión y vuelve a entrar con Google para reconectarla.");

            var cacheKey = usuario.Id + "|" + refreshToken;
            return _cache.GetOrAdd(cacheKey, _ => Construir(usuario.Id, refreshToken));
        }

        private SheetsService Construir(int usuarioId, string refreshToken)
        {
            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets { ClientId = _clientId, ClientSecret = _clientSecret },
                Scopes = new[] { SheetsService.Scope.Spreadsheets }
            });

            var credential = new UserCredential(flow, usuarioId.ToString(), new TokenResponse
            {
                RefreshToken = refreshToken
            });

            return new SheetsService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "ACEED"
            });
        }
    }
}
