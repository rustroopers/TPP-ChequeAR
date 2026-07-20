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

-- Verificación (queda en logs del contenedor):
-- SELECT TABLE_NAME, PRIVILEGE
-- FROM   DBA_TAB_PRIVS
-- WHERE  GRANTEE = 'CHEQUEAR_RO'
-- ORDER  BY TABLE_NAME, PRIVILEGE;
