-- Least-privilege grants for the application's database account.
-- Run as an administrator AFTER migrations (and after the API has started once, so Hangfire's tables exist):
--   mysql -h <host> -u <admin> -p <database> < scripts/db/app-user-grants.sql
-- then CALL rg_grant_app_user('<app_user>', '<host pattern, e.g. %>'); and DROP PROCEDURE rg_grant_app_user;
-- The user must already exist: CREATE USER '<app_user>'@'%' IDENTIFIED BY '<from your secret store>';
--
-- Append-only tables (audit trail, task history, payroll run snapshots) get SELECT and INSERT only: the account
-- cannot UPDATE or DELETE them even if the triggers were dropped. The audit chain head is maintained by the audit
-- trigger with its definer's rights, so the account gets only SELECT on it. Hangfire tables get full DML.
-- Schema changes (migrations) must be run with a separate, privileged account.

DROP PROCEDURE IF EXISTS rg_grant_app_user;

DELIMITER $$
CREATE PROCEDURE rg_grant_app_user(IN app_user VARCHAR(64), IN app_host VARCHAR(255))
BEGIN
  DECLARE done INT DEFAULT 0;
  DECLARE t VARCHAR(64);
  DECLARE privs VARCHAR(64);
  DECLARE c CURSOR FOR
    SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE';
  DECLARE CONTINUE HANDLER FOR NOT FOUND SET done = 1;
  OPEN c;
  grant_loop: LOOP
    FETCH c INTO t;
    IF done THEN LEAVE grant_loop; END IF;
    SET privs = CASE
      WHEN t IN ('AuditEvents', 'TaskStateHistory', 'PayrollRunEmployees', 'PayrollRunItems') THEN 'SELECT, INSERT'
      WHEN t IN ('AuditChainHead', '__EFMigrationsHistory') THEN 'SELECT'
      ELSE 'SELECT, INSERT, UPDATE, DELETE'
    END;
    SET @rg_sql = CONCAT('GRANT ', privs, ' ON `', DATABASE(), '`.`', t, '` TO ', QUOTE(app_user), '@', QUOTE(app_host));
    PREPARE s FROM @rg_sql;
    EXECUTE s;
    DEALLOCATE PREPARE s;
  END LOOP;
  CLOSE c;
END$$
DELIMITER ;
