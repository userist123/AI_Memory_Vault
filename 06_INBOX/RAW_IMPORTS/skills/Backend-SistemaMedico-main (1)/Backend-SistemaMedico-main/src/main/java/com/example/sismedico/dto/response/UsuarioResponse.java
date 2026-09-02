package com.example.sismedico.dto.response;

import com.example.sismedico.enums.Genero;
import lombok.*;

import java.time.LocalDate;
import java.time.LocalDateTime;

@Getter
@Setter
@NoArgsConstructor
@AllArgsConstructor
@Builder
public class UsuarioResponse {

    /**
     * ID del usuario
     */
    private Long id;

    /**
     * UUID
     */
    private String uuid;

    /**
     * Información personal
     */
    private String nombre;

    private String apellido;

    private String nombreCompleto;

    private String correo;

    private String telefono;

    private String direccion;

    private String foto;

    private Genero genero;

    private LocalDate fechaNacimiento;

    /**
     * Rol
     */
    private Long rolId;

    private String rol;

    /**
     * Estado
     */
    private Boolean activo;

    private Boolean emailVerificado;

    /**
     * Firebase
     */
    private String tokenFirebase;

    /**
     * Auditoría
     */
    private LocalDateTime ultimoAcceso;

    private LocalDateTime fechaRegistro;

    private LocalDateTime ultimaActualizacion;

}