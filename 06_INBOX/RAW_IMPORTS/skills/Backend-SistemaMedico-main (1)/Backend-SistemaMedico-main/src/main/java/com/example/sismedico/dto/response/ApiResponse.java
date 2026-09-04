package com.example.sismedico.dto.response;

import lombok.*;

import java.time.LocalDateTime;

@Getter
@Setter
@NoArgsConstructor
@AllArgsConstructor
@Builder
public class ApiResponse<T> {

    /**
     * Indica si la operación fue exitosa
     */
    private Boolean success;

    /**
     * Mensaje de respuesta
     */
    private String message;

    /**
     * Datos devueltos (opcional)
     */
    private T data;

    /**
     * Fecha y hora de la respuesta
     */
    @Builder.Default
    private LocalDateTime timestamp = LocalDateTime.now();

}