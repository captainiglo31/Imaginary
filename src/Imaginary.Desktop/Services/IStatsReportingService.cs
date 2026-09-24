using System.Threading.Tasks;

namespace Imaginary.Desktop.Services;

public interface IStatsReportingService
{
    void ReportConversion(int count, long savedBytes);
    void ReportEdit(int count = 1);
    Task<long?> GetGlobalCounterAsync();
}
