using System.Data.Entity;
using pjtSPEF.Data.Mappings;
using pjtSPEF.Models.Entities;

namespace pjtSPEF.Data
{
    public class SpefDbContext : DbContext
    {
        static SpefDbContext()
        {
            Database.SetInitializer<SpefDbContext>(null);
        }

        public SpefDbContext() : base("name=SpefDb")
        {
        }

        public DbSet<Usuario> Usuarios { get; set; }
        public DbSet<Curso> Cursos { get; set; }
        public DbSet<Unidad> Unidades { get; set; }
        public DbSet<TipoEvaluacion> TiposEvaluacion { get; set; }
        public DbSet<ExamenBase> ExamenesBase { get; set; }
        public DbSet<PreguntaClave> PreguntasClave { get; set; }
        public DbSet<EvaluacionEstudiante> EvaluacionesEstudiante { get; set; }
        public DbSet<RespuestaEstudiante> RespuestasEstudiante { get; set; }

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            modelBuilder.Configurations.Add(new UsuarioMap());
            modelBuilder.Configurations.Add(new CursoMap());
            modelBuilder.Configurations.Add(new UnidadMap());
            modelBuilder.Configurations.Add(new TipoEvaluacionMap());
            modelBuilder.Configurations.Add(new ExamenBaseMap());
            modelBuilder.Configurations.Add(new PreguntaClaveMap());
            modelBuilder.Configurations.Add(new EvaluacionEstudianteMap());
            modelBuilder.Configurations.Add(new RespuestaEstudianteMap());
        }
    }
}
