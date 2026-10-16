-- =====================================================================
-- 04-verify.sql — Asserts post-seed (FR-018, SC-006).
-- =====================================================================
-- Cualquier fallo dispara RAISE_APPLICATION_ERROR. La transaccion del
-- entrypoint del contenedor falla y el contenedor queda no-healthy.
-- =====================================================================

ALTER SESSION SET CONTAINER = XEPDB1;

SET SERVEROUTPUT ON SIZE UNLIMITED;

DECLARE
  v_cnt           NUMBER;
  v_pac_sin_epi   NUMBER;
  v_pac_cronico   NUMBER;
  v_obs_textual   NUMBER;
  v_turno_futuro  NUMBER;
  v_dx_solo_princ NUMBER;
BEGIN
  -- Conteos minimos (FR-010..FR-012, SC-006)
  SELECT COUNT(*) INTO v_cnt FROM HOSPITAL_MOCK.HOSP_PACIENTE;
  IF v_cnt < 50 THEN
    RAISE_APPLICATION_ERROR(-20001, 'Pacientes < 50: ' || v_cnt);
  END IF;
  DBMS_OUTPUT.PUT_LINE('Pacientes: ' || v_cnt);

  SELECT COUNT(*) INTO v_cnt FROM HOSPITAL_MOCK.HOSP_EPISODIO;
  IF v_cnt < 150 THEN
    RAISE_APPLICATION_ERROR(-20002, 'Episodios < 150: ' || v_cnt);
  END IF;
  DBMS_OUTPUT.PUT_LINE('Episodios: ' || v_cnt);

  SELECT COUNT(*) INTO v_cnt FROM HOSPITAL_MOCK.HOSP_OBSERVACION_LAB;
  IF v_cnt < 500 THEN
    RAISE_APPLICATION_ERROR(-20003, 'Observaciones < 500: ' || v_cnt);
  END IF;
  DBMS_OUTPUT.PUT_LINE('Observaciones: ' || v_cnt);

  -- Edge cases (FR-023)
  SELECT COUNT(*) INTO v_pac_sin_epi
  FROM   HOSPITAL_MOCK.HOSP_PACIENTE P
  WHERE  NOT EXISTS (SELECT 1 FROM HOSPITAL_MOCK.HOSP_EPISODIO E
                     WHERE E.ID_PACIENTE = P.ID_PACIENTE);
  IF v_pac_sin_epi < 1 THEN
    RAISE_APPLICATION_ERROR(-20004, 'Falta al menos 1 paciente sin episodios');
  END IF;

  SELECT COUNT(*) INTO v_pac_cronico FROM (
    SELECT ID_PACIENTE
    FROM   HOSPITAL_MOCK.HOSP_EPISODIO
    GROUP  BY ID_PACIENTE
    HAVING COUNT(*) >= 10
  );
  IF v_pac_cronico < 1 THEN
    RAISE_APPLICATION_ERROR(-20005, 'Falta al menos 1 paciente cronico (>=10 episodios)');
  END IF;

  SELECT COUNT(*) INTO v_obs_textual
  FROM   HOSPITAL_MOCK.HOSP_OBSERVACION_LAB
  WHERE  VALOR_TEXTO IS NOT NULL;
  IF v_obs_textual < 1 THEN
    RAISE_APPLICATION_ERROR(-20006, 'Falta al menos 1 observacion con valor textual');
  END IF;

  SELECT COUNT(*) INTO v_turno_futuro
  FROM   HOSPITAL_MOCK.HOSP_TURNO
  WHERE  FECHA_HORA > TRUNC(SYSDATE);
  IF v_turno_futuro < 1 THEN
    RAISE_APPLICATION_ERROR(-20007, 'Falta al menos 1 turno con FECHA_HORA futura');
  END IF;

  SELECT COUNT(*) INTO v_dx_solo_princ FROM (
    SELECT ID_EPISODIO
    FROM   HOSPITAL_MOCK.HOSP_DIAGNOSTICO
    GROUP  BY ID_EPISODIO
    HAVING COUNT(*) = 1 AND MAX(ES_PRINCIPAL) = 1
  );
  IF v_dx_solo_princ < 1 THEN
    RAISE_APPLICATION_ERROR(-20008, 'Falta al menos 1 episodio sin dx secundario');
  END IF;

  -- Integridad referencial (huerfanos)
  SELECT COUNT(*) INTO v_cnt
  FROM   HOSPITAL_MOCK.HOSP_OBSERVACION_LAB O
  WHERE  NOT EXISTS (SELECT 1 FROM HOSPITAL_MOCK.HOSP_EPISODIO E
                     WHERE E.ID_EPISODIO = O.ID_EPISODIO);
  IF v_cnt > 0 THEN
    RAISE_APPLICATION_ERROR(-20009, 'Observaciones huerfanas: ' || v_cnt);
  END IF;

  -- Edad pediatrica (0..17 al anclaje) -- FR-010
  SELECT COUNT(*) INTO v_cnt
  FROM   HOSPITAL_MOCK.HOSP_PACIENTE
  WHERE  FECHA_NACIMIENTO < TRUNC(SYSDATE) - INTERVAL '18' YEAR
     OR  FECHA_NACIMIENTO > TRUNC(SYSDATE);
  IF v_cnt > 0 THEN
    RAISE_APPLICATION_ERROR(-20010, 'Hay ' || v_cnt || ' pacientes fuera del rango 0..17');
  END IF;

  DBMS_OUTPUT.PUT_LINE('Verificacion OK.');
END;
/
