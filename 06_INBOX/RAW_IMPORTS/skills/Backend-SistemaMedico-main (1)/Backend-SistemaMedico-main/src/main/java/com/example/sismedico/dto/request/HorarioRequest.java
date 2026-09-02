package com.example.sismedico.dto.request;

import com.example.sismedico.enums.DiaSemana;
import jakarta.validation.constraints.NotNull;
import lombok.*;

import java.time.LocalTime;

@Getter
@Setter
@NoArgsConstructor
@AllArgsConstructor
@Builder
public class HorarioRequest {

    @NotNull(message = "El médico es obligatorio")
    private Long medicoId;

    @NotNull(message = "El día de la semana es obligatorio")
    private DiaSemana diaSemana;

    @NotNull(message = "La hora de inicio es obligatoria")
    private LocalTime horaInicio;

    @NotNull(message = "La hora final es obligatoria")
    private LocalTime horaFin;

    @Builder.Default
    private Boolean activo = true;

}