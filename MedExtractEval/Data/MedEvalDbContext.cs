using MedExtractEval.Shared.Model;
using Microsoft.EntityFrameworkCore;

namespace MedExtractEval.Data
{
    public class MedEvalDbContext(DbContextOptions<MedEvalDbContext> options) : DbContext(options)
    {
        public DbSet<CaseItem> Cases { get; set; } = null!;

        public DbSet<Rater> Raters { get; set; } = null!;

        public DbSet<Annotation> Annotations { get; set; } = null!;

        public DbSet<ModelConfig> ModelConfigs { get; set; } = null!;

        public DbSet<Experiment> Experiments { get; set; } = null!;

        public DbSet<ModelExtraction> ModelExtractions { get; set; } = null!;

        public DbSet<CaseAssignment> CaseAssignments { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder b)
        {
            base.OnModelCreating(b);

            b.Entity<Rater>()
                .HasIndex(x => x.LoginName)
                .IsUnique();

            b.Entity<Annotation>()
                .HasOne(a => a.CaseItem)
                .WithMany()
                .HasForeignKey(a => a.CaseId)
                .OnDelete(DeleteBehavior.Restrict);

            b.Entity<Annotation>()
                .HasOne(a => a.Rater)
                .WithMany()
                .HasForeignKey(a => a.RaterId)
                .OnDelete(DeleteBehavior.Restrict);

            // 需要你在 Annotation 上有 Round 字段
            b.Entity<Annotation>()
                .HasIndex(a => new { a.CaseId, a.RaterId, a.Round })
                .IsUnique();

            b.Entity<CaseAssignment>()
             .HasIndex(x => new { x.CaseId, x.Round })
             .IsUnique()
             .HasFilter("[Status] = 'Assigned'");
        }
    }
}
