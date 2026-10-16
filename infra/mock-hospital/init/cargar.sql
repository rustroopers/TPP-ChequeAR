-- Carga completa del mock. Como SYSDBA, desde este directorio:
--   sqlplus -S "/ as sysdba" @cargar.sql
WHENEVER SQLERROR EXIT FAILURE
@@00-users.sql
@@01-schema.sql
@@02-views.sql
@@03-seed.sql
@@04-verify.sql
@@99-grants.sql
EXIT
