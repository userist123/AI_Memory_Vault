package com.example.sismedico.dto.request;

import jakarta.validation.constraints.FutureOrPresent;
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
public class CitaRequest {


    @NotNull(message = "El paciente es obligatorio")
    private Long pacienteId;


    @NotNull(message = "El médico es obligatorio")
    private Long medicoId;


    @NotNull(message = "La especialidad es obligatoria")
    private Long especialidadId;


    @NotNull(message = "La fecha es obligatoria")
    @FutureOrPresent(message = "La fecha debe ser hoy o una fecha futura")
    private LocalDate fecha;


    @NotNull(message = "La hora es obligatoria")
    private LocalTime hora;


    @Size(
            max = 500,
            message = "El motivo no puede exceder los 500 caracteres"
    )
    private String motivo;


    @Size(
            max = 1000,
            message = "Las observaciones no pueden exceder los 1000 caracteres"
    )
    private String observaciones;

}