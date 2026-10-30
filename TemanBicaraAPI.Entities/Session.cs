using System;
using System.Collections.Generic;

namespace TemanBicaraAPI.Entities;

public partial class Session
{
    public long Id { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? EndedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid Usersid { get; set; }

    public virtual ICollection<EventLog> EventLogs { get; set; } = new List<EventLog>();

    public virtual User Users { get; set; } = null!;
}
