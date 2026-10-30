using System;
using System.Collections.Generic;

namespace TemanBicaraAPI.Entities;

public partial class User
{
    public Guid Id { get; set; }

    public string UserName { get; set; } = null!;

    public string Email { get; set; } = null!;

    public string Password { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public long Rolesid { get; set; }

    public long AuthVersion { get; set; }

    public DateTime? EmailVerifiedAt { get; set; }

    public string? VerificationCodeHash { get; set; }

    public DateTime? VerificationExpiresAt { get; set; }

    public DateTime? VerificationSentAt { get; set; }

    public int VerificationAttempts { get; set; }

    public virtual ICollection<Asset> Assets { get; set; } = new List<Asset>();

    public virtual ICollection<ClassRoomsStudent> ClassRoomsStudents { get; set; } = new List<ClassRoomsStudent>();

    public virtual ICollection<ClassRoomsTeacher> ClassRoomsTeachers { get; set; } = new List<ClassRoomsTeacher>();

    public virtual Role Roles { get; set; } = null!;

    public virtual ICollection<Session> Sessions { get; set; } = new List<Session>();
}
