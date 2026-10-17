namespace ReworkGuard.Infrastructure.Persistence.Migrations;

/// <summary>
/// Database-level guarantees for deductions, whatever client issues the SQL: a finalized line (Locked, Applied,
/// Reversed) keeps its money columns forever and may only move forward (Locked → Applied → Reversed); deduction
/// lines and disputes are never deleted.
/// </summary>
internal static class DeductionGuards
{
    private const string Finalized = "('Locked','Applied','Reversed')";

    public static readonly string[] Create =
    [
        "CREATE TRIGGER `TR_Deductions_FinalizedImmutable` BEFORE UPDATE ON `Deductions` FOR EACH ROW " +
        "BEGIN " +
        $"IF OLD.`Status` IN {Finalized} AND (" +
        "NOT (NEW.`Amount` <=> OLD.`Amount`) OR NOT (NEW.`Hours` <=> OLD.`Hours`) OR NOT (NEW.`HourlyRateUsed` <=> OLD.`HourlyRateUsed`) " +
        "OR NOT (NEW.`SharePercent` <=> OLD.`SharePercent`) OR NOT (NEW.`EmployeeId` <=> OLD.`EmployeeId`) OR NOT (NEW.`BugId` <=> OLD.`BugId`) " +
        "OR NOT (NEW.`TimeLogId` <=> OLD.`TimeLogId`) OR NOT (NEW.`Kind` <=> OLD.`Kind`) OR NOT (NEW.`SourceMonth` <=> OLD.`SourceMonth`) " +
        "OR NOT (NEW.`ApplyMonth` <=> OLD.`ApplyMonth`) OR NOT (NEW.`ReversesDeductionId` <=> OLD.`ReversesDeductionId`) " +
        "OR NOT (NEW.`PayrollRunId` <=> OLD.`PayrollRunId`) OR NOT (NEW.`WorkDate` <=> OLD.`WorkDate`)) THEN " +
        "SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Finalized deduction lines cannot be edited; raise a compensating line'; " +
        "END IF; " +
        "IF (OLD.`Status` = 'Locked' AND NEW.`Status` NOT IN ('Locked','Applied')) " +
        "OR (OLD.`Status` = 'Applied' AND NEW.`Status` NOT IN ('Applied','Reversed')) " +
        "OR (OLD.`Status` = 'Reversed' AND NEW.`Status` <> 'Reversed') THEN " +
        "SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Finalized deduction lines can only move Locked -> Applied -> Reversed'; " +
        "END IF; " +
        "END",
        "CREATE TRIGGER `TR_Deductions_NoDelete` BEFORE DELETE ON `Deductions` FOR EACH ROW " +
        "SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Deductions are never deleted'",
        "CREATE TRIGGER `TR_DeductionDisputes_NoDelete` BEFORE DELETE ON `DeductionDisputes` FOR EACH ROW " +
        "SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Deduction disputes are never deleted'",
    ];

    public static readonly string[] Drop =
    [
        "DROP TRIGGER IF EXISTS `TR_Deductions_FinalizedImmutable`",
        "DROP TRIGGER IF EXISTS `TR_Deductions_NoDelete`",
        "DROP TRIGGER IF EXISTS `TR_DeductionDisputes_NoDelete`",
    ];
}
