package com.example.sismedico.dto.request;

import jakarta.validation.constraints.Future;
import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.NotNull;
import jakarta.validation.constraints.Size;
import lombok.*;

import java.time.LocalDate;
import java.time.LocalTime;

@Getter
@Setter
@NoArgsConstructor
@AllArgsConstructor
@Builder
public class ReprogramarCitaRequest {

    @NotNull(message = "La cita es obligatoria")
    private Long citaId;

    @NotNull(message = "La nueva fecha es obligatoria")
    @Future(message = "La nueva fecha debe ser posterior a la fecha actual")
    private LocalDate nuevaFecha;

    @NotNull(message = "La nueva hora es obligatoria")
    private LocalTime nuevaHora;

    @NotBlank(message = "Debe indicar el motivo de la reprogramación")
    @Size(max = 500, message = "El motivo no puede superar los 500 caracteres")
    private String motivo;

}