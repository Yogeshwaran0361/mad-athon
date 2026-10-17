namespace ReworkGuard.Infrastructure.Persistence.Migrations;

/// <summary>
/// Database-level guarantees for payroll runs: a run's computed figures and inputs hash never change after it is
/// generated; a finalized or locked run only moves forward; runs, their employee rows and line items are never
/// deleted, and the employee rows and items are never updated.
/// </summary>
internal static class PayrollGuards
{
    public static readonly string[] Create =
    [
        "CREATE TRIGGER `TR_PayrollRuns_Immutable` BEFORE UPDATE ON `PayrollRuns` FOR EACH ROW " +
        "BEGIN " +
        "IF NOT (NEW.`InputsHash` <=> OLD.`InputsHash`) OR NOT (NEW.`PeriodMonth` <=> OLD.`PeriodMonth`) OR NOT (NEW.`RunVersion` <=> OLD.`RunVersion`) " +
        "OR NOT (NEW.`CapPercent` <=> OLD.`CapPercent`) OR NOT (NEW.`EmployeeCount` <=> OLD.`EmployeeCount`) OR NOT (NEW.`TotalApplied` <=> OLD.`TotalApplied`) " +
        "OR NOT (NEW.`TotalCarriedForward` <=> OLD.`TotalCarriedForward`) OR NOT (NEW.`TotalRefunds` <=> OLD.`TotalRefunds`) " +
        "OR NOT (NEW.`TotalSettledManually` <=> OLD.`TotalSettledManually`) " +
        "OR (OLD.`ApprovedInputsHash` IS NOT NULL AND NOT (NEW.`ApprovedInputsHash` <=> OLD.`ApprovedInputsHash`)) THEN " +
        "SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Payroll run figures cannot be changed; generate a new version'; " +
        "END IF; " +
        "IF (OLD.`Status` = 'Finalized' AND NEW.`Status` NOT IN ('Finalized','Locked')) " +
        "OR (OLD.`Status` IN ('Locked','Superseded') AND NEW.`Status` <> OLD.`Status`) THEN " +
        "SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Finalized payroll runs can only move Finalized -> Locked'; " +
        "END IF; " +
        "END",
        "CREATE TRIGGER `TR_PayrollRuns_NoDelete` BEFORE DELETE ON `PayrollRuns` FOR EACH ROW " +
        "SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Payroll runs are never deleted'",
        "CREATE TRIGGER `TR_PayrollRunEmployees_NoUpdate` BEFORE UPDATE ON `PayrollRunEmployees` FOR EACH ROW " +
        "SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'PayrollRunEmployees is append-only'",
        "CREATE TRIGGER `TR_PayrollRunEmployees_NoDelete` BEFORE DELETE ON `PayrollRunEmployees` FOR EACH ROW " +
        "SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'PayrollRunEmployees is append-only'",
        "CREATE TRIGGER `TR_PayrollRunItems_NoUpdate` BEFORE UPDATE ON `PayrollRunItems` FOR EACH ROW " +
        "SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'PayrollRunItems is append-only'",
        "CREATE TRIGGER `TR_PayrollRunItems_NoDelete` BEFORE DELETE ON `PayrollRunItems` FOR EACH ROW " +
        "SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'PayrollRunItems is append-only'",
        "CREATE TRIGGER `TR_PayrollPeriods_StayLocked` BEFORE UPDATE ON `PayrollPeriods` FOR EACH ROW " +
        "BEGIN " +
        "IF OLD.`IsLocked` AND (NOT NEW.`IsLocked` OR NOT (NEW.`FinalizedRunId` <=> OLD.`FinalizedRunId`)) THEN " +
        "SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'A finalized payroll month cannot be reopened'; " +
        "END IF; " +
        "END",
        "CREATE TRIGGER `TR_PayrollPeriods_NoDelete` BEFORE DELETE ON `PayrollPeriods` FOR EACH ROW " +
        "SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Payroll periods are never deleted'",
    ];

    public static readonly string[] Drop =
    [
        "DROP TRIGGER IF EXISTS `TR_PayrollRuns_Immutable`",
        "DROP TRIGGER IF EXISTS `TR_PayrollRuns_NoDelete`",
        "DROP TRIGGER IF EXISTS `TR_PayrollRunEmployees_NoUpdate`",
        "DROP TRIGGER IF EXISTS `TR_PayrollRunEmployees_NoDelete`",
        "DROP TRIGGER IF EXISTS `TR_PayrollRunItems_NoUpdate`",
        "DROP TRIGGER IF EXISTS `TR_PayrollRunItems_NoDelete`",
        "DROP TRIGGER IF EXISTS `TR_PayrollPeriods_StayLocked`",
        "DROP TRIGGER IF EXISTS `TR_PayrollPeriods_NoDelete`",
    ];
}
