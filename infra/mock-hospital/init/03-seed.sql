-- =====================================================================
-- 03-seed.sql — Seed determinístico (PROVISORIO).
-- =====================================================================
-- Reglas:
--  * RNG fijado con DBMS_RANDOM.SEED(20260625) al inicio.
--  * Todas las fechas relativas se calculan contra c_anclaje = 2026-06-25.
--  * Identificadores derivados de contadores, nunca de SYS_GUID/SYSTIMESTAMP.
--  * Misma semilla -> mismo dataset byte-a-byte (SC-002).
-- Conteos objetivo: >=50 pacientes, >=150 episodios, >=500 observaciones lab.
-- =====================================================================

ALTER SESSION SET CONTAINER = XEPDB1;
ALTER SESSION SET NLS_DATE_FORMAT       = 'YYYY-MM-DD';
ALTER SESSION SET NLS_TIMESTAMP_FORMAT  = 'YYYY-MM-DD HH24:MI:SS';
ALTER SESSION SET NLS_NUMERIC_CHARACTERS = '.,';

BEGIN DBMS_RANDOM.SEED(20260625); END;
/

-- =====================================================================
-- Bloque maestro: todo el seed en un único PL/SQL para mantener orden y
-- compartir variables (catálogos, contadores).
-- =====================================================================
DECLARE
  c_anclaje CONSTANT DATE := DATE '2026-06-25';

  -- Catálogos
  TYPE t_str_arr IS TABLE OF VARCHAR2(200);
  TYPE t_num_arr IS TABLE OF NUMBER;

  -- Nombres y apellidos es-AR (30 x 30 = 900 combinaciones posibles)
  v_nombres   t_str_arr := t_str_arr(
    'Juan','Mariano','Lucia','Sofia','Mateo','Valentina','Bruno','Camila',
    'Tomas','Martina','Joaquin','Catalina','Benjamin','Emma','Felipe','Mia',
    'Agustin','Olivia','Lautaro','Renata','Bautista','Julieta','Santino','Isabella',
    'Ciro','Delfina','Thiago','Antonella','Gael','Pilar');
  v_apellidos t_str_arr := t_str_arr(
    'Gonzalez','Rodriguez','Lopez','Fernandez','Martinez','Diaz','Perez','Garcia',
    'Sanchez','Romero','Sosa','Torres','Alvarez','Ramirez','Flores','Benitez',
    'Acosta','Medina','Suarez','Herrera','Aguirre','Caceres','Cabrera','Molina',
    'Ortiz','Silva','Castro','Vega','Carballo','Quiroga');

  -- Coberturas
  v_coberturas t_str_arr := t_str_arr(
    'OSDE','Swiss Medical','Galeno','PAMI','IOMA','Particular','OSDE','Swiss Medical','Galeno',NULL);

  -- Tipos de documento
  v_tipos_doc t_str_arr := t_str_arr('DNI','DNI','DNI','DNI','DNI','DNI','DNI','PAS','CI',NULL);

  -- Sexos
  v_sexos t_str_arr := t_str_arr('M','F','F','M','O');

  -- CIE-10 pediátricos frecuentes (codigo + display)
  v_cie10_cod t_str_arr := t_str_arr(
    'J06.9','A09','J45.9','K21.9','R50.9','H66.9','J20.9','B01.9','L20.9','J18.9',
    'K59.0','R10.4','J03.9','H10.9','J00','R11','R51','L50.9','R05','N39.0',
    'J34.0','K35.8','R06.0','S00.9','T78.4');
  v_cie10_dsp t_str_arr := t_str_arr(
    'Infeccion aguda de vias respiratorias superiores',
    'Diarrea y gastroenteritis de presunto origen infeccioso',
    'Asma no especificada',
    'Enfermedad por reflujo gastroesofagico',
    'Fiebre no especificada',
    'Otitis media no especificada',
    'Bronquitis aguda no especificada',
    'Varicela no especificada',
    'Dermatitis atopica',
    'Neumonia no especificada',
    'Constipacion',
    'Dolor abdominal no especificado',
    'Faringitis aguda no especificada',
    'Conjuntivitis no especificada',
    'Rinofaringitis aguda',
    'Nauseas y vomitos',
    'Cefalea',
    'Urticaria no especificada',
    'Tos',
    'Infeccion de vias urinarias',
    'Absceso de tabique nasal',
    'Apendicitis aguda',
    'Disnea',
    'Traumatismo de cabeza',
    'Alergia no especificada');

  -- LOINC laboratorio (codigo + display + unidad + min + max numerico)
  v_loinc_cod t_str_arr := t_str_arr(
    '718-7','4544-3','2345-7','2951-2','2823-3','2160-0','6690-2','789-8',
    '777-3','1751-7','1742-6','2093-3','17861-6','2085-9','2571-8');
  v_loinc_dsp t_str_arr := t_str_arr(
    'Hemoglobina en sangre','Hematocrito','Glucosa en sangre','Sodio en suero',
    'Potasio en suero','Creatinina en suero','Leucocitos en sangre','Eritrocitos en sangre',
    'Plaquetas en sangre','Albumina en suero','Alanina aminotransferasa',
    'Colesterol total','Calcio en suero','HDL colesterol','Trigliceridos');
  v_loinc_uni t_str_arr := t_str_arr(
    'g/dL','%','mg/dL','mmol/L','mmol/L','mg/dL','10^3/uL','10^6/uL',
    '10^3/uL','g/dL','U/L','mg/dL','mg/dL','mg/dL','mg/dL');
  v_loinc_min t_num_arr := t_num_arr(10, 30, 60, 135, 3.5, 0.3, 4.0, 3.8, 150, 3.5, 7, 120, 8.5, 35, 50);
  v_loinc_max t_num_arr := t_num_arr(16, 50,110, 145, 5.5, 1.0,12.0, 5.5, 450, 5.0,40, 200,10.5, 75,150);

  -- ATC (codigo + nombre comercial + via)
  v_atc_cod t_str_arr := t_str_arr(
    'N02BE01','J01CA04','R03AC02','R06AE07','A02BC01','R05CB06','J01CR02','M01AE01',
    'A03FA01','B05XA03','J01XD01','D07AB01','S01AA01','H02AB06','R03BA02','A07EC01',
    'C09AA02','J01EE01','N05CD08','J05AB01');
  v_atc_nom t_str_arr := t_str_arr(
    'Paracetamol jarabe','Amoxicilina suspension','Salbutamol inhalador','Cetirizina gotas',
    'Omeprazol capsulas','Ambroxol jarabe','Amoxi-clavulanico susp','Ibuprofeno jarabe',
    'Metoclopramida gotas','Suero fisiologico','Metronidazol comprimidos','Hidrocortisona crema',
    'Cloranfenicol colirio','Prednisolona jarabe','Budesonide inhalador','Mesalazina granulado',
    'Enalapril comprimidos','Sulfametoxazol+TMP susp','Midazolam gotas','Aciclovir comprimidos');
  v_atc_via t_str_arr := t_str_arr(
    'ORAL','ORAL','INH','ORAL','ORAL','ORAL','ORAL','ORAL','ORAL','IV','ORAL','TOPICA','TOPICA',
    'ORAL','INH','ORAL','ORAL','ORAL','ORAL','ORAL');

  -- Alergenos comunes
  v_alerg t_str_arr := t_str_arr(
    'Penicilina','AINEs','Frutos secos','Lacteos','Polen','Acaros','Mariscos','Latex','Huevo','Polvo');

  -- Motivos de consulta
  v_motivos t_str_arr := t_str_arr(
    'Control pediatrico de rutina','Cuadro febril','Tos persistente','Diarrea aguda',
    'Erupcion cutanea','Dolor abdominal','Dificultad respiratoria','Otalgia',
    'Vomitos','Dolor de garganta','Control de crecimiento','Seguimiento');

  -- Tipos de episodio
  v_tipos_epi t_str_arr := t_str_arr('AMBULATORIO','AMBULATORIO','AMBULATORIO','URGENCIA','INTERNACION');

  -- Estados de turno
  v_estados_tur t_str_arr := t_str_arr('PROGRAMADO','ATENDIDO','AUSENTE','CANCELADO');

  -- Estados de alergia
  v_estados_ale t_str_arr := t_str_arr('CONFIRMADA','SOSPECHADA','DESCONOCIDAS','DESCARTADA');
  v_severidades t_str_arr := t_str_arr('LEVE','MODERADA','SEVERA');

  -- Especialidades pediatricas
  v_esp_cod t_str_arr := t_str_arr('PED','NEO','CARP','NEUM','GAST','INFE','NEFRO','ENDO');
  v_esp_nom t_str_arr := t_str_arr(
    'Pediatria General','Neonatologia','Cardiologia Pediatrica','Neumonologia Pediatrica',
    'Gastroenterologia Pediatrica','Infectologia Pediatrica','Nefrologia Pediatrica','Endocrinologia Pediatrica');

  -- Trabajo
  v_total_pacientes  NUMBER := 52;
  v_id_paciente      VARCHAR2(16);
  v_id_episodio      VARCHAR2(16);
  v_id_profesional   VARCHAR2(16);
  v_id_turno         VARCHAR2(16);
  v_fecha_nac        DATE;
  v_edad_meses       NUMBER;
  v_n_episodios      NUMBER;
  v_fecha_episodio   TIMESTAMP(0);
  v_fecha_fin        TIMESTAMP(0);
  v_n_obs            NUMBER;
  v_n_med            NUMBER;
  v_idx_loinc        NUMBER;
  v_idx_dx           NUMBER;
  v_valor            NUMBER;
  v_seq_dx           NUMBER := 0;
  v_seq_obs          NUMBER := 0;
  v_seq_signos       NUMBER := 0;
  v_seq_med          NUMBER := 0;
  v_seq_ale          NUMBER := 0;
  v_n_episodios_tot  NUMBER := 0;
  v_n_obs_tot        NUMBER := 0;

  FUNCTION pick (a IN t_str_arr) RETURN VARCHAR2 IS
  BEGIN
    RETURN a(TRUNC(DBMS_RANDOM.VALUE(1, a.COUNT + 1)));
  END;

  FUNCTION pick_idx (a IN t_str_arr) RETURN NUMBER IS
  BEGIN
    RETURN TRUNC(DBMS_RANDOM.VALUE(1, a.COUNT + 1));
  END;
BEGIN
  -- =================================================================
  -- Especialidades (catalogo fijo, 8 filas)
  -- =================================================================
  FOR i IN 1 .. v_esp_cod.COUNT LOOP
    INSERT INTO HOSPITAL_MOCK.HOSP_ESPECIALIDAD (COD_ESPECIALIDAD, NOMBRE)
    VALUES (v_esp_cod(i), v_esp_nom(i));
  END LOOP;

  -- =================================================================
  -- Profesionales (12, distribuidos entre 8 especialidades)
  -- =================================================================
  FOR i IN 1 .. 12 LOOP
    v_id_profesional := 'PROF-' || LPAD(i, 6, '0');
    INSERT INTO HOSPITAL_MOCK.HOSP_PROFESIONAL
      (ID_PROFESIONAL, MATRICULA, NOMBRE, APELLIDO, COD_ESPECIALIDAD)
    VALUES
      (v_id_profesional,
       'MN-' || LPAD(10000 + i, 5, '0'),
       pick(v_nombres),
       pick(v_apellidos),
       v_esp_cod(MOD(i - 1, v_esp_cod.COUNT) + 1));
  END LOOP;

  -- =================================================================
  -- Pacientes (52: 1 sin episodios, 1 cronico c/12 episodios)
  -- =================================================================
  FOR i IN 1 .. v_total_pacientes LOOP
    v_id_paciente := 'PAC-' || LPAD(i, 6, '0');

    -- Edad 0..17 anios; distribucion mas densa en 1..10
    v_edad_meses := TRUNC(DBMS_RANDOM.VALUE(0, 216)); -- 0..215 meses (0..17 anios)
    v_fecha_nac  := ADD_MONTHS(c_anclaje, -v_edad_meses)
                    - TRUNC(DBMS_RANDOM.VALUE(0, 28));

    INSERT INTO HOSPITAL_MOCK.HOSP_PACIENTE (
      ID_PACIENTE, DOCUMENTO, TIPO_DOCUMENTO, NOMBRE, APELLIDO,
      FECHA_NACIMIENTO, SEXO, COBERTURA, FECHA_ALTA_ADM, ID_INTERNO
    ) VALUES (
      v_id_paciente,
      CASE WHEN DBMS_RANDOM.VALUE < 0.10 THEN NULL
           ELSE TO_CHAR(40000000 + i * 7919 + TRUNC(DBMS_RANDOM.VALUE(0, 9999))) END,
      pick(v_tipos_doc),
      pick(v_nombres),
      pick(v_apellidos),
      v_fecha_nac,
      pick(v_sexos),
      pick(v_coberturas),
      v_fecha_nac + TRUNC(DBMS_RANDOM.VALUE(1, 365)),
      i
    );

    -- Distribucion de episodios:
    --   i = 1  -> 0 episodios (FR-023)
    --   i = 2  -> 12 episodios (cronico, FR-023)
    --   resto: 30% con 1, 50% con 2..4, 20% con 5..8
    IF    i = 1 THEN v_n_episodios := 0;
    ELSIF i = 2 THEN v_n_episodios := 12;
    ELSIF DBMS_RANDOM.VALUE < 0.30 THEN v_n_episodios := 1;
    ELSIF DBMS_RANDOM.VALUE < 0.80 THEN v_n_episodios := TRUNC(DBMS_RANDOM.VALUE(2, 5));
    ELSE                                v_n_episodios := TRUNC(DBMS_RANDOM.VALUE(5, 9));
    END IF;

    -- =================================================================
    -- Episodios + dependientes (dx, obs, signos, med)
    -- =================================================================
    FOR k IN 1 .. v_n_episodios LOOP
      v_n_episodios_tot := v_n_episodios_tot + 1;
      v_id_episodio    := 'EPI-' || LPAD(v_n_episodios_tot, 6, '0');

      -- Fecha en los ultimos min(edad, 5) anios y siempre >= fecha_nac
      v_fecha_episodio := CAST(
        v_fecha_nac + TRUNC(DBMS_RANDOM.VALUE(30, GREATEST(31, (c_anclaje - v_fecha_nac))))
        AS TIMESTAMP(0));
      IF v_fecha_episodio > CAST(c_anclaje AS TIMESTAMP(0)) THEN
        v_fecha_episodio := CAST(c_anclaje - TRUNC(DBMS_RANDOM.VALUE(1, 30)) AS TIMESTAMP(0));
      END IF;

      -- ~10% episodios "abiertos" (FECHA_FIN NULL)
      IF DBMS_RANDOM.VALUE < 0.10 THEN
        v_fecha_fin := NULL;
      ELSE
        v_fecha_fin := v_fecha_episodio + NUMTODSINTERVAL(TRUNC(DBMS_RANDOM.VALUE(1, 14)), 'DAY');
      END IF;

      INSERT INTO HOSPITAL_MOCK.HOSP_EPISODIO (
        ID_EPISODIO, ID_PACIENTE, TIPO, COD_ESPECIALIDAD,
        ID_PROFESIONAL_RESP, FECHA_INICIO, FECHA_FIN, MOTIVO_CONSULTA
      ) VALUES (
        v_id_episodio,
        v_id_paciente,
        pick(v_tipos_epi),
        v_esp_cod(TRUNC(DBMS_RANDOM.VALUE(1, v_esp_cod.COUNT + 1))),
        'PROF-' || LPAD(TRUNC(DBMS_RANDOM.VALUE(1, 13)), 6, '0'),
        v_fecha_episodio,
        v_fecha_fin,
        pick(v_motivos)
      );

      -- Diagnostico principal (siempre 1)
      v_seq_dx := v_seq_dx + 1;
      v_idx_dx := pick_idx(v_cie10_cod);
      INSERT INTO HOSPITAL_MOCK.HOSP_DIAGNOSTICO (
        ID_DIAGNOSTICO, ID_EPISODIO, COD_CIE10, DISPLAY, ES_PRINCIPAL, FECHA_DIAGNOSTICO
      ) VALUES (
        v_seq_dx, v_id_episodio,
        v_cie10_cod(v_idx_dx), v_cie10_dsp(v_idx_dx),
        1, CAST(v_fecha_episodio AS DATE)
      );

      -- Diagnosticos secundarios (0..2). El primer episodio del paciente 1 (no aplica
      -- porque tiene 0 episodios). Para garantizar al menos un episodio sin dx
      -- secundario, el primer episodio de cada paciente no recibe secundarios.
      IF k > 1 THEN
        FOR s IN 1 .. TRUNC(DBMS_RANDOM.VALUE(0, 3)) LOOP
          v_seq_dx := v_seq_dx + 1;
          v_idx_dx := pick_idx(v_cie10_cod);
          INSERT INTO HOSPITAL_MOCK.HOSP_DIAGNOSTICO (
            ID_DIAGNOSTICO, ID_EPISODIO, COD_CIE10, DISPLAY, ES_PRINCIPAL, FECHA_DIAGNOSTICO
          ) VALUES (
            v_seq_dx, v_id_episodio,
            v_cie10_cod(v_idx_dx), v_cie10_dsp(v_idx_dx),
            0, CAST(v_fecha_episodio AS DATE)
          );
        END LOOP;
      END IF;

      -- Observaciones de laboratorio (3..6 por episodio para asegurar >=500)
      v_n_obs := TRUNC(DBMS_RANDOM.VALUE(3, 7));
      FOR o IN 1 .. v_n_obs LOOP
        v_seq_obs := v_seq_obs + 1;
        v_n_obs_tot := v_n_obs_tot + 1;
        v_idx_loinc := pick_idx(v_loinc_cod);
        v_valor := ROUND(v_loinc_min(v_idx_loinc)
                         + DBMS_RANDOM.VALUE * (v_loinc_max(v_idx_loinc) - v_loinc_min(v_idx_loinc)),
                         2);

        -- Cada 50 observaciones, una textual (cualitativa) para cumplir FR-023.
        IF MOD(v_seq_obs, 50) = 0 THEN
          INSERT INTO HOSPITAL_MOCK.HOSP_OBSERVACION_LAB (
            ID_OBSERVACION, ID_EPISODIO, ID_PACIENTE, COD_LOINC, DISPLAY,
            VALOR_NUMERICO, VALOR_TEXTO, UNIDAD, FECHA_TOMA
          ) VALUES (
            v_seq_obs, v_id_episodio, v_id_paciente,
            v_loinc_cod(v_idx_loinc), v_loinc_dsp(v_idx_loinc),
            NULL, 'No detectable', NULL,
            v_fecha_episodio + NUMTODSINTERVAL(TRUNC(DBMS_RANDOM.VALUE(0, 24)), 'HOUR')
          );
        ELSE
          INSERT INTO HOSPITAL_MOCK.HOSP_OBSERVACION_LAB (
            ID_OBSERVACION, ID_EPISODIO, ID_PACIENTE, COD_LOINC, DISPLAY,
            VALOR_NUMERICO, VALOR_TEXTO, UNIDAD, FECHA_TOMA
          ) VALUES (
            v_seq_obs, v_id_episodio, v_id_paciente,
            v_loinc_cod(v_idx_loinc), v_loinc_dsp(v_idx_loinc),
            v_valor, NULL, v_loinc_uni(v_idx_loinc),
            v_fecha_episodio + NUMTODSINTERVAL(TRUNC(DBMS_RANDOM.VALUE(0, 24)), 'HOUR')
          );
        END IF;
      END LOOP;

      -- Signos vitales (1 set por episodio)
      v_seq_signos := v_seq_signos + 1;
      INSERT INTO HOSPITAL_MOCK.HOSP_SIGNOS_VITALES (
        ID_SIGNO, ID_EPISODIO, FECHA_TOMA,
        PESO_KG, TALLA_CM, FC_LPM, FR_RPM, TA_SISTOLICA, TA_DIASTOLICA, TEMPERATURA_C
      ) VALUES (
        v_seq_signos, v_id_episodio,
        v_fecha_episodio + NUMTODSINTERVAL(1, 'HOUR'),
        ROUND(3 + (v_edad_meses / 216) * 65 + DBMS_RANDOM.VALUE(-5, 5), 2),
        ROUND(50 + (v_edad_meses / 216) * 120 + DBMS_RANDOM.VALUE(-5, 5), 1),
        TRUNC(70 + DBMS_RANDOM.VALUE(0, 60)),
        TRUNC(15 + DBMS_RANDOM.VALUE(0, 25)),
        TRUNC(80 + DBMS_RANDOM.VALUE(0, 40)),
        TRUNC(50 + DBMS_RANDOM.VALUE(0, 25)),
        ROUND(36 + DBMS_RANDOM.VALUE(0, 3), 1)
      );

      -- Medicaciones (0..3 por episodio)
      v_n_med := TRUNC(DBMS_RANDOM.VALUE(0, 4));
      FOR m IN 1 .. v_n_med LOOP
        v_seq_med := v_seq_med + 1;
        DECLARE v_idx_atc NUMBER := pick_idx(v_atc_cod); BEGIN
          INSERT INTO HOSPITAL_MOCK.HOSP_MEDICACION (
            ID_MEDICACION, ID_EPISODIO, COD_ATC, NOMBRE_COMERCIAL, DOSIS, VIA,
            FECHA_INICIO, FECHA_FIN
          ) VALUES (
            v_seq_med, v_id_episodio,
            v_atc_cod(v_idx_atc),
            v_atc_nom(v_idx_atc),
            TRUNC(DBMS_RANDOM.VALUE(5, 50)) || ' mg/kg cada '
              || TRUNC(DBMS_RANDOM.VALUE(6, 13)) || ' hs',
            v_atc_via(v_idx_atc),
            CAST(v_fecha_episodio AS DATE),
            CASE WHEN DBMS_RANDOM.VALUE < 0.30 THEN NULL
                 ELSE CAST(v_fecha_episodio AS DATE)
                      + TRUNC(DBMS_RANDOM.VALUE(3, 14)) END
          );
        END;
      END LOOP;

    END LOOP; -- episodios

    -- =================================================================
    -- Alergias por paciente
    -- =================================================================
    -- ~70% de pacientes con 1..2 alergias; ~20% con ESTADO=DESCONOCIDAS;
    -- ~10% sin registro de alergia.
    IF DBMS_RANDOM.VALUE < 0.20 THEN
      v_seq_ale := v_seq_ale + 1;
      INSERT INTO HOSPITAL_MOCK.HOSP_ALERGIA (
        ID_ALERGIA, ID_PACIENTE, SUSTANCIA, REACCION, SEVERIDAD, ESTADO, FECHA_REGISTRO
      ) VALUES (
        v_seq_ale, v_id_paciente,
        NULL, NULL, NULL, 'DESCONOCIDAS',
        v_fecha_nac + TRUNC(DBMS_RANDOM.VALUE(1, 365))
      );
    ELSIF DBMS_RANDOM.VALUE < 0.90 THEN
      FOR a IN 1 .. TRUNC(DBMS_RANDOM.VALUE(1, 3)) LOOP
        v_seq_ale := v_seq_ale + 1;
        INSERT INTO HOSPITAL_MOCK.HOSP_ALERGIA (
          ID_ALERGIA, ID_PACIENTE, SUSTANCIA, REACCION, SEVERIDAD, ESTADO, FECHA_REGISTRO
        ) VALUES (
          v_seq_ale, v_id_paciente,
          pick(v_alerg),
          CASE TRUNC(DBMS_RANDOM.VALUE(1, 4))
            WHEN 1 THEN 'Erupcion cutanea'
            WHEN 2 THEN 'Edema'
            ELSE        'Prurito' END,
          pick(v_severidades),
          v_estados_ale(TRUNC(DBMS_RANDOM.VALUE(1, 3))), -- CONFIRMADA o SOSPECHADA
          v_fecha_nac + TRUNC(DBMS_RANDOM.VALUE(1, 365))
        );
      END LOOP;
    END IF;

    -- =================================================================
    -- Turnos por paciente (1..3, mezclando pasado y futuro)
    -- =================================================================
    IF v_n_episodios > 0 THEN
      FOR t IN 1 .. TRUNC(DBMS_RANDOM.VALUE(1, 4)) LOOP
        v_id_turno := 'TRN-' || LPAD(i * 10 + t, 6, '0');
        INSERT INTO HOSPITAL_MOCK.HOSP_TURNO (
          ID_TURNO, ID_PACIENTE, ID_PROFESIONAL, COD_ESPECIALIDAD, FECHA_HORA, ESTADO
        ) VALUES (
          v_id_turno,
          v_id_paciente,
          'PROF-' || LPAD(TRUNC(DBMS_RANDOM.VALUE(1, 13)), 6, '0'),
          v_esp_cod(TRUNC(DBMS_RANDOM.VALUE(1, v_esp_cod.COUNT + 1))),
          -- El primer turno del paciente 2 (cronico) se fuerza al futuro (FR-023)
          CASE WHEN i = 2 AND t = 1
               THEN CAST(c_anclaje + 14 AS TIMESTAMP(0))
               ELSE CAST(c_anclaje + TRUNC(DBMS_RANDOM.VALUE(-180, 60)) AS TIMESTAMP(0))
          END,
          pick(v_estados_tur)
        );
      END LOOP;
    END IF;

  END LOOP; -- pacientes

  COMMIT;

  DBMS_OUTPUT.PUT_LINE('Seed completado. Episodios=' || v_n_episodios_tot
                      || ', Observaciones=' || v_n_obs_tot);
END;
/
