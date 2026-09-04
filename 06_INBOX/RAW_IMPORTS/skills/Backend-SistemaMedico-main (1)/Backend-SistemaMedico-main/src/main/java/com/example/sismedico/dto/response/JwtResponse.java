package com.example.sismedico.dto.response;

import lombok.*;

@Getter
@Setter
@NoArgsConstructor
@AllArgsConstructor
@Builder
public class JwtResponse {

    /**
     * Token JWT
     */
    private String token;

    /**
     * Tipo de token
     */
    @Builder.Default
    private String type = "Bearer";

    /**
     * ID del usuario
     */
    private Long id;

    /**
     * UUID del usuario
     */
    private String uuid;

    /**
     * Nombre
     */
    private String nombre;

    /**
     * Apellido
     */
    private String apellido;

    /**
     * Nombre completo
     */
    private String nombreCompleto;

    /**
     * Correo electrónico
     */
    private String correo;

    /**
     * Rol
     */
    private String rol;

    /**
     * Estado del usuario
     */
    private Boolean activo;

    /**
     * Correo verificado
     */
    private Boolean emailVerificado;

}