package com.example.sismedico.dto.response;

import lombok.*;

import java.time.LocalDateTime;

@Getter
@Setter
@NoArgsConstructor
@AllArgsConstructor
@Builder
public class NotificacionResponse {

    /**
     * ID de la notificación
     */
    private Long id;

    /**
     * Usuario
     */
    private Long usuarioId;

    private String nombreUsuario;

    /**
     * Información de la notificación
     */
    private String titulo;

    private String mensaje;

    private String tipo;

    /**
     * Estado
     */
    private Boolean leida;

    /**
     * Información adicional
     */
    private String icono;

    private String color;

    private String url;

    /**
     * Auditoría
     */
    private LocalDateTime fechaRegistro;

}