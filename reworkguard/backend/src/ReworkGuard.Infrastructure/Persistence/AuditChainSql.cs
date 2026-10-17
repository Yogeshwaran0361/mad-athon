namespace ReworkGuard.Infrastructure.Persistence;

/// <summary>
/// The audit hash chain, computed by MySQL so every writer (the app, a migration, a DBA) is covered:
/// Hash = SHA-256 over the entry's sequence, the previous entry's hash and its stored columns, joined with the ASCII
/// unit separator. A single-row head table holds the last sequence and hash; the insert trigger locks it, so entries
/// are chained in commit order even with concurrent writers.
/// </summary>
internal static class AuditChainSql
{
    public const string Genesis = "0000000000000000000000000000000000000000000000000000000000000000";

    /// <summary>The canonical hash expression over a row alias (<c>NEW</c> in the trigger, a table alias when verifying).</summary>
    public static string HashOf(string row, string sequence, string prevHash) =>
        "SHA2(CONCAT_WS(CHAR(31), 'v1', " + sequence + ", " + prevHash + ", "
        + $"DATE_FORMAT({row}.`OccurredAtUtc`, '%Y-%m-%d %H:%i:%s.%f'), IFNULL({row}.`ActorEmployeeId`, ''), "
        + $"{row}.`EntityType`, {row}.`EntityId`, {row}.`Action`, CAST({row}.`DetailsJson` AS CHAR CHARACTER SET utf8mb4)), 256)";

    public static readonly string CreateHead =
        "CREATE TABLE `AuditChainHead` (`Id` TINYINT NOT NULL PRIMARY KEY, `LastSequence` BIGINT NOT NULL, `LastHash` CHAR(64) NOT NULL, "
        + "CONSTRAINT `CK_AuditChainHead_Single` CHECK (`Id` = 1)) CHARACTER SET utf8mb4";

    /// <summary>Chains existing rows in id order and seeds the head. Runs once, while the no-update trigger is dropped.</summary>
    public static readonly string[] Backfill =
    [
        "CREATE PROCEDURE `rg_audit_chain_backfill`() BEGIN "
        + "DECLARE done INT DEFAULT 0; DECLARE rid BIGINT; DECLARE seq BIGINT DEFAULT 0; "
        + $"DECLARE prev CHAR(64) DEFAULT '{Genesis}'; DECLARE h CHAR(64); "
        + "DECLARE c CURSOR FOR SELECT `Id` FROM `AuditEvents` ORDER BY `Id`; "
        + "DECLARE CONTINUE HANDLER FOR NOT FOUND SET done = 1; "
        + "OPEN c; l: LOOP FETCH c INTO rid; IF done THEN LEAVE l; END IF; SET seq = seq + 1; "
        + $"SELECT {HashOf("a", "seq", "prev")} INTO h FROM `AuditEvents` a WHERE a.`Id` = rid; "
        + "UPDATE `AuditEvents` SET `Sequence` = seq, `PrevHash` = prev, `Hash` = h WHERE `Id` = rid; SET prev = h; "
        + "END LOOP; CLOSE c; "
        + "INSERT INTO `AuditChainHead` (`Id`, `LastSequence`, `LastHash`) VALUES (1, seq, prev); END",
        "CALL `rg_audit_chain_backfill`()",
        "DROP PROCEDURE `rg_audit_chain_backfill`",
    ];

    public static readonly string CreateTrigger =
        "CREATE TRIGGER `TR_AuditEvents_Chain` BEFORE INSERT ON `AuditEvents` FOR EACH ROW BEGIN "
        + "DECLARE seq BIGINT; DECLARE prev CHAR(64); "
        + "SELECT `LastSequence` + 1, `LastHash` INTO seq, prev FROM `AuditChainHead` WHERE `Id` = 1 FOR UPDATE; "
        + "SET NEW.`Sequence` = seq, NEW.`PrevHash` = prev; "
        + $"SET NEW.`Hash` = {HashOf("NEW", "seq", "prev")}; "
        + "UPDATE `AuditChainHead` SET `LastSequence` = seq, `LastHash` = NEW.`Hash` WHERE `Id` = 1; END";

    public const string DropTrigger = "DROP TRIGGER IF EXISTS `TR_AuditEvents_Chain`";

    public const string CreateNoUpdate =
        "CREATE TRIGGER `TR_AuditEvents_NoUpdate` BEFORE UPDATE ON `AuditEvents` FOR EACH ROW "
        + "SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'AuditEvents is append-only: updates are not allowed';";

    public const string DropNoUpdate = "DROP TRIGGER IF EXISTS `TR_AuditEvents_NoUpdate`";

    /// <summary>
    /// The first entry that breaks the chain (bad hash, broken link or gap), as Sequence/Id/Reason, or no row.
    /// Each entry is joined to its predecessor through the unique Sequence index (a missing predecessor is a gap, or
    /// for sequence 1 the genesis hash), so MySQL walks the index in order and stops at the first break instead of
    /// materialising window functions over the whole table.
    /// </summary>
    public static readonly string FirstBreak =
        "SELECT a.`Sequence`, a.`Id`, CASE "
        + $"WHEN a.`Hash` <> {HashOf("a", "a.`Sequence`", "a.`PrevHash`")} THEN 'hash' "
        + "WHEN p.`Id` IS NULL AND a.`Sequence` <> 1 THEN 'gap' "
        + "ELSE 'link' END AS `Reason` "
        + "FROM `AuditEvents` a FORCE INDEX (`IX_AuditEvents_Sequence`) "
        + "LEFT JOIN `AuditEvents` p ON p.`Sequence` = a.`Sequence` - 1 "
        + $"WHERE a.`Hash` <> {HashOf("a", "a.`Sequence`", "a.`PrevHash`")} "
        + "OR (p.`Id` IS NULL AND a.`Sequence` <> 1) "
        + $"OR a.`PrevHash` <> COALESCE(p.`Hash`, '{Genesis}') "
        + "ORDER BY a.`Sequence` LIMIT 1";
}
