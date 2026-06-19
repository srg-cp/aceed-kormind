using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace pjtSPEF.Services
{
    // Almacena los PDFs en el Google Drive del docente autenticado (scope drive.file).
    // El fileRef que devuelve/recibe es el Id del archivo en Drive.
    public class DriveStorageService
    {
        private const string CarpetaRaiz = "ACEED";
        private const string MimeCarpeta = "application/vnd.google-apps.folder";
        private const string MimePdf = "application/pdf";

        // Prefijos de las URLs web de Drive para abrir carpetas/archivos en el navegador.
        private const string UrlCarpeta = "https://drive.google.com/drive/folders/";
        private const string UrlArchivo = "https://drive.google.com/file/d/";

        // El DriveService (y su token) se reutiliza por usuario para no rehacer el refresh
        // del access token en cada subida. La clave incluye el refresh token: si el usuario
        // se reautentica con uno nuevo, se crea un cliente nuevo y el viejo queda descartado.
        private static readonly ConcurrentDictionary<string, DriveService> _driveCache =
            new ConcurrentDictionary<string, DriveService>();

        // Mapa ruta-completa -> Id de carpeta en Drive, para no consultar/crear las carpetas
        // (ACEED/Curso/Unidad/Tipo/...) en cada subida. La clave lleva el Id de usuario porque
        // las carpetas son del Drive de cada docente.
        private static readonly ConcurrentDictionary<string, string> _carpetaCache =
            new ConcurrentDictionary<string, string>();

        private readonly GoogleCurrentUserService _currentUser;
        private readonly string _clientId;
        private readonly string _clientSecret;

        public DriveStorageService(GoogleCurrentUserService currentUser)
        {
            _currentUser = currentUser;
            _clientId = ConfigurationManager.AppSettings["GoogleAuth:ClientId"];
            _clientSecret = ConfigurationManager.AppSettings["GoogleAuth:ClientSecret"];
        }

        public string Guardar(Stream contenido, string categoria, string extension)
        {
            int usuarioId;
            var drive = CrearDrive(out usuarioId);
            if (contenido.CanSeek)
                contenido.Position = 0;

            var carpeta = AsegurarRuta(drive, usuarioId, categoria);

            var meta = new DriveFile
            {
                Name = Guid.NewGuid().ToString("N") + extension,
                Parents = new List<string> { carpeta }
            };

            var upload = drive.Files.Create(meta, contenido, MimePdf);
            upload.Fields = "id";
            var progreso = upload.Upload();
            if (progreso.Status != UploadStatus.Completed)
                throw progreso.Exception ?? new Exception("No se pudo subir el archivo a Google Drive.");

            return upload.ResponseBody.Id;
        }

        // Mueve un archivo ya subido a otra carpeta de la jerarquía (p. ej. entregas -> calificados).
        // El fileRef (Id de Drive) no cambia, así que el llamador no necesita re-persistirlo.
        public void Mover(string fileRef, string categoriaDestino)
        {
            if (string.IsNullOrEmpty(fileRef))
                return;

            int usuarioId;
            var drive = CrearDrive(out usuarioId);
            var destino = AsegurarRuta(drive, usuarioId, categoriaDestino);

            var getArchivo = drive.Files.Get(fileRef);
            getArchivo.Fields = "parents";
            var parents = getArchivo.Execute().Parents;

            // Ya está en la carpeta destino: nada que mover.
            if (parents != null && parents.Contains(destino))
                return;

            var update = drive.Files.Update(new DriveFile(), fileRef);
            update.AddParents = destino;
            if (parents != null && parents.Count > 0)
                update.RemoveParents = string.Join(",", parents);
            update.Fields = "id";
            update.Execute();
        }

        // Reemplaza el contenido (bytes) de un archivo ya subido, conservando su Id (fileRef).
        // Se usa para guardar la versión sellada con la nota sobre el mismo archivo de la entrega.
        public void ReemplazarContenido(string fileRef, Stream contenido)
        {
            if (string.IsNullOrEmpty(fileRef))
                return;

            var drive = CrearDrive();
            if (contenido.CanSeek)
                contenido.Position = 0;

            var update = drive.Files.Update(new DriveFile(), fileRef, contenido, MimePdf);
            update.Fields = "id";
            var progreso = update.Upload();
            if (progreso.Status != UploadStatus.Completed)
                throw progreso.Exception ?? new Exception("No se pudo actualizar el archivo en Google Drive.");
        }

        // URL web para abrir en el navegador la carpeta de una categoría (p. ej. la del examen,
        // que contiene el PDF base y las subcarpetas entregas/ y calificados/). La resuelve
        // creándola si hiciera falta, igual que al subir.
        public string EnlaceCarpeta(string categoria)
        {
            int usuarioId;
            var drive = CrearDrive(out usuarioId);
            var carpetaId = AsegurarRuta(drive, usuarioId, categoria);
            return UrlCarpeta + carpetaId;
        }

        // URL web para abrir en el navegador un archivo ya subido (su fileRef es el Id de Drive).
        // Valida que haya sesión/credenciales de Drive antes de entregar el enlace.
        public string EnlaceArchivo(string fileRef)
        {
            if (string.IsNullOrEmpty(fileRef))
                return null;
            CrearDrive();
            return UrlArchivo + fileRef + "/view";
        }

        public Stream Abrir(string fileRef)
        {
            var drive = CrearDrive();
            var ms = new MemoryStream();
            drive.Files.Get(fileRef).Download(ms);
            ms.Position = 0;
            return ms;
        }

        public void Eliminar(string fileRef)
        {
            if (string.IsNullOrEmpty(fileRef))
                return;

            try
            {
                var drive = CrearDrive();
                drive.Files.Delete(fileRef).Execute();
            }
            catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
            {
                // El archivo ya no está en Drive; nada que eliminar.
            }
        }

        // Renombra la carpeta que contiene el archivo dado (arrastra su contenido: el PDF base
        // y la subcarpeta 'entregas'). Solo renombra si la carpeta tiene el nombre esperado, para
        // no tocar por error carpetas compartidas. Útil cuando cambia el título del examen.
        public void RenombrarCarpetaContenedora(string fileRef, string nombreActualEsperado, string nuevoNombre)
        {
            if (string.IsNullOrEmpty(fileRef) || nombreActualEsperado == nuevoNombre)
                return;

            var drive = CrearDrive();

            var getArchivo = drive.Files.Get(fileRef);
            getArchivo.Fields = "parents";
            var parents = getArchivo.Execute().Parents;
            if (parents == null || parents.Count == 0)
                return;
            var carpetaId = parents[0];

            var getCarpeta = drive.Files.Get(carpetaId);
            getCarpeta.Fields = "name";
            if (!string.Equals(getCarpeta.Execute().Name, nombreActualEsperado, StringComparison.Ordinal))
                return; // No es la carpeta del examen (p. ej. una estructura antigua): no tocar.

            var update = drive.Files.Update(new DriveFile { Name = nuevoNombre }, carpetaId);
            update.Fields = "id";
            update.Execute();
        }

        private DriveService CrearDrive()
        {
            int usuarioId;
            return CrearDrive(out usuarioId);
        }

        private DriveService CrearDrive(out int usuarioId)
        {
            if (string.IsNullOrWhiteSpace(_clientId) || string.IsNullOrWhiteSpace(_clientSecret))
                throw new DriveNoAutorizadoException(
                    "Faltan las credenciales de Google (GoogleAuth:ClientId / ClientSecret) en Secrets.config.");

            var usuario = _currentUser.ObtenerUsuarioActual();
            if (usuario == null)
                throw new DriveNoAutorizadoException("No hay una sesión activa para acceder a Google Drive.");

            var refreshToken = TokenProtector.Desproteger(usuario.RefreshToken);
            if (string.IsNullOrEmpty(refreshToken))
                throw new DriveNoAutorizadoException(
                    "Tu cuenta no tiene autorización de Google Drive. Cierra sesión y vuelve a entrar con Google para reconectarla.");

            usuarioId = usuario.Id;
            // Reutiliza el cliente (y su access token vigente) mientras no cambie el refresh token.
            var cacheKey = usuario.Id + "|" + refreshToken;
            return _driveCache.GetOrAdd(cacheKey, _ => ConstruirDrive(usuario.Id, refreshToken));
        }

        private DriveService ConstruirDrive(int usuarioId, string refreshToken)
        {
            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets { ClientId = _clientId, ClientSecret = _clientSecret },
                Scopes = new[] { DriveService.Scope.DriveFile }
            });

            // El access token se obtiene/renueva solo a partir del refresh token en la primera llamada.
            var credential = new UserCredential(flow, usuarioId.ToString(), new TokenResponse
            {
                RefreshToken = refreshToken
            });

            return new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = CarpetaRaiz
            });
        }

        // Resuelve (creando si hace falta) la cadena de carpetas ACEED/<categoria> y devuelve
        // el Id de la carpeta hoja. Cachea cada nivel por usuario para no reconsultarlos.
        private string AsegurarRuta(DriveService drive, int usuarioId, string categoria)
        {
            var segmentos = new List<string> { CarpetaRaiz };
            segmentos.AddRange((categoria ?? string.Empty).Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries));

            string parentId = null;
            var acumulada = new StringBuilder();
            foreach (var segmento in segmentos)
            {
                acumulada.Append('/').Append(segmento);
                var padre = parentId; // copia local para el closure del GetOrAdd
                var nombre = segmento;
                var cacheKey = usuarioId + "|" + acumulada;
                parentId = _carpetaCache.GetOrAdd(cacheKey, _ => AsegurarCarpeta(drive, nombre, padre));
            }
            return parentId;
        }

        // Busca una carpeta por nombre (opcionalmente dentro de un padre) y la crea si no existe.
        // Con scope drive.file solo se ven las carpetas que esta app creó, así que no hay colisión
        // con otras carpetas del Drive del docente.
        private static string AsegurarCarpeta(DriveService drive, string nombre, string parentId)
        {
            var nombreEscapado = nombre.Replace("\\", "\\\\").Replace("'", "\\'");
            var q = string.Format("name = '{0}' and mimeType = '{1}' and trashed = false", nombreEscapado, MimeCarpeta);
            if (parentId != null)
                q += string.Format(" and '{0}' in parents", parentId);

            var list = drive.Files.List();
            list.Q = q;
            list.Fields = "files(id)";
            list.PageSize = 1;
            var existente = list.Execute().Files.FirstOrDefault();
            if (existente != null)
                return existente.Id;

            var meta = new DriveFile { Name = nombre, MimeType = MimeCarpeta };
            if (parentId != null)
                meta.Parents = new List<string> { parentId };

            var create = drive.Files.Create(meta);
            create.Fields = "id";
            return create.Execute().Id;
        }
    }
}
