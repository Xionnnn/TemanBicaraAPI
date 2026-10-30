using System;
using System.Collections.Generic;

namespace TemanBicaraAPI.Entities;

public partial class ClassRoom
{
    public long Id { get; set; }

    public string ClassName { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual ICollection<ClassRoomsStudent> ClassRoomsStudents { get; set; } = new List<ClassRoomsStudent>();

    public virtual ICollection<ClassRoomsTeacher> ClassRoomsTeachers { get; set; } = new List<ClassRoomsTeacher>();
}
