using System.Collections.Generic;

namespace glFTPd_Commander.Models
{
    public static class UnitProvider
    {
        public static List<UnitItem> SpeedUnits { get; } =
        [
            new UnitItem { Display = "KiB/s", Code = "" },
            new UnitItem { Display = "MiB/s", Code = "m" },
            new UnitItem { Display = "GiB/s", Code = "g" },
            new UnitItem { Display = "TiB/s", Code = "t" }
        ];

        public static List<UnitItem> SizeUnits { get; } =
        [
            new UnitItem { Display = "MiB", Code = "M" },
            new UnitItem { Display = "GiB", Code = "G" },
            new UnitItem { Display = "TiB", Code = "T" }
        ];

        public static List<UnitItem> CreditUnits => SizeUnits; // Credits use size units (MiB, GiB, etc.)
    }
}
