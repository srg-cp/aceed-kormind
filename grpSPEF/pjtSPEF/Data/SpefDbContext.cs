using System.Data.Entity;
using pjtSPEF.Data.Mappings;
using pjtSPEF.Models.Entities;

namespace pjtSPEF.Data
{
    public class SpefDbContext : DbContext
    {
        static SpefDbContext()
        {
            // La BD se crea y evoluciona con los scripts manuales de database/;
            // EF nunca debe intentar crearla ni modificarla.
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

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            modelBuilder.Configurations.Add(new UsuarioMap());
            modelBuilder.Configurations.Add(new CursoMap());
            modelBuilder.Configurations.Add(new UnidadMap());
            modelBuilder.Configurations.Add(new TipoEvaluacionMap());
            modelBuilder.Configurations.Add(new ExamenBaseMap());
        }
    }
}
