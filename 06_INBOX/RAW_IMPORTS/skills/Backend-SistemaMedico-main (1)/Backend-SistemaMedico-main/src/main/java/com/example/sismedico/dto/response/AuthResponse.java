package com.example.sismedico.dto.response;

import lombok.*;

@Getter
@Setter
@NoArgsConstructor
@AllArgsConstructor
@Builder
public class AuthResponse {

    /**
     * Token JWT
     */
    private String token;

    /**
     * Tipo de token
     */
    @Builder.Default
    private String tokenType = "Bearer";

    /**
     * ID del usuario
     */
    private Long usuarioId;

    /**
     * UUID del usuario
     */
    private String uuid;

    /**
     * Nombre completo
     */
    private String nombre;

    /**
     * Correo electrónico
     */
    private String correo;

    /**
     * Rol del usuario
     */
    private String rol;

    /**
     * Estado de la cuenta
     */
    private Boolean activo;

    /**
     * Correo verificado
     */
    private Boolean emailVerificado;

}