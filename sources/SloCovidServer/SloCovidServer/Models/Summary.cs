namespace SloCovidServer.Models
{
    /// <summary>
    /// Total summary
    /// </summary>
    public record Summary(
        CasesToDateSummary CasesToDateSummary,
        CasesActive CasesActive,
        CasesAvg7Days CasesAvg7Days,
        HospitalizedCurrent HospitalizedCurrent,
        ICUCurrent ICUCurrent,
        DeceasedToDate DeceasedToDate,
        TestsToday TestsToday,
        TestsTodayHAT TestsTodayHAT);
    /// <summary>
    /// Base class for summary items
    /// </summary>
    public abstract record SummaryBase(float? DiffPercentage, int Year, int Month, int Day);
    public record CasesToDateSummarySubValues(int? In);
    public record CasesToDateSummary(int? Value, CasesToDateSummarySubValues SubValues, float? DiffPercentage, int Year, int Month, int Day) : SummaryBase(DiffPercentage, Year, Month, Day);
    public record CasesActiveSubValues(int? In, int? Out);
    public record CasesActive(int? Value, CasesActiveSubValues SubValues, float? DiffPercentage, int Year, int Month, int Day) : SummaryBase(DiffPercentage, Year, Month, Day);
    public record CasesAvg7Days(float? Value, bool Sublabel, float? DiffPercentage, int Year, int Month, int Day) : SummaryBase(DiffPercentage, Year, Month, Day);
    public record HospitalizedCurrentSubValues(int? In, int? Out, int? Deceased);
    public record HospitalizedCurrent(int? Value, HospitalizedCurrentSubValues SubValues, float? DiffPercentage, int Year, int Month, int Day) : SummaryBase(DiffPercentage, Year, Month, Day);
    public record ICUCurrentSubValues(int? In, int? Out, int? Deceased);
    public record ICUCurrent(int? Value, ICUCurrentSubValues SubValues, float? DiffPercentage, int Year, int Month, int Day) : SummaryBase(DiffPercentage, Year, Month, Day);
    public record DeceasedToDateSubValues(int? Deceased);
    public record DeceasedToDate(int? Value, DeceasedToDateSubValues SubValues, float? DiffPercentage, int Year, int Month, int Day) : SummaryBase(DiffPercentage, Year, Month, Day);
    public record TestsTodaySubValues(int? Positive, float? Percent);
    public record TestsToday(int? Value, TestsTodaySubValues SubValues, int Year, int Month, int Day);
    public record TestsTodayHAT(int? Value, TestsTodaySubValues SubValues, int Year, int Month, int Day);
}
