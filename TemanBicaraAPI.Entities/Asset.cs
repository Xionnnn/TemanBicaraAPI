using System;
using System.Collections.Generic;

namespace TemanBicaraAPI.Entities;

public partial class Asset
{
    public long Id { get; set; }

    public string AssetName { get; set; } = null!;

    public string DefaultName { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Guid? Usersid { get; set; }

    public string SourceType { get; set; } = null!;

    public virtual User? Users { get; set; }
}
