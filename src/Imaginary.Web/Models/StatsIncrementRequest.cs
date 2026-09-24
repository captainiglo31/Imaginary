namespace Imaginary.Web.Models;

public class StatsIncrementRequest
{
    public int Converted { get; set; }
    public int Edited { get; set; }
    public long SavedBytes { get; set; }
    public string? ClientVersion { get; set; }
}
