-- =====================================================================
-- 00-users.sql — Usuarios del mock (PROVISIONAL, sólo desarrollo)
-- =====================================================================
-- HOSPITAL_MOCK es dueño de las tablas HOSP_* y las vistas VW_*.
-- CHEQUEAR_RO es el consumidor: sólo SELECT sobre las VW_* (99-grants.sql).
-- Correr como SYSDBA, antes que el resto de los scripts (ver README).
-- Las contraseñas son las de desarrollo que usan por omisión los tests
-- (MockHospitalFixture); no se usan en ningún otro entorno.
-- =====================================================================

ALTER SESSION SET CONTAINER = XEPDB1;

CREATE USER HOSPITAL_MOCK IDENTIFIED BY "hospitalmockdev"
  DEFAULT TABLESPACE USERS QUOTA UNLIMITED ON USERS;
GRANT CREATE SESSION TO HOSPITAL_MOCK;

CREATE USER CHEQUEAR_RO IDENTIFIED BY "devdev";
GRANT CREATE SESSION TO CHEQUEAR_RO;
