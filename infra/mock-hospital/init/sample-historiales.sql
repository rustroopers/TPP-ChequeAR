-- =====================================================================
-- sample-historiales.sql — Reporte de 5 historiales para stakeholder review.
-- =====================================================================
-- Stand-alone: cargar.sql no lo incluye. Se invoca a mano, desde init/:
--
--   sqlplus -S CHEQUEAR_RO/devdev@//localhost:1521/XEPDB1 @sample-historiales.sql
--
-- Selecciona 5 pacientes con la semilla 20260101 (DISTINTA a la del seed,
-- para que la muestra no privilegie ni los pacientes "1" ni "2" siempre).
-- Output: bloques tabulados por paciente con episodios + dx + observaciones
-- + signos vitales + alergias.
-- =====================================================================

SET SERVEROUTPUT ON SIZE UNLIMITED FORMAT WRAPPED
SET LINESIZE 200
SET PAGESIZE 100
SET FEEDBACK OFF

DECLARE
  v_seed         CONSTANT NUMBER := 20260101;
  v_n_muestra    CONSTANT NUMBER := 5;
  v_id_paciente  VARCHAR2(16);
  v_p            HOSPITAL_MOCK.VW_PACIENTE%ROWTYPE;
  CURSOR c_episodios (p VARCHAR2) IS
    SELECT * FROM HOSPITAL_MOCK.VW_EPISODIO
    WHERE  ID_PACIENTE = p
    ORDER  BY FECHA_INICIO DESC;
  CURSOR c_dx (e VARCHAR2) IS
    SELECT * FROM HOSPITAL_MOCK.VW_DIAGNOSTICO
    WHERE  ID_EPISODIO = e
    ORDER  BY ES_PRINCIPAL DESC;
  CURSOR c_obs (e VARCHAR2) IS
    SELECT * FROM HOSPITAL_MOCK.VW_OBSERVACION_LAB
    WHERE  ID_EPISODIO = e
    ORDER  BY FECHA_TOMA DESC;
  CURSOR c_signos (e VARCHAR2) IS
    SELECT * FROM HOSPITAL_MOCK.VW_SIGNOS_VITALES
    WHERE  ID_EPISODIO = e;
  CURSOR c_alergias (p VARCHAR2) IS
    SELECT * FROM HOSPITAL_MOCK.VW_ALERGIA
    WHERE  ID_PACIENTE = p
    ORDER  BY FECHA_REGISTRO DESC;

  v_total_pacientes NUMBER;
BEGIN
  BEGIN DBMS_RANDOM.SEED(v_seed); END;

  SELECT COUNT(*) INTO v_total_pacientes FROM HOSPITAL_MOCK.VW_PACIENTE;

  DBMS_OUTPUT.PUT_LINE('=========================================================');
  DBMS_OUTPUT.PUT_LINE('  MUESTRA DE HISTORIALES PARA STAKEHOLDER REVIEW');
  DBMS_OUTPUT.PUT_LINE('  Semilla muestra: ' || v_seed
                       || '  |  Total pacientes en base: ' || v_total_pacientes);
  DBMS_OUTPUT.PUT_LINE('=========================================================');

  FOR i IN 1 .. v_n_muestra LOOP
    v_id_paciente := 'PAC-' || LPAD(
      TRUNC(DBMS_RANDOM.VALUE(1, v_total_pacientes + 1)), 6, '0');

    BEGIN
      SELECT * INTO v_p FROM HOSPITAL_MOCK.VW_PACIENTE WHERE ID_PACIENTE = v_id_paciente;
    EXCEPTION WHEN NO_DATA_FOUND THEN
      CONTINUE;
    END;

    DBMS_OUTPUT.PUT_LINE(' ');
    DBMS_OUTPUT.PUT_LINE('--- Historial ' || i || ' / ' || v_n_muestra
                         || ' --------------------------------------');
    DBMS_OUTPUT.PUT_LINE('PACIENTE   : ' || v_p.ID_PACIENTE || '  '
                         || v_p.APELLIDO || ', ' || v_p.NOMBRE
                         || '  (' || v_p.SEXO || ')');
    DBMS_OUTPUT.PUT_LINE('NAC.       : ' || TO_CHAR(v_p.FECHA_NACIMIENTO, 'YYYY-MM-DD')
                         || '  |  Documento: '
                         || NVL(v_p.TIPO_DOCUMENTO || ' ' || v_p.DOCUMENTO, '(sin documento)'));
    DBMS_OUTPUT.PUT_LINE('COBERTURA  : ' || NVL(v_p.COBERTURA, '(no informada)'));

    -- Alergias del paciente
    DBMS_OUTPUT.PUT_LINE(' ');
    DBMS_OUTPUT.PUT_LINE('  Alergias:');
    FOR a IN c_alergias(v_id_paciente) LOOP
      DBMS_OUTPUT.PUT_LINE('    - [' || a.ESTADO || '] '
                           || NVL(a.SUSTANCIA, '(s/d)') || '  '
                           || NVL('reaccion: ' || a.REACCION, '')
                           || '  ' || NVL('severidad: ' || a.SEVERIDAD, ''));
    END LOOP;

    -- Episodios y sus dx/obs/signos
    DBMS_OUTPUT.PUT_LINE(' ');
    DBMS_OUTPUT.PUT_LINE('  Episodios:');
    FOR e IN c_episodios(v_id_paciente) LOOP
      DBMS_OUTPUT.PUT_LINE('    > ' || e.ID_EPISODIO || '  '
                           || TO_CHAR(e.FECHA_INICIO, 'YYYY-MM-DD HH24:MI')
                           || '  [' || e.TIPO || ' / ' || e.ESPECIALIDAD || ']');
      IF e.MOTIVO_CONSULTA IS NOT NULL THEN
        DBMS_OUTPUT.PUT_LINE('      Motivo: ' || e.MOTIVO_CONSULTA);
      END IF;

      FOR d IN c_dx(e.ID_EPISODIO) LOOP
        DBMS_OUTPUT.PUT_LINE('      Dx ' || CASE WHEN d.ES_PRINCIPAL = 1
                              THEN 'PRINCIPAL' ELSE 'secundario' END
                             || ': ' || d.COD_CIE10 || '  ' || d.DISPLAY);
      END LOOP;

      FOR s IN c_signos(e.ID_EPISODIO) LOOP
        DBMS_OUTPUT.PUT_LINE('      Signos:  peso='
                             || NVL(TO_CHAR(s.PESO_KG), '?') || 'kg'
                             || '  talla=' || NVL(TO_CHAR(s.TALLA_CM), '?') || 'cm'
                             || '  FC=' || NVL(TO_CHAR(s.FC_LPM), '?')
                             || '  FR=' || NVL(TO_CHAR(s.FR_RPM), '?')
                             || '  TA=' || NVL(TO_CHAR(s.TA_SISTOLICA), '?')
                                   || '/' || NVL(TO_CHAR(s.TA_DIASTOLICA), '?')
                             || '  T°=' || NVL(TO_CHAR(s.TEMPERATURA_C), '?'));
      END LOOP;

      FOR o IN c_obs(e.ID_EPISODIO) LOOP
        DBMS_OUTPUT.PUT_LINE('      Lab '
                             || RPAD(o.COD_LOINC, 8) || RPAD(o.DISPLAY, 40)
                             || ' = '
                             || NVL(TO_CHAR(o.VALOR_NUMERICO), o.VALOR_TEXTO)
                             || ' ' || NVL(o.UNIDAD, ''));
      END LOOP;

      DBMS_OUTPUT.PUT_LINE(' ');
    END LOOP;
  END LOOP;

  DBMS_OUTPUT.PUT_LINE('=========================================================');
  DBMS_OUTPUT.PUT_LINE('  FIN DE MUESTRA');
  DBMS_OUTPUT.PUT_LINE('=========================================================');
END;
/

EXIT
