namespace ReworkGuard.Domain.Entities;

public class PolicySetting
{
    public const string StandardHoursPerMonth = "StandardHoursPerMonth";
    public const string DailyHours = "DailyHours";
    public const string DeductionCapPercent = "DeductionCapPercent";
    public const string DefaultWarrantyDays = "DefaultWarrantyDays";
    public const string DisputeWindowDays = "DisputeWindowDays";
    public const string QaMissDeductible = "QaMissDeductible";
    public const string QaMissSharePercent = "QaMissSharePercent";
    public const string RequireEmployeeAcknowledgement = "RequireEmployeeAcknowledgement";
    public const string ProfitSharePercent = "ProfitSharePercent";
    public const string LabourOverheadFactor = "LabourOverheadFactor";

    private PolicySetting()
    {
    }

    public PolicySetting(string key, string value)
    {
        Key = key;
        Value = value;
    }

    public string Key { get; private set; } = string.Empty;

    public string Value { get; private set; } = string.Empty;

    public void Change(string value) => Value = value;
}
