package com.example.sismedico.dto.response;

import lombok.*;

import java.time.LocalDateTime;

@Getter
@Setter
@NoArgsConstructor
@AllArgsConstructor
@Builder
public class EspecialidadResponse {

    /**
     * ID de la especialidad
     */
    private Long id;

    /**
     * Nombre de la especialidad
     */
    private String nombre;

    /**
     * Descripción
     */
    private String descripcion;

    /**
     * Icono
     */
    private String icono;

    /**
     * Color de identificación
     */
    private String color;

    /**
     * Ubicación o consultorio
     */
    private String ubicacion;

    /**
     * Duración de la consulta (minutos)
     */
    private Integer duracionConsulta;

    /**
     * Costo de la consulta
     */
    private Double costoConsulta;

    /**
     * Estado de la especialidad
     */
    private Boolean activo;

    /**
     * Número de médicos registrados
     */
    private Integer totalMedicos;

    /**
     * Fecha de registro
     */
    private LocalDateTime fechaRegistro;

}