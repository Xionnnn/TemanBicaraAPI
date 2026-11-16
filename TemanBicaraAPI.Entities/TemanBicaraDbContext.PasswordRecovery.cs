using Microsoft.EntityFrameworkCore;

namespace TemanBicaraAPI.Entities;

public partial class TemanBicaraDbContext
{
    public DbSet<PasswordResetChallenge> PasswordResetChallenges => Set<PasswordResetChallenge>();

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PasswordResetChallenge>(entity =>
        {
            entity.ToTable("password_reset_challenges", "public", table =>
            {
                table.HasCheckConstraint("password_reset_challenges_attempts_check", "attempts BETWEEN 0 AND 5");
                table.HasCheckConstraint("password_reset_challenges_stage_check",
                    "(verified_at IS NULL AND code_hash IS NOT NULL AND reset_token_hash IS NULL AND reset_expires_at IS NULL) OR " +
                    "(verified_at IS NOT NULL AND code_hash IS NULL AND reset_token_hash IS NOT NULL AND reset_expires_at IS NOT NULL)");
            });
            entity.HasKey(value => value.UserId);
            entity.HasIndex(value => value.Id).IsUnique();
            entity.HasIndex(value => value.ResetTokenHash).IsUnique();
            entity.Property(value => value.UserId).HasColumnName("user_id");
            entity.Property(value => value.Id).HasColumnName("id");
            entity.Property(value => value.CodeHash).HasColumnName("code_hash").HasMaxLength(64);
            entity.Property(value => value.CodeExpiresAt).HasColumnName("code_expires_at");
            entity.Property(value => value.SentAt).HasColumnName("sent_at");
            entity.Property(value => value.Attempts).HasColumnName("attempts").HasDefaultValue(0);
            entity.Property(value => value.PasswordStamp).HasColumnName("password_stamp").HasMaxLength(64);
            entity.Property(value => value.VerifiedAt).HasColumnName("verified_at");
            entity.Property(value => value.ResetTokenHash).HasColumnName("reset_token_hash").HasMaxLength(64);
            entity.Property(value => value.ResetExpiresAt).HasColumnName("reset_expires_at");
            entity.HasOne<User>().WithMany().HasForeignKey(value => value.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
