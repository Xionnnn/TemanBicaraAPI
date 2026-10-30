using System;
using System.Collections.Generic;

namespace TemanBicaraAPI.Entities;

public partial class EventLog
{
    public long Id { get; set; }

    public string EventType { get; set; } = null!;

    public string? Payload { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public long Sessionsid { get; set; }

    public DateTime ReceivedAt { get; set; }

    public virtual Session Sessions { get; set; } = null!;
}
