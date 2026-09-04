package com.example.sismedico.dto.response;

import com.example.sismedico.enums.DiaSemana;
import lombok.*;

import java.time.LocalDateTime;
import java.time.LocalTime;

@Getter
@Setter
@NoArgsConstructor
@AllArgsConstructor
@Builder
public class HorarioResponse {

    /**
     * ID del horario
     */
    private Long id;

    /**
     * Médico
     */
    private Long medicoId;

    private String nombreMedico;

    /**
     * Especialidad
     */
    private Long especialidadId;

    private String especialidad;

    /**
     * Día de atención
     */
    private DiaSemana diaSemana;

    /**
     * Horario
     */
    private LocalTime horaInicio;

    private LocalTime horaFin;

    /**
     * Estado
     */
    private Boolean activo;

    /**
     * Duración de la consulta (minutos)
     */
    private Integer duracionConsulta;

    /**
     * Consultorio
     */
    private String consultorio;

    /**
     * Fecha de registro
     */
    private LocalDateTime fechaRegistro;

}