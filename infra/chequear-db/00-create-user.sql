-- =====================================================================
-- 00-create-user.sql — Usuario/schema propio de ChequeAR (XEPDB1).
-- =====================================================================
-- Este es el store TRANSACCIONAL de la app (cuestionarios, respuestas,
-- usuarios, auditoria, reportes guardados). NO confundir con HOSPITAL_MOCK,
-- que emula las vistas de solo-lectura del hospital.
--
-- Las tablas las crea Entity Framework via migraciones; aca solo se crea
-- el usuario y sus permisos. Correr como SYSDBA:
--
--   sqlplus / AS SYSDBA @infra/chequear-db/00-create-user.sql
--
-- La password debe coincidir con la connection string "ChequeAR" del
-- Web.config. Valor de desarrollo local unicamente.
-- =====================================================================

ALTER SESSION SET CONTAINER = XEPDB1;

CREATE USER CHEQUEAR IDENTIFIED BY "Chequear_dev1"
  DEFAULT TABLESPACE USERS
  TEMPORARY TABLESPACE TEMP
  QUOTA UNLIMITED ON USERS;

GRANT CREATE SESSION, RESOURCE TO CHEQUEAR;
GRANT CREATE TABLE, CREATE VIEW, CREATE SEQUENCE TO CHEQUEAR;

EXIT;
