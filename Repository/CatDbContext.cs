using Microsoft.EntityFrameworkCore;
using Mewgenics.SaveFileViewer.Models;

namespace Mewgenics.SaveFileViewer.Data {
    public class CatDbContext : DbContext {
        public CatDbContext(DbContextOptions<CatDbContext> options)
            : base(options) {
        }

        public DbSet<CatEntity> Cats { get; set; }
        public DbSet<FileEntity> Files { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder) {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<CatEntity>(entity => {
                entity.ToTable("cats");
                entity.HasKey(e => e.Key);
                entity.Property(e => e.Key).HasColumnName("key");
                entity.Property(e => e.Data).HasColumnName("data");
            });

            modelBuilder.Entity<FileEntity>(entity =>
            {
                entity.ToTable("files");
                entity.HasKey(e => e.Key);
                entity.Property(e => e.Key).HasColumnName("key");
                entity.Property(e => e.Data).HasColumnName("data");
            });
        }
    }
}