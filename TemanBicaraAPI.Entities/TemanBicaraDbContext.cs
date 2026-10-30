using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace TemanBicaraAPI.Entities;

public partial class TemanBicaraDbContext : DbContext
{
    public TemanBicaraDbContext(DbContextOptions<TemanBicaraDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Asset> Assets { get; set; }

    public virtual DbSet<ClassRoom> ClassRooms { get; set; }

    public virtual DbSet<ClassRoomsStudent> ClassRoomsStudents { get; set; }

    public virtual DbSet<ClassRoomsTeacher> ClassRoomsTeachers { get; set; }

    public virtual DbSet<EventLog> EventLogs { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<Session> Sessions { get; set; }

    public virtual DbSet<User> Users { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasPostgresEnum("auth", "aal_level", new[] { "aal1", "aal2", "aal3" })
            .HasPostgresEnum("auth", "code_challenge_method", new[] { "s256", "plain" })
            .HasPostgresEnum("auth", "factor_status", new[] { "unverified", "verified" })
            .HasPostgresEnum("auth", "factor_type", new[] { "totp", "webauthn", "phone", "recovery_code" })
            .HasPostgresEnum("auth", "oauth_authorization_status", new[] { "pending", "approved", "denied", "expired" })
            .HasPostgresEnum("auth", "oauth_client_type", new[] { "public", "confidential" })
            .HasPostgresEnum("auth", "oauth_registration_type", new[] { "dynamic", "manual" })
            .HasPostgresEnum("auth", "oauth_response_type", new[] { "code" })
            .HasPostgresEnum("auth", "one_time_token_type", new[] { "confirmation_token", "reauthentication_token", "recovery_token", "email_change_token_new", "email_change_token_current", "phone_change_token" })
            .HasPostgresEnum("realtime", "action", new[] { "INSERT", "UPDATE", "DELETE", "TRUNCATE", "ERROR" })
            .HasPostgresEnum("realtime", "equality_op", new[] { "eq", "neq", "lt", "lte", "gt", "gte", "in", "like", "ilike", "is", "match", "imatch", "isdistinct" })
            .HasPostgresEnum("storage", "buckettype", new[] { "STANDARD", "ANALYTICS", "VECTOR" })
            .HasPostgresExtension("extensions", "pg_stat_statements")
            .HasPostgresExtension("extensions", "pgcrypto")
            .HasPostgresExtension("extensions", "uuid-ossp")
            .HasPostgresExtension("vault", "supabase_vault");

        modelBuilder.Entity<Asset>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("assets_pkey");

            entity.ToTable("assets");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AssetName)
                .HasColumnType("character varying")
                .HasColumnName("asset_name");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(now() AT TIME ZONE 'utc'::text)")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DefaultName)
                .HasColumnType("character varying")
                .HasColumnName("default_name");
            entity.Property(e => e.SourceType)
                .HasColumnType("character varying")
                .HasColumnName("source_type");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("(now() AT TIME ZONE 'utc'::text)")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");
            entity.Property(e => e.Usersid).HasColumnName("usersid");

            entity.HasOne(d => d.Users).WithMany(p => p.Assets)
                .HasForeignKey(d => d.Usersid)
                .HasConstraintName("assets_usersid_fkey");
        });

        modelBuilder.Entity<ClassRoom>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("class_rooms_pkey");

            entity.ToTable("class_rooms");

            entity.HasIndex(e => e.ClassName, "class_rooms_class_name_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ClassName)
                .HasColumnType("character varying")
                .HasColumnName("class_name");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(now() AT TIME ZONE 'utc'::text)")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("(now() AT TIME ZONE 'utc'::text)")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");
        });

        modelBuilder.Entity<ClassRoomsStudent>(entity =>
        {
            entity.HasKey(e => new { e.ClassRoomsid, e.Studentid }).HasName("class_rooms_students_pkey");

            entity.ToTable("class_rooms_students");

            entity.Property(e => e.ClassRoomsid).HasColumnName("class_roomsid");
            entity.Property(e => e.Studentid).HasColumnName("studentid");
            entity.Property(e => e.JoinedAt)
                .HasDefaultValueSql("(now() AT TIME ZONE 'utc'::text)")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("joined_at");

            entity.HasOne(d => d.ClassRooms).WithMany(p => p.ClassRoomsStudents)
                .HasForeignKey(d => d.ClassRoomsid)
                .HasConstraintName("class_rooms_students_class_roomsid_fkey");

            entity.HasOne(d => d.Student).WithMany(p => p.ClassRoomsStudents)
                .HasForeignKey(d => d.Studentid)
                .HasConstraintName("class_rooms_students_studentid_fkey");
        });

        modelBuilder.Entity<ClassRoomsTeacher>(entity =>
        {
            entity.HasKey(e => new { e.ClassRoomsid, e.Teachersid }).HasName("class_rooms_teachers_pkey");

            entity.ToTable("class_rooms_teachers");

            entity.Property(e => e.ClassRoomsid).HasColumnName("class_roomsid");
            entity.Property(e => e.Teachersid).HasColumnName("teachersid");
            entity.Property(e => e.JoinedAt)
                .HasDefaultValueSql("(now() AT TIME ZONE 'utc'::text)")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("joined_at");

            entity.HasOne(d => d.ClassRooms).WithMany(p => p.ClassRoomsTeachers)
                .HasForeignKey(d => d.ClassRoomsid)
                .HasConstraintName("class_rooms_teachers_class_roomsid_fkey");

            entity.HasOne(d => d.Teachers).WithMany(p => p.ClassRoomsTeachers)
                .HasForeignKey(d => d.Teachersid)
                .HasConstraintName("class_rooms_teachers_teachersid_fkey");
        });

        modelBuilder.Entity<EventLog>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("event_logs_pkey");

            entity.ToTable("event_logs");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(now() AT TIME ZONE 'utc'::text)")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.EventType)
                .HasColumnType("character varying")
                .HasColumnName("event_type");
            entity.Property(e => e.Payload)
                .HasColumnType("json")
                .HasColumnName("payload");
            entity.Property(e => e.ReceivedAt)
                .HasDefaultValueSql("(now() AT TIME ZONE 'utc'::text)")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("received_at");
            entity.Property(e => e.Sessionsid).HasColumnName("sessionsid");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("(now() AT TIME ZONE 'utc'::text)")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Sessions).WithMany(p => p.EventLogs)
                .HasForeignKey(d => d.Sessionsid)
                .HasConstraintName("event_logs_sessionsid_fkey");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("roles_pkey");

            entity.ToTable("roles");

            entity.HasIndex(e => e.RoleName, "roles_role_name_key").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(now() AT TIME ZONE 'utc'::text)")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.RoleName)
                .HasColumnType("character varying")
                .HasColumnName("role_name");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("(now() AT TIME ZONE 'utc'::text)")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");
        });

        modelBuilder.Entity<Session>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("sessions_pkey");

            entity.ToTable("sessions");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(now() AT TIME ZONE 'utc'::text)")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.EndedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("ended_at");
            entity.Property(e => e.StartedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("started_at");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("(now() AT TIME ZONE 'utc'::text)")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");
            entity.Property(e => e.Usersid).HasColumnName("usersid");

            entity.HasOne(d => d.Users).WithMany(p => p.Sessions)
                .HasForeignKey(d => d.Usersid)
                .HasConstraintName("sessions_usersid_fkey");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("users_pkey");

            entity.ToTable("users");

            entity.HasIndex(e => e.Email, "users_email_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.AuthVersion).HasColumnName("auth_version");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(now() AT TIME ZONE 'utc'::text)")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Email)
                .HasMaxLength(255)
                .HasColumnName("email");
            entity.Property(e => e.EmailVerifiedAt).HasColumnName("email_verified_at");
            entity.Property(e => e.Password)
                .HasMaxLength(255)
                .HasColumnName("password");
            entity.Property(e => e.Rolesid).HasColumnName("rolesid");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("(now() AT TIME ZONE 'utc'::text)")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");
            entity.Property(e => e.UserName)
                .HasMaxLength(255)
                .HasColumnName("user_name");
            entity.Property(e => e.VerificationAttempts).HasColumnName("verification_attempts");
            entity.Property(e => e.VerificationCodeHash).HasColumnName("verification_code_hash");
            entity.Property(e => e.VerificationExpiresAt).HasColumnName("verification_expires_at");
            entity.Property(e => e.VerificationSentAt).HasColumnName("verification_sent_at");

            entity.HasOne(d => d.Roles).WithMany(p => p.Users)
                .HasForeignKey(d => d.Rolesid)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("users_rolesid_fkey");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
