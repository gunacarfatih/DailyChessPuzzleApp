
using DailyChessPuzzleApi.Models;
using Microsoft.EntityFrameworkCore;

namespace DailyChessPuzzleApi.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users => Set<User>();
        public DbSet<Puzzle> Puzzles => Set<Puzzle>();
        public DbSet<DailyPuzzle> DailyPuzzles => Set<DailyPuzzle>();
        public DbSet<PuzzleAttempt> PuzzleAttempts => Set<PuzzleAttempt>();

        public DbSet<LeaderboardCacheOutbox> LeaderboardCacheOutboxes
            => Set<LeaderboardCacheOutbox>();

        public DbSet<LeaderboardCacheOutboxDelivery> LeaderboardCacheOutboxDeliveries
            => Set<LeaderboardCacheOutboxDelivery>();

        public DbSet<LeaderboardScore> LeaderboardScores
            => Set<LeaderboardScore>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // USER
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(x => x.Id);

                entity.Property(x => x.Username)
                    .HasMaxLength(50)
                    .IsRequired();

                entity.Property(x => x.FirstName)
                    .HasMaxLength(50)
                    .IsRequired();

                entity.Property(x => x.LastName)
                    .HasMaxLength(50)
                    .IsRequired();

                entity.Property(x => x.Email)
                    .HasMaxLength(254)
                    .IsRequired();

                entity.Property(x => x.PasswordHash)
                    .IsRequired(false);

                entity.Property(x => x.GoogleId)
                    .HasMaxLength(100)
                    .IsRequired(false);

                entity.Property(x => x.CountryCode)
                    .HasMaxLength(2);

                entity.HasIndex(x => x.Username)
                    .IsUnique();

                entity.HasIndex(x => x.Email)
                    .IsUnique();

                entity.HasIndex(x => x.GoogleId)
                    .IsUnique()
                    .HasFilter("\"GoogleId\" IS NOT NULL");
            });

            // PUZZLE
            modelBuilder.Entity<Puzzle>(entity =>
            {
                entity.HasKey(x => x.Id);

                entity.HasIndex(x => x.LichessPuzzleId)
                    .IsUnique();
            });

            // DAILY PUZZLE
            modelBuilder.Entity<DailyPuzzle>(entity =>
            {
                entity.HasKey(x => x.Id);

                entity.HasIndex(x => x.PuzzleDate)
                    .IsUnique();

                entity.HasOne(x => x.Puzzle)
                    .WithMany(x => x.DailyPuzzles)
                    .HasForeignKey(x => x.PuzzleId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // PUZZLE ATTEMPT
            modelBuilder.Entity<PuzzleAttempt>(entity =>
            {
                entity.HasKey(x => x.Id);

                entity.Property(x => x.ClientId)
                    .HasMaxLength(100);

                entity.HasOne(x => x.Puzzle)
                    .WithMany()
                    .HasForeignKey(x => x.PuzzleId);

                entity.HasOne(x => x.User)
                    .WithMany(x => x.PuzzleAttempts)
                    .HasForeignKey(x => x.UserId)
                    .OnDelete(DeleteBehavior.SetNull);

                // Misafir kullanıcıların aynı bulmacayı
                // birden fazla kez çözmesini engeller.
                entity.HasIndex(x => new
                {
                    x.ClientId,
                    x.PuzzleId
                })
                .IsUnique()
                .HasFilter("\"UserId\" IS NULL AND \"ClientId\" IS NOT NULL");

                // Kayıtlı kullanıcıların aynı bulmacayı
                // birden fazla kez çözmesini engeller.
                entity.HasIndex(x => new
                {
                    x.UserId,
                    x.PuzzleId
                })
                .IsUnique()
                .HasFilter("\"UserId\" IS NOT NULL");

                entity.HasIndex(x => x.PlayedOn);

                entity.HasIndex(x => new
                {
                    x.CountryCode,
                    x.PlayedOn
                });
            });

            // LEADERBOARD CACHE OUTBOX
            modelBuilder.Entity<LeaderboardCacheOutbox>(entity =>
            {
                entity.HasKey(x => x.Id);

                entity.Property(x => x.ClientId)
                    .HasMaxLength(105)
                    .IsRequired();

                entity.HasIndex(x => x.PuzzleAttemptId);

                entity.HasIndex(x => x.CreatedAtUtc);

                entity.HasOne<PuzzleAttempt>()
                    .WithMany()
                    .HasForeignKey(x => x.PuzzleAttemptId)
                    .IsRequired(false)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // LEADERBOARD CACHE OUTBOX DELIVERY
            modelBuilder.Entity<LeaderboardCacheOutboxDelivery>(entity =>
            {
                entity.HasKey(x => x.Id);

                entity.Property(x => x.TargetClientId)
                    .HasMaxLength(105)
                    .IsRequired();

                entity.Property(x => x.IsRemoval)
                    .HasDefaultValue(false)
                    .IsRequired();

                entity.Property(x => x.Period)
                    .HasMaxLength(10)
                    .IsRequired();

                entity.Property(x => x.PeriodKey)
                    .HasMaxLength(20)
                    .IsRequired();

                entity.Property(x => x.CountryCode)
                    .HasMaxLength(2);

                entity.Property(x => x.CacheKey)
                    .HasMaxLength(300)
                    .IsRequired();

                entity.Property(x => x.LastError)
                    .HasMaxLength(2000);

                entity.Property(x => x.LockedBy)
                    .HasMaxLength(100);

                entity.HasOne(x => x.Outbox)
                    .WithMany(x => x.Deliveries)
                    .HasForeignKey(x => x.OutboxId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(x => new
                {
                    x.Status,
                    x.NextRetryAtUtc,
                    x.CreatedAtUtc
                });

                entity.HasIndex(x => new
                {
                    x.OutboxId,
                    x.TargetClientId,
                    x.IsRemoval,
                    x.Period,
                    x.PeriodKey
                })
                .IsUnique()
                .HasFilter("\"CountryCode\" IS NULL");

                entity.HasIndex(x => new
                {
                    x.OutboxId,
                    x.TargetClientId,
                    x.IsRemoval,
                    x.Period,
                    x.PeriodKey,
                    x.CountryCode
                })
                .IsUnique()
                .HasFilter("\"CountryCode\" IS NOT NULL");
            });

            // LEADERBOARD SCORE
            modelBuilder.Entity<LeaderboardScore>(entity =>
            {
                entity.HasKey(x => x.Id);

                entity.Property(x => x.ClientId)
                    .HasMaxLength(105)
                    .IsRequired();

                entity.Property(x => x.Period)
                    .HasMaxLength(10)
                    .IsRequired();

                entity.Property(x => x.PeriodKey)
                    .HasMaxLength(20)
                    .IsRequired();

                entity.Property(x => x.CountryCode)
                    .HasMaxLength(2);

                entity.Property(x => x.Revision)
                    .HasDefaultValue(0L)
                    .IsRequired();

                // Global leaderboard benzersizliği.
                entity.HasIndex(x => new
                {
                    x.ClientId,
                    x.Period,
                    x.PeriodKey
                })
                .IsUnique()
                .HasFilter("\"CountryCode\" IS NULL");

                // Ülke leaderboard benzersizliği.
                entity.HasIndex(x => new
                {
                    x.ClientId,
                    x.Period,
                    x.PeriodKey,
                    x.CountryCode
                })
                .IsUnique()
                .HasFilter("\"CountryCode\" IS NOT NULL");

                entity.HasIndex(x => new
                {
                    x.Period,
                    x.PeriodKey,
                    x.CountryCode,
                    x.TotalScore,
                    x.SolvedCount
                });
            });
        }
    }
}
