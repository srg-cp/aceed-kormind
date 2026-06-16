using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Hosting;
using Newtonsoft.Json;
using pjtSPEF.Models.Entities;

namespace pjtSPEF.Data
{
    // Almacén local de los docentes (autenticación). Reemplaza a la tabla 'usuarios' de SQL:
    // las notas van a Google Sheets, pero el refresh token es sensible y se queda en el servidor,
    // cifrado con DPAPI (ver TokenProtector). Persiste en App_Data/usuarios.json.
    public class LocalUserStore
    {
        // El acceso al archivo se serializa entre hilos del proceso (un solo servidor).
        private static readonly object _lock = new object();
        private readonly string _ruta;

        public LocalUserStore()
        {
            var dir = HostingEnvironment.MapPath("~/App_Data") ?? AppDomain.CurrentDomain.BaseDirectory;
            _ruta = Path.Combine(dir, "usuarios.json");
        }

        public Usuario PorEmail(string email)
        {
            if (string.IsNullOrEmpty(email))
                return null;
            lock (_lock)
                return Cargar().FirstOrDefault(u =>
                    string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));
        }

        public Usuario PorGoogleId(string googleId)
        {
            if (string.IsNullOrEmpty(googleId))
                return null;
            lock (_lock)
                return Cargar().FirstOrDefault(u => u.GoogleId == googleId);
        }

        // Inserta o actualiza por Id (si Id == 0, asigna el siguiente). Coincide por Id, GoogleId o Email.
        public Usuario Guardar(Usuario usuario)
        {
            lock (_lock)
            {
                var lista = Cargar();
                var existente = lista.FirstOrDefault(u =>
                    (usuario.Id != 0 && u.Id == usuario.Id) ||
                    (!string.IsNullOrEmpty(usuario.GoogleId) && u.GoogleId == usuario.GoogleId) ||
                    (!string.IsNullOrEmpty(usuario.Email) &&
                        string.Equals(u.Email, usuario.Email, StringComparison.OrdinalIgnoreCase)));

                if (existente == null)
                {
                    usuario.Id = lista.Count == 0 ? 1 : lista.Max(u => u.Id) + 1;
                    lista.Add(usuario);
                }
                else
                {
                    usuario.Id = existente.Id;
                    lista[lista.IndexOf(existente)] = usuario;
                }

                Persistir(lista);
                return usuario;
            }
        }

        private List<Usuario> Cargar()
        {
            if (!File.Exists(_ruta))
                return new List<Usuario>();

            var json = File.ReadAllText(_ruta);
            if (string.IsNullOrWhiteSpace(json))
                return new List<Usuario>();

            return JsonConvert.DeserializeObject<List<Usuario>>(json) ?? new List<Usuario>();
        }

        private void Persistir(List<Usuario> lista)
        {
            var dir = Path.GetDirectoryName(_ruta);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // Escritura atómica: se escribe a un temporal y se reemplaza, para no corromper
            // el archivo si el proceso muere a media escritura.
            var json = JsonConvert.SerializeObject(lista, Formatting.Indented);
            var tmp = _ruta + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(_ruta))
                File.Replace(tmp, _ruta, null);
            else
                File.Move(tmp, _ruta);
        }
    }
}
