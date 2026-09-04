package com.example.sismedico.dto.response;

import lombok.*;

import java.time.LocalDateTime;

@Getter
@Setter
@NoArgsConstructor
@AllArgsConstructor
@Builder
public class RecetaResponse {

    /**
     * ID de la receta
     */
    private Long id;

    /**
     * Diagnóstico
     */
    private Long diagnosticoId;

    /**
     * Cita
     */
    private Long citaId;

    /**
     * Paciente
     */
    private Long pacienteId;

    private String nombrePaciente;

    /**
     * Médico
     */
    private Long medicoId;

    private String nombreMedico;

    /**
     * Información de la receta
     */
    private String medicamentos;

    private String indicaciones;

    private String observaciones;

    /**
     * Estado
     */
    private Boolean surtida;

    /**
     * Auditoría
     */
    private LocalDateTime fechaRegistro;

}