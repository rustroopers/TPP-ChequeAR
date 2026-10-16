-- Borra el mock para volver a cargarlo. Como SYSDBA:
--   sqlplus -S "/ as sysdba" @borrar.sql
ALTER SESSION SET CONTAINER = XEPDB1;
DROP USER CHEQUEAR_RO CASCADE;
DROP USER HOSPITAL_MOCK CASCADE;
EXIT
