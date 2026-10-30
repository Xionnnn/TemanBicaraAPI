using System;
using System.Collections.Generic;

namespace TemanBicaraAPI.Entities;

public partial class ClassRoomsStudent
{
    public long ClassRoomsid { get; set; }

    public Guid Studentid { get; set; }

    public DateTime JoinedAt { get; set; }

    public virtual ClassRoom ClassRooms { get; set; } = null!;

    public virtual User Student { get; set; } = null!;
}
