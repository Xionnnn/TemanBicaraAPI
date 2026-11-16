namespace TemanBicaraAPI.Entities;

public sealed class PasswordResetChallenge
{
    public Guid UserId { get; set; }
    public Guid Id { get; set; }
    public string? CodeHash { get; set; }
    public DateTime CodeExpiresAt { get; set; }
    public DateTime SentAt { get; set; }
    public int Attempts { get; set; }
    public string PasswordStamp { get; set; } = null!;
    public DateTime? VerifiedAt { get; set; }
    public string? ResetTokenHash { get; set; }
    public DateTime? ResetExpiresAt { get; set; }
}
