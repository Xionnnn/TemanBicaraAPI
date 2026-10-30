using System;
using System.Collections.Generic;

namespace TemanBicaraAPI.Entities;

public partial class ClassRoomsTeacher
{
    public long ClassRoomsid { get; set; }

    public Guid Teachersid { get; set; }

    public DateTime JoinedAt { get; set; }

    public virtual ClassRoom ClassRooms { get; set; } = null!;

    public virtual User Teachers { get; set; } = null!;
}
