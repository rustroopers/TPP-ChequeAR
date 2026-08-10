-- =====================================================================
-- 99-grants.sql — Grants finales a CHEQUEAR_RO.
-- =====================================================================
-- CHEQUEAR_RO recibe SELECT sólo sobre las VW_*. Nunca sobre HOSP_*.
-- Esta es la segunda línea de defensa contra el bypass de la allowlist:
-- aun si la app intenta SELECT * FROM HOSPITAL_MOCK.HOSP_PACIENTE,
-- Oracle responde ORA-00942 (table or view does not exist).
--
-- NO usar: GRANT ALL, GRANT ... TO PUBLIC, ni roles laxos.
-- =====================================================================

ALTER SESSION SET CONTAINER = XEPDB1;

GRANT SELECT ON HOSPITAL_MOCK.VW_PACIENTE        TO CHEQUEAR_RO;
GRANT SELECT ON HOSPITAL_MOCK.VW_EPISODIO        TO CHEQUEAR_RO;
GRANT SELECT ON HOSPITAL_MOCK.VW_DIAGNOSTICO     TO CHEQUEAR_RO;
GRANT SELECT ON HOSPITAL_MOCK.VW_OBSERVACION_LAB TO CHEQUEAR_RO;
GRANT SELECT ON HOSPITAL_MOCK.VW_SIGNOS_VITALES  TO CHEQUEAR_RO;
GRANT SELECT ON HOSPITAL_MOCK.VW_MEDICACION      TO CHEQUEAR_RO;
GRANT SELECT ON HOSPITAL_MOCK.VW_ALERGIA         TO CHEQUEAR_RO;
GRANT SELECT ON HOSPITAL_MOCK.VW_PROFESIONAL     TO CHEQUEAR_RO;
GRANT SELECT ON HOSPITAL_MOCK.VW_ESPECIALIDAD    TO CHEQUEAR_RO;
GRANT SELECT ON HOSPITAL_MOCK.VW_TURNO           TO CHEQUEAR_RO;

-- ---------------------------------------------------------------------
-- Sinonimos privados.
-- ---------------------------------------------------------------------
-- El GRANT solo habilita el acceso; no hace que el objeto sea resoluble
-- por nombre corto. Sin estos sinonimos, CHEQUEAR_RO tiene que escribir
-- HOSPITAL_MOCK.VW_PACIENTE, lo que meteria el nombre del schema del
-- hospital dentro del codigo de la app (y de la allowlist).
--
-- Con los sinonimos, el consumidor consulta siempre VW_*, y cuando llegue
-- el hospital real solo cambia a que apuntan.
CREATE OR REPLACE SYNONYM CHEQUEAR_RO.VW_PACIENTE        FOR HOSPITAL_MOCK.VW_PACIENTE;
CREATE OR REPLACE SYNONYM CHEQUEAR_RO.VW_EPISODIO        FOR HOSPITAL_MOCK.VW_EPISODIO;
CREATE OR REPLACE SYNONYM CHEQUEAR_RO.VW_DIAGNOSTICO     FOR HOSPITAL_MOCK.VW_DIAGNOSTICO;
CREATE OR REPLACE SYNONYM CHEQUEAR_RO.VW_OBSERVACION_LAB FOR HOSPITAL_MOCK.VW_OBSERVACION_LAB;
CREATE OR REPLACE SYNONYM CHEQUEAR_RO.VW_SIGNOS_VITALES  FOR HOSPITAL_MOCK.VW_SIGNOS_VITALES;
CREATE OR REPLACE SYNONYM CHEQUEAR_RO.VW_MEDICACION      FOR HOSPITAL_MOCK.VW_MEDICACION;
CREATE OR REPLACE SYNONYM CHEQUEAR_RO.VW_ALERGIA         FOR HOSPITAL_MOCK.VW_ALERGIA;
CREATE OR REPLACE SYNONYM CHEQUEAR_RO.VW_PROFESIONAL     FOR HOSPITAL_MOCK.VW_PROFESIONAL;
CREATE OR REPLACE SYNONYM CHEQUEAR_RO.VW_ESPECIALIDAD    FOR HOSPITAL_MOCK.VW_ESPECIALIDAD;
CREATE OR REPLACE SYNONYM CHEQUEAR_RO.VW_TURNO           FOR HOSPITAL_MOCK.VW_TURNO;

-- Verificación (queda en logs del contenedor):
-- SELECT TABLE_NAME, PRIVILEGE
-- FROM   DBA_TAB_PRIVS
-- WHERE  GRANTEE = 'CHEQUEAR_RO'
-- ORDER  BY TABLE_NAME, PRIVILEGE;
